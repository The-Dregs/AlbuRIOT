using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class WakwakAI : BaseEnemyAI
{
    [Header("Silent Descent (dive strike)")]
    public int descentDamage = 40;
    public float descentHitRadius = 1.6f;
    public float descentWindup = 0.5f;
    public float descentCooldown = 8f;
    public float descentSpeed = 18f;
    public GameObject descentWindupVFX;
    public GameObject descentImpactVFX;
    public Vector3 descentVFXOffset = Vector3.zero;
    public float descentVFXScale = 1.0f;
    public AudioClip descentWindupSFX;
    public AudioClip descentImpactSFX;
    public string descentTrigger = "Descent";

    [Header("Echoing Wings (close AOE)")]
    public int wingsDamage = 10;
    public float wingsRadius = 3.0f;
    public float wingsWindup = 0.45f;
    public float wingsCooldown = 9f;
    public GameObject wingsWindupVFX;
    public GameObject wingsImpactVFX;
    public Vector3 wingsVFXOffset = Vector3.zero;
    public float wingsVFXScale = 1.0f;
    public AudioClip wingsWindupSFX;
    public AudioClip wingsImpactSFX;
    public string wingsTrigger = "Wings";

    [Header("Skill Selection Tuning")]
    public float descentPreferredMinDistance = 5f;
    public float descentPreferredMaxDistance = 13f;
    [Range(0f, 1f)] public float descentSkillWeight = 0.8f;
    [SerializeField] private float descentStoppageTime = 1f;
    public float wingsPreferredMinDistance = 2f;
    public float wingsPreferredMaxDistance = 7.5f;
    [Range(0f, 1f)] public float wingsSkillWeight = 0.7f;
    [SerializeField] private float wingsStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 3.2f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastDescentTime = -9999f;
    private float lastWingsTime = -9999f;
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
        var canDescent = new ConditionNode(blackboard, CanDescent, "can_descent");
        var doDescent = new ActionNode(blackboard, () => { StartDescent(); return NodeState.Success; }, "descent");
        var canWings = new ConditionNode(blackboard, CanWings, "can_wings");
        var doWings = new ActionNode(blackboard, () => { StartWings(); return NodeState.Success; }, "wings");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "descent_seq").Add(canDescent, doDescent),
                        new Sequence(blackboard, "wings_seq").Add(canWings, doWings),
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
        bool inDescentRange = dist >= descentPreferredMinDistance && dist <= descentPreferredMaxDistance;
        bool inWingsRange = dist >= wingsPreferredMinDistance && dist <= wingsPreferredMaxDistance;
        float descentMid = (descentPreferredMinDistance + descentPreferredMaxDistance) * 0.5f;
        float wingsMid = (wingsPreferredMinDistance + wingsPreferredMaxDistance) * 0.5f;
        float descentScore = (inDescentRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(descentMid - dist) / 10f)) * descentSkillWeight : 0f;
        float wingsScore = (inWingsRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(wingsMid - dist) / 6f)) * wingsSkillWeight : 0f;
        if (CanDescent() && descentScore >= wingsScore && descentScore > 0.15f) { StartDescent(); return true; }
        if (CanWings() && wingsScore > descentScore && wingsScore > 0.15f) { StartWings(); return true; }
        return false;
    }

    private bool CanDescent()
    {
        if (Time.time - lastDescentTime < descentCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= 12f;
    }

    private void StartDescent()
    {
        lastDescentTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoDescent());
    }

    private IEnumerator CoDescent()
    {
        if (animator != null && HasTrigger(descentTrigger)) animator.SetTrigger(descentTrigger);
        if (audioSource != null && descentWindupSFX != null) audioSource.PlayOneShot(descentWindupSFX);
        GameObject wind = null;
        if (descentWindupVFX != null)
        {
            wind = Instantiate(descentWindupVFX, transform);
            wind.transform.localPosition = descentVFXOffset;
            if (descentVFXScale > 0f) wind.transform.localScale = Vector3.one * descentVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, descentWindup));
        if (wind != null) Destroy(wind);

        var target = blackboard.Get<Transform>("target");
        Vector3 dir = target != null ? (target.position - transform.position) : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize(); else dir = transform.forward;
        float travel = 0.8f;
        while (travel > 0f)
        {
            travel -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(dir * descentSpeed * Time.deltaTime);
            var hits = Physics.OverlapSphere(transform.position, descentHitRadius, LayerMask.GetMask("Player"));
            foreach (var h in hits)
            {
                var ps = h.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(descentDamage);
            }
            yield return null;
        }

        if (descentImpactVFX != null)
        {
            var fx = Instantiate(descentImpactVFX, transform);
            fx.transform.localPosition = descentVFXOffset;
            if (descentVFXScale > 0f) fx.transform.localScale = Vector3.one * descentVFXScale;
        }
        if (audioSource != null && descentImpactSFX != null) audioSource.PlayOneShot(descentImpactSFX);
    }

    private bool CanWings()
    {
        if (Time.time - lastWingsTime < wingsCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= wingsRadius + 1f;
    }

    private void StartWings()
    {
        lastWingsTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoWings());
    }

    private IEnumerator CoWings()
    {
        if (animator != null && HasTrigger(wingsTrigger)) animator.SetTrigger(wingsTrigger);
        if (audioSource != null && wingsWindupSFX != null) audioSource.PlayOneShot(wingsWindupSFX);
        GameObject wind = null;
        if (wingsWindupVFX != null)
        {
            wind = Instantiate(wingsWindupVFX, transform);
            wind.transform.localPosition = wingsVFXOffset;
            if (wingsVFXScale > 0f) wind.transform.localScale = Vector3.one * wingsVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, wingsWindup));
        if (wind != null) Destroy(wind);
        if (wingsImpactVFX != null)
        {
            var fx = Instantiate(wingsImpactVFX, transform);
            fx.transform.localPosition = wingsVFXOffset;
            if (wingsVFXScale > 0f) fx.transform.localScale = Vector3.one * wingsVFXScale;
        }
        if (audioSource != null && wingsImpactSFX != null) audioSource.PlayOneShot(wingsImpactSFX);

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