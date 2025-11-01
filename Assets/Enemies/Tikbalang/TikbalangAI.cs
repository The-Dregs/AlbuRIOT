using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class TikbalangAI : BaseEnemyAI
{
    [Header("Charge Attack")]
    public int chargeDamage = 35;
    public float chargeCooldown = 6f;
    public float chargeWindup = 0.5f;
    public float chargeDuration = 3f;
    public float chargeSpeed = 10f;
    public float chargeHitRadius = 1.7f;
    public float chargeMinDistance = 10f;
    public float chargeRecoverDuration = 3f;
    public GameObject chargeVFX; // windup
    public AudioClip chargeSFX;  // windup
    public GameObject chargeImpactVFX; // activation
    public Vector3 chargeVFXOffset = Vector3.zero;
    public float chargeVFXScale = 1.0f;
    public Vector3 chargeImpactVFXOffset = Vector3.zero;
    public float chargeImpactVFXScale = 1.0f;
    public AudioClip chargeImpactSFX;

    [Header("Stomp Attack")]
    public int stompDamage = 25;
    public float stompRadius = 3.5f;
    public float stompCooldown = 5f;
    public float stompWindup = 0.5f;
    public float stompMinDistance = 0f;
    public GameObject stompVFX; // windup
    public AudioClip stompSFX;  // windup
    public GameObject stompImpactVFX; // activation
    public Vector3 stompVFXOffset = Vector3.zero;
    public float stompVFXScale = 1.0f;
    public Vector3 stompImpactVFXOffset = Vector3.zero;
    public float stompImpactVFXScale = 1.0f;
    public AudioClip stompImpactSFX;

    [Header("Animation")]
    public string chargeTrigger = "Charge";
    public string stompTrigger = "Stomp";

    [Header("Skill Selection Tuning")]
    public float chargePreferredMinDistance = 10f;
    public float chargePreferredMaxDistance = 20f;
    [Range(0f, 1f)] public float chargeSkillWeight = 0.8f;
    [SerializeField] private float chargeStoppageTime = 1f;
    public float stompPreferredMinDistance = 2.0f;
    public float stompPreferredMaxDistance = 5.5f;
    [Range(0f, 1f)] public float stompSkillWeight = 0.7f;
    [SerializeField] private float stompStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 4.5f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    // Runtime state
    private float lastChargeTime = -9999f;
    private float lastStompTime = -9999f;
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

        var canCharge = new ConditionNode(blackboard, CanCharge, "can_charge");
        var doCharge = new ActionNode(blackboard, () => { StartCharge(); return NodeState.Success; }, "charge");
        var canStomp = new ConditionNode(blackboard, CanStomp, "can_stomp");
        var doStomp = new ActionNode(blackboard, () => { StartStomp(); return NodeState.Success; }, "stomp");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "charge_seq").Add(canCharge, doCharge),
                        new Sequence(blackboard, "stomp_seq").Add(canStomp, doStomp),
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
        bool inChargeRange = dist >= chargePreferredMinDistance && dist <= chargePreferredMaxDistance;
        bool inStompRange = dist >= stompPreferredMinDistance && dist <= stompPreferredMaxDistance;
        float chargeMid = (chargePreferredMinDistance + chargePreferredMaxDistance) * 0.5f;
        float stompMid = (stompPreferredMinDistance + stompPreferredMaxDistance) * 0.5f;
        float chargeScore = (inChargeRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(chargeMid - dist) / 15f)) * chargeSkillWeight : 0f;
        float stompScore = (inStompRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(stompMid - dist) / 5f)) * stompSkillWeight : 0f;
        if (CanCharge() && chargeScore >= stompScore && chargeScore > 0.15f) { StartCharge(); return true; }
        if (CanStomp() && stompScore > chargeScore && stompScore > 0.15f) { StartStomp(); return true; }
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

    #region Charge Attack

    private bool CanCharge()
    {
        if (Time.time - lastChargeTime < chargeCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= chargeMinDistance;
    }

    private void StartCharge()
    {
        lastChargeTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time; // gate basic after special
        StartCoroutine(CoCharge());
    }

    private IEnumerator CoCharge()
    {
        if (animator != null && HasTrigger(chargeTrigger)) animator.SetTrigger(chargeTrigger);
        // windup SFX (stoppable)
        if (audioSource != null && chargeSFX != null)
        {
            audioSource.clip = chargeSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup VFX
        GameObject chargeWindupFx = null;
        if (chargeVFX != null)
        {
            chargeWindupFx = Instantiate(chargeVFX, transform);
            chargeWindupFx.transform.localPosition = chargeVFXOffset;
            if (chargeVFXScale > 0f) chargeWindupFx.transform.localScale = Vector3.one * chargeVFXScale;
        }

        // Windup
        yield return new WaitForSeconds(chargeWindup);

        // End windup visuals/audio and play activation impact VFX/SFX
        if (audioSource != null && audioSource.clip == chargeSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (chargeWindupFx != null) Destroy(chargeWindupFx);
        if (chargeImpactVFX != null)
        {
            var fx = Instantiate(chargeImpactVFX, transform);
            fx.transform.localPosition = chargeImpactVFXOffset;
            if (chargeImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * chargeImpactVFXScale;
        }
        if (audioSource != null && chargeImpactSFX != null) audioSource.PlayOneShot(chargeImpactSFX);

        // Charge
        var target = blackboard.Get<Transform>("target");
        if (target != null)
        {
            Vector3 chargeDirection = (target.position - transform.position).normalized;
            chargeDirection.y = 0f;

            float chargeTime = chargeDuration;
            while (chargeTime > 0f && target != null)
            {
                if (controller != null && controller.enabled)
                {
                    controller.Move(chargeDirection * chargeSpeed * Time.deltaTime);
                }

                // Check for hits during charge
                var hitColliders = Physics.OverlapSphere(transform.position, chargeHitRadius);
                foreach (var hit in hitColliders)
                {
                    if (hit.CompareTag("Player"))
                    {
                        var playerStats = hit.GetComponent<PlayerStats>();
                        if (playerStats != null) playerStats.TakeDamage(chargeDamage);
                    }
                }

                chargeTime -= Time.deltaTime;
            yield return null;
            }
        }

        // Recovery
        yield return new WaitForSeconds(chargeRecoverDuration);
    }

    #endregion

    #region Stomp Attack

    private bool CanStomp()
    {
        if (Time.time - lastStompTime < stompCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        if (target == null) return false;
        float distance = Vector3.Distance(transform.position, target.position);
        return distance >= stompMinDistance;
    }

    private void StartStomp()
    {
        lastStompTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time; // gate basic after special
        StartCoroutine(CoStomp());
    }

    private IEnumerator CoStomp()
    {
        if (animator != null && HasTrigger(stompTrigger)) animator.SetTrigger(stompTrigger);
        // windup sfx (stoppable)
        if (audioSource != null && stompSFX != null)
        {
            audioSource.clip = stompSFX;
            audioSource.loop = false;
            audioSource.Play();
        }
        // windup vfx
        GameObject stompWindupFx = null;
        if (stompVFX != null)
        {
            stompWindupFx = Instantiate(stompVFX, transform);
            stompWindupFx.transform.localPosition = stompVFXOffset;
            if (stompVFXScale > 0f) stompWindupFx.transform.localScale = Vector3.one * stompVFXScale;
        }

        // Windup
        yield return new WaitForSeconds(stompWindup);

        // end windup visuals/audio and play activation vfx/sfx
        if (audioSource != null && audioSource.clip == stompSFX)
        {
            audioSource.Stop();
            audioSource.clip = null;
        }
        if (stompWindupFx != null) Destroy(stompWindupFx);
        if (stompImpactVFX != null)
        {
            var fx = Instantiate(stompImpactVFX, transform);
            fx.transform.localPosition = stompImpactVFXOffset;
            if (stompImpactVFXScale > 0f) fx.transform.localScale = Vector3.one * stompImpactVFXScale;
        }
        if (audioSource != null && stompImpactSFX != null) audioSource.PlayOneShot(stompImpactSFX);

        var hitColliders = Physics.OverlapSphere(transform.position, stompRadius);
        foreach (var hit in hitColliders)
        {
            if (hit.CompareTag("Player"))
            {
                var playerStats = hit.GetComponent<PlayerStats>();
                if (playerStats != null) playerStats.TakeDamage(stompDamage);
            }
        }
    }

    #endregion
}