using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BungisngisAI : BaseEnemyAI
{
    [Header("Belly Laugh (Cone)")]
    public int laughDamage = 15;
    [Range(0f,180f)] public float laughConeAngle = 80f;
    public float laughRange = 7f;
    public float laughWindup = 0.5f;
    public float laughCooldown = 8f;
    public GameObject laughWindupVFX;
    public GameObject laughImpactVFX;
    public Vector3 laughVFXOffset = Vector3.zero;
    public float laughVFXScale = 1.0f;
    public AudioClip laughWindupSFX;
    public AudioClip laughImpactSFX;
    public string laughTrigger = "Laugh";

    [Header("Ground Pound (Strip Shockwave)")]
    public int poundDamage = 22;
    public float poundWidth = 2.0f;
    public float poundRange = 6f;
    public float poundWindup = 0.45f;
    public float poundCooldown = 7f;
    public GameObject poundWindupVFX;
    public GameObject poundImpactVFX;
    public Vector3 poundVFXOffset = Vector3.zero;
    public float poundVFXScale = 1.0f;
    public AudioClip poundWindupSFX;
    public AudioClip poundImpactSFX;
    public string poundTrigger = "Pound";

    [Header("Skill Selection Tuning")]
    public float laughPreferredMinDistance = 3f;
    public float laughPreferredMaxDistance = 8f;
    [Range(0f, 1f)] public float laughSkillWeight = 0.7f;
    [SerializeField] private float laughStoppageTime = 1f;
    public float poundPreferredMinDistance = 5f;
    public float poundPreferredMaxDistance = 12f;
    [Range(0f, 1f)] public float poundSkillWeight = 0.8f;
    [SerializeField] private float poundStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 3.3f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    [Header("Laugh Projectile")]
    public GameObject laughProjectilePrefab;
    public Vector3 laughProjectileSpawnOffset = new Vector3(0f,1.2f,1.8f);
    public float laughProjectileSpeed = 18f;
    public float laughProjectileLifetime = 2.5f;
    [Range(0f, 40f)] public float laughProjectileSpreadAngle = 0f;
    [Range(1,10)] public int laughProjectileCount = 1;

    private float lastLaughTime = -9999f;
    private float lastPoundTime = -9999f;
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
        var canLaugh = new ConditionNode(blackboard, CanLaugh, "can_laugh");
        var doLaugh = new ActionNode(blackboard, () => { StartLaugh(); return NodeState.Success; }, "laugh");
        var canPound = new ConditionNode(blackboard, CanPound, "can_pound");
        var doPound = new ActionNode(blackboard, () => { StartPound(); return NodeState.Success; }, "pound");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "laugh_seq").Add(canLaugh, doLaugh),
                        new Sequence(blackboard, "pound_seq").Add(canPound, doPound),
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
        bool inLaughRange = dist >= laughPreferredMinDistance && dist <= laughPreferredMaxDistance;
        bool inPoundRange = dist >= poundPreferredMinDistance && dist <= poundPreferredMaxDistance;
        float laughMid = (laughPreferredMinDistance + laughPreferredMaxDistance) * 0.5f;
        float poundMid = (poundPreferredMinDistance + poundPreferredMaxDistance) * 0.5f;
        float laughScore = (inLaughRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(laughMid - dist) / 7f)) * laughSkillWeight : 0f;
        float poundScore = (inPoundRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(poundMid - dist) / 7f)) * poundSkillWeight : 0f;
        if (CanLaugh() && laughScore >= poundScore && laughScore > 0.15f) { StartLaugh(); return true; }
        if (CanPound() && poundScore > laughScore && poundScore > 0.15f) { StartPound(); return true; }
        return false;
    }

    private bool CanLaugh()
    {
        if (Time.time - lastLaughTime < laughCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= laughRange + 0.5f;
    }

    private void StartLaugh()
    {
        lastLaughTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoLaugh());
    }

    private IEnumerator CoLaugh()
    {
        if (animator != null && HasTrigger(laughTrigger)) animator.SetTrigger(laughTrigger);
        if (audioSource != null && laughWindupSFX != null) audioSource.PlayOneShot(laughWindupSFX);
        GameObject wind = null;
        if (laughWindupVFX != null)
        {
            wind = Instantiate(laughWindupVFX, transform);
            wind.transform.localPosition = laughVFXOffset;
            if (laughVFXScale > 0f) wind.transform.localScale = Vector3.one * laughVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, laughWindup));
        if (wind != null) Destroy(wind);
        if (laughImpactVFX != null)
        {
            var fx = Instantiate(laughImpactVFX, transform);
            fx.transform.localPosition = laughVFXOffset;
            if (laughVFXScale > 0f) fx.transform.localScale = Vector3.one * laughVFXScale;
        }
        if (audioSource != null && laughImpactSFX != null) audioSource.PlayOneShot(laughImpactSFX);
        // Shoot projectiles forward
        if (laughProjectilePrefab != null && laughProjectileCount > 0)
        {
            float step = (laughProjectileCount > 1) ? laughProjectileSpreadAngle / (laughProjectileCount - 1) : 0f;
            float startYaw = -laughProjectileSpreadAngle * 0.5f;
            for (int i = 0; i < laughProjectileCount; i++)
            {
                float angle = startYaw + step * i;
                Quaternion rot = transform.rotation * Quaternion.Euler(0f, angle, 0f);
                Vector3 spawnPos = transform.position + rot * laughProjectileSpawnOffset;
                var projObj = Instantiate(laughProjectilePrefab, spawnPos, rot);
                var proj = projObj.GetComponent<EnemyProjectile>();
                if (proj != null)
                    proj.Initialize(gameObject, laughDamage, laughProjectileSpeed, laughProjectileLifetime);
            }
        }

        var all = Physics.OverlapSphere(transform.position, laughRange, LayerMask.GetMask("Player"));
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        float halfAngle = Mathf.Clamp(laughConeAngle * 0.5f, 0f, 90f);
        foreach (var c in all)
        {
            Vector3 to = c.transform.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude < 0.0001f) continue;
            float angle = Vector3.Angle(fwd, to.normalized);
            if (angle <= halfAngle)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(laughDamage);
            }
        }
    }

    private bool CanPound()
    {
        if (Time.time - lastPoundTime < poundCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= poundRange + 0.5f;
    }

    private void StartPound()
    {
        lastPoundTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoPound());
    }

    private IEnumerator CoPound()
    {
        if (animator != null && HasTrigger(poundTrigger)) animator.SetTrigger(poundTrigger);
        if (audioSource != null && poundWindupSFX != null) audioSource.PlayOneShot(poundWindupSFX);
        GameObject wind = null;
        if (poundWindupVFX != null)
        {
            wind = Instantiate(poundWindupVFX, transform);
            wind.transform.localPosition = poundVFXOffset;
            if (poundVFXScale > 0f) wind.transform.localScale = Vector3.one * poundVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, poundWindup));
        if (wind != null) Destroy(wind);
        if (poundImpactVFX != null)
        {
            var fx = Instantiate(poundImpactVFX, transform);
            fx.transform.localPosition = poundVFXOffset;
            if (poundVFXScale > 0f) fx.transform.localScale = Vector3.one * poundVFXScale;
        }
        if (audioSource != null && poundImpactSFX != null) audioSource.PlayOneShot(poundImpactSFX);

        // strip: project forward; hit players within width band
        var all = Physics.OverlapSphere(transform.position + transform.forward * (poundRange * 0.5f), poundRange, LayerMask.GetMask("Player"));
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        foreach (var c in all)
        {
            Vector3 rel = c.transform.position - transform.position;
            rel.y = 0f;
            float along = Vector3.Dot(rel, fwd);
            float across = Vector3.Cross(fwd, rel.normalized).magnitude * rel.magnitude;
            if (along >= 0f && along <= poundRange && Mathf.Abs(across) <= (poundWidth * 0.5f))
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) ps.TakeDamage(poundDamage);
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