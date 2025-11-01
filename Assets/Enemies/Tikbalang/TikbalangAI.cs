using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class TikbalangAI : BaseEnemyAI
{
    [Header("Charge Attack")]
    public int chargeDamage = 35;
    public float chargeCooldown = 15f;
    public float chargeWindup = 2f;
    public float chargeDuration = 2f;
    public float chargeSpeed = 14f;
    public float chargeHitRadius = 1.7f;
    public float chargeMinDistance = 2f;
    public GameObject chargeVFX; // windup
    public AudioClip chargeSFX;  // windup
    public GameObject chargeImpactVFX; // activation
    public Vector3 chargeVFXOffset = Vector3.zero;
    public float chargeVFXScale = 1.0f;
    public Vector3 chargeImpactVFXOffset = Vector3.zero;
    public float chargeImpactVFXScale = 1.0f;
    public AudioClip chargeImpactSFX;

    [Header("Stomp Attack")]
    public int stompDamage = 25;
    public float stompRadius = 4f;
    public float stompCooldown = 10f;
    public float stompWindup = 0.3f;
    public float stompMinDistance = 4f;
    public GameObject stompVFX; // windup
    public AudioClip stompSFX;  // windup
    public GameObject stompImpactVFX; // activation
    public Vector3 stompVFXOffset = Vector3.zero;
    public float stompVFXScale = 1.0f;
    public Vector3 stompImpactVFXOffset = Vector3.zero;
    public float stompImpactVFXScale = 1.0f;
    public AudioClip stompImpactSFX;

    [Header("Animation")]
    public string chargeWindupTrigger = "ChargeWindup";
    public string chargeTrigger = "Charge";
    public string stompWindupTrigger = "StompWindup";
    public string stompTrigger = "Stomp";
    public string skillStoppageTrigger = "SkillStoppage";

    [Header("Skill Selection Tuning")]
    public float chargePreferredMinDistance = 10f;
    public float chargePreferredMaxDistance = 20f;
    [Range(0f, 1f)] public float chargeSkillWeight = 0.8f;
    public float chargeStoppageTime = 1f;
    public float chargeRecoveryTime = 0.5f;
    public float stompPreferredMinDistance = 2.0f;
    public float stompPreferredMaxDistance = 5.5f;
    [Range(0f, 1f)] public float stompSkillWeight = 0.7f;
    public float stompStoppageTime = 1f;
    public float stompRecoveryTime = 0.5f;
    [Header("Spacing")]
    public float preferredDistance = 4.5f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    // Runtime state
    private float lastChargeTime = -9999f;
    private float lastStompTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f; // Track when ANY skill's recovery ended
    private float lastAnySkillRecoveryStart = -9999f; // Track when recovery phase starts
    private AudioSource audioSource;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;

    // Debug accessors
    public float ChargeCooldownRemaining => Mathf.Max(0f, chargeCooldown - (Time.time - lastChargeTime));
    public float StompCooldownRemaining => Mathf.Max(0f, stompCooldown - (Time.time - lastStompTime));

    #region Initialization

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

        var canCharge = new ConditionNode(blackboard, CanCharge, "can_charge");
        var doCharge = new ActionNode(blackboard, () => { StartCharge(); return NodeState.Success; }, "charge");
        var canStomp = new ConditionNode(blackboard, CanStomp, "can_stomp");
        var doStomp = new ActionNode(blackboard, () => { StartStomp(); return NodeState.Success; }, "stomp");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "charge_seq").Add(canCharge, doCharge),
                        new Sequence(blackboard, "stomp_seq").Add(canStomp, doStomp),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
            moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    #endregion

    #region BaseEnemyAI Overrides

    protected override void PerformBasicAttack()
    {
        if (basicRoutine != null) return;
        if (activeAbility != null) return; // Don't interrupt special abilities
        if (isBusy || globalBusyTimer > 0f) return; // Don't interrupt other actions
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
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
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
        if (activeAbility != null) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, specialFacingAngle);
        bool inChargeRange = dist >= chargePreferredMinDistance && dist <= chargePreferredMaxDistance;
        bool inStompRange = dist >= stompPreferredMinDistance && dist <= stompPreferredMaxDistance;
        float chargeMid = (chargePreferredMinDistance + chargePreferredMaxDistance) * 0.5f;
        float stompMid = (stompPreferredMinDistance + stompPreferredMaxDistance) * 0.5f;
        float chargeScore = (inChargeRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(chargeMid - dist) / 15f)) * chargeSkillWeight : 0f;
        float stompScore = (inStompRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(stompMid - dist) / 5f)) * stompSkillWeight : 0f;
        if (CanCharge() && chargeScore >= stompScore && chargeScore > 0.15f) { StartCharge(); return true; }
        if (CanStomp() && stompScore > chargeScore && stompScore > 0.15f) { StartStomp(); return true; }
        return false;
    }

    private bool IsFacingTarget(Transform target, float maxAngle)
    {
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z).normalized, to.normalized);
        return angle <= Mathf.Clamp(maxAngle, 1f, 60f);
    }
    private void FaceTarget(Transform target)
    {
        Vector3 look = new Vector3(target.position.x, transform.position.y, target.position.z);
        Vector3 dir = (look - transform.position);
        if (dir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotationSpeedDegrees * Time.deltaTime);
        }
        if (controller != null && controller.enabled)
            controller.SimpleMove(Vector3.zero);
    }

    #endregion

    #region Charge Attack

    private bool CanCharge()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false; // Don't interrupt basic attacks
        if (isBusy || globalBusyTimer > 0f) return false; // Don't interrupt basic attacks or other actions
        if (Time.time - lastChargeTime < chargeCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false; // 4 second lock after any skill recovery
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= chargeMinDistance;
    }

    private void StartCharge()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoCharge());
    }

    private IEnumerator CoCharge()
    {
        BeginAction(AIState.Special1);
        
        // Capture charge direction before windup (where to charge)
        var target = blackboard.Get<Transform>("target");
        Vector3 chargeDirection = transform.forward; // Default to current forward
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                chargeDirection = toTarget.normalized;
                // Set rotation once before windup
                transform.rotation = Quaternion.LookRotation(chargeDirection);
            }
        }
        
        // Windup animation
        if (animator != null && HasTrigger(chargeWindupTrigger)) animator.SetTrigger(chargeWindupTrigger);
        // windup SFX (stoppable)
        if (audioSource != null && chargeSFX != null)
        {
            audioSource.clip = chargeSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup VFX
        GameObject chargeWindupFx = null;
        if (chargeVFX != null)
        {
            chargeWindupFx = Instantiate(chargeVFX, transform);
            chargeWindupFx.transform.localPosition = chargeVFXOffset;
            if (chargeVFXScale > 0f) chargeWindupFx.transform.localScale = Vector3.one * chargeVFXScale;
        }
        
        // Windup phase - lock rotation (don't face player)
        float windup = chargeWindup;
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            // Lock rotation during windup
            transform.rotation = Quaternion.LookRotation(chargeDirection);
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            yield return null;
        }

        // End windup visuals/audio and play activation impact VFX/SFX
        if (audioSource != null && audioSource.clip == chargeSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (chargeWindupFx != null) Destroy(chargeWindupFx);
        if (chargeImpactVFX != null)
        {
            var fx = Instantiate(chargeImpactVFX, transform);
            fx.transform.localPosition = chargeImpactVFXOffset;
            if (chargeImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * chargeImpactVFXScale;
        }
        if (audioSource != null && chargeImpactSFX != null) audioSource.PlayOneShot(chargeImpactSFX);

        // Charge animation trigger
        if (animator != null && HasTrigger(chargeTrigger)) animator.SetTrigger(chargeTrigger);

        // Charge in locked direction (set before windup)
        float chargeTime = chargeDuration;
        HashSet<PlayerStats> hitPlayers = new HashSet<PlayerStats>(); // Track players already hit
        
        while (chargeTime > 0f)
        {
            if (controller != null && controller.enabled)
            {
                controller.Move(chargeDirection * chargeSpeed * Time.deltaTime);
            }

            // Check for hits during charge
            var hitColliders = Physics.OverlapSphere(transform.position, chargeHitRadius);
            foreach (var hit in hitColliders)
            {
                if (hit.CompareTag("Player"))
                {
                    var playerStats = hit.GetComponent<PlayerStats>();
                    if (playerStats != null && !hitPlayers.Contains(playerStats))
                    {
                        playerStats.TakeDamage(chargeDamage);
                        hitPlayers.Add(playerStats);
                    }
                }
            }

            chargeTime -= Time.deltaTime;
            yield return null;
        }

        // Stoppage recovery (AI frozen after attack)
        if (chargeStoppageTime > 0f)
        {
            // Stoppage animation trigger for skills
            if (animator != null && HasTrigger(skillStoppageTrigger)) animator.SetTrigger(skillStoppageTrigger);
            
            float stopTimer = chargeStoppageTime;
            float quarterStoppage = chargeStoppageTime * 0.75f;
            
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

        // Recovery time (AI can move but skill still on cooldown)
        if (chargeRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time; // Mark recovery start for gradual speed
            float recovery = chargeRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
        }

        activeAbility = null;
        lastChargeTime = Time.time; // Set cooldown timer after all recovery is done
        lastAnySkillRecoveryEnd = Time.time; // Set global lock timer after recovery ends
        EndAction();
    }

    #endregion

    #region Stomp Attack

    private bool CanStomp()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false; // Don't interrupt basic attacks
        if (isBusy || globalBusyTimer > 0f) return false; // Don't interrupt basic attacks or other actions
        if (Time.time - lastStompTime < stompCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false; // 4 second lock after any skill recovery
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= stompMinDistance;
    }

    private void StartStomp()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoStomp());
    }

    private IEnumerator CoStomp()
    {
        BeginAction(AIState.Special2);
        
        // Windup animation
        if (animator != null && HasTrigger(stompWindupTrigger)) animator.SetTrigger(stompWindupTrigger);
        // windup sfx (stoppable)
        if (audioSource != null && stompSFX != null)
        {
            audioSource.clip = stompSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup vfx
        GameObject stompWindupFx = null;
        if (stompVFX != null)
        {
            stompWindupFx = Instantiate(stompVFX, transform);
            stompWindupFx.transform.localPosition = stompVFXOffset;
            if (stompVFXScale > 0f) stompWindupFx.transform.localScale = Vector3.one * stompVFXScale;
        }

        // Windup wait
        yield return new WaitForSeconds(stompWindup);

        // end windup visuals/audio and play activation vfx/sfx
        if (audioSource != null && audioSource.clip == stompSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (stompWindupFx != null) Destroy(stompWindupFx);
        if (stompImpactVFX != null)
        {
            var fx = Instantiate(stompImpactVFX, transform);
            fx.transform.localPosition = stompImpactVFXOffset;
            if (stompImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * stompImpactVFXScale;
        }
        if (audioSource != null && stompImpactSFX != null) audioSource.PlayOneShot(stompImpactSFX);

        // Stomp animation trigger
        if (animator != null && HasTrigger(stompTrigger)) animator.SetTrigger(stompTrigger);

        var hitColliders = Physics.OverlapSphere(transform.position, stompRadius);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Player"))
            {
                var playerStats = hit.GetComponent<PlayerStats>();
                if (playerStats != null) playerStats.TakeDamage(stompDamage);
            }
        }

        // Stoppage recovery (AI frozen after attack)
        if (stompStoppageTime > 0f)
        {
            // Stoppage animation trigger for skills
            if (animator != null && HasTrigger(skillStoppageTrigger)) animator.SetTrigger(skillStoppageTrigger);
            
            float stopTimer = stompStoppageTime;
            float quarterStoppage = stompStoppageTime * 0.75f;
            
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

        // Recovery time (AI can move but skill still on cooldown)
        if (stompRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time; // Mark recovery start for gradual speed
            float recovery = stompRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                yield return null;
            }
        }

        activeAbility = null;
        lastStompTime = Time.time; // Set cooldown timer after all recovery is done
        lastAnySkillRecoveryEnd = Time.time; // Set global lock timer after recovery ends
        EndAction();
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
                float speedMultiplier = Mathf.Lerp(0.3f, 1.0f, progress); // Start at 30% speed, lerp to 100%
                return baseSpeed * speedMultiplier;
            }
        }
        
        return baseSpeed;
    }
}