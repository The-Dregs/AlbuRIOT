using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class ShadowTouchedDiwataAI : BaseEnemyAI
{
    [Header("Special Abilities")]
    public int specialDamage = 20;
    public float specialCooldown = 5f;
    public float specialRange = 5f;
    public GameObject specialVFX; // windup
    public AudioClip specialSFX;  // windup
    public GameObject specialImpactVFX; // activation
    public Vector3 specialVFXOffset = Vector3.zero;
    public float specialVFXScale = 1.0f;
    public Vector3 specialImpactVFXOffset = Vector3.zero;
    public float specialImpactVFXScale = 1.0f;
    public AudioClip specialImpactSFX;
    public string specialTrigger = "Special";

    [Header("Skill Selection Tuning")]
    public float specialPreferredMinDistance = 2.8f;
    public float specialPreferredMaxDistance = 7.0f;
    [Range(0f, 1f)] public float specialSkillWeight = 0.82f;
    [SerializeField] private float specialStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 3.1f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastSpecialTime = -9999f;
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
        var canSpecial = new ConditionNode(blackboard, CanSpecial, "can_special");
        var doSpecial = new ActionNode(blackboard, () => { StartSpecial(); return NodeState.Success; }, "special");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "special_seq").Add(canSpecial, doSpecial),
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
        bool inSpecialRange = dist >= specialPreferredMinDistance && dist <= specialPreferredMaxDistance;
        float specialMid = (specialPreferredMinDistance + specialPreferredMaxDistance) * 0.5f;
        float specialDistScore = 1f - Mathf.Clamp01(Mathf.Abs(specialMid - dist) / 5f);
        float specialScore = (inSpecialRange && facingTarget) ? specialDistScore * specialSkillWeight : 0f;
        if (CanSpecial() && specialScore > 0.18f) { StartSpecial(); return true; }
        return false;
    }

    private bool CanSpecial()
    {
        if (Time.time - lastSpecialTime < specialCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= specialRange;
    }

    private void StartSpecial()
    {
        lastSpecialTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time; // gate basic after special
        StartCoroutine(CoSpecial());
    }

    private IEnumerator CoSpecial()
    {
        if (animator != null && HasTrigger(specialTrigger)) animator.SetTrigger(specialTrigger);
        // windup sfx
        if (audioSource != null && specialSFX != null)
        {
            audioSource.clip = specialSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup vfx
        GameObject windupFx = null;
        if (specialVFX != null)
        {
            windupFx = Instantiate(specialVFX, transform);
            windupFx.transform.localPosition = specialVFXOffset;
            if (specialVFXScale > 0f) windupFx.transform.localScale = Vector3.one * specialVFXScale;
        }

        // end windup visuals/audio and play activation fx/sfx exactly at damage moment
        if (audioSource != null && audioSource.clip == specialSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (windupFx != null) Destroy(windupFx);
        if (specialImpactVFX != null)
        {
            var fx = Instantiate(specialImpactVFX, transform);
            fx.transform.localPosition = specialImpactVFXOffset;
            if (specialImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * specialImpactVFXScale;
        }
        if (audioSource != null && specialImpactSFX != null) audioSource.PlayOneShot(specialImpactSFX);

        var hitColliders = Physics.OverlapSphere(transform.position, specialRange);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Player"))
            {
                var playerStats = hit.GetComponent<PlayerStats>();
                if (playerStats != null) playerStats.TakeDamage(specialDamage);
            }
        }

        yield return new WaitForSeconds(0.5f);
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