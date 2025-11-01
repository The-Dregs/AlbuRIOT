using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class MinokawaAI : BaseEnemyAI
{
    [Header("Solar Wrath (line cleave)")]
    public int solarDamage = 45;
    public float solarRange = 12f;
    public float solarWidth = 2.0f;
    public float solarWindup = 0.8f;
    public float solarCooldown = 12f;
    public string solarTrigger = "SolarWrath";

    [Header("Wings of Judgment (AOE)")]
    public int wingsDamage = 14;
    public float wingsRadius = 4.0f;
    public float wingsWindup = 0.6f;
    public float wingsCooldown = 10f;
    public string wingsTrigger = "Wings";

    [Header("Skill Selection Tuning")]
    public float solarPreferredMinDistance = 4f;
    public float solarPreferredMaxDistance = 13f;
    [Range(0f, 1f)] public float solarSkillWeight = 0.7f;
    [SerializeField] private float solarStoppageTime = 1f;
    public float wingsPreferredMinDistance = 2f;
    public float wingsPreferredMaxDistance = 7f;
    [Range(0f, 1f)] public float wingsSkillWeight = 0.85f;
    [SerializeField] private float wingsStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 3.8f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastSolarTime = -9999f;
    private float lastWingsTime = -9999f;

    protected override void InitializeEnemy()
    {
        // No special initialization required for now
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
        var canSolar = new ConditionNode(blackboard, CanSolar, "can_solar");
        var doSolar = new ActionNode(blackboard, () => { StartSolar(); return NodeState.Success; }, "solar");
        var canWings = new ConditionNode(blackboard, CanWings, "can_wings");
        var doWings = new ActionNode(blackboard, () => { StartWings(); return NodeState.Success; }, "wings");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "solar_seq").Add(canSolar, doSolar),
                        new Sequence(blackboard, "wings_seq").Add(canWings, doWings),
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
        bool inSolarRange = dist >= solarPreferredMinDistance && dist <= solarPreferredMaxDistance;
        bool inWingsRange = dist >= wingsPreferredMinDistance && dist <= wingsPreferredMaxDistance;
        float solarMid = (solarPreferredMinDistance + solarPreferredMaxDistance) * 0.5f;
        float wingsMid = (wingsPreferredMinDistance + wingsPreferredMaxDistance) * 0.5f;
        float solarScore = (inSolarRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(solarMid - dist) / 10f)) * solarSkillWeight : 0f;
        float wingsScore = (inWingsRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(wingsMid - dist) / 6f)) * wingsSkillWeight : 0f;
        if (CanSolar() && solarScore >= wingsScore && solarScore > 0.15f) { StartSolar(); return true; }
        if (CanWings() && wingsScore > solarScore && wingsScore > 0.15f) { StartWings(); return true; }
        return false;
    }

    private bool CanSolar()
    {
        if (Time.time - lastSolarTime < solarCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }
    private void StartSolar()
    {
        lastSolarTime = Time.time;
        StartCoroutine(CoSolar());
    }
    private IEnumerator CoSolar()
    {
        if (animator != null && HasTrigger(solarTrigger)) animator.SetTrigger(solarTrigger);
        yield return new WaitForSeconds(Mathf.Max(0f, solarWindup));
        // line cleave: sample points along forward
        Vector3 start = transform.position;
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        int samples = 8;
        for (int i = 1; i <= samples; i++)
        {
            Vector3 p = start + fwd * (solarRange * i / samples);
            var cols = Physics.OverlapSphere(p, solarWidth * 0.5f, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(solarDamage);
            }
        }
        yield return null;
    }

    private bool CanWings()
    {
        if (Time.time - lastWingsTime < wingsCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= wingsRadius + 1.5f;
    }
    private void StartWings()
    {
        lastWingsTime = Time.time;
        StartCoroutine(CoWings());
    }
    private IEnumerator CoWings()
    {
        if (animator != null && HasTrigger(wingsTrigger)) animator.SetTrigger(wingsTrigger);
        yield return new WaitForSeconds(Mathf.Max(0f, wingsWindup));
        var cols = Physics.OverlapSphere(transform.position, wingsRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(wingsDamage);
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