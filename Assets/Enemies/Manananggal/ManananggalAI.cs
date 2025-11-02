using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class ManananggalAI : BaseEnemyAI
{
    [Header("Shadow Dive (heavy dive strike)")]
    public int diveDamage = 45;
    public float diveHitRadius = 1.6f;
    public float diveWindup = 0.6f;
    public float diveCooldown = 8f;
    public float diveAscendTime = 0.35f;
    public float diveDescendSpeed = 18f;
    public GameObject diveWindupVFX;
    public GameObject diveImpactVFX;
    public Vector3 diveVFXOffset = Vector3.zero;
    public float diveVFXScale = 1.0f;
    public AudioClip diveWindupSFX;
    public AudioClip diveImpactSFX;
    public string diveWindupTrigger = "DiveWindup";
    public string diveTrigger = "Dive";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float diveStoppageTime = 1f;
    public float diveRecoveryTime = 0.5f;

    private float lastDiveTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private AudioSource audioSource;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float DiveCooldownRemaining => Mathf.Max(0f, diveCooldown - (Time.time - lastDiveTime));

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
        var canDive = new ConditionNode(blackboard, CanDive, "can_dive");
        var doDive = new ActionNode(blackboard, () => { StartDive(); return NodeState.Success; }, "dive");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "dive_seq").Add(canDive, doDive),
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

    #region Shadow Dive

    private bool CanDive()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastDiveTime < diveCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= 6f && distance <= 12f;
    }

    private void StartDive()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoDive());
    }

    private IEnumerator CoDive()
    {
        BeginAction(AIState.Special1);

        // Capture dive direction before windup
        var target = blackboard.Get<Transform>("target");
        Vector3 diveDirection = transform.forward;
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                diveDirection = toTarget.normalized;
                transform.rotation = Quaternion.LookRotation(diveDirection);
            }
        }

        // Windup animation trigger
        if (animator != null && HasTrigger(diveWindupTrigger)) animator.SetTrigger(diveWindupTrigger);
        else if (animator != null && HasTrigger(diveTrigger)) animator.SetTrigger(diveTrigger);
        if (audioSource != null && diveWindupSFX != null) audioSource.PlayOneShot(diveWindupSFX);
        GameObject wind = null;
        if (diveWindupVFX != null)
        {
            wind = Instantiate(diveWindupVFX, transform);
            wind.transform.localPosition = diveVFXOffset;
            if (diveVFXScale > 0f) wind.transform.localScale = Vector3.one * diveVFXScale;
        }

        // Windup phase - lock rotation
        float windup = Mathf.Max(0f, diveWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            transform.rotation = Quaternion.LookRotation(diveDirection);
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Ascend phase
        float ascend = Mathf.Max(0f, diveAscendTime);
        while (ascend > 0f)
        {
            ascend -= Time.deltaTime;
            yield return null;
        }

        // Update dive direction to current target position
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                diveDirection = toTarget.normalized;
            }
        }

        // Dive animation trigger
        if (animator != null && HasTrigger(diveTrigger)) animator.SetTrigger(diveTrigger);

        // Descend phase - move forward and check for hits
        float travel = 0.8f;
        HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>();
        while (travel > 0f)
        {
            travel -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(diveDirection * diveDescendSpeed * Time.deltaTime);

            // Check for hits during dive - ONE DAMAGE PER PLAYER
            var hits = Physics.OverlapSphere(transform.position, diveHitRadius, LayerMask.GetMask("Player"));
            foreach (var h in hits)
            {
                var ps = h.GetComponentInParent<PlayerStats>();
                if (ps != null && !hitPlayers.Contains(ps))
                {
                    ps.TakeDamage(diveDamage);
                    hitPlayers.Add(ps);
                }
            }

            yield return null;
        }

        // Impact VFX/SFX
        if (diveImpactVFX != null)
        {
            var fx = Instantiate(diveImpactVFX, transform);
            fx.transform.localPosition = diveVFXOffset;
            if (diveVFXScale > 0f) fx.transform.localScale = Vector3.one * diveVFXScale;
        }
        if (audioSource != null && diveImpactSFX != null) audioSource.PlayOneShot(diveImpactSFX);

        // Stoppage recovery (AI frozen after attack)
        if (diveStoppageTime > 0f)
        {
            if (animator != null && HasTrigger(skillStoppageTrigger)) animator.SetTrigger(skillStoppageTrigger);

            float stopTimer = diveStoppageTime;
            float quarterStoppage = diveStoppageTime * 0.75f;

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
        if (diveRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = diveRecoveryTime;
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
        lastDiveTime = Time.time;
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
