using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class SarimanokAI : BaseEnemyAI
{
    [Header("Despair Cry (DoT)")]
    public int cryTickDamage = 4;
    public float cryTickInterval = 0.5f;
    public int cryTicks = 6;
    public float cryRadius = 5f;
    public float cryWindup = 0.6f;
    public float cryCooldown = 10f;
    public string cryTrigger = "Cry";

    [Header("Blazing Feathers (rays)")]
    public EnemyProjectile featherProjectilePrefab;
    public Transform featherMuzzle;
    public int featherDamage = 10;
    public int featherCount = 5;
    public float featherSpreadDeg = 12f;
    public float featherRange = 10f;
    public float featherWindup = 0.5f;
    public float featherCooldown = 9f;
    public float featherSpeed = 18f;
    public string featherTrigger = "Feathers";

    [Header("Skill Selection Tuning")]
    public float cryPreferredMinDistance = 2.5f;
    public float cryPreferredMaxDistance = 7.5f;
    [Range(0f, 1f)] public float crySkillWeight = 0.8f;
    [SerializeField] private float cryStoppageTime = 1f;
    public float feathersPreferredMinDistance = 4f;
    public float feathersPreferredMaxDistance = 15f;
    [Range(0f, 1f)] public float feathersSkillWeight = 0.7f;
    [SerializeField] private float feathersStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 3.0f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastCryTime = -9999f;
    private float lastFeatherTime = -9999f;

    protected override void InitializeEnemy()
    {
        // No special initialization for now
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

    protected override void BuildBehaviorTree()
    {
        var updateTarget = new ActionNode(blackboard, UpdateTarget, "update_target");
        var hasTarget = new ConditionNode(blackboard, HasTarget, "has_target");
        var targetInDetection = new ConditionNode(blackboard, TargetInDetectionRange, "in_detect_range");
        var moveToTarget = new ActionNode(blackboard, MoveTowardsTarget, "move_to_target");
        var targetInAttack = new ConditionNode(blackboard, TargetInAttackRange, "in_attack_range");
        var basicAttack = new ActionNode(blackboard, () => { PerformBasicAttack(); return NodeState.Success; }, "basic");
        var canCry = new ConditionNode(blackboard, CanCry, "can_cry");
        var doCry = new ActionNode(blackboard, () => { StartCry(); return NodeState.Success; }, "cry");
        var canFeathers = new ConditionNode(blackboard, CanFeathers, "can_feathers");
        var doFeathers = new ActionNode(blackboard, () => { StartFeathers(); return NodeState.Success; }, "feathers");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "cry_seq").Add(canCry, doCry),
                        new Sequence(blackboard, "feathers_seq").Add(canFeathers, doFeathers),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    protected override bool TrySpecialAbilities()
    {
        if (isBusy) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float dist = Vector3.Distance(transform.position, target.position);
        bool facingTarget = IsFacingTarget(target, specialFacingAngle);
        bool inCryRange = dist >= cryPreferredMinDistance && dist <= cryPreferredMaxDistance;
        bool inFeathersRange = dist >= feathersPreferredMinDistance && dist <= feathersPreferredMaxDistance;
        float cryMid = (cryPreferredMinDistance + cryPreferredMaxDistance) * 0.5f;
        float feathersMid = (feathersPreferredMinDistance + feathersPreferredMaxDistance) * 0.5f;
        float cryScore = (inCryRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(cryMid - dist) / 5f)) * crySkillWeight : 0f;
        float featherScore = (inFeathersRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(feathersMid - dist) / 10f)) * feathersSkillWeight : 0f;
        if (CanCry() && cryScore >= featherScore && cryScore > 0.15f) { StartCry(); return true; }
        if (CanFeathers() && featherScore > cryScore && featherScore > 0.15f) { StartFeathers(); return true; }
        return false;
    }

    private bool CanCry()
    {
        if (Time.time - lastCryTime < cryCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= cryRadius + 1f;
    }
    private void StartCry()
    {
        lastCryTime = Time.time;
        StartCoroutine(CoCry());
    }
    private IEnumerator CoCry()
    {
        if (animator != null && HasTrigger(cryTrigger)) animator.SetTrigger(cryTrigger);
        yield return new WaitForSeconds(Mathf.Max(0f, cryWindup));
        int ticks = Mathf.Max(1, cryTicks);
        float interval = Mathf.Max(0.1f, cryTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var cols = Physics.OverlapSphere(transform.position, cryRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(cryTickDamage);
            }
            yield return new WaitForSeconds(interval);
        }
    }

    private bool CanFeathers()
    {
        if (Time.time - lastFeatherTime < featherCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }
    private void StartFeathers()
    {
        lastFeatherTime = Time.time;
        StartCoroutine(CoFeathers());
    }
    private IEnumerator CoFeathers()
    {
        if (animator != null && HasTrigger(featherTrigger)) animator.SetTrigger(featherTrigger);
        yield return new WaitForSeconds(Mathf.Max(0f, featherWindup));
        Vector3 muzzlePos = featherMuzzle != null ? featherMuzzle.position : (transform.position + transform.forward * 1.2f);
        for (int i = 0; i < Mathf.Max(1, featherCount); i++)
        {
            float t = (featherCount <= 1) ? 0f : (i / (float)(featherCount - 1));
            float angle = -featherSpreadDeg * 0.5f + (featherSpreadDeg * t);
            Quaternion rot = Quaternion.AngleAxis(angle, Vector3.up) * transform.rotation;
            if (featherProjectilePrefab != null)
            {
                var proj = Instantiate(featherProjectilePrefab, muzzlePos, rot);
                proj.Initialize(gameObject, featherDamage, featherSpeed, featherRange / Mathf.Max(0.1f, featherSpeed), null);
                proj.maxDistance = featherRange;
            }
        }
        yield return null;
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