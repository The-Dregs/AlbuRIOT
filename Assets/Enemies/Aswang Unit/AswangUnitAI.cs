using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class AswangUnitAI : BaseEnemyAI
{
    [Header("Pounce Attack")]
    public int pounceDamage = 24;
    public float pounceWindup = 0.4f;
    public float pounceCooldown = 5.5f;
    public float pounceLeapDistance = 6f;
    public float pounceLeapSpeed = 12f;
    public float pounceHitRadius = 1.5f;
    public GameObject pounceVFX; // windup
    public AudioClip pounceSFX;  // windup
    public GameObject pounceImpactVFX; // activation at landing
    public Vector3 pounceVFXOffset = Vector3.zero;
    public float pounceVFXScale = 1.0f;
    public Vector3 pounceImpactVFXOffset = Vector3.zero;
    public float pounceImpactVFXScale = 1.0f;
    public AudioClip pounceImpactSFX;

    [Header("Shadow Swarm")]
    public int swarmTickDamage = 5;
    public float swarmTickInterval = 0.5f;
    public float swarmDuration = 3.0f;
    public float swarmCooldown = 7.5f;
    public float swarmRadius = 3.5f;
    public float swarmWindup = 0.6f;
    public GameObject swarmVFX; // windup
    public AudioClip swarmSFX;  // windup
    public GameObject swarmImpactVFX; // activation (when DoT starts)
    public Vector3 swarmVFXOffset = Vector3.zero;
    public float swarmVFXScale = 1.0f;
    public Vector3 swarmImpactVFXOffset = Vector3.zero;
    public float swarmImpactVFXScale = 1.0f;
    public AudioClip swarmImpactSFX;

    [Header("Skill Selection Tuning")]
    public float pouncePreferredMinDistance = 3f;
    public float pouncePreferredMaxDistance = 8f;
    [Range(0f, 1f)] public float pounceSkillWeight = 0.7f;
    [SerializeField] private float pounceStoppageTime = 1f;
    public float swarmPreferredMinDistance = 2.5f;
    public float swarmPreferredMaxDistance = 5.5f;
    [Range(0f, 1f)] public float swarmSkillWeight = 0.85f;
    [SerializeField] private float swarmStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 2.7f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 0.8f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    [Header("Animation")]
    public string pounceTrigger = "Pounce";
    public string swarmTrigger = "Swarm";

    // Runtime state
    private float lastPounceTime = -9999f;
    private float lastSwarmTime = -9999f;
    private GameObject activeSwarmVFX;
    private Coroutine swarmCoroutine;
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

        var canPounce = new ConditionNode(blackboard, CanPounce, "can_pounce");
        var doPounce = new ActionNode(blackboard, () => { StartPounce(); return NodeState.Success; }, "pounce");
        var canSwarm = new ConditionNode(blackboard, CanSwarm, "can_swarm");
        var doSwarm = new ActionNode(blackboard, () => { StartSwarm(); return NodeState.Success; }, "swarm");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "pounce_seq").Add(canPounce, doPounce),
                        new Sequence(blackboard, "swarm_seq").Add(canSwarm, doSwarm),
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
        bool inPounceRange = dist >= pouncePreferredMinDistance && dist <= pouncePreferredMaxDistance;
        bool inSwarmRange = dist >= swarmPreferredMinDistance && dist <= swarmPreferredMaxDistance;
        float pounceMid = (pouncePreferredMinDistance + pouncePreferredMaxDistance) * 0.5f;
        float swarmMid = (swarmPreferredMinDistance + swarmPreferredMaxDistance) * 0.5f;
        float pounceScore = (inPounceRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(pounceMid - dist) / 7f)) * pounceSkillWeight : 0f;
        float swarmScore = (inSwarmRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(swarmMid - dist) / 7f)) * swarmSkillWeight : 0f;
        if (CanPounce() && pounceScore >= swarmScore && pounceScore > 0.15f) { StartPounce(); return true; }
        if (CanSwarm() && swarmScore > pounceScore && swarmScore > 0.15f) { StartSwarm(); return true; }
        return false;
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

    #endregion

    #region Pounce Attack

    private bool CanPounce()
    {
        if (Time.time - lastPounceTime < pounceCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= pounceLeapDistance * 0.5f && distance <= pounceLeapDistance * 1.5f;
    }

    private void StartPounce()
    {
        lastPounceTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time; // gate basic after special
        StartCoroutine(CoPounce());
    }

    private IEnumerator CoPounce()
    {
        if (animator != null && HasTrigger(pounceTrigger)) animator.SetTrigger(pounceTrigger);
        // windup sfx (stoppable)
        if (audioSource != null && pounceSFX != null)
        {
            audioSource.clip = pounceSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup vfx
        GameObject pounceWindupFx = null;
        if (pounceVFX != null)
        {
            pounceWindupFx = Instantiate(pounceVFX, transform);
            pounceWindupFx.transform.localPosition = pounceVFXOffset;
            if (pounceVFXScale > 0f) pounceWindupFx.transform.localScale = Vector3.one * pounceVFXScale;
        }

        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            Vector3 leapDirection = (target.position - transform.position).normalized;
            leapDirection.y = 0f;

            // end windup visuals/audio and play activation vfx/sfx before leap
            if (audioSource != null && audioSource.clip == pounceSFX)
            {
                audioSource.Stop();
                audioSource.clip = null;
            }
            if (pounceWindupFx != null) Destroy(pounceWindupFx);
            if (pounceImpactVFX != null)
            {
                var fx = Instantiate(pounceImpactVFX, transform);
                fx.transform.localPosition = pounceImpactVFXOffset;
                if (pounceImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * pounceImpactVFXScale;
            }
            if (audioSource != null && pounceImpactSFX != null) audioSource.PlayOneShot(pounceImpactSFX);

            float leapTime = pounceLeapDistance / pounceLeapSpeed;
            float elapsedTime = 0f;

            while (elapsedTime < leapTime && target != null)
            {
                Vector3 newPosition = transform.position + leapDirection * pounceLeapSpeed * Time.deltaTime;
                if (controller != null && controller.enabled)
                {
                    controller.Move((newPosition - transform.position));
                }
                elapsedTime += Time.deltaTime;
                yield return null;
            }

            // Damage on landing
            var hitColliders = Physics.OverlapSphere(transform.position, pounceHitRadius);
            foreach (var hit in hitColliders)
            {
                if (hit.CompareTag("Player"))
                {
                    var playerStats = hit.GetComponent<PlayerStats>();
                    if (playerStats != null) playerStats.TakeDamage(pounceDamage);
                }
            }
        }
    }

    #endregion

    #region Shadow Swarm

    private bool CanSwarm()
    {
        if (Time.time - lastSwarmTime < swarmCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance <= swarmRadius;
    }

    private void StartSwarm()
    {
        lastSwarmTime = Time.time;
        StartCoroutine(CoSwarm());
    }

    private IEnumerator CoSwarm()
    {
        if (animator != null && HasTrigger(swarmTrigger)) animator.SetTrigger(swarmTrigger);
        if (audioSource != null && swarmSFX != null) audioSource.PlayOneShot(swarmSFX);
        if (swarmVFX != null) activeSwarmVFX = Instantiate(swarmVFX, transform.position, transform.rotation);

        swarmCoroutine = StartCoroutine(CoSwarmDamageTicks());
        yield return new WaitForSeconds(swarmDuration);

        if (swarmCoroutine != null) StopCoroutine(swarmCoroutine);
        if (activeSwarmVFX != null) Destroy(activeSwarmVFX);
    }

    private IEnumerator CoSwarmDamageTicks()
    {
        while (true)
        {
            var hitColliders = Physics.OverlapSphere(transform.position, swarmRadius);
            foreach (var hit in hitColliders)
            {
                if (hit.CompareTag("Player"))
                {
                    var playerStats = hit.GetComponent<PlayerStats>();
                    if (playerStats != null) playerStats.TakeDamage(swarmTickDamage);
                }
            }
            yield return new WaitForSeconds(swarmTickInterval);
        }
    }

    #endregion

    #region Cleanup

    void OnDestroy()
    {
        if (swarmCoroutine != null) StopCoroutine(swarmCoroutine);
        if (activeSwarmVFX != null) Destroy(activeSwarmVFX);
    }

    #endregion
}