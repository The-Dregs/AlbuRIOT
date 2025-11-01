using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class DiwataPunsoAI : BaseEnemyAI
{
    [Header("Roots (snare hit)")]
    public int rootsDamage = 10;
    public float rootsRange = 7f;
    public float rootsWindup = 0.35f;
    public float rootsCooldown = 7f;
    public GameObject rootsWindupVFX;
    public GameObject rootsImpactVFX;
    public Vector3 rootsVFXOffset = Vector3.zero;
    public float rootsVFXScale = 1.0f;
    public AudioClip rootsWindupSFX;
    public AudioClip rootsImpactSFX;
    public string rootsTrigger = "Roots";

    [Header("Nature Bolt (projectile)")]
    public EnemyProjectile boltProjectilePrefab;
    public Transform boltMuzzle;
    public int boltDamage = 15;
    public float boltSpeed = 14f;
    public float boltLifetime = 1.2f;
    public float boltCooldown = 4f;
    public float boltRange = 20f;
    public GameObject boltWindupVFX;
    public Vector3 boltVFXOffset = Vector3.zero;
    public float boltVFXScale = 1.0f;
    public AudioClip boltWindupSFX;
    public AudioClip boltFireSFX;
    public string boltTrigger = "Bolt";

    [Header("Skill Selection Tuning")]
    public float rootsPreferredMinDistance = 3.2f;
    public float rootsPreferredMaxDistance = 8.5f;
    [Range(0f, 1f)] public float rootsSkillWeight = 0.7f;
    [SerializeField] private float rootsStoppageTime = 1f;
    public float boltPreferredMinDistance = 7f;
    public float boltPreferredMaxDistance = 20f;
    [Range(0f, 1f)] public float boltSkillWeight = 0.8f;
    [SerializeField] private float boltStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 5.0f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastRootsTime = -9999f;
    private float lastBoltTime = -9999f;
    private AudioSource audioSource;

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
        var canRoots = new ConditionNode(blackboard, CanRoots, "can_roots");
        var doRoots = new ActionNode(blackboard, () => { StartRoots(); return NodeState.Success; }, "roots");
        var canBolt = new ConditionNode(blackboard, CanBolt, "can_bolt");
        var doBolt = new ActionNode(blackboard, () => { StartBolt(); return NodeState.Success; }, "bolt");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "roots_seq").Add(canRoots, doRoots),
                        new Sequence(blackboard, "bolt_seq").Add(canBolt, doBolt),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
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
        bool facingTarget = IsFacingTarget(target, specialFacingAngle);
        bool inRootsRange = dist >= rootsPreferredMinDistance && dist <= rootsPreferredMaxDistance;
        bool inBoltRange = dist >= boltPreferredMinDistance && dist <= boltPreferredMaxDistance;
        float rootsMid = (rootsPreferredMinDistance + rootsPreferredMaxDistance) * 0.5f;
        float boltMid = (boltPreferredMinDistance + boltPreferredMaxDistance) * 0.5f;
        float rootsScore = (inRootsRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(rootsMid - dist) / 5f)) * rootsSkillWeight : 0f;
        float boltScore = (inBoltRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(boltMid - dist) / 6f)) * boltSkillWeight : 0f;
        if (CanRoots() && rootsScore >= boltScore && rootsScore > 0.15f) { StartRoots(); return true; }
        if (CanBolt() && boltScore > rootsScore && boltScore > 0.15f) { StartBolt(); return true; }
        return false;
    }

    private bool CanRoots()
    {
        if (Time.time - lastRootsTime < rootsCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= rootsRange + 0.5f;
    }

    private void StartRoots()
    {
        lastRootsTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoRoots());
    }

    private IEnumerator CoRoots()
    {
        if (animator != null && HasTrigger(rootsTrigger)) animator.SetTrigger(rootsTrigger);
        if (audioSource != null && rootsWindupSFX != null) audioSource.PlayOneShot(rootsWindupSFX);
        GameObject wind = null;
        if (rootsWindupVFX != null)
        {
            wind = Instantiate(rootsWindupVFX, transform);
            wind.transform.localPosition = rootsVFXOffset;
            if (rootsVFXScale > 0f) wind.transform.localScale = Vector3.one * rootsVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, rootsWindup));
        if (wind != null) Destroy(wind);
        if (rootsImpactVFX != null)
        {
            var fx = Instantiate(rootsImpactVFX, transform);
            fx.transform.localPosition = rootsVFXOffset;
            if (rootsVFXScale > 0f) fx.transform.localScale = Vector3.one * rootsVFXScale;
        }
        if (audioSource != null && rootsImpactSFX != null) audioSource.PlayOneShot(rootsImpactSFX);

        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            // simple LoS check
            Vector3 origin = transform.position + Vector3.up * 0.5f;
            Vector3 dir = (target.position + Vector3.up * 0.5f) - origin;
            if (Physics.Raycast(origin, dir.normalized, out var hit, rootsRange))
            {
                var ps = hit.collider.GetComponentInParent<PlayerStats>();
                if (ps != null)
                {
                    ps.TakeDamage(rootsDamage);
                    // snare effect can be handled by status system if available
                }
            }
        }
    }

    private bool CanBolt()
    {
        if (Time.time - lastBoltTime < boltCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartBolt()
    {
        lastBoltTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoBolt());
    }

    private IEnumerator CoBolt()
    {
        if (animator != null && HasTrigger(boltTrigger)) animator.SetTrigger(boltTrigger);
        if (audioSource != null && boltWindupSFX != null) audioSource.PlayOneShot(boltWindupSFX);
        GameObject wind = null;
        if (boltWindupVFX != null)
        {
            wind = Instantiate(boltWindupVFX, transform);
            wind.transform.localPosition = boltVFXOffset;
            if (boltVFXScale > 0f) wind.transform.localScale = Vector3.one * boltVFXScale;
        }
        yield return new WaitForSeconds(0.1f);
        if (wind != null) Destroy(wind);
        if (audioSource != null && boltFireSFX != null) audioSource.PlayOneShot(boltFireSFX);

        var target = blackboard.Get<Transform>("target");
        Vector3 muzzlePos = boltMuzzle != null ? boltMuzzle.position : (transform.position + transform.forward * 1.0f);
        Vector3 dir = target != null ? (target.position - muzzlePos) : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize(); else dir = transform.forward;

        if (boltProjectilePrefab != null)
        {
            var proj = Instantiate(boltProjectilePrefab, muzzlePos, Quaternion.LookRotation(dir));
            proj.Initialize(gameObject, boltDamage, boltSpeed, boltLifetime, null);
            proj.maxDistance = boltRange;
        }
        else
        {
            // fallback instant line hit
            if (Physics.Raycast(muzzlePos, dir, out var rh, boltRange, ~0))
            {
                var ps = rh.collider.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(boltDamage);
            }
        }
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
}