using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class KapreAI : BaseEnemyAI
{
    [Header("Smoke Vanish → Strike")]
    public int vanishStrikeDamage = 30;
    public float vanishStrikeRadius = 1.6f;
    public float vanishWindup = 0.5f;
    public float vanishCooldown = 9f;
    public float vanishTeleportBehindDistance = 1.2f;
    public GameObject vanishWindupVFX;
    public GameObject vanishImpactVFX;
    public Vector3 vanishWindupVFXOffset = Vector3.zero;
    public float vanishWindupVFXScale = 1.0f;
    public Vector3 vanishImpactVFXOffset = Vector3.zero;
    public float vanishImpactVFXScale = 1.0f;
    public AudioClip vanishWindupSFX;
    public AudioClip vanishImpactSFX;
    public string vanishWindupTrigger = "VanishWindup";
    public string vanishMainTrigger = "VanishMain";

    [Header("Tree Slam (frontal AOE)")]
    public int treeSlamDamage = 35;
    public float treeSlamRadius = 2.6f;
    public float treeSlamWindup = 0.6f;
    public float treeSlamCooldown = 10f;
    public float treeSlamLeapDistance = 5f;
    public float treeSlamLeapDuration = 0.5f;
    public float treeSlamLeapHeight = 1.5f;
    public GameObject treeSlamWindupVFX;
    public GameObject treeSlamImpactVFX;
    public Vector3 treeSlamWindupVFXOffset = Vector3.zero;
    public float treeSlamWindupVFXScale = 1.0f;
    public Vector3 treeSlamImpactVFXOffset = Vector3.zero;
    public float treeSlamImpactVFXScale = 1.0f;
    public AudioClip treeSlamWindupSFX;
    public AudioClip treeSlamImpactSFX;
    public string treeSlamWindupTrigger = "TreeSlamWindup";
    public string treeSlamMainTrigger = "TreeSlamMain";

    [Header("Skill Selection Tuning")]
    public float vanishPreferredMinDistance = 2f;
    public float vanishPreferredMaxDistance = 7f;
    [Range(0f, 1f)] public float vanishSkillWeight = 0.7f;
    public float vanishStoppageTime = 1f;
    public float vanishRecoveryTime = 0.5f;
    public float treeSlamPreferredMinDistance = 3f;
    public float treeSlamPreferredMaxDistance = 9f;
    [Range(0f, 1f)] public float treeSlamSkillWeight = 0.8f;
    public float treeSlamStoppageTime = 1f;
    public float treeSlamRecoveryTime = 0.5f;

    // Runtime state
    private float lastVanishTime = -9999f;
    private float lastTreeSlamTime = -9999f;
    private float lastAnySkillRecoveryEnd = -9999f;
    private float lastAnySkillRecoveryStart = -9999f;
    private AudioSource audioSource;
    private Coroutine activeAbility;
    private Coroutine basicRoutine;
    
    // Debug accessors
    public float VanishCooldownRemaining => Mathf.Max(0f, vanishCooldown - (Time.time - lastVanishTime));
    public float TreeSlamCooldownRemaining => Mathf.Max(0f, treeSlamCooldown - (Time.time - lastTreeSlamTime));

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
        var canVanish = new ConditionNode(blackboard, CanVanishStrike, "can_vanish");
        var doVanish = new ActionNode(blackboard, () => { StartVanishStrike(); return NodeState.Success; }, "vanish");
        var canTreeSlam = new ConditionNode(blackboard, CanTreeSlam, "can_treeslam");
        var doTreeSlam = new ActionNode(blackboard, () => { StartTreeSlam(); return NodeState.Success; }, "treeslam");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "vanish_seq").Add(canVanish, doVanish),
                        new Sequence(blackboard, "treeslam_seq").Add(canTreeSlam, doTreeSlam),
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
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, SpecialFacingAngle);
        bool inVanishRange = dist >= vanishPreferredMinDistance && dist <= vanishPreferredMaxDistance;
        bool inTreeSlamRange = dist >= treeSlamPreferredMinDistance && dist <= treeSlamPreferredMaxDistance;
        float vanishMid = (vanishPreferredMinDistance + vanishPreferredMaxDistance) * 0.5f;
        float treeMid = (treeSlamPreferredMinDistance + treeSlamPreferredMaxDistance) * 0.5f;
        float vanishScore = (inVanishRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(vanishMid - dist) / 5f)) * vanishSkillWeight : 0f;
        float treeSlamScore = (inTreeSlamRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(treeMid - dist) / 6f)) * treeSlamSkillWeight : 0f;
        if (CanVanishStrike() && vanishScore >= treeSlamScore && vanishScore > 0.15f) { StartVanishStrike(); return true; }
        if (CanTreeSlam() && treeSlamScore > vanishScore && treeSlamScore > 0.15f) { StartTreeSlam(); return true; }
        return false;
    }

    private bool CanVanishStrike()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastVanishTime < vanishCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= enemyData.detectionRange;
    }

    private void StartVanishStrike()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        
        // Capture target position when ability is initiated (FIRST LOCATED)
        var target = blackboard.Get<Transform>("target");
        Vector3 capturedPos = target != null ? target.position : transform.position;
        Vector3 capturedForward = target != null ? target.forward : Vector3.forward;
        
        activeAbility = StartCoroutine(CoVanishStrike(capturedPos, capturedForward));
    }

    private IEnumerator CoVanishStrike(Vector3 capturedPos, Vector3 capturedForward)
    {
        BeginAction(AIState.Special1);
        
        // Windup phase - separate trigger, VFX, and SFX
        if (animator != null && HasTrigger(vanishWindupTrigger))
            animator.SetTrigger(vanishWindupTrigger);
        if (audioSource != null && vanishWindupSFX != null) 
            audioSource.PlayOneShot(vanishWindupSFX);
        GameObject wind = null;
        if (vanishWindupVFX != null)
        {
            wind = Instantiate(vanishWindupVFX, transform);
            wind.transform.localPosition = vanishWindupVFXOffset;
            if (vanishWindupVFXScale > 0f) 
                wind.transform.localScale = Vector3.one * vanishWindupVFXScale;
        }
        
        // Windup phase - freeze movement (no continuous facing for teleport attacks)
        float windup = Mathf.Max(0f, vanishWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            if (controller != null && controller.enabled) 
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);

        // Teleport behind captured position (where player was FIRST LOCATED)
        Vector3 behind = capturedPos - capturedForward * Mathf.Max(0.2f, vanishTeleportBehindDistance);
        behind.y = transform.position.y;
        
        // Disable CharacterController to allow position change
        if (controller != null)
        {
            controller.enabled = false;
            transform.position = behind;
            controller.enabled = true;
        }
        else
        {
            transform.position = behind;
        }
        
        // Face captured position
        Vector3 dir = (capturedPos - transform.position);
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(dir.normalized);
        
        // Impact phase - separate trigger, VFX, and SFX
        if (animator != null && HasTrigger(vanishMainTrigger))
            animator.SetTrigger(vanishMainTrigger);
            
        if (vanishImpactVFX != null)
        {
            var fx = Instantiate(vanishImpactVFX, transform);
            fx.transform.localPosition = vanishImpactVFXOffset;
            if (vanishImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * vanishImpactVFXScale;
        }
        if (audioSource != null && vanishImpactSFX != null) audioSource.PlayOneShot(vanishImpactSFX);

        var cols = Physics.OverlapSphere(transform.position, vanishStrikeRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(vanishStrikeDamage);
        }

        // Stoppage recovery (AI frozen after attack)
        if (vanishStoppageTime > 0f)
        {
            float stopTimer = vanishStoppageTime;
            float quarterStoppage = vanishStoppageTime * 0.75f;
            
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
        if (vanishRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = vanishRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                // AI can move during recovery - no movement freeze
                yield return null;
            }
            lastAnySkillRecoveryEnd = Time.time;
        }
        else
        {
            lastAnySkillRecoveryEnd = Time.time;
        }

        activeAbility = null;
        lastVanishTime = Time.time;
    }

    private bool CanTreeSlam()
    {
        if (activeAbility != null) return false;
        if (basicRoutine != null) return false;
        if (isBusy || globalBusyTimer > 0f) return false;
        if (Time.time - lastTreeSlamTime < treeSlamCooldown) return false;
        if (Time.time - lastAnySkillRecoveryEnd < 4f) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartTreeSlam()
    {
        if (activeAbility != null) return;
        if (enemyData != null) lastAttackTime = Time.time;
        activeAbility = StartCoroutine(CoTreeSlam());
    }

    private IEnumerator CoTreeSlam()
    {
        BeginAction(AIState.Special2);
        
        // Windup phase - separate trigger, VFX, and SFX
        if (animator != null && HasTrigger(treeSlamWindupTrigger))
            animator.SetTrigger(treeSlamWindupTrigger);
        if (audioSource != null && treeSlamWindupSFX != null) 
            audioSource.PlayOneShot(treeSlamWindupSFX);
        GameObject wind = null;
        if (treeSlamWindupVFX != null)
        {
            wind = Instantiate(treeSlamWindupVFX, transform);
            wind.transform.localPosition = treeSlamWindupVFXOffset;
            if (treeSlamWindupVFXScale > 0f) 
                wind.transform.localScale = Vector3.one * treeSlamWindupVFXScale;
        }
        
        // Capture leap direction before windup (where to leap)
        var target = blackboard.Get<Transform>("target");
        Vector3 leapDirection = transform.forward; // Default to current forward
        if (target != null)
        {
            Vector3 toTarget = new Vector3(target.position.x, transform.position.y, target.position.z) - transform.position;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                leapDirection = toTarget.normalized;
                // Set rotation once before windup
                transform.rotation = Quaternion.LookRotation(leapDirection);
            }
        }
        
        // Windup phase - lock rotation (don't face player)
        float windup = Mathf.Max(0f, treeSlamWindup);
        while (windup > 0f)
        {
            windup -= Time.deltaTime;
            // Lock rotation during windup
            transform.rotation = Quaternion.LookRotation(leapDirection);
            if (controller != null && controller.enabled) 
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (wind != null) Destroy(wind);
        
        // Forward leap (locked rotation, jumps forward)
        Vector3 startPos = transform.position;
        Vector3 endPos = startPos + leapDirection * treeSlamLeapDistance;
        endPos.y = startPos.y; // Keep Y level for landing
        
        float leapTime = 0f;
        while (leapTime < treeSlamLeapDuration)
        {
            leapTime += Time.deltaTime;
            float progress = leapTime / treeSlamLeapDuration;
            
            // Parabolic arc for jump
            float height = Mathf.Sin(progress * Mathf.PI) * treeSlamLeapHeight;
            Vector3 currentPos = Vector3.Lerp(startPos, endPos, progress);
            currentPos.y = startPos.y + height;
            
            // Move with CharacterController
            if (controller != null && controller.enabled)
            {
                Vector3 move = currentPos - transform.position;
                controller.Move(move);
            }
            else
            {
                transform.position = currentPos;
            }
            
            // Lock rotation during leap
            transform.rotation = Quaternion.LookRotation(leapDirection);
            
            yield return null;
        }
        
        // Ensure we're at end position
        if (controller != null && controller.enabled)
        {
            controller.enabled = false;
            transform.position = endPos;
            controller.enabled = true;
        }
        else
        {
            transform.position = endPos;
        }
        
        // Impact phase - separate trigger, VFX, and SFX
        if (animator != null && HasTrigger(treeSlamMainTrigger))
            animator.SetTrigger(treeSlamMainTrigger);
        
        if (treeSlamImpactVFX != null)
        {
            var fx = Instantiate(treeSlamImpactVFX, transform);
            fx.transform.localPosition = treeSlamImpactVFXOffset;
            if (treeSlamImpactVFXScale > 0f) 
                fx.transform.localScale = Vector3.one * treeSlamImpactVFXScale;
        }
        if (audioSource != null && treeSlamImpactSFX != null) 
            audioSource.PlayOneShot(treeSlamImpactSFX);

        // frontal AOE based on radius ahead
        var all = Physics.OverlapSphere(transform.position + transform.forward * (treeSlamRadius * 0.75f), treeSlamRadius, LayerMask.GetMask("Player"));
        foreach (var c in all)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(treeSlamDamage);
        }

        // Stoppage recovery (AI frozen after attack)
        if (treeSlamStoppageTime > 0f)
        {
            float stopTimer = treeSlamStoppageTime;
            float quarterStoppage = treeSlamStoppageTime * 0.75f;
            
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
        if (treeSlamRecoveryTime > 0f)
        {
            lastAnySkillRecoveryStart = Time.time;
            float recovery = treeSlamRecoveryTime;
            while (recovery > 0f)
            {
                recovery -= Time.deltaTime;
                // AI can move during recovery - no movement freeze
                yield return null;
            }
            lastAnySkillRecoveryEnd = Time.time;
        }
        else
        {
            lastAnySkillRecoveryEnd = Time.time;
        }

        activeAbility = null;
        lastTreeSlamTime = Time.time;
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
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
        }
        if (controller != null && controller.enabled)
            controller.SimpleMove(Vector3.zero);
    }

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