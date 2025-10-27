using UnityEngine;
using Photon.Pun;
using TMPro;
using UnityEngine.UI;
using AlbuRIOT.AI.BehaviorTree;

public class MinokawaAI : MonoBehaviourPun
{
    [Header("stats")]
    public int maxHealth = 220;
    public int basicDamage = 30;
    public float basicRange = 2.8f;
    public float basicCooldown = 2.5f;
    public float basicWindup = 0.6f;

    [Header("Solar Wrath")]
    public int wrathDamage = 40;
    public float wrathRange = 8.0f;
    public float wrathCooldown = 12f;
    public float wrathWindup = 1.1f;
    public float wrathRadius = 3.5f;

    [Header("Wings of Judgment")]
    public int wingsDamage = 36;
    public float wingsRange = 5.5f;
    public float wingsCooldown = 10f;
    public float wingsWindup = 0.9f;
    public float wingsConeAngle = 70f;

    [Header("movement: speed settings")]
    public float patrolSpeed = 2.3f;
    public float chaseSpeed = 4.8f;
    public float attackMoveLock = 0.35f;
    public bool orbitWhenBasicOnCooldown = true;
    [Range(0.1f, 1.5f)] public float orbitSpeedMultiplier = 0.7f;

    [Header("animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string attackTrigger = "Attack";
    public string wrathTrigger = "Wrath";
    public string wingsTrigger = "Wings";
    public string hitTrigger = "Hit";
    public string dieTrigger = "Die";
    public string isDeadBool = "IsDead";
    public string busyBool = "Busy";

    [Header("behavior")]
    public bool enablePatrol = true;
    public float patrolRadius = 10f;
    public float patrolWait = 1.25f;

    [Header("ui")]
    public GameObject healthBarRoot;
    public Slider healthBar;
    public float healthbarHideDelay = 3f;
    private Coroutine hbHideCo;

    private CharacterController controller;
    private Vector3 spawnPoint;
    private int currentHealth;
    private float lastBasicTime;
    private float lastWrathTime;
    private float lastWingsTime;
    private float attackLockTimer;
    private bool isDead;
    private bool isWrathing;
    private bool isWingsing;
    private Coroutine activeAbility;
    private bool IsBusy => activeAbility != null || isWrathing || isWingsing || attackLockTimer > 0f;
    private enum ActionState { None, Basic, Wrath, Wings }
    private ActionState currentAction = ActionState.None;
    public float abilityHardTimeout = 8f;
    [Header("global ability gating")]
    public float postSpecialLock = 0.75f;
    private float postSpecialTimer = 0f;

    // debug overlay
    [Header("debug overlay")]
    public bool showDebugOverhead = false;
    public float debugYOffset = 2.4f;
    public Color debugColor = Color.yellow;
    public bool debugShowDetails = true;
    private TextMeshPro debugText;
    private string currentState = "idle";

    // BT
    private Blackboard bb;
    private Node tree;

    // runtime
    private Transform targetPlayer;
    private Vector3 patrolTarget;
    private float patrolWaitTimer;

    // cooldown helpers
    public float BasicCooldownRemaining => Mathf.Max(0f, basicCooldown - (Time.time - lastBasicTime));
    public float WrathCooldownRemaining => Mathf.Max(0f, wrathCooldown - (Time.time - lastWrathTime));
    public float WingsCooldownRemaining => Mathf.Max(0f, wingsCooldown - (Time.time - lastWingsTime));

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        spawnPoint = transform.position;
        currentHealth = maxHealth;
        lastBasicTime = -9999f;
        lastWrathTime = -9999f;
        lastWingsTime = -9999f;

        bb = new Blackboard { owner = gameObject };
        BuildTree();

        if (healthBarRoot == null)
        {
            var t = transform.Find("HealthBar");
            if (t != null) healthBarRoot = t.gameObject;
        }
        if (healthBar == null && healthBarRoot != null)
        {
            healthBar = healthBarRoot.GetComponentInChildren<Slider>(true);
        }
        if (healthBar != null)
        {
            healthBar.minValue = 0f;
            healthBar.maxValue = 1f;
            healthBar.value = 1f;
        }
        if (healthBarRoot != null) healthBarRoot.SetActive(false);
    }

    private void Update()
    {
        if (isDead) return;

        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;

        if (animator != null && HasFloatParam(speedParam))
        {
            float planar = 0f;
            if (controller != null && controller.enabled)
            {
                Vector3 v = controller.velocity;
                planar = new Vector3(v.x, 0f, v.z).magnitude;
                if (planar < 0.05f || (currentState == "patrolling" && IsAtPatrolTarget()))
                {
                    planar = 0f;
                }
            }
            animator.SetFloat(speedParam, planar);
        }

        if (attackLockTimer > 0f)
            attackLockTimer -= Time.deltaTime;
        if (postSpecialTimer > 0f)
            postSpecialTimer -= Time.deltaTime;

        if (!IsBusy)
        {
            var _ = tree != null ? tree.Tick() : NodeState.Failure;
        }
        else
        {
            currentState = IsBusy ? $"busy - {currentAction}" : currentState;
        }

        if (healthBarRoot != null && healthBarRoot.activeSelf)
        {
            var cam = Camera.main;
            if (cam != null)
            {
                var fwd = cam.transform.rotation * Vector3.forward;
                var up = cam.transform.rotation * Vector3.up;
                healthBarRoot.transform.rotation = Quaternion.LookRotation(fwd, up);
            }
        }

        var tgt = bb != null ? bb.Get<Transform>("target") : null;
        if (tgt != null && Vector3.Distance(transform.position, tgt.position) > 18f)
        {
            targetPlayer = null; bb.Remove("target"); currentState = "lost target";
        }

        if (showDebugOverhead) EnsureDebugOverlay(); else DestroyDebugOverlay();
        if (showDebugOverhead && debugText != null) UpdateDebugOverlay();
    }

    private void BuildTree()
    {
        var updateTarget = new ActionNode(bb, ()=>{ currentState = "scanning target"; return UpdateTarget(); }, "update_target");
        var hasTarget = new ConditionNode(bb, HasTarget, "has_target");
        var moveToTarget = new ActionNode(bb, ()=>{ currentState = "chasing"; return MoveTowardsTarget(); }, "move_to_target");
        var targetInBasic = new ConditionNode(bb, TargetInBasicRange, "in_basic_range");
        var attackBasic = new ActionNode(bb, ()=>{ currentState = "basic attack"; return AttackBasic(); }, "attack_basic");
        var canWrath = new ConditionNode(bb, ()=>{ return CanDoWrath(); }, "can_wrath");
        var doWrath = new ActionNode(bb, ()=>{ currentState = "wrath windup"; return DoWrath(); }, "do_wrath");
        var canWings = new ConditionNode(bb, ()=>{ return CanDoWings(); }, "can_wings");
        var doWings = new ActionNode(bb, ()=>{ currentState = "wings windup"; return DoWings(); }, "do_wings");
        var patrol = new ActionNode(bb, ()=>{ currentState = enablePatrol ? "patrolling" : "idle"; return Patrol(); }, "patrol");

        var abilitySelector = new Selector(bb, "abilities_or_chase");
        abilitySelector.Add(
            new Sequence(bb, "basic_seq").Add(targetInBasic, attackBasic),
            new Sequence(bb, "wrath_seq").Add(canWrath, doWrath),
            new Sequence(bb, "wings_seq").Add(canWings, doWings),
            moveToTarget
        );

        tree = new Selector(bb, "root").Add(
            new Sequence(bb, "combat").Add(updateTarget, hasTarget, abilitySelector),
            patrol
        );
    }

    private bool IsAtPatrolTarget()
    {
        if (patrolTarget == Vector3.zero) return false;
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTar = new Vector3(patrolTarget.x, 0f, patrolTarget.z);
        return Vector3.Distance(flatPos, flatTar) < 0.5f;
    }

    #if UNITY_2023_1_OR_NEWER
    private static PlayerStats[] FindAllPlayers() => Object.FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
    #else
    private static PlayerStats[] FindAllPlayers() => Object.FindObjectsOfType<PlayerStats>();
    #endif

    private NodeState UpdateTarget()
    {
        if (IsBusy) return NodeState.Success;
        var players = FindAllPlayers();
        float best = float.MaxValue;
        Transform nearest = null;
        foreach (var p in players)
        {
            if (p == null) continue;
            float d = Vector3.Distance(transform.position, p.transform.position);
            if (d < best)
            {
                best = d; nearest = p.transform;
            }
        }
        if (nearest != null && best <= 18f)
        {
            targetPlayer = nearest;
            bb.Set("target", targetPlayer);
        }
        return NodeState.Success;
    }

    private bool HasTarget()
    {
        var t = bb.Get<Transform>("target");
        return t != null;
    }

    private bool TargetInBasicRange()
    {
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float distance = Vector3.Distance(transform.position, t.position);
        return distance <= basicRange;
    }

    private NodeState MoveTowardsTarget()
    {
        var t = bb.Get<Transform>("target");
        if (t == null || controller == null) return NodeState.Failure;
        if (attackLockTimer > 0f || isWrathing || isWingsing || activeAbility != null) return NodeState.Running;

        Vector3 dir = (t.position - transform.position);
        dir.y = 0f;
        if (dir.magnitude <= basicRange)
        {
            bool basicReady = (Time.time - lastBasicTime) >= basicCooldown;
            if (!basicReady && orbitWhenBasicOnCooldown)
            {
                Vector3 fwd = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
                Vector3 tangent = Vector3.Cross(Vector3.up, fwd);
                float speed = chaseSpeed * Mathf.Clamp(orbitSpeedMultiplier, 0.1f, 2f);
                if (controller.enabled)
                    controller.SimpleMove(tangent * speed);
                var lookOrbit = new Vector3(t.position.x, transform.position.y, t.position.z);
                transform.LookAt(lookOrbit);
                currentState = "orbiting (basic cd)";
                return NodeState.Running;
            }
            return NodeState.Success;
        }
        dir.Normalize();
        if (controller.enabled)
            controller.SimpleMove(dir * chaseSpeed);
        var look = new Vector3(t.position.x, transform.position.y, t.position.z);
        transform.LookAt(look);
        return NodeState.Running;
    }

    private NodeState AttackBasic()
    {
        if (IsBusy) return NodeState.Failure;
        if (Time.time - lastBasicTime < basicCooldown) return NodeState.Failure;
        var t = bb.Get<Transform>("target");
        if (t == null) return NodeState.Failure;
        if (activeAbility != null) return NodeState.Running;

        Debug.Log($"[MinokawaAI] Starting basic attack on {t.name}");
        activeAbility = StartCoroutine(BasicRoutine(t));
        return NodeState.Running;
    }

    private System.Collections.IEnumerator BasicRoutine(Transform target)
    {
        currentAction = ActionState.Basic;
        lastBasicTime = Time.time;
        if (animator != null && HasTrigger(attackTrigger)) animator.SetTrigger(attackTrigger);
        float wind = basicWindup;
        float timeout = abilityHardTimeout;
        while (wind > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            wind -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            if (target != null) FaceTarget(target);
            yield return null;
        }
        bool applied = false;
        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist <= basicRange + 0.6f)
            {
                DamageRelay.ApplyToPlayer(target.gameObject, basicDamage);
                applied = true;
            }
        }
        if (!applied)
        {
            Vector3 center = transform.position + transform.forward * (basicRange * 0.5f);
            float radius = Mathf.Max(0.8f, basicRange);
            var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null)
                    DamageRelay.ApplyToPlayer(ps.gameObject, basicDamage);
            }
        }
        float rec = Mathf.Max(0.1f, attackMoveLock);
        while (rec > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            rec -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            if (target != null) FaceTarget(target);
            yield return null;
        }
        currentAction = ActionState.None;
        activeAbility = null;
        currentState = "idle";
    }

    private bool CanDoWrath()
    {
        if (IsBusy) return false;
        if (postSpecialTimer > 0f) return false;
        if (isWrathing) return false;
        if (Time.time - lastWrathTime < wrathCooldown) return false;
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float d = Vector3.Distance(transform.position, t.position);
        return d <= wrathRange + 2f;
    }

    private NodeState DoWrath()
    {
        if (IsBusy) return NodeState.Running;
        if (activeAbility != null) return NodeState.Running;
        lastWrathTime = Time.time;
        activeAbility = StartCoroutine(WrathRoutine());
        return NodeState.Success;
    }

    private System.Collections.IEnumerator WrathRoutine()
    {
        currentAction = ActionState.Wrath;
        isWrathing = true;
        float wind = wrathWindup;
        float timeout = abilityHardTimeout;
        if (animator != null && HasTrigger(wrathTrigger)) animator.SetTrigger(wrathTrigger);
        var t = bb.Get<Transform>("target");
        if (t != null) FaceTarget(t);
        while (wind > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            wind -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        // AOE
        var cols = Physics.OverlapSphere(transform.position, wrathRadius, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null)
                DamageRelay.ApplyToPlayer(ps.gameObject, wrathDamage);
        }
        isWrathing = false;
        float rec = Mathf.Max(0.1f, attackMoveLock);
        while (rec > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            rec -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        postSpecialTimer = postSpecialLock;
        currentAction = ActionState.None;
        activeAbility = null;
        currentState = "idle";
        yield break;
    }

    private bool CanDoWings()
    {
        if (IsBusy) return false;
        if (postSpecialTimer > 0f) return false;
        if (isWingsing) return false;
        if (Time.time - lastWingsTime < wingsCooldown) return false;
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float d = Vector3.Distance(transform.position, t.position);
        return d <= wingsRange + 2f;
    }

    private NodeState DoWings()
    {
        if (IsBusy) return NodeState.Running;
        if (activeAbility != null) return NodeState.Running;
        lastWingsTime = Time.time;
        activeAbility = StartCoroutine(WingsRoutine());
        return NodeState.Success;
    }

    private System.Collections.IEnumerator WingsRoutine()
    {
        currentAction = ActionState.Wings;
        isWingsing = true;
        float wind = wingsWindup;
        float timeout = abilityHardTimeout;
        if (animator != null && HasTrigger(wingsTrigger)) animator.SetTrigger(wingsTrigger);
        var t = bb.Get<Transform>("target");
        if (t != null) FaceTarget(t);
        while (wind > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            wind -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        // cone
        var cols = Physics.OverlapSphere(transform.position, wingsRange, LayerMask.GetMask("Player"));
        foreach (var c in cols)
        {
            var ps = c.GetComponentInParent<PlayerStats>();
            if (ps != null)
            {
                Vector3 to = (ps.transform.position - transform.position); to.y = 0f;
                float angle = Vector3.Angle(transform.forward, to);
                if (angle <= wingsConeAngle * 0.5f)
                    DamageRelay.ApplyToPlayer(ps.gameObject, wingsDamage);
            }
        }
        isWingsing = false;
        float rec = Mathf.Max(0.1f, attackMoveLock);
        while (rec > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            rec -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        postSpecialTimer = postSpecialLock;
        currentAction = ActionState.None;
        activeAbility = null;
        currentState = "idle";
        yield break;
    }

    private NodeState Patrol()
    {
        if (!enablePatrol) return NodeState.Failure;
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTar = new Vector3(patrolTarget.x, 0f, patrolTarget.z);
        if (patrolTarget == Vector3.zero || Vector3.Distance(flatPos, flatTar) < 0.5f)
        {
            if (patrolWaitTimer <= 0f)
            {
                Vector2 circle = Random.insideUnitCircle * patrolRadius;
                patrolTarget = new Vector3(spawnPoint.x + circle.x, transform.position.y, spawnPoint.z + circle.y);
                patrolWaitTimer = patrolWait;
            }
            else
            {
                patrolWaitTimer -= Time.deltaTime;
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                return NodeState.Running;
            }
        }
        Vector3 dir = (patrolTarget - transform.position);
        dir.y = 0f;
        if (dir.magnitude > 0.1f)
        {
            dir.Normalize();
            if (controller.enabled)
                controller.SimpleMove(dir * patrolSpeed);
            transform.LookAt(new Vector3(patrolTarget.x, transform.position.y, patrolTarget.z));
            return NodeState.Running;
        }
        return NodeState.Success;
    }

    private void EnsureDebugOverlay()
    {
        if (debugText != null) return;
        var go = new GameObject("MinokawaDebugText");
        go.transform.SetParent(transform);
        go.transform.localPosition = new Vector3(0f, debugYOffset, 0f);
        debugText = go.AddComponent<TextMeshPro>();
        debugText.fontSize = 3;
        debugText.alignment = TextAlignmentOptions.Center;
        debugText.color = debugColor;
        debugText.text = "";
        debugText.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void DestroyDebugOverlay()
    {
        if (debugText != null)
        {
            Destroy(debugText.gameObject);
            debugText = null;
        }
    }

    private void UpdateDebugOverlay()
    {
        var cam = Camera.main;
        if (cam != null)
        {
            debugText.transform.rotation = Quaternion.LookRotation(debugText.transform.position - cam.transform.position);
        }
        debugText.color = debugColor;
        string details = string.Empty;
        if (debugShowDetails)
        {
            var t = bb != null ? bb.Get<Transform>("target") : null;
            float dist = t != null ? Vector3.Distance(transform.position, t.position) : -1f;
            float cdBasic = BasicCooldownRemaining;
            float cdWrath = WrathCooldownRemaining;
            float cdWings = WingsCooldownRemaining;
            details = $"dist:{(dist>=0?dist.ToString("F1"):"-")} lock:{attackLockTimer:F1} wrath:{(isWrathing?"Y":"N")} wings:{(isWingsing?"Y":"N")}\ncd B:{cdBasic:F1} W:{cdWrath:F1} J:{cdWings:F1}";
        }
        var stateLine = IsBusy ? $"BUSY - {currentState}" : currentState;
        debugText.text = string.IsNullOrEmpty(details) ? stateLine : ($"{stateLine}\n{details}");
    }

    public void TakeEnemyDamage(int amount, GameObject source)
    {
        if (isDead) return;
        currentHealth -= Mathf.Max(0, amount);
        if (animator != null && HasTrigger(hitTrigger)) animator.SetTrigger(hitTrigger);
        RefreshHealthBar(true);
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    [PunRPC]
    private void RPC_EnemyTakeDamage(int amount, int sourceViewId)
    {
        GameObject source = null;
        if (sourceViewId > 0)
        {
            var pv = PhotonView.Find(sourceViewId);
            if (pv != null) source = pv.gameObject;
        }
        TakeEnemyDamage(amount, source);
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;
        if (activeAbility != null)
        {
            try { StopCoroutine(activeAbility); } catch { }
            activeAbility = null;
        }
        if (animator != null)
        {
            foreach (var p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger)
                    animator.ResetTrigger(p.name);
            }
            if (HasBool(isDeadBool)) animator.SetBool(isDeadBool, true);
            if (HasBool(busyBool)) animator.SetBool(busyBool, false);
            if (HasTrigger(dieTrigger)) animator.SetTrigger(dieTrigger);
        }
        if (controller != null) controller.enabled = false;
        RefreshHealthBar(true);
        Destroy(gameObject, 5f);
    }

    private void RefreshHealthBar(bool show)
    {
        if (healthBar != null)
        {
            float frac = maxHealth > 0 ? Mathf.Clamp01((float)currentHealth / maxHealth) : 0f;
            healthBar.value = frac;
        }
        if (show && healthBarRoot != null)
        {
            healthBarRoot.SetActive(true);
            if (hbHideCo != null) StopCoroutine(hbHideCo);
            hbHideCo = StartCoroutine(HideHealthBarLater());
        }
    }

    private System.Collections.IEnumerator HideHealthBarLater()
    {
        yield return new WaitForSeconds(healthbarHideDelay);
        if (!isDead && healthBarRoot != null) healthBarRoot.SetActive(false);
        hbHideCo = null;
    }

    private void FaceTarget(Transform t)
    {
        if (t == null) return;
        var look = new Vector3(t.position.x, transform.position.y, t.position.z);
        transform.LookAt(look);
    }

    private bool HasFloatParam(string param)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name == param) return true;
        return false;
    }
    private bool HasTrigger(string param)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == param) return true;
        return false;
    }
    private bool HasBool(string param)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Bool && p.name == param) return true;
        return false;
    }
}
