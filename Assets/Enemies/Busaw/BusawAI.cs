using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BusawAI : BaseEnemyAI
{
    [Header("Corpse Feast (self-heal)")]
    public int feastHealPerTick = 6;
    public float feastTickInterval = 0.5f;
    public int feastTicks = 6; // 3.0s
    public float feastCooldown = 12f;
    public GameObject feastWindupVFX;
    public GameObject feastActiveVFX;
    public Vector3 feastVFXOffset = Vector3.zero;
    public float feastVFXScale = 1.0f;
    public AudioClip feastWindupSFX;
    public AudioClip feastActiveSFX;
    public string feastTrigger = "Feast";

    [Header("Graveyard Grasp (AOE)")]
    public int graspDamage = 15;
    public float graspRadius = 4.0f;
    public float graspWindup = 0.6f;
    public float graspCooldown = 9f;
    public GameObject graspWindupVFX;
    public GameObject graspImpactVFX;
    public Vector3 graspVFXOffset = Vector3.zero;
    public float graspVFXScale = 1.0f;
    public AudioClip graspWindupSFX;
    public AudioClip graspImpactSFX;
    public string graspTrigger = "Grasp";

    [Header("Skill Selection Tuning")]
    public float feastPreferredMinDistance = 0f;
    public float feastPreferredMaxDistance = 100f;
    [Range(0f, 1f)] public float feastSkillWeight = 0.75f;
    [SerializeField] private float feastStoppageTime = 1f;
    public float graspPreferredMinDistance = 2.5f;
    public float graspPreferredMaxDistance = 7.5f;
    [Range(0f, 1f)] public float graspSkillWeight = 0.85f;
    [SerializeField] private float graspStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 3.5f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastFeastTime = -9999f;
    private float lastGraspTime = -9999f;
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
        var canFeast = new ConditionNode(blackboard, CanFeast, "can_feast");
        var doFeast = new ActionNode(blackboard, () => { StartFeast(); return NodeState.Success; }, "feast");
        var canGrasp = new ConditionNode(blackboard, CanGrasp, "can_grasp");
        var doGrasp = new ActionNode(blackboard, () => { StartGrasp(); return NodeState.Success; }, "grasp");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "feast_seq").Add(canFeast, doFeast),
                        new Sequence(blackboard, "grasp_seq").Add(canGrasp, doGrasp),
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
        bool inFeastRange = dist >= feastPreferredMinDistance && dist <= feastPreferredMaxDistance;
        bool inGraspRange = dist >= graspPreferredMinDistance && dist <= graspPreferredMaxDistance;
        float feastMid = (feastPreferredMinDistance + feastPreferredMaxDistance) * 0.5f;
        float graspMid = (graspPreferredMinDistance + graspPreferredMaxDistance) * 0.5f;
        float feastScore = (inFeastRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(feastMid - dist) / 50f)) * feastSkillWeight : 0f;
        float graspScore = (inGraspRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(graspMid - dist) / 5f)) * graspSkillWeight : 0f;
        if (CanFeast() && feastScore >= graspScore && feastScore > 0.15f) { StartFeast(); return true; }
        if (CanGrasp() && graspScore > feastScore && graspScore > 0.15f) { StartGrasp(); return true; }
        return false;
    }

    private bool CanFeast()
    {
        if (Time.time - lastFeastTime < feastCooldown) return false;
        // prefer to use when not at full health
        return currentHealth < MaxHealth;
    }

    private void StartFeast()
    {
        lastFeastTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoFeast());
    }

    private IEnumerator CoFeast()
    {
        if (animator != null && HasTrigger(feastTrigger)) animator.SetTrigger(feastTrigger);
        if (audioSource != null && feastWindupSFX != null) audioSource.PlayOneShot(feastWindupSFX);
        GameObject wind = null;
        if (feastWindupVFX != null)
        {
            wind = Instantiate(feastWindupVFX, transform);
            wind.transform.localPosition = feastVFXOffset;
            if (feastVFXScale > 0f) wind.transform.localScale = Vector3.one * feastVFXScale;
        }
        yield return new WaitForSeconds(0.25f);
        if (wind != null) Destroy(wind);
        GameObject active = null;
        if (feastActiveVFX != null)
        {
            active = Instantiate(feastActiveVFX, transform);
            active.transform.localPosition = feastVFXOffset;
            if (feastVFXScale > 0f) active.transform.localScale = Vector3.one * feastVFXScale;
        }
        if (audioSource != null && feastActiveSFX != null) audioSource.PlayOneShot(feastActiveSFX);

        int ticks = Mathf.Max(1, feastTicks);
        float interval = Mathf.Max(0.1f, feastTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            currentHealth = Mathf.Min(MaxHealth, currentHealth + feastHealPerTick);
            OnEnemyTookDamage?.Invoke(this, -feastHealPerTick);
            yield return new WaitForSeconds(interval);
        }
        if (active != null) Destroy(active);
    }

    private bool CanGrasp()
    {
        if (Time.time - lastGraspTime < graspCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= graspRadius + 1f;
    }

    private void StartGrasp()
    {
        lastGraspTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoGrasp());
    }

    private IEnumerator CoGrasp()
    {
        if (animator != null && HasTrigger(graspTrigger)) animator.SetTrigger(graspTrigger);
        if (audioSource != null && graspWindupSFX != null) audioSource.PlayOneShot(graspWindupSFX);
        GameObject wind = null;
        if (graspWindupVFX != null)
        {
            wind = Instantiate(graspWindupVFX, transform);
            wind.transform.localPosition = graspVFXOffset;
            if (graspVFXScale > 0f) wind.transform.localScale = Vector3.one * graspVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, graspWindup));
        if (wind != null) Destroy(wind);
        if (graspImpactVFX != null)
        {
            var fx = Instantiate(graspImpactVFX, transform);
            fx.transform.localPosition = graspVFXOffset;
            if (graspVFXScale > 0f) fx.transform.localScale = Vector3.one * graspVFXScale;
        }
        if (audioSource != null && graspImpactSFX != null) audioSource.PlayOneShot(graspImpactSFX);

        var cols = Physics.OverlapSphere(transform.position, graspRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(graspDamage);
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