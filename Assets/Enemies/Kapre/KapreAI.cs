using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class KapreAI : BaseEnemyAI
{
    [Header("Smoke Vanish → Strike")]
    public int vanishStrikeDamage = 30;
    public float vanishStrikeRadius = 1.6f;
    public float vanishWindup = 0.5f;
    public float vanishCooldown = 9f;
    public float vanishTeleportBehindDistance = 1.2f;
    public GameObject vanishWindupVFX;
    public GameObject vanishImpactVFX;
    public Vector3 vanishVFXOffset = Vector3.zero;
    public float vanishVFXScale = 1.0f;
    public AudioClip vanishWindupSFX;
    public AudioClip vanishImpactSFX;
    public string vanishTrigger = "Vanish";

    [Header("Tree Slam (frontal AOE)")]
    public int treeSlamDamage = 35;
    public float treeSlamRadius = 2.6f;
    public float treeSlamWindup = 0.6f;
    public float treeSlamCooldown = 10f;
    public GameObject treeSlamWindupVFX;
    public GameObject treeSlamImpactVFX;
    public Vector3 treeSlamVFXOffset = Vector3.zero;
    public float treeSlamVFXScale = 1.0f;
    public AudioClip treeSlamWindupSFX;
    public AudioClip treeSlamImpactSFX;
    public string treeSlamTrigger = "TreeSlam";

    [Header("Skill Selection Tuning")]
    public float vanishPreferredMinDistance = 2f;
    public float vanishPreferredMaxDistance = 7f;
    [Range(0f, 1f)] public float vanishSkillWeight = 0.7f;
    [SerializeField] private float vanishStoppageTime = 1f;
    public float treeSlamPreferredMinDistance = 3f;
    public float treeSlamPreferredMaxDistance = 9f;
    [Range(0f, 1f)] public float treeSlamSkillWeight = 0.8f;
    [SerializeField] private float treeSlamStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 4.0f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastVanishTime = -9999f;
    private float lastTreeSlamTime = -9999f;
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
        var canVanish = new ConditionNode(blackboard, CanVanishStrike, "can_vanish");
        var doVanish = new ActionNode(blackboard, () => { StartVanishStrike(); return NodeState.Success; }, "vanish");
        var canTreeSlam = new ConditionNode(blackboard, CanTreeSlam, "can_treeslam");
        var doTreeSlam = new ActionNode(blackboard, () => { StartTreeSlam(); return NodeState.Success; }, "treeslam");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "vanish_seq").Add(canVanish, doVanish),
                        new Sequence(blackboard, "treeslam_seq").Add(canTreeSlam, doTreeSlam),
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
        bool inVanishRange = dist >= vanishPreferredMinDistance && dist <= vanishPreferredMaxDistance;
        bool inTreeSlamRange = dist >= treeSlamPreferredMinDistance && dist <= treeSlamPreferredMaxDistance;
        float vanishMid = (vanishPreferredMinDistance + vanishPreferredMaxDistance) * 0.5f;
        float treeMid = (treeSlamPreferredMinDistance + treeSlamPreferredMaxDistance) * 0.5f;
        float vanishScore = (inVanishRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(vanishMid - dist) / 5f)) * vanishSkillWeight : 0f;
        float treeSlamScore = (inTreeSlamRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(treeMid - dist) / 6f)) * treeSlamSkillWeight : 0f;
        if (CanVanishStrike() && vanishScore >= treeSlamScore && vanishScore > 0.15f) { StartVanishStrike(); return true; }
        if (CanTreeSlam() && treeSlamScore > vanishScore && treeSlamScore > 0.15f) { StartTreeSlam(); return true; }
        return false;
    }

    private bool CanVanishStrike()
    {
        if (Time.time - lastVanishTime < vanishCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        return Vector3.Distance(transform.position, target.position) <= enemyData.detectionRange;
    }

    private void StartVanishStrike()
    {
        lastVanishTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoVanishStrike());
    }

    private IEnumerator CoVanishStrike()
    {
        if (animator != null && HasTrigger(vanishTrigger)) animator.SetTrigger(vanishTrigger);
        if (audioSource != null && vanishWindupSFX != null) audioSource.PlayOneShot(vanishWindupSFX);
        GameObject wind = null;
        if (vanishWindupVFX != null)
        {
            wind = Instantiate(vanishWindupVFX, transform);
            wind.transform.localPosition = vanishVFXOffset;
            if (vanishVFXScale > 0f) wind.transform.localScale = Vector3.one * vanishVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, vanishWindup));
        if (wind != null) Destroy(wind);

        // teleport behind target
        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            Vector3 to = (transform.position - target.position);
            to.y = 0f;
            Vector3 behind = target.position - target.forward * Mathf.Max(0.2f, vanishTeleportBehindDistance);
            behind.y = transform.position.y;
            transform.position = behind;
            // face target
            Vector3 dir = (target.position - transform.position);
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized);
        }
        if (vanishImpactVFX != null)
        {
            var fx = Instantiate(vanishImpactVFX, transform);
            fx.transform.localPosition = vanishVFXOffset;
            if (vanishVFXScale > 0f) fx.transform.localScale = Vector3.one * vanishVFXScale;
        }
        if (audioSource != null && vanishImpactSFX != null) audioSource.PlayOneShot(vanishImpactSFX);

        var cols = Physics.OverlapSphere(transform.position, vanishStrikeRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(vanishStrikeDamage);
        }
    }

    private bool CanTreeSlam()
    {
        if (Time.time - lastTreeSlamTime < treeSlamCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null;
    }

    private void StartTreeSlam()
    {
        lastTreeSlamTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoTreeSlam());
    }

    private IEnumerator CoTreeSlam()
    {
        if (animator != null && HasTrigger(treeSlamTrigger)) animator.SetTrigger(treeSlamTrigger);
        if (audioSource != null && treeSlamWindupSFX != null) audioSource.PlayOneShot(treeSlamWindupSFX);
        GameObject wind = null;
        if (treeSlamWindupVFX != null)
        {
            wind = Instantiate(treeSlamWindupVFX, transform);
            wind.transform.localPosition = treeSlamVFXOffset;
            if (treeSlamVFXScale > 0f) wind.transform.localScale = Vector3.one * treeSlamVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, treeSlamWindup));
        if (wind != null) Destroy(wind);
        if (treeSlamImpactVFX != null)
        {
            var fx = Instantiate(treeSlamImpactVFX, transform);
            fx.transform.localPosition = treeSlamVFXOffset;
            if (treeSlamVFXScale > 0f) fx.transform.localScale = Vector3.one * treeSlamVFXScale;
        }
        if (audioSource != null && treeSlamImpactSFX != null) audioSource.PlayOneShot(treeSlamImpactSFX);

        // frontal AOE based on radius ahead
        var all = Physics.OverlapSphere(transform.position + transform.forward * (treeSlamRadius * 0.75f), treeSlamRadius, LayerMask.GetMask("Player"));
        foreach (var c in all)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null) ps.TakeDamage(treeSlamDamage);
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