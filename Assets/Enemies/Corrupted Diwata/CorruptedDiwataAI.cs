using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class CorruptedDiwataAI : BaseEnemyAI
{
    [Header("Sorrow Bloom (petal burst)")]
    public int bloomDamage = 20;
    public float bloomRadius = 4.0f;
    public float bloomWindup = 0.5f;
    public float bloomCooldown = 8f;
    public string bloomTrigger = "Bloom";

    [Header("Wailing Pulse (AOE sound wave)")]
    public int pulseTickDamage = 4; // every 0.5s for 3.0s => 24 total
    public float pulseTickInterval = 0.5f;
    public int pulseTicks = 6;
    public float pulseRadius = 5.0f;
    public float pulseWindup = 0.6f;
    public float pulseCooldown = 10f;
    public string pulseTrigger = "Pulse";

    [Header("Skill Selection Tuning")]
    public float bloomPreferredMinDistance = 2f;
    public float bloomPreferredMaxDistance = 6f;
    [Range(0f, 1f)] public float bloomSkillWeight = 0.75f;
    [SerializeField] private float bloomStoppageTime = 1f;
    public float pulsePreferredMinDistance = 3f;
    public float pulsePreferredMaxDistance = 8f;
    [Range(0f, 1f)] public float pulseSkillWeight = 0.85f;
    [SerializeField] private float pulseStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 3.2f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastBloomTime = -9999f;
    private float lastPulseTime = -9999f;
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
        var canBloom = new ConditionNode(blackboard, CanBloom, "can_bloom");
        var doBloom = new ActionNode(blackboard, () => { StartBloom(); return NodeState.Success; }, "bloom");
        var canPulse = new ConditionNode(blackboard, CanPulse, "can_pulse");
        var doPulse = new ActionNode(blackboard, () => { StartPulse(); return NodeState.Success; }, "pulse");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "bloom_seq").Add(canBloom, doBloom),
                        new Sequence(blackboard, "pulse_seq").Add(canPulse, doPulse),
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
        bool inBloomRange = dist >= bloomPreferredMinDistance && dist <= bloomPreferredMaxDistance;
        bool inPulseRange = dist >= pulsePreferredMinDistance && dist <= pulsePreferredMaxDistance;
        float bloomMid = (bloomPreferredMinDistance + bloomPreferredMaxDistance) * 0.5f;
        float pulseMid = (pulsePreferredMinDistance + pulsePreferredMaxDistance) * 0.5f;
        float bloomScore = (inBloomRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(bloomMid - dist) / 6f)) * bloomSkillWeight : 0f;
        float pulseScore = (inPulseRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(pulseMid - dist) / 8f)) * pulseSkillWeight : 0f;
        if (CanBloom() && bloomScore >= pulseScore && bloomScore > 0.15f) { StartBloom(); return true; }
        if (CanPulse() && pulseScore > bloomScore && pulseScore > 0.15f) { StartPulse(); return true; }
        return false;
    }

    private bool CanBloom()
    {
        if (Time.time - lastBloomTime < bloomCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= bloomRadius + 1f;
    }
    private void StartBloom()
    {
        lastBloomTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoBloom());
    }
    private IEnumerator CoBloom()
    {
        if (animator != null && HasTrigger(bloomTrigger)) animator.SetTrigger(bloomTrigger);
        yield return new WaitForSeconds(Mathf.Max(0f, bloomWindup));
        var cols = Physics.OverlapSphere(transform.position, bloomRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(bloomDamage);
        }
    }

    private bool CanPulse()
    {
        if (Time.time - lastPulseTime < pulseCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= pulseRadius + 1f;
    }
    private void StartPulse()
    {
        lastPulseTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoPulse());
    }
    private IEnumerator CoPulse()
    {
        if (animator != null && HasTrigger(pulseTrigger)) animator.SetTrigger(pulseTrigger);
        yield return new WaitForSeconds(Mathf.Max(0f, pulseWindup));
        int ticks = Mathf.Max(1, pulseTicks);
        float interval = Mathf.Max(0.1f, pulseTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var cols = Physics.OverlapSphere(transform.position, pulseRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(pulseTickDamage);
            }
            yield return new WaitForSeconds(interval);
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