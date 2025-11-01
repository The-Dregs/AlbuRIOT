using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class TiyanakAI : BaseEnemyAI
{
    [Header("Lunge Bite")]
    public int lungeDamage = 20;
    public float lungeRange = 1.2f;
    public float lungeWindup = 0.3f;
    public float lungeCooldown = 4f;
    public float lungeSpeed = 10f;
    public float lungeDuration = 0.25f;
    public GameObject lungeWindupVFX;
    public GameObject lungeImpactVFX;
    public Vector3 lungeVFXOffset = Vector3.zero;
    public float lungeVFXScale = 1.0f;
    public AudioClip lungeWindupSFX;
    public AudioClip lungeImpactSFX;
    public string lungeTrigger = "Lunge";

    [Header("Wail Fear (cone DoT)")]
    public int wailTickDamage = 6; // 6 dmg every 0.4s, 3 ticks => 18 total
    public float wailTickInterval = 0.4f;
    public int wailTicks = 3;
    [Range(0f,180f)] public float wailConeAngle = 60f;
    public float wailRange = 6f;
    public float wailWindup = 0.6f;
    public float wailCooldown = 7f;
    public GameObject wailWindupVFX;
    public GameObject wailImpactVFX;
    public Vector3 wailVFXOffset = Vector3.zero;
    public float wailVFXScale = 1.0f;
    public AudioClip wailWindupSFX;
    public AudioClip wailImpactSFX;
    public string wailTrigger = "Wail";

    [Header("Skill Selection Tuning")]
    public float lungePreferredMinDistance = 2f;
    public float lungePreferredMaxDistance = 5.5f;
    [Range(0f, 1f)] public float lungeSkillWeight = 0.85f;
    [SerializeField] private float lungeStoppageTime = 1f;
    public float wailPreferredMinDistance = 3f;
    public float wailPreferredMaxDistance = 8.0f;
    [Range(0f, 1f)] public float wailSkillWeight = 0.75f;
    [SerializeField] private float wailStoppageTime = 1f;
    [Header("Spacing")]
    public float preferredDistance = 1.8f;
    [Range(0.1f,2f)] public float backoffSpeedMultiplier = 1.0f;
    [Header("Facing")]
    [Range(1f,60f)] public float specialFacingAngle = 20f;

    private float lastLungeTime = -9999f;
    private float lastWailTime = -9999f;
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
        var canLunge = new ConditionNode(blackboard, CanLunge, "can_lunge");
        var doLunge = new ActionNode(blackboard, () => { StartLunge(); return NodeState.Success; }, "lunge");
        var canWail = new ConditionNode(blackboard, CanWail, "can_wail");
        var doWail = new ActionNode(blackboard, () => { StartWail(); return NodeState.Success; }, "wail");

        behaviorTree = new Selector(blackboard, "root")
            .Add(
                new Sequence(blackboard, "combat").Add(
                    updateTarget,
                    hasTarget,
                    targetInDetection,
                    new Selector(blackboard, "attack_opts").Add(
                        new Sequence(blackboard, "lunge_seq").Add(canLunge, doLunge),
                        new Sequence(blackboard, "wail_seq").Add(canWail, doWail),
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
        bool inLungeRange = dist >= lungePreferredMinDistance && dist <= lungePreferredMaxDistance;
        bool inWailRange = dist >= wailPreferredMinDistance && dist <= wailPreferredMaxDistance;
        float lungeMid = (lungePreferredMinDistance + lungePreferredMaxDistance) * 0.5f;
        float wailMid = (wailPreferredMinDistance + wailPreferredMaxDistance) * 0.5f;
        float lungeScore = (inLungeRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(lungeMid - dist) / 4f)) * lungeSkillWeight : 0f;
        float wailScore = (inWailRange && facingTarget) ? (1f - Mathf.Clamp01(Mathf.Abs(wailMid - dist) / 5f)) * wailSkillWeight : 0f;
        if (CanLunge() && lungeScore >= wailScore && lungeScore > 0.15f) { StartLunge(); return true; }
        if (CanWail() && wailScore > lungeScore && wailScore > 0.15f) { StartWail(); return true; }
        return false;
    }

    private bool CanLunge()
    {
        if (Time.time - lastLungeTime < lungeCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= (lungeRange + 2.5f);
    }

    private void StartLunge()
    {
        lastLungeTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoLunge());
    }

    private IEnumerator CoLunge()
    {
        if (animator != null && HasTrigger(lungeTrigger)) animator.SetTrigger(lungeTrigger);
        if (audioSource != null && lungeWindupSFX != null) audioSource.PlayOneShot(lungeWindupSFX);
        GameObject wind = null;
        if (lungeWindupVFX != null)
        {
            wind = Instantiate(lungeWindupVFX, transform);
            wind.transform.localPosition = lungeVFXOffset;
            if (lungeVFXScale > 0f) wind.transform.localScale = Vector3.one * lungeVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, lungeWindup));
        if (wind != null) Destroy(wind);
        if (lungeImpactVFX != null)
        {
            var fx = Instantiate(lungeImpactVFX, transform);
            fx.transform.localPosition = lungeVFXOffset;
            if (lungeVFXScale > 0f) fx.transform.localScale = Vector3.one * lungeVFXScale;
        }
        if (audioSource != null && lungeImpactSFX != null) audioSource.PlayOneShot(lungeImpactSFX);

        var target = blackboard.Get<Transform>("target");
        Vector3 dir = target != null ? (target.position - transform.position) : transform.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.0001f) dir.Normalize(); else dir = transform.forward;

        float t = Mathf.Max(0.05f, lungeDuration);
        while (t > 0f)
        {
            t -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.Move(dir * lungeSpeed * Time.deltaTime);
            // hit if within lunge range
            if (target != null && Vector3.Distance(transform.position, target.position) <= lungeRange)
            {
                var ps = target.GetComponent<PlayerStats>();
                if (ps != null) ps.TakeDamage(lungeDamage);
            }
            yield return null;
        }
    }

    private bool CanWail()
    {
        if (Time.time - lastWailTime < wailCooldown) return false;
        var target = blackboard.Get<Transform>("target");
        return target != null && Vector3.Distance(transform.position, target.position) <= wailRange + 1f;
    }

    private void StartWail()
    {
        lastWailTime = Time.time;
        if (enemyData != null) lastAttackTime = Time.time;
        StartCoroutine(CoWail());
    }

    private IEnumerator CoWail()
    {
        if (animator != null && HasTrigger(wailTrigger)) animator.SetTrigger(wailTrigger);
        if (audioSource != null && wailWindupSFX != null) audioSource.PlayOneShot(wailWindupSFX);
        GameObject wind = null;
        if (wailWindupVFX != null)
        {
            wind = Instantiate(wailWindupVFX, transform);
            wind.transform.localPosition = wailVFXOffset;
            if (wailVFXScale > 0f) wind.transform.localScale = Vector3.one * wailVFXScale;
        }
        yield return new WaitForSeconds(Mathf.Max(0f, wailWindup));
        if (wind != null) Destroy(wind);
        if (wailImpactVFX != null)
        {
            var fx = Instantiate(wailImpactVFX, transform);
            fx.transform.localPosition = wailVFXOffset;
            if (wailVFXScale > 0f) fx.transform.localScale = Vector3.one * wailVFXScale;
        }
        if (audioSource != null && wailImpactSFX != null) audioSource.PlayOneShot(wailImpactSFX);

        // cone DoT ticks
        Vector3 fwd = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
        float halfAngle = Mathf.Clamp(wailConeAngle * 0.5f, 0f, 90f);
        int ticks = Mathf.Max(1, wailTicks);
        float interval = Mathf.Max(0.1f, wailTickInterval);
        for (int i = 0; i < ticks; i++)
        {
            var all = Physics.OverlapSphere(transform.position, wailRange, LayerMask.GetMask("Player"));
            foreach (var c in all)
            {
                Vector3 to = c.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 0.0001f) continue;
                float angle = Vector3.Angle(fwd, to.normalized);
                if (angle <= halfAngle)
                {
                    var ps = c.GetComponentInParent<PlayerStats>();
                    if (ps != null) ps.TakeDamage(wailTickDamage);
                }
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