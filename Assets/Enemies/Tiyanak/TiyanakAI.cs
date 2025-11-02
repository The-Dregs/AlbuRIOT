using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class TiyanakAI : BaseEnemyAI
{
    [Header("Lunge Bite")]
    public int lungeDamage = 20;
    public float lungeRange = 1.2f;
    public float lungeWindup = 0.3f;
    public float lungeCooldown = 4f;
    public float lungeSpeed = 10f;
    public float lungeDuration = 0.25f;
    public GameObject lungeWindupVFX;
    public GameObject lungeImpactVFX;
    public Vector3 lungeWindupVFXOffset = Vector3.zero;
    public float lungeWindupVFXScale = 1.0f;
    public Vector3 lungeImpactVFXOffset = Vector3.zero;
    public float lungeImpactVFXScale = 1.0f;
    public AudioClip lungeWindupSFX;
    public AudioClip lungeImpactSFX;
    public string lungeWindupTrigger = "LungeWindup";
    public string lungeTrigger = "Lunge";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float lungeStoppageTime = 1f;
    public float lungeRecoveryTime = 0.5f;

    private float lastLungeTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private AudioSource audioSource;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float LungeCooldownRemaining => Mathf.Max(0f, lungeCooldown - (Time.time - lastLungeTime));

    protected override void InitializeEnemy()
    {
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
    }

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRange, "in_attack_range");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canLunge = new ConditionNode(blackboard, CanLunge, "can_lunge");
        var doLunge = new ActionNode(blackboard, () => { StartLunge(); return NodeState.Success; }, "lunge");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "lunge_seq").Add(canLunge, doLunge),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null) return;
        if (activeAbility != null) return;
        if (isBusy || globalBusyTimer > 0f) return;
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return;

        basicRoutine = StartCoroutine(CoBasicAttack(target));
    }

    private IEnumerator CoBasicAttack(Transform target)
    {
        BeginAction(AIState.BasicAttack);

        // Windup animation trigger
        if (animator != null)
        {
            if (HasTrigger(attackWindupTrigger))
                animator.SetTrigger(attackWindupTrigger);
            else if (HasTrigger(attackTrigger))
                animator.SetTrigger(attackTrigger);
        }

        // Windup phase - freeze movement during windup
        float windup = Mathf.Max(0f, enemyData.attackWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // Impact animation trigger
        if (animator != null && HasTrigger(attackImpactTrigger))
            animator.SetTrigger(attackImpactTrigger);

        // Apply damage after windup
        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(enemyData.basicDamage);
        }

        // Post-stop using attackMoveLock duration
        float post = Mathf.Max(0.1f, enemyData.attackMoveLock);
        while (post > 0f)
        {
            post -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
        basicRoutine = null;
        EndAction();
    }

    protected override bool TrySpecialAbilities()
    {
        return false;
    }

    #region Lunge Bite

    private bool CanLunge()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastLungeTime < lungeCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= lungeRange + 2.5f;
    }

    private void StartLunge()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoLunge());
    }

    private IEnumerator CoLunge()
    {
        BeginAction(AIState.Special1);

        // Capture lunge direction before windup
        var target = blackboard.Get<Transform>("target");
        Vector3 lungeDirection = transform.forward;
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                lungeDirection = toTarget.normalized;
                transform.rotation = Quaternion.LookRotation(lungeDirection);
            }
        }

        // Windup animation trigger
        if (animator != null && HasTrigger(lungeWindupTrigger)) animator.SetTrigger(lungeWindupTrigger);
        else if (animator != null && HasTrigger(lungeTrigger)) animator.SetTrigger(lungeTrigger);
        if (audioSource != null && lungeWindupSFX != null) audioSource.PlayOneShot(lungeWindupSFX);
        GameObject wind = null;
        if (lungeWindupVFX != null)
        {
            wind = Instantiate(lungeWindupVFX, transform);
            wind.transform.localPosition = lungeWindupVFXOffset;
            if (lungeWindupVFXScale > 0f) wind.transform.localScale = Vector3.one * lungeWindupVFXScale;
        }

        // Windup phase - lock rotation
        float windup = Mathf.Max(0f, lungeWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(lungeDirection);
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Impact VFX/SFX
        if (lungeImpactVFX != null)
        {
            var fx = Instantiate(lungeImpactVFX, transform);
            fx.transform.localPosition = lungeImpactVFXOffset;
            if (lungeImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * lungeImpactVFXScale;
        }
        if (audioSource != null && lungeImpactSFX != null) audioSource.PlayOneShot(lungeImpactSFX);

        // Lunge animation trigger
        if (animator != null && HasTrigger(lungeTrigger)) animator.SetTrigger(lungeTrigger);

        // Lunge forward
        float t = Mathf.Max(0.05f, lungeDuration);
        HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>();
        while (t > 0f)
        {
            t -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(lungeDirection * lungeSpeed * Time.deltaTime);

            // Check for hits during lunge - ONE DAMAGE PER PLAYER
            if (target != null && Vector3.Distance(transform.position, target.position) <= lungeRange)
            {
                var ps = target.GetComponent<PlayerStats>();
                if (ps != null && !hitPlayers.Contains(ps))
                {
                    ps.TakeDamage(lungeDamage);
                    hitPlayers.Add(ps);
                }
            }
            yield return null;
        }

        // Stoppage recovery (AI frozen after attack)
        if (lungeStoppageTime > 0f)
        {
            if (animator != null && HasTrigger(skillStoppageTrigger)) animator.SetTrigger(skillStoppageTrigger);

            float stopTimer = lungeStoppageTime;
            float quarterStoppage = lungeStoppageTime * 0.75f;

            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);

                // Set Exhausted boolean parameter when 75% of stoppage time remains (skills only)
                if (stopTimer <= quarterStoppage && animator != null && !animator.GetBool("Exhausted"))
                {
                    animator.SetBool("Exhausted", true);
                }

                yield return null;
            }

            // Clear Exhausted boolean parameter
            if (animator != null) animator.SetBool("Exhausted", false);
        }

        // End busy state so AI can move during recovery
        EndAction();

        // Recovery time (AI can move but skill still on cooldown, gradual speed recovery)
        if (lungeRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = lungeRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
            lastAnySkillRecoveryEnd = Time.time;
        }
        else
        {
            lastAnySkillRecoveryEnd = Time.time;
        }

        activeAbility = null;
        lastLungeTime = Time.time;
    }

    #endregion

    protected override float GetMoveSpeed()
    {
        // Return 0 if AI is busy or has active ability (should be stopped)
        if (isBusy || globalBusyTimer > 0f || activeAbility != null || basicRoutine != null)
        {
            return 0f;
        }

        // If AI is idle (not patrolling or chasing), return 0
        if (aiState == AIState.Idle)
        {
            return 0f;
        }

        float baseSpeed = base.GetMoveSpeed();

        // If we're in recovery phase, gradually increase speed from 0.3 to 1.0
        if (Time.time >= lastAnySkillRecoveryStart && Time.time <= lastAnySkillRecoveryEnd && lastAnySkillRecoveryStart >= 0f)
        {
            float recoveryDuration = lastAnySkillRecoveryEnd - lastAnySkillRecoveryStart;
            if (recoveryDuration > 0f)
            {
                float elapsed = Time.time - lastAnySkillRecoveryStart;
                float progress = Mathf.Clamp01(elapsed / recoveryDuration);
                float speedMultiplier = Mathf.Lerp(0.3f, 1.0f, progress);
                return baseSpeed * speedMultiplier;
            }
        }

        return baseSpeed;
    }
}
