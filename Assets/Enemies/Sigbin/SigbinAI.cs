using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class SigbinAI : BaseEnemyAI
{
    [Header("Backstep Slash")]
    public int backstepDamage = 28;
    public float backstepEffectiveRange = 1.4f;
    public float backstepWindup = 0.35f;
    public float backstepCooldown = 7f;
    public float backstepDistance = 2.0f;
    public float backstepDuration = 0.15f;
    public GameObject backstepVFX;
    public GameObject backstepImpactVFX;
    public Vector3 backstepVFXOffset = Vector3.zero;
    public float backstepVFXScale = 1.0f;
    public AudioClip backstepWindupSFX;
    public AudioClip backstepImpactSFX;
    public string backstepTrigger = "Backstep";

    [Header("Drain Essence (PBAOE DoT)")]
    public int drainTickDamage = 5; // every 0.5s for 2.0s (4 ticks)
    public float drainTickInterval = 0.5f;
    public int drainTicks = 4;
    public float drainRadius = 2.0f;
    public float drainCooldown = 10f;
    public GameObject drainWindupVFX;
    public GameObject drainActiveVFX;
    public Vector3 drainVFXOffset = Vector3.zero;
    public float drainVFXScale = 1.0f;
    public AudioClip drainWindupSFX;
    public AudioClip drainActiveSFX;
    public string drainTrigger = "Drain";

    [Header("Skill Selection Tuning")]
    public float backstepPreferredMinDistance = 1f;
    public float backstepPreferredMaxDistance = 2.5f;
    [Range(0f, 1f)] public float backstepSkillWeight = 0.7f;
    [SerializeField] private float backstepStoppageTime = 1f;
    public float drainPreferredMinDistance = 1.5f;
    public float drainPreferredMaxDistance = 3.8f;
    [Range(0f, 1f)] public float drainSkillWeight = 0.9f;
    [SerializeField] private float drainStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 1.7f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastBackstepTime = -9999f;
    private float lastDrainTime = -9999f;
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
        var canBackstep = new ConditionNode(blackboard, CanBackstep, "can_backstep");
        var doBackstep = new ActionNode(blackboard, () => { StartBackstep(); return NodeState.Success; }, "backstep");
        var canDrain = new ConditionNode(blackboard, CanDrain, "can_drain");
        var doDrain = new ActionNode(blackboard, () => { StartDrain(); return NodeState.Success; }, "drain");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "backstep_seq").Add(canBackstep, doBackstep),
                        new Sequence(blackboard, "drain_seq").Add(canDrain, doDrain),
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
        bool inBackstepRange = dist >= backstepPreferredMinDistance && dist <= backstepPreferredMaxDistance;
        bool inDrainRange = dist >= drainPreferredMinDistance && dist <= drainPreferredMaxDistance;
        float backstepMid = (backstepPreferredMinDistance + backstepPreferredMaxDistance) * 0.5f;
        float drainMid = (drainPreferredMinDistance + drainPreferredMaxDistance) * 0.5f;
        float backstepScore = (inBackstepRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(backstepMid - dist) / 2f)) * backstepSkillWeight : 0f;
        float drainScore = (inDrainRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(drainMid - dist) / 3f)) * drainSkillWeight : 0f;
        if (CanBackstep() && backstepScore >= drainScore && backstepScore > 0.15f) { StartBackstep(); return true; }
        if (CanDrain() && drainScore > backstepScore && drainScore > 0.15f) { StartDrain(); return true; }
        return false;
    }

    private bool CanBackstep()
    {
        if (Time.time - lastBackstepTime < backstepCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartBackstep()
    {
        lastBackstepTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoBackstep());
    }

    private IEnumerator CoBackstep()
    {
        if (animator != null && HasTrigger(backstepTrigger)) animator.SetTrigger(backstepTrigger);
        if (audioSource != null && backstepWindupSFX != null) audioSource.PlayOneShot(backstepWindupSFX);
        GameObject wind = null;
        if (backstepVFX != null)
        {
            wind = Instantiate(backstepVFX, transform);
            wind.transform.localPosition = backstepVFXOffset;
            if (backstepVFXScale > 0f) wind.transform.localScale = Vector3.one * backstepVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, backstepWindup));
        if (wind != null) Destroy(wind);

        // brief backstep (backwards) then slash hit check
        float t = Mathf.Max(0f, backstepDuration);
        Vector3 backward = -transform.forward;
        while (t > 0f)
        {
            t -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(backward * (backstepDistance / Mathf.Max(0.01f, backstepDuration)) * Time.deltaTime);
            yield return null;
        }

        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist <= backstepEffectiveRange)
            {
                if (audioSource != null && backstepImpactSFX != null) audioSource.PlayOneShot(backstepImpactSFX);
                if (backstepImpactVFX != null)
                {
                    var fx = Instantiate(backstepImpactVFX, transform);
                    fx.transform.localPosition = backstepVFXOffset;
                    if (backstepVFXScale > 0f) fx.transform.localScale = Vector3.one * backstepVFXScale;
                }
                var ps = target.GetComponent<PlayerStats>();
                if (ps != null) ps.TakeDamage(backstepDamage);
            }
        }
    }

    private bool CanDrain()
    {
        if (Time.time - lastDrainTime < drainCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= drainRadius + 1f;
    }

    private void StartDrain()
    {
        lastDrainTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoDrain());
    }

    private IEnumerator CoDrain()
    {
        if (animator != null && HasTrigger(drainTrigger)) animator.SetTrigger(drainTrigger);
        if (audioSource != null && drainWindupSFX != null) audioSource.PlayOneShot(drainWindupSFX);
        GameObject wind = null;
        if (drainWindupVFX != null)
        {
            wind = Instantiate(drainWindupVFX, transform);
            wind.transform.localPosition = drainVFXOffset;
            if (drainVFXScale > 0f) wind.transform.localScale = Vector3.one * drainVFXScale;
        }
        // small windup before active DoT
        yield return new WaitForSeconds(0.2f);
        if (wind != null) Destroy(wind);
        if (drainActiveVFX != null)
        {
            var fx = Instantiate(drainActiveVFX, transform);
            fx.transform.localPosition = drainVFXOffset;
            if (drainVFXScale > 0f) fx.transform.localScale = Vector3.one * drainVFXScale;
        }
        if (audioSource != null && drainActiveSFX != null) audioSource.PlayOneShot(drainActiveSFX);

        int ticks = Mathf.Max(1, drainTicks);
        float interval = Mathf.Max(0.1f, drainTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var cols = Physics.OverlapSphere(transform.position, drainRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(drainTickDamage);
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