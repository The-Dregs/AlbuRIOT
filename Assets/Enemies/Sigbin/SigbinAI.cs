using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class SigbinAI : BaseEnemyAI
{
    [Header("Backstep Slash")]
    public int backstepDamage = 28;
    public float backstepEffectiveRange = 1.4f;
    public float backstepWindup = 0.35f;
    public float backstepCooldown = 7f;
    public float backstepDistance = 2.0f;
    public float backstepDuration = 0.15f;
    public GameObject backstepWindupVFX;
    public GameObject backstepImpactVFX;
    public Vector3 backstepWindupVFXOffset = Vector3.zero;
    public float backstepWindupVFXScale = 1.0f;
    public Vector3 backstepImpactVFXOffset = Vector3.zero;
    public float backstepImpactVFXScale = 1.0f;
    public AudioClip backstepWindupSFX;
    public AudioClip backstepImpactSFX;
    public string backstepWindupTrigger = "BackstepWindup";
    public string backstepTrigger = "Backstep";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float backstepStoppageTime = 1f;
    public float backstepRecoveryTime = 0.5f;

    private float lastBackstepTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private AudioSource audioSource;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float BackstepCooldownRemaining => Mathf.Max(0f, backstepCooldown - (Time.time - lastBackstepTime));

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
        var canBackstep = new ConditionNode(blackboard, CanBackstep, "can_backstep");
        var doBackstep = new ActionNode(blackboard, () => { StartBackstep(); return NodeState.Success; }, "backstep");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "backstep_seq").Add(canBackstep, doBackstep),
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

    #region Backstep Slash

    private bool CanBackstep()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastBackstepTime < backstepCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartBackstep()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoBackstep());
    }

    private IEnumerator CoBackstep()
    {
        BeginAction(AIState.Special1);

        // Windup animation trigger
        if (animator != null && HasTrigger(backstepWindupTrigger)) animator.SetTrigger(backstepWindupTrigger);
        else if (animator != null && HasTrigger(backstepTrigger)) animator.SetTrigger(backstepTrigger);
        if (audioSource != null && backstepWindupSFX != null) audioSource.PlayOneShot(backstepWindupSFX);
        GameObject wind = null;
        if (backstepWindupVFX != null)
        {
            wind = Instantiate(backstepWindupVFX, transform);
            wind.transform.localPosition = backstepWindupVFXOffset;
            if (backstepWindupVFXScale > 0f) wind.transform.localScale = Vector3.one * backstepWindupVFXScale;
        }

        // Windup phase - freeze movement
        float windup = Mathf.Max(0f, backstepWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Backstep backwards
        float t = Mathf.Max(0f, backstepDuration);
        Vector3 backward = -transform.forward;
        while (t > 0f)
        {
            t -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(backward * (backstepDistance / Mathf.Max(0.01f, backstepDuration)) * Time.deltaTime);
            yield return null;
        }

        // Check for target within effective range and apply damage
        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist <= backstepEffectiveRange)
            {
                if (audioSource != null && backstepImpactSFX != null) audioSource.PlayOneShot(backstepImpactSFX);
                if (backstepImpactVFX != null)
                {
                    var fx = Instantiate(backstepImpactVFX, transform);
                    fx.transform.localPosition = backstepImpactVFXOffset;
                    if (backstepImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * backstepImpactVFXScale;
                }
                var ps = target.GetComponent<PlayerStats>();
                if (ps != null) ps.TakeDamage(backstepDamage);
            }
        }

        // Stoppage recovery (AI frozen after attack)
        if (backstepStoppageTime > 0f)
        {
            if (animator != null && HasTrigger(skillStoppageTrigger)) animator.SetTrigger(skillStoppageTrigger);

            float stopTimer = backstepStoppageTime;
            float quarterStoppage = backstepStoppageTime * 0.75f;

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
        if (backstepRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = backstepRecoveryTime;
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
        lastBackstepTime = Time.time;
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
