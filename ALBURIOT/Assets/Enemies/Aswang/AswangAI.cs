using Photon.Pun;
using UnityEngine;
using AlbuRIOT.AI.BehaviorTree;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class AswangAI : MonoBehaviourPun, IEnemyDamageable
{
    [Header("stats (inline)")]
    public int maxHealth = 200;

    [Header("basic attack")]
    public int basicDamage = 10;
    public float basicRange = 2.0f;
    public float basicCooldown = 1.5f;
    public float basicWindup = 0.3f;

    [Header("special: pounce")]
    public int pounceDamage = 24;
    public float pounceWindup = 0.4f;
    public float pounceCooldown = 5.5f;
    public float pounceLeapDistance = 6f;
    public float pounceLeapSpeed = 12f;
    public float pounceHitRadius = 1.5f;

    [Header("special: shadow swarm")]
    public int swarmTickDamage = 5;
    public float swarmTickInterval = 0.5f;
    public float swarmDuration = 3.0f;
    public float swarmCooldown = 7.5f;
    public float swarmRadius = 3.5f;
    public float swarmWindup = 0.6f;

    [Header("movement: speed settings")]
    public float patrolSpeed = 2f;
    public float chaseSpeed = 4f;
    public float attackMoveLock = 0.35f;
    public bool orbitWhenBasicOnCooldown = true;
    [Range(0.1f, 1.5f)] public float orbitSpeedMultiplier = 0.7f;

    [Header("animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string attackTrigger = "Attack";
    public string pounceTrigger = "Pounce";
    public string swarmTrigger = "Swarm";
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
    private float lastPounceTime;
    private float lastSwarmTime;
    private float attackLockTimer;
    private bool isDead;
    private bool isPouncing;
    private bool isSwarming;
    private Coroutine activeAbility;
    private bool IsBusy => activeAbility != null || isPouncing || attackLockTimer > 0f;
    private enum ActionState { None, Basic, Pounce, Swarm }
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
    public float PounceCooldownRemaining => Mathf.Max(0f, pounceCooldown - (Time.time - lastPounceTime));
    public float SwarmCooldownRemaining => Mathf.Max(0f, swarmCooldown - (Time.time - lastSwarmTime));

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        spawnPoint = transform.position;
        currentHealth = maxHealth;
        lastBasicTime = -9999f;
        lastPounceTime = -9999f;
        lastSwarmTime = -9999f;

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
        var canPounce = new ConditionNode(bb, ()=>{ return CanDoPounce(); }, "can_pounce");
        var doPounce = new ActionNode(bb, ()=>{ currentState = "pounce windup"; return DoPounce(); }, "do_pounce");
        var canSwarm = new ConditionNode(bb, ()=>{ return CanDoSwarm(); }, "can_swarm");
        var doSwarm = new ActionNode(bb, ()=>{ currentState = "swarm windup"; return DoSwarm(); }, "do_swarm");
        var patrol = new ActionNode(bb, ()=>{ currentState = enablePatrol ? "patrolling" : "idle"; return Patrol(); }, "patrol");

        var abilitySelector = new Selector(bb, "abilities_or_chase");
        abilitySelector.Add(
            new Sequence(bb, "basic_seq").Add(targetInBasic, attackBasic),
            new Sequence(bb, "pounce_seq").Add(canPounce, doPounce),
            new Sequence(bb, "swarm_seq").Add(canSwarm, doSwarm),
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
        if (attackLockTimer > 0f || isPouncing || activeAbility != null) return NodeState.Running;

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

        Debug.Log($"[AswangAI] Starting basic attack on {t.name}");
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

    private bool CanDoPounce()
    {
        if (IsBusy) return false;
        if (postSpecialTimer > 0f) return false;
        if (isPouncing) return false;
        if (Time.time - lastPounceTime < pounceCooldown) return false;
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float d = Vector3.Distance(transform.position, t.position);
        return d >= 2.5f && d <= pounceLeapDistance + 2f;
    }

    private NodeState DoPounce()
    {
        if (IsBusy) return NodeState.Running;
        if (activeAbility != null) return NodeState.Running;
        lastPounceTime = Time.time;
        activeAbility = StartCoroutine(PounceRoutine());
        return NodeState.Success;
    }

    private System.Collections.IEnumerator PounceRoutine()
    {
        currentAction = ActionState.Pounce;
        isPouncing = true;
        float wind = pounceWindup;
        float timeout = abilityHardTimeout;
        if (animator != null && HasTrigger(pounceTrigger)) animator.SetTrigger(pounceTrigger);
        var t = bb.Get<Transform>("target");
        if (t != null) FaceTarget(t);
        while (wind > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            wind -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        // leap forward
        float leapTime = Mathf.Min(0.7f, pounceLeapDistance / pounceLeapSpeed);
        float leapSpeed = pounceLeapSpeed;
        Vector3 leapDir = t != null ? (t.position - transform.position) : transform.forward;
        leapDir.y = 0f;
        leapDir.Normalize();
        float leapTimer = leapTime;
        bool hitApplied = false;
        while (leapTimer > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            leapTimer -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(leapDir * leapSpeed);
            // check for hit
            var cols = Physics.OverlapSphere(transform.position, pounceHitRadius, LayerMask.GetMask("Player"));
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null && !hitApplied)
                {
                    DamageRelay.ApplyToPlayer(ps.gameObject, pounceDamage);
                    hitApplied = true;
                }
            }
            yield return null;
        }
        isPouncing = false;
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

    private bool CanDoSwarm()
    {
        if (IsBusy) return false;
        if (postSpecialTimer > 0f) return false;
        if (isSwarming) return false;
        if (Time.time - lastSwarmTime < swarmCooldown) return false;
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float d = Vector3.Distance(transform.position, t.position);
        return d <= swarmRadius + 2f;
    }

    private NodeState DoSwarm()
    {
        if (IsBusy) return NodeState.Running;
        if (activeAbility != null) return NodeState.Running;
        lastSwarmTime = Time.time;
        activeAbility = StartCoroutine(SwarmRoutine());
        return NodeState.Success;
    }

    private System.Collections.IEnumerator SwarmRoutine()
    {
        currentAction = ActionState.Swarm;
        isSwarming = true;
        float wind = swarmWindup;
        float timeout = abilityHardTimeout;
        if (animator != null && HasTrigger(swarmTrigger)) animator.SetTrigger(swarmTrigger);
        while (wind > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            wind -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        // DoT around self
        float swarmTimer = swarmDuration;
        float tick = swarmTickInterval;
        while (swarmTimer > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            swarmTimer -= Time.deltaTime;
            tick -= Time.deltaTime;
            if (tick <= 0f)
            {
                tick = swarmTickInterval;
                var cols = Physics.OverlapSphere(transform.position, swarmRadius, LayerMask.GetMask("Player"));
                foreach (var c in cols)
                {
                    var ps = c.GetComponentInParent<PlayerStats>();
                    if (ps != null)
                        DamageRelay.ApplyToPlayer(ps.gameObject, swarmTickDamage);
                }
            }
            yield return null;
        }
        isSwarming = false;
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
        var go = new GameObject("AswangDebugText");
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
            float cdPounce = PounceCooldownRemaining;
            float cdSwarm = SwarmCooldownRemaining;
            details = $"dist:{(dist>=0?dist.ToString("F1"):"-")} lock:{attackLockTimer:F1} pounce:{(isPouncing?"Y":"N")} swarm:{(isSwarming?"Y":"N")}\ncd B:{cdBasic:F1} P:{cdPounce:F1} S:{cdSwarm:F1}";
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
        // stop any running ability coroutine so we don't apply damage after death
        if (activeAbility != null)
        {
            try { StopCoroutine(activeAbility); } catch { }
            activeAbility = null;
        }
        // clear/mark animator state
        if (animator != null)
        {
            // reset triggers to avoid lingering attack/special triggers
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
