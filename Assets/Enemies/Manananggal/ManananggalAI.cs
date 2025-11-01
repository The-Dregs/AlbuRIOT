using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

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
    public string diveTrigger = "Dive";

    [Header("Screech of Hunger (AOE pulse)")]
    public int screechDamage = 10;
    public float screechRadius = 4.5f;
    public float screechWindup = 0.5f;
    public float screechCooldown = 10f;
    public GameObject screechWindupVFX;
    public GameObject screechImpactVFX;
    public Vector3 screechVFXOffset = Vector3.zero;
    public float screechVFXScale = 1.0f;
    public AudioClip screechWindupSFX;
    public AudioClip screechImpactSFX;
    public string screechTrigger = "Screech";

    // Skill Selection Tuning
    [Header("Skill Selection Tuning")]
    public float divePreferredMinDistance = 6f;
    public float divePreferredMaxDistance = 12f;
    [Range(0f, 1f)] public float diveSkillWeight = 0.6f;
    public float screechPreferredMinDistance = 2.5f;
    public float screechPreferredMaxDistance = 6f;
    [Range(0f, 1f)] public float screechSkillWeight = 0.8f;
    [SerializeField] private float diveStoppageTime = 1f;
    [SerializeField] private float screechStoppageTime = 1f;

    // Spacing and Facing
    [Header("Spacing")]
    public float preferredDistance = 3.0f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 0.8f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastDiveTime = -9999f;
    private float lastScreechTime = -9999f;
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
        var canDive = new ConditionNode(blackboard, CanDive, "can_dive");
        var doDive = new ActionNode(blackboard, () => { StartDive(); return NodeState.Success; }, "dive");
        var canScreech = new ConditionNode(blackboard, CanScreech, "can_screech");
        var doScreech = new ActionNode(blackboard, () => { StartScreech(); return NodeState.Success; }, "screech");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "dive_seq").Add(canDive, doDive),
                        new Sequence(blackboard, "screech_seq").Add(canScreech, doScreech),
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
        bool inDiveRange = dist >= divePreferredMinDistance && dist <= divePreferredMaxDistance;
        bool inScreechRange = dist >= screechPreferredMinDistance && dist <= screechPreferredMaxDistance;
        float diveMid = (divePreferredMinDistance + divePreferredMaxDistance) * 0.5f;
        float screechMid = (screechPreferredMinDistance + screechPreferredMaxDistance) * 0.5f;
        float diveDistScore = 1f - Mathf.Clamp01(Mathf.Abs(diveMid - dist) / 10f);
        float screechDistScore = 1f - Mathf.Clamp01(Mathf.Abs(screechMid - dist) / 10f);
        float diveScore = (inDiveRange && facingTarget) ? diveDistScore * diveSkillWeight : 0f;
        float screechScore = (inScreechRange && facingTarget) ? screechDistScore * screechSkillWeight : 0f;
        if (CanDive() && diveScore >= screechScore && diveScore > 0.15f) { StartDive(); return true; }
        if (CanScreech() && screechScore > diveScore && screechScore > 0.15f) { StartScreech(); return true; }
        return false;
    }

    private bool CanDive()
    {
        if (Time.time - lastDiveTime < diveCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float d = Vector3.Distance(transform.position, target.position);
        return d <= 12f;
    }

    private void StartDive()
    {
        lastDiveTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoDive());
    }

    private IEnumerator CoDive()
    {
        if (animator != null && HasTrigger(diveTrigger)) animator.SetTrigger(diveTrigger);
        if (audioSource != null && diveWindupSFX != null) audioSource.PlayOneShot(diveWindupSFX);
        GameObject wind = null;
        if (diveWindupVFX != null)
        {
            wind = Instantiate(diveWindupVFX, transform);
            wind.transform.localPosition = diveVFXOffset;
            if (diveVFXScale > 0f) wind.transform.localScale = Vector3.one * diveVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, diveWindup));
        if (wind != null) Destroy(wind);

        float ascend = Mathf.Max(0f, diveAscendTime);
        while (ascend > 0f)
        {
            ascend -= Time.deltaTime;
            yield return null;
        }

        var target = blackboard.Get<Transform>("target");
        Vector3 dir = target != null ? (target.position - transform.position) : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize(); else dir = transform.forward;

        float travel = 0.8f;
        while (travel > 0f)
        {
            travel -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(dir * diveDescendSpeed * Time.deltaTime);
            var hits = Physics.OverlapSphere(transform.position, diveHitRadius, LayerMask.GetMask("Player"));
            foreach (var h in hits)
            {
                var ps = h.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(diveDamage);
            }
            yield return null;
        }

        if (diveImpactVFX != null)
        {
            var fx = Instantiate(diveImpactVFX, transform);
            fx.transform.localPosition = diveVFXOffset;
            if (diveVFXScale > 0f) fx.transform.localScale = Vector3.one * diveVFXScale;
        }
        if (audioSource != null && diveImpactSFX != null) audioSource.PlayOneShot(diveImpactSFX);
    }

    private bool CanScreech()
    {
        if (Time.time - lastScreechTime < screechCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= screechRadius + 1f;
    }

    private void StartScreech()
    {
        lastScreechTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoScreech());
    }

    private IEnumerator CoScreech()
    {
        if (animator != null && HasTrigger(screechTrigger)) animator.SetTrigger(screechTrigger);
        if (audioSource != null && screechWindupSFX != null) audioSource.PlayOneShot(screechWindupSFX);
        GameObject wind = null;
        if (screechWindupVFX != null)
        {
            wind = Instantiate(screechWindupVFX, transform);
            wind.transform.localPosition = screechVFXOffset;
            if (screechVFXScale > 0f) wind.transform.localScale = Vector3.one * screechVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, screechWindup));
        if (wind != null) Destroy(wind);
        if (screechImpactVFX != null)
        {
            var fx = Instantiate(screechImpactVFX, transform);
            fx.transform.localPosition = screechVFXOffset;
            if (screechVFXScale > 0f) fx.transform.localScale = Vector3.one * screechVFXScale;
        }
        if (audioSource != null && screechImpactSFX != null) audioSource.PlayOneShot(screechImpactSFX);

        var cols = Physics.OverlapSphere(transform.position, screechRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(screechDamage);
        }
    }

    // Helpers for facing
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
        // Pause movement when not facing
        if (controller != null && controller.enabled)
            controller.SimpleMove(Vector3.zero);
    }
}