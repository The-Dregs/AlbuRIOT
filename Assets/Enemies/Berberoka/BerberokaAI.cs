using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BerberokaAI : BaseEnemyAI
{
    // Organize all Berberoka fields with clear headers
    [Header("Water Vortex (DoT Pull)")]
    public int vortexTickDamage = 6;
    public float vortexTickInterval = 0.25f;
    public int vortexTicks = 10;
    public float vortexRadius = 5.5f;
    public float vortexWindup = 0.6f;
    public float vortexCooldown = 10f;
    public float vortexPullStrength = 6f;
    [Header("Vortex VFX/SFX")]
    public GameObject vortexWindupVFX;
    public GameObject vortexActiveVFX;
    public Vector3 vortexVFXOffset = Vector3.zero;
    public float vortexVFXScale = 1.0f;
    public AudioClip vortexWindupSFX;
    public AudioClip vortexActiveSFX;
    public string vortexTrigger = "Vortex";
    [Header("Vortex Indicator")]
    public GameObject vortexIndicatorPrefab;
    public Vector3 vortexIndicatorOffset = new Vector3(0f, -0.1f, 0f);
    public Vector3 vortexIndicatorScale = new Vector3(3f, 1f, 3f);
    public bool vortexIndicatorRotate90X = true;
    [SerializeField] private float vortexStoppageTime = 1f;
    [Header("Flood Crash (AoE + Projectile)")]
    public int floodCrashDamage = 35;
    [Range(0f,180f)] public float floodCrashConeAngle = 60f;
    public float floodCrashRange = 6.5f;
    public float floodCrashWindup = 0.5f;
    public float floodCrashCooldown = 8f;
    [Header("Flood VFX/SFX")]
    public GameObject floodWindupVFX;
    public GameObject floodImpactVFX;
    public Vector3 floodVFXOffset = Vector3.zero;
    public float floodVFXScale = 1.0f;
    public AudioClip floodWindupSFX;
    public AudioClip floodImpactSFX;
    public string floodTrigger = "Flood";
    [Header("Flood Indicator")]
    public GameObject floodIndicatorPrefab;
    public Vector3 floodIndicatorOffset = new Vector3(0f, -0.1f, 0f);
    public Vector3 floodIndicatorScale = new Vector3(3.5f, 1f, 3.5f);
    public bool floodIndicatorRotate90X = true;
    [SerializeField] private float floodStoppageTime = 1f;
    [Header("Flood Projectile")]
    public GameObject floodProjectilePrefab;
    public Vector3 floodProjectileSpawnOffset = new Vector3(0f,1f,1.5f);
    public float projectileSpeed = 14f;
    public float projectileLifetime = 2.5f;
    [Range(1,10)] public int projectileCount = 3;
    [Range(1f, 120f)] public float projectileSpreadAngle = 30f;

    [Header("Skill Selection Tuning")]
    public float vortexPreferredMinDistance = 6f;
    public float vortexPreferredMaxDistance = 12f;
    public float floodPreferredMinDistance = 3f;
    public float floodPreferredMaxDistance = 7f;
    [Range(0f, 1f)] public float vortexSkillWeight = 0.6f;
    [Range(0f, 1f)] public float floodSkillWeight = 0.8f;

    private float lastVortexTime = -9999f;
    private float lastFloodTime = -9999f;
    private AudioSource audioSource;
    private bool vortexActive = false; // allow movement while active but block other specials


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
        var maintainSpace = new ActionNode(blackboard, MaintainSpacing, "maintain_space");
        var targetInAttackFacing = new ConditionNode(blackboard, TargetInAttackRangeFacing, "in_attack_range_facing");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canVortex = new ConditionNode(blackboard, CanVortex, "can_vortex");
        var doVortex = new ActionNode(blackboard, () => { StartVortex(); return NodeState.Success; }, "vortex");
        var canFlood = new ConditionNode(blackboard, CanFloodCrash, "can_flood");
        var doFlood = new ActionNode(blackboard, () => { StartFloodCrash(); return NodeState.Success; }, "flood");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    maintainSpace,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "vortex_seq").Add(canVortex, doVortex),
                        new Sequence(blackboard, "flood_seq").Add(canFlood, doFlood),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttackFacing, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override void PerformBasicAttack()
    {
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return;
        if (!IsFacingTarget(target, SpecialFacingAngle))
        {
            FaceTarget(target);
            return; // wait until faced
        }
        if (animator != null && HasTrigger(attackTrigger)) animator.SetTrigger(attackTrigger);

        float radius = Mathf.Max(0.8f, enemyData.attackRange);
        Vector3 center = transform.position + transform.forward * (enemyData.attackRange * 0.5f);
        var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(enemyData.basicDamage);
        }

        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
    }

    protected override bool TrySpecialAbilities()
    {
        if (isBusy) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        // Range gating
        bool inVortexRange = dist >= vortexPreferredMinDistance && dist <= vortexPreferredMaxDistance;
        bool inFloodRange = dist >= floodPreferredMinDistance && dist <= floodPreferredMaxDistance;
        // Scoring based on distance from preferred mid range
        float vortexMid = (vortexPreferredMinDistance + vortexPreferredMaxDistance) * 0.5f;
        float floodMid = (floodPreferredMinDistance + floodPreferredMaxDistance) * 0.5f;
        float vortexDistScore = 1f - Mathf.Clamp01(Mathf.Abs(vortexMid - dist) / 10f);
        float floodDistScore = 1f - Mathf.Clamp01(Mathf.Abs(floodMid - dist) / 10f);
        float vortexScore = inVortexRange ? vortexDistScore * vortexSkillWeight : 0f;
        float floodScore = inFloodRange ? floodDistScore * floodSkillWeight : 0f;
        if (CanVortex() && vortexScore >= floodScore && vortexScore > 0.15f) { StartVortex(); return true; }
        if (CanFloodCrash() && floodScore > vortexScore && floodScore > 0.15f) { StartFloodCrash(); return true; }
        return false;
    }

    // Water Vortex (DoT pull)
    private bool CanVortex()
    {
        if (isBusy || vortexActive) return false;
        if (Time.time - lastVortexTime < vortexCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        return Vector3.Distance(transform.position, target.position) <= vortexRadius + 1.5f;
    }

    private void StartVortex()
    {
        lastVortexTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoVortex());
    }

    private IEnumerator CoVortex()
    {
        BeginAction(AIState.Special1);
        if (animator != null && HasTrigger(vortexTrigger)) animator.SetTrigger(vortexTrigger);
        if (audioSource != null && vortexWindupSFX != null)
        {
            audioSource.clip = vortexWindupSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        GameObject windupFx = null;
        if (vortexWindupVFX != null)
        {
            windupFx = Instantiate(vortexWindupVFX, transform);
            windupFx.transform.localPosition = vortexVFXOffset;
            if (vortexVFXScale > 0f) windupFx.transform.localScale = Vector3.one * vortexVFXScale;
        }
        // Indicator appears at windup start and grows to full radius
        GameObject indicatorWindup = null;
        Vector3 indicatorTarget = new Vector3(
            Mathf.Max(0.01f, vortexIndicatorScale.x),
            Mathf.Max(0.01f, vortexIndicatorScale.y),
            Mathf.Max(0.01f, vortexIndicatorScale.z)
        );
        if (vortexIndicatorPrefab != null)
        {
            indicatorWindup = Instantiate(vortexIndicatorPrefab, transform);
            indicatorWindup.transform.localPosition = vortexIndicatorOffset;
            if (vortexIndicatorRotate90X) indicatorWindup.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            indicatorWindup.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        }
        float wind = Mathf.Max(0f, vortexWindup);
        float indicatorGrowTime = Mathf.Max(0.01f, vortexWindup * 0.2f); // indicator emerges in 50% of windup
        float indicatorTimer = 0f;
        while (wind > 0f)
        {
            wind -= Time.deltaTime;
            // freeze movement during windup
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            // face target during windup
            var t = blackboard.Get<Transform>("target");
            if (t != null)
            {
                Vector3 look = new Vector3(t.position.x, transform.position.y, t.position.z);
                Vector3 dir = (look - transform.position);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion r = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, r, RotationSpeed * Time.deltaTime);
                }
            }
            // grow indicator
            if (indicatorWindup != null && indicatorTimer < indicatorGrowTime)
            {
                indicatorTimer += Time.deltaTime;
                float pct = Mathf.Clamp01(indicatorTimer / indicatorGrowTime);
                Vector3 s = Vector3.Lerp(new Vector3(0.01f, 0.01f, 0.01f), indicatorTarget, pct);
                indicatorWindup.transform.localScale = s;
            }
            yield return null;
        }
        if (audioSource != null && audioSource.clip == vortexWindupSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (windupFx != null) Destroy(windupFx);
        // replace windup indicator with active one (keeps same scale)
        if (indicatorWindup != null) Destroy(indicatorWindup);
        if (audioSource != null && vortexActiveSFX != null) audioSource.PlayOneShot(vortexActiveSFX);
        GameObject activeFx = null;
        GameObject indicatorFx = null;
        if (vortexActiveVFX != null)
        {
            activeFx = Instantiate(vortexActiveVFX, transform);
            activeFx.transform.localPosition = vortexVFXOffset;
            if (vortexVFXScale > 0f) activeFx.transform.localScale = Vector3.one * vortexVFXScale;
        }
        if (vortexIndicatorPrefab != null)
        {
            indicatorFx = Instantiate(vortexIndicatorPrefab, transform);
            indicatorFx.transform.localPosition = vortexIndicatorOffset;
            if (vortexIndicatorRotate90X) indicatorFx.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            indicatorFx.transform.localScale = new Vector3(
                Mathf.Max(0.01f, vortexIndicatorScale.x),
                Mathf.Max(0.01f, vortexIndicatorScale.y),
                Mathf.Max(0.01f, vortexIndicatorScale.z)
            );
        }
        // allow movement while vortex is active
        vortexActive = true;
        EndAction();
        int ticks = Mathf.Max(1, vortexTicks);
        float interval = Mathf.Max(0.05f, vortexTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var cols = Physics.OverlapSphere(transform.position, vortexRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(vortexTickDamage);
                // pull towards center
                var rb = c.attachedRigidbody;
                if (rb != null)
                {
                    Vector3 dir = (transform.position - c.transform.position);
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.0001f)
                        rb.AddForce(dir.normalized * vortexPullStrength, ForceMode.Acceleration);
                }
            }
            yield return new WaitForSeconds(interval);
        }
        if (activeFx != null) Destroy(activeFx);
        if (indicatorFx != null) Destroy(indicatorFx);
        vortexActive = false;
        if (vortexStoppageTime > 0f)
        {
            float stopTimer = vortexStoppageTime;
            // Do not rotate or move while stopped
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                // NO FaceTarget or rotation
                yield return null;
            }
        }
    }

    // Flood Crash (cone)
    private bool CanFloodCrash()
    {
        if (isBusy || vortexActive) return false;
        if (Time.time - lastFloodTime < floodCrashCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        if (!IsFacingTarget(target, SpecialFacingAngle)) return false;
        return Vector3.Distance(transform.position, target.position) <= floodCrashRange + 0.5f;
    }

    private void StartFloodCrash()
    {
        lastFloodTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoFloodCrash());
    }

    private IEnumerator CoFloodCrash()
    {
        BeginAction(AIState.Special2);
        if (animator != null && HasTrigger(floodTrigger)) animator.SetTrigger(floodTrigger);
        if (audioSource != null && floodWindupSFX != null) audioSource.PlayOneShot(floodWindupSFX);
        GameObject windFx = null;
        if (floodWindupVFX != null)
        {
            windFx = Instantiate(floodWindupVFX, transform);
            windFx.transform.localPosition = floodVFXOffset;
            if (floodVFXScale > 0f) windFx.transform.localScale = Vector3.one * floodVFXScale;
        }
        // Flood indicator during windup
        GameObject floodIndicator = null;
        if (floodIndicatorPrefab != null)
        {
            floodIndicator = Instantiate(floodIndicatorPrefab, transform);
            floodIndicator.transform.localPosition = floodIndicatorOffset;
            if (floodIndicatorRotate90X) floodIndicator.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            floodIndicator.transform.localScale = new Vector3(0.01f, 0.01f, 0.01f);
        }
        Vector3 floodTargetScale = new Vector3(
            Mathf.Max(0.01f, floodIndicatorScale.x),
            Mathf.Max(0.01f, floodIndicatorScale.y),
            Mathf.Max(0.01f, floodIndicatorScale.z)
        );
        float windTime = Mathf.Max(0f, floodCrashWindup);
        float floodIndicatorGrowTime = Mathf.Max(0.01f, floodCrashWindup * 0.9f);
        float floodIndicatorTimer = 0f;
        while (windTime > 0f)
        {
            windTime -= Time.deltaTime;
            if (controller != null && controller.enabled) controller.SimpleMove(Vector3.zero);
            var t = blackboard.Get<Transform>("target");
            if (t != null)
            {
                Vector3 look = new Vector3(t.position.x, transform.position.y, t.position.z);
                Vector3 dir = (look - transform.position);
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion r = Quaternion.LookRotation(dir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, r, RotationSpeed * Time.deltaTime);
                }
            }
            if (floodIndicator != null && floodIndicatorTimer < floodIndicatorGrowTime)
            {
                floodIndicatorTimer += Time.deltaTime;
                float t01 = Mathf.Clamp01(floodIndicatorTimer / floodIndicatorGrowTime);
                Vector3 s = Vector3.Lerp(new Vector3(0.01f,0.01f,0.01f), floodTargetScale, t01);
                floodIndicator.transform.localScale = s;
            }
            yield return null;
        }
        if (windFx != null) Destroy(windFx);
        if (floodImpactVFX != null)
        {
            var fx = Instantiate(floodImpactVFX, transform);
            fx.transform.localPosition = floodVFXOffset;
            if (floodVFXScale > 0f) fx.transform.localScale = Vector3.one * floodVFXScale;
        }
        if (audioSource != null && floodImpactSFX != null) audioSource.PlayOneShot(floodImpactSFX);
        if (floodIndicator != null) Destroy(floodIndicator);

        // cone damage
        var all = Physics.OverlapSphere(transform.position, floodCrashRange, LayerMask.GetMask("Player"));
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        float halfAngle = Mathf.Clamp(floodCrashConeAngle * 0.5f, 0f, 90f);
        foreach (var c in all)
        {
            Vector3 to = c.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;
            float angle = Vector3.Angle(fwd, to.normalized);
            if (angle <= halfAngle)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(floodCrashDamage);
            }
        }
        EndAction();
        if (floodProjectilePrefab != null && projectileCount > 0)
        {
            float step = (projectileCount > 1) ? projectileSpreadAngle / (projectileCount - 1) : 0f;
            float startYaw = -projectileSpreadAngle * 0.5f;
            for (int i = 0; i < projectileCount; i++)
            {
                float angle = startYaw + step * i;
                Quaternion rot = transform.rotation * Quaternion.Euler(0f, angle, 0f);
                Vector3 spawnPos = transform.position + rot * floodProjectileSpawnOffset;
                var projObj = Instantiate(floodProjectilePrefab, spawnPos, rot);
                var proj = projObj.GetComponent<EnemyProjectile>();
                if (proj != null)
                    proj.Initialize(gameObject, floodCrashDamage, projectileSpeed, projectileLifetime);
            }
        }
        if (floodStoppageTime > 0f)
        {
            float stopTimer = floodStoppageTime;
            while (stopTimer > 0f)
            {
                stopTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                // NO FaceTarget or rotation
                yield return null;
            }
        }
    }

    // Keep distance from target; if too close, back off or strafe
    private NodeState MaintainSpacing()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || controller == null || enemyData == null) return NodeState.Success;
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < Mathf.Max(0.1f, PreferredDistance))
        {
            if (attackLockTimer > 0f || isBusy) return NodeState.Running;
            Vector3 dir = -to.normalized;
            float speed = GetMoveSpeed() * Mathf.Clamp(BackoffSpeedMultiplier, 0.1f, 2f);
            controller.SimpleMove(dir * speed);
            // face target while backing off
            Vector3 lookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
            Vector3 dirToLook = (lookTarget - transform.position);
            if (dirToLook.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dirToLook);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
            }
            aiState = AIState.Chase;
            return NodeState.Running;
        }
        return NodeState.Success;
    }

    private bool IsFacingTarget(Transform target, float maxAngle)
    {
        Vector3 to = target.position - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return false;
        float angle = Vector3.Angle(new Vector3(transform.forward.x, 0f, transform.forward.z).normalized, to.normalized);
        return angle <= Mathf.Clamp(maxAngle, 1f, 60f);
    }

    private bool TargetInAttackRangeFacing()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        if (dist > enemyData.attackRange + 0.5f) return false;
        return IsFacingTarget(target, SpecialFacingAngle);
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
        // Pause movement when not facing
        if (controller != null && controller.enabled)
            controller.SimpleMove(Vector3.zero);
    }
}