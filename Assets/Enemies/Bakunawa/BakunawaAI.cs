using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class BakunawaAI : BaseEnemyAI
{
    [Header("Lunar Devour")]
    public float lunarDevourWindup = 0.8f;
    public float lunarDevourCooldown = 15f;
    public float lunarDevourEmpowerDuration = 8f;
    public GameObject lunarDevourVFX;
    public AudioClip lunarDevourSFX;

    [Header("Tsunami Roar")]
    public int tsunamiRoarDamage = 45;
    public float tsunamiRoarRadius = 12f;
    public float tsunamiRoarWindup = 0.8f;
    public float tsunamiRoarCooldown = 12f;
    public GameObject tsunamiRoarVFX;
    public AudioClip tsunamiRoarSFX;

    [Header("Skill Selection Tuning")]
    public float lunarDevourPreferredMinDistance = 7f;
    public float lunarDevourPreferredMaxDistance = 17f;
    [Range(0f, 1f)] public float lunarDevourSkillWeight = 0.65f;
    public float tsunamiRoarPreferredMinDistance = 4f;
    public float tsunamiRoarPreferredMaxDistance = 13f;
    [Range(0f, 1f)] public float tsunamiRoarSkillWeight = 0.80f;
    [SerializeField] private float lunarDevourStoppageTime = 1f;
    [SerializeField] private float tsunamiRoarStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 5.0f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 0.8f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    [Header("Animation")]
    public string lunarDevourTrigger = "LunarDevour";
    public string tsunamiRoarTrigger = "TsunamiRoar";

    // Runtime state
    private float lastLunarDevourTime = -9999f;
    private float lastTsunamiRoarTime = -9999f;
    private AudioSource audioSource;

    #region Initialization

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

        var canLunarDevour = new ConditionNode(blackboard, CanLunarDevour, "can_lunar_devour");
        var doLunarDevour = new ActionNode(blackboard, () => { StartLunarDevour(); return NodeState.Success; }, "lunar_devour");
        var canTsunamiRoar = new ConditionNode(blackboard, CanTsunamiRoar, "can_tsunami_roar");
        var doTsunamiRoar = new ActionNode(blackboard, () => { StartTsunamiRoar(); return NodeState.Success; }, "tsunami_roar");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "lunar_devour_seq").Add(canLunarDevour, doLunarDevour),
                        new Sequence(blackboard, "tsunami_roar_seq").Add(canTsunamiRoar, doTsunamiRoar),
                        new Sequence(blackboard, "basic_seq").Add(targetInAttack, basicAttack),
                        moveToTarget
                    )
                ),
                new ActionNode(blackboard, Patrol, "patrol")
            );
    }

    #endregion

    #region BaseEnemyAI Overrides

    protected override void PerformBasicAttack()
    {
        if (enemyData == null) return;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return;

        var target = blackboard.Get<Transform>("target");
        if (target == null) return;

        if (animator != null && HasTrigger(attackTrigger)) animator.SetTrigger(attackTrigger);

        // Apply damage
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
        bool inDevourRange = dist >= lunarDevourPreferredMinDistance && dist <= lunarDevourPreferredMaxDistance;
        bool inRoarRange = dist >= tsunamiRoarPreferredMinDistance && dist <= tsunamiRoarPreferredMaxDistance;
        float devourMid = (lunarDevourPreferredMinDistance + lunarDevourPreferredMaxDistance) * 0.5f;
        float roarMid = (tsunamiRoarPreferredMinDistance + tsunamiRoarPreferredMaxDistance) * 0.5f;
        float devourScore = (inDevourRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(devourMid - dist) / 12.5f)) * lunarDevourSkillWeight : 0f;
        float roarScore = (inRoarRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(roarMid - dist) / 12.5f)) * tsunamiRoarSkillWeight : 0f;
        if (CanLunarDevour() && devourScore >= roarScore && devourScore > 0.15f) { StartLunarDevour(); return true; }
        if (CanTsunamiRoar() && roarScore > devourScore && roarScore > 0.15f) { StartTsunamiRoar(); return true; }
        return false;
    }

    #endregion

    #region Lunar Devour

    private bool CanLunarDevour()
    {
        if (Time.time - lastLunarDevourTime < lunarDevourCooldown) return false;
        return true;
    }

    private void StartLunarDevour()
    {
        lastLunarDevourTime = Time.time;
        StartCoroutine(CoLunarDevour());
    }

    private IEnumerator CoLunarDevour()
    {
        if (animator != null && HasTrigger(lunarDevourTrigger)) animator.SetTrigger(lunarDevourTrigger);
        if (audioSource != null && lunarDevourSFX != null) audioSource.PlayOneShot(lunarDevourSFX);
        if (lunarDevourVFX != null) Instantiate(lunarDevourVFX, transform.position, transform.rotation);

        yield return new WaitForSeconds(lunarDevourWindup);
        // Apply empower effect here if needed
        yield return new WaitForSeconds(lunarDevourEmpowerDuration);
    }

    #endregion

    #region Tsunami Roar

    private bool CanTsunamiRoar()
    {
        if (Time.time - lastTsunamiRoarTime < tsunamiRoarCooldown) return false;
        return true;
    }

    private void StartTsunamiRoar()
    {
        lastTsunamiRoarTime = Time.time;
        StartCoroutine(CoTsunamiRoar());
    }

    private IEnumerator CoTsunamiRoar()
    {
        if (animator != null && HasTrigger(tsunamiRoarTrigger)) animator.SetTrigger(tsunamiRoarTrigger);
        if (audioSource != null && tsunamiRoarSFX != null) audioSource.PlayOneShot(tsunamiRoarSFX);
        if (tsunamiRoarVFX != null) Instantiate(tsunamiRoarVFX, transform.position, transform.rotation);

        yield return new WaitForSeconds(tsunamiRoarWindup);

        // Apply AOE damage
        var hitColliders = Physics.OverlapSphere(transform.position, tsunamiRoarRadius);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Player"))
            {
                var playerStats = hit.GetComponent<PlayerStats>();
                if (playerStats != null) playerStats.TakeDamage(tsunamiRoarDamage);
            }
        }
    }

    #endregion

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