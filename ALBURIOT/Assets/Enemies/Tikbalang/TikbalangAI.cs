using Photon.Pun;
using UnityEngine;
using AlbuRIOT.AI.BehaviorTree;
using TMPro;
using UnityEngine.UI;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class TikbalangAI : MonoBehaviourPun, IEnemyDamageable
{
    [Header("stats (inline)")]
    public int maxHealth = 200;

    [Header("basic attack")]
    public int basicDamage = 10;
    public float basicRange = 3.0f;
    public float basicCooldown = 1.5f;
    public float basicWindup = 0.5f;

    [Header("special: charge")]
    public int chargeDamage = 35;
    public float chargeCooldown = 30f;
    public float chargeWindup = 2f;
    public float chargeDuration = 2f;
    public float chargeSpeed = 10f;
    public float chargeHitRadius = 1.7f;
    public float chargeMinDistance = 10f;
    public float chargeRecoverDuration = 3f;
    public string chargeWindupTrigger = "ChargeWindup";
    public string chargeTrigger = "Charge";

    [Header("special: stomp")]
    public int stompDamage = 25;
    public float stompRadius = 7f;
    public float stompCooldown = 15f;
    public float stompWindup = 0.5f;
    public float stompRecoverDuration = 0.5f;
    [Tooltip("minimum distance before considering stomp (keeps very-close situations for basic)")]
    public float stompMinDistance = 4f;

    [Header("ranges")]
    public float detectionRange = 12f;
    public float chaseLoseRange = 12f;

    [Header("decision: preferred ranges")]
    // decision: preferred ranges
    [Tooltip("if target distance is <= this, prefer BASIC attack")] public float preferBasicMax = 3f;
    [Tooltip("prefer STOMP when target distance is >= this")] public float preferStompMin = 4f;
    [Tooltip("prefer STOMP when target distance is <= this (set 0 to disable max)")] public float preferStompMax = 7f;
    [Tooltip("prefer CHARGE when target distance is >= this")] public float preferChargeMin = 8f;
    [Tooltip("prefer CHARGE when target distance is <= this (set 0 to disable max)")] public float preferChargeMax = 0f;

    [Header("movement: speed settings")]
    // movement: speed settings
    [Tooltip("base movement speed for patrol and idle")] public float patrolSpeed = 2f;
    [Tooltip("movement speed when chasing a target")] public float chaseSpeed = 5f;
    public float attackMoveLock = 0.5f;
    [Tooltip("when in basic range but basic is cooling down, orbit the target instead of standing still")]
    public bool orbitWhenBasicOnCooldown = true;
    [Range(0.1f, 1.5f)]
    [Tooltip("multiplier applied to chaseSpeed while orbiting")]
    public float orbitSpeedMultiplier = 0.5f;

    [Header("animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string attackTrigger = "Attack";
    public string stompTrigger = "Stomp";
    public string hitTrigger = "Hit";
    public string dieTrigger = "Die";
    public string isDeadBool = "IsDead";

    [Header("behavior")]
    public bool enablePatrol = true;
    public float patrolRadius = 10f;
    public float patrolWait = 2f;
    // animation event damage removed: all hits are applied via code, not animation events
    

    [Header("ui")]
    public GameObject healthBarRoot;
    public Slider healthBar;
    public float healthbarHideDelay = 3f;
    private Coroutine hbHideCo;

    private CharacterController controller;
    private Vector3 spawnPoint;
    private int currentHealth;
    private float lastBasicTime;
    private float lastChargeTime;
    private float lastStompTime;
    private float attackLockTimer;
    private bool isDead;
    private bool isCharging;
    private bool chargeDidHit;
    private float chargeTimer;
    private Coroutine activeAbility;
    private bool IsBusy => activeAbility != null || isCharging || attackLockTimer > 0f;
    private enum ActionState { None, Basic, Stomp, Charge }
    private ActionState currentAction = ActionState.None;
    public float abilityHardTimeout = 8f; // safety cutoff to avoid ever getting stuck
    [Header("global ability gating")]
    public float postSpecialLock = 0.75f; // after a special, wait a bit before attempting another special
    private float postSpecialTimer = 0f;
    

    // utility: find all players with version-agnostic api
    private static PlayerStats[] FindAllPlayers()
    {
#if UNITY_2023_1_OR_NEWER
    return Object.FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
#else
    return Object.FindObjectsOfType<PlayerStats>();
#endif
    }

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

    // cooldown helpers (for UI / debug)
    public float BasicCooldownRemaining => Mathf.Max(0f, basicCooldown - (Time.time - lastBasicTime));
    public float ChargeCooldownRemaining => Mathf.Max(0f, chargeCooldown - (Time.time - lastChargeTime));
    public float StompCooldownRemaining  => Mathf.Max(0f, stompCooldown  - (Time.time - lastStompTime));

    public float BasicCooldownProgress => basicCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(BasicCooldownRemaining / basicCooldown);
    public float ChargeCooldownProgress => chargeCooldown <= 0f ? 1f : 1f - Mathf.Clamp01(ChargeCooldownRemaining / chargeCooldown);
    public float StompCooldownProgress  => stompCooldown  <= 0f ? 1f : 1f - Mathf.Clamp01(StompCooldownRemaining / stompCooldown);

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
    spawnPoint = transform.position;
    currentHealth = maxHealth;
    // abilities should be available immediately on spawn
    lastBasicTime = -9999f;
    lastChargeTime = -9999f;
    lastStompTime = -9999f;

        // ensure there is no second AI driving this character (prevents double-ticking/looping)
        // Use name-based lookup to avoid assembly reference issues
        var genericAi = GetComponent("EnemyAIController") as Behaviour;
        if (genericAi != null && genericAi.enabled)
        {
            Debug.LogWarning($"{name}: EnemyAIController found alongside TikbalangAI; disabling generic AI to avoid conflicts.");
            genericAi.enabled = false;
        }
        // guard against accidental duplicates of this component
        var dupes = GetComponents<TikbalangAI>();
        if (dupes != null && dupes.Length > 1)
        {
            for (int i = 1; i < dupes.Length; i++)
                dupes[i].enabled = false;
            Debug.LogWarning($"{name}: multiple TikbalangAI components detected; disabled extras.");
        }

        bb = new Blackboard { owner = gameObject };
        BuildTree();

        // find & init health bar
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

    // helper: are we at patrol target?
    private bool IsAtPatrolTarget()
    {
        if (patrolTarget == Vector3.zero) return false;
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTar = new Vector3(patrolTarget.x, 0f, patrolTarget.z);
        return Vector3.Distance(flatPos, flatTar) < 0.5f;
    }

    private void Update()
    {
        if (isDead) return;

        // master client authority
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;

        // locomotion param (set speed to 0 if not moving or at patrol target)
        if (animator != null && HasFloatParam(speedParam))
        {
            float planar = 0f;
            if (controller != null && controller.enabled)
            {
                Vector3 v = controller.velocity;
                planar = new Vector3(v.x, 0f, v.z).magnitude;
                // force speed to 0 if not moving or at patrol target
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
        

        // if busy with an ability, do not run the behavior tree (prevents re-detection or stacking moves)
        if (!IsBusy)
        {
            var _ = tree != null ? tree.Tick() : NodeState.Failure;
        }
        else
        {
            // while busy, skip tree tick but continue updating ui/billboard/debug
            currentState = IsBusy ? $"busy - {currentAction}" : currentState;
        }

        // billboard healthbar to camera (even while busy)
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

        // immediate lose-target check (runs even while busy)
        var tgt = bb != null ? bb.Get<Transform>("target") : null;
        if (tgt != null && Vector3.Distance(transform.position, tgt.position) > chaseLoseRange)
        {
            targetPlayer = null; bb.Remove("target"); currentState = "lost target";
        }

        // debug overlay maintenance
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
        var canStomp = new ConditionNode(bb, ()=>{ return CanDoStomp(); }, "can_stomp");
        var doStomp = new ActionNode(bb, ()=>{ currentState = "stomp windup"; return DoStomp(); }, "do_stomp");
        var canCharge = new ConditionNode(bb, ()=>{ return CanDoCharge(); }, "can_charge");
        var doCharge = new ActionNode(bb, ()=>{ currentState = "charge windup"; return DoCharge(); }, "do_charge");
        var patrol = new ActionNode(bb, ()=>{ currentState = enablePatrol ? "patrolling" : "idle"; return Patrol(); }, "patrol");

    // Priority selector for abilities so we re-evaluate each tick
    var abilitySelector = new Selector(bb, "abilities_or_chase");
        abilitySelector.Add(
            new Sequence(bb, "basic_seq").Add(targetInBasic, attackBasic),
            new Sequence(bb, "stomp_seq").Add(canStomp, doStomp),
            new Sequence(bb, "charge_seq").Add(canCharge, doCharge),
            moveToTarget
        );

        tree = new Selector(bb, "root").Add(
            new Sequence(bb, "combat").Add(updateTarget, hasTarget, abilitySelector),
            patrol
        );
    }

    private float targetScanInterval = 0.5f;
    private float targetScanTimer = 0f;

    private NodeState UpdateTarget()
    {
        if (IsBusy) return NodeState.Success; // do not change targets while executing a move
        if (targetScanTimer > 0f)
        {
            targetScanTimer -= Time.deltaTime;
        }
        if (targetScanTimer <= 0f)
        {
            targetScanTimer = targetScanInterval;
            // If we already have a target, keep it until it exceeds chaseLoseRange
            var current = bb.Get<Transform>("target");
            if (current != null)
            {
                float dist = Vector3.Distance(transform.position, current.position);
                if (dist > chaseLoseRange)
                {
                    targetPlayer = null;
                    bb.Remove("target");
                }
            }
            else
            {
                // Acquire nearest only if within detection range
                float best = float.MaxValue;
                Transform nearest = null;
                var players = FindAllPlayers();
                foreach (var p in players)
                {
                    if (p == null) continue;
                    float d = Vector3.Distance(transform.position, p.transform.position);
                    if (d < best)
                    {
                        best = d; nearest = p.transform;
                    }
                }
                if (nearest != null && best <= detectionRange)
                {
                    targetPlayer = nearest;
                    bb.Set("target", targetPlayer);
                }
            }
        }

        // lose target if too far from us (effective chase range)
        var t = bb.Get<Transform>("target");
        if (t != null && Vector3.Distance(transform.position, t.position) > chaseLoseRange)
        {
            targetPlayer = null;
            bb.Remove("target");
        }
        return NodeState.Success;
    }

    private bool HasTarget()
    {
        var t = bb.Get<Transform>("target");
        return t != null;
    }

    private bool TargetInDetectionRange()
    {
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        return Vector3.Distance(transform.position, t.position) <= detectionRange;
    }

    private bool TargetInBasicRange()
    {
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float distance = Vector3.Distance(transform.position, t.position);
        // inspector-controlled preference for basic selection
        bool inRange = distance <= Mathf.Max(0.05f, preferBasicMax);
        return inRange;
    }

    private NodeState MoveTowardsTarget()
    {
        var t = bb.Get<Transform>("target");
        if (t == null || controller == null) return NodeState.Failure;
        if (attackLockTimer > 0f || isCharging || activeAbility != null) return NodeState.Running; // don't move during abilities/windups

        Vector3 dir = (t.position - transform.position);
        dir.y = 0f;
        var dist = dir.magnitude;
        // if within preferred basic range (inspector-set)
        if (dist <= Mathf.Max(0.05f, preferBasicMax))
        {
            bool basicReady = (Time.time - lastBasicTime) >= basicCooldown;
            if (!basicReady && orbitWhenBasicOnCooldown)
            {
                // orbit around target while waiting on basic cooldown
                Vector3 fwd = dir.sqrMagnitude > 0.0001f ? dir.normalized : transform.forward;
                Vector3 tangent = Vector3.Cross(Vector3.up, fwd); // left strafe
                float speed = chaseSpeed * Mathf.Clamp(orbitSpeedMultiplier, 0.1f, 2f);
                if (controller.enabled)
                    controller.SimpleMove(tangent * speed);
                // keep facing the target while orbiting
                var lookOrbit = new Vector3(t.position.x, transform.position.y, t.position.z);
                transform.LookAt(lookOrbit);
                currentState = "orbiting (basic cd)";
                return NodeState.Running;
            }
            // stop and let basic branch take over next tick
            return NodeState.Success;
        }
        dir.Normalize();
        if (controller.enabled)
            controller.SimpleMove(dir * chaseSpeed);
        // rotate towards target
        var look = new Vector3(t.position.x, transform.position.y, t.position.z);
        transform.LookAt(look);
        return NodeState.Running;
    }

    private NodeState AttackBasic()
    {
        // if we can't actually start a basic now, fail so selector can try stomp/charge/chase
        if (IsBusy) return NodeState.Failure; // Extra safety check
        if (Time.time - lastBasicTime < basicCooldown) return NodeState.Failure;
        var t = bb.Get<Transform>("target");
        if (t == null) 
        {
            Debug.Log($"[TikbalangAI] AttackBasic failed - no target in blackboard");
            return NodeState.Failure;
        }
        if (activeAbility != null) return NodeState.Running;

        Debug.Log($"[TikbalangAI] Starting basic attack on {t.name}");
        activeAbility = StartCoroutine(BasicRoutine(t));
        return NodeState.Running;
    }

    private System.Collections.IEnumerator BasicRoutine(Transform target)
    {
        currentAction = ActionState.Basic;
        lastBasicTime = Time.time;
        Debug.Log($"[TikbalangAI] Basic attack cooldown started at {Time.time}");
        // windup
        if (animator != null && HasTrigger(attackTrigger)) animator.SetTrigger(attackTrigger);
        float wind = basicWindup;
        float timeout = abilityHardTimeout;
        while (wind > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            wind -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            // face target during windup
            if (target != null) FaceTarget(target);
            yield return null;
        }
        // hit - robust: prefer the tracked target, else a forward arc overlap
        bool applied = false;
        if (target != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            if (dist <= basicRange + 0.6f)
            {
                Debug.Log($"[TikbalangAI] Basic attack hitting {target.name} for {basicDamage} dmg (dist {dist:F2})");
                DamageRelay.ApplyToPlayer(target.gameObject, basicDamage);
                applied = true;
            }
        }
        if (!applied)
        {
            Vector3 center = transform.position + transform.forward * (basicRange * 0.5f);
            float radius = Mathf.Max(0.8f, basicRange);
            var cols = Physics.OverlapSphere(center, radius, LayerMask.GetMask("Player"));
            var candidates = new System.Collections.Generic.List<GameObject>();
            if (cols != null && cols.Length > 0)
            {
                foreach (var c in cols)
                {
                    var ps = c.GetComponentInParent<PlayerStats>();
                    if (ps != null)
                    {
                        Vector3 to = (ps.transform.position - transform.position); to.y = 0f;
                        if (Vector3.Angle(transform.forward, to) <= 60f)
                            candidates.Add(ps.gameObject);
                    }
                }
            }
            if (candidates.Count == 0)
            {
                var players = FindAllPlayers();
                foreach (var ps in players)
                {
                    if (ps == null) continue;
                    float d = Vector3.Distance(center, ps.transform.position);
                    if (d <= radius)
                    {
                        Vector3 to = (ps.transform.position - transform.position); to.y = 0f;
                        if (Vector3.Angle(transform.forward, to) <= 60f)
                            candidates.Add(ps.gameObject);
                    }
                }
            }
            if (candidates.Count > 0)
            {
                GameObject best = candidates[0]; float bestD = Vector3.Distance(transform.position, best.transform.position);
                for (int i=1;i<candidates.Count;i++)
                {
                    float d = Vector3.Distance(transform.position, candidates[i].transform.position);
                    if (d < bestD) { bestD = d; best = candidates[i]; }
                }
                Debug.Log($"[TikbalangAI] Basic overlap hit {best.name} for {basicDamage} dmg (dist {bestD:F2})");
                DamageRelay.ApplyToPlayer(best, basicDamage);
            }
        }
        // brief recover using attackMoveLock
        currentState = "basic recover";
        float rec = Mathf.Max(0.1f, attackMoveLock);
        while (rec > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            rec -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            // keep facing target during brief recovery
            if (target != null) FaceTarget(target);
            yield return null;
        }
        currentAction = ActionState.None;
        activeAbility = null;
        // after a basic, continue as normal
        currentState = "idle";
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
                // pick a random point in XZ circle around spawn
                Vector2 circle = Random.insideUnitCircle * patrolRadius;
                patrolTarget = new Vector3(spawnPoint.x + circle.x, transform.position.y, spawnPoint.z + circle.y);
                patrolWaitTimer = patrolWait;
            }
            else
            {
                patrolWaitTimer -= Time.deltaTime;
                // ensure we do not move while waiting
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

    // animation event damage removed (handled in BasicRoutine)

    // ===== Debug Overlay =====
    private void EnsureDebugOverlay()
    {
        if (debugText != null) return;
        var go = new GameObject("TikbalangDebugText");
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
        // face camera
        var cam = Camera.main;
        if (cam != null)
        {
            debugText.transform.rotation = Quaternion.LookRotation(debugText.transform.position - cam.transform.position);
        }

        // update color in case user changed it
        debugText.color = debugColor;

        // build state text
        string details = string.Empty;
        if (debugShowDetails)
        {
            var t = bb != null ? bb.Get<Transform>("target") : null;
            float dist = t != null ? Vector3.Distance(transform.position, t.position) : -1f;
            float cdBasic = Mathf.Max(0f, basicCooldown - (Time.time - lastBasicTime));
            float cdCharge = Mathf.Max(0f, chargeCooldown - (Time.time - lastChargeTime));
            float cdStomp = Mathf.Max(0f, stompCooldown - (Time.time - lastStompTime));
            details = $"dist:{(dist>=0?dist.ToString("F1"):"-")} lock:{attackLockTimer:F1} chg:{(isCharging?"Y":"N")}\n" +
                      $"cd B:{cdBasic:F1} C:{cdCharge:F1} S:{cdStomp:F1}";
        }
        var stateLine = IsBusy ? $"BUSY - {currentState}" : currentState;
        debugText.text = string.IsNullOrEmpty(details) ? stateLine : ($"{stateLine}\n{details}");
    }

    // STOMP
    private bool CanDoStomp()
    {
        if (IsBusy) return false;
        if (postSpecialTimer > 0f) return false;
        
        if (Time.time - lastStompTime < stompCooldown) return false;
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float d = Vector3.Distance(transform.position, t.position);
        // prefer stomp purely by inspector-set preferred band
        float min = Mathf.Max(0.05f, preferStompMin);
        float max = preferStompMax > 0f ? preferStompMax : float.MaxValue;
        bool canStomp = d >= min && d <= max;
        if (canStomp)
        {
            Debug.Log($"[TikbalangAI] prefer STOMP: dist {d:F2} in [{min:F2},{(max<float.MaxValue?max.ToString("F2"):"∞")}]");
        }
        return canStomp;
    }

    private NodeState DoStomp()
    {
        if (IsBusy) return NodeState.Running; // Extra safety check
        if (activeAbility != null) return NodeState.Running;
        // mark cooldown at start to avoid any re-entry edge cases
        lastStompTime = Time.time;
        Debug.Log($"[TikbalangAI] Stomp cooldown started at {Time.time}");
        activeAbility = StartCoroutine(StompRoutine());
        return NodeState.Success; // Return success immediately to prevent re-evaluation
    }

    private System.Collections.IEnumerator StompRoutine()
    {
        // windup
    if (animator != null && HasTrigger(stompTrigger)) animator.SetTrigger(stompTrigger);
        // stay still during windup
        float sw = stompWindup;
        float timeout = abilityHardTimeout;
        currentAction = ActionState.Stomp;
        while (sw > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            sw -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }

    // apply aoe damage to all players in radius
    var cols = Physics.OverlapSphere(transform.position, stompRadius, LayerMask.GetMask("Player"));
        // fallback if Player layer not used: just find by PlayerStats
        if (cols == null || cols.Length == 0)
        {
            var players = FindAllPlayers();
            foreach (var ps in players)
            {
                if (ps != null && Vector3.Distance(transform.position, ps.transform.position) <= stompRadius)
                    DamageRelay.ApplyToPlayer(ps.gameObject, stompDamage);
            }
        }
        else
        {
            var seen = new System.Collections.Generic.HashSet<GameObject>();
            foreach (var c in cols)
            {
                var ps = c.GetComponentInParent<PlayerStats>();
                if (ps != null) seen.Add(ps.gameObject);
            }
            foreach (var go in seen) DamageRelay.ApplyToPlayer(go, stompDamage);
        }

        // brief recovery
        float sr = stompRecoverDuration;
        while (sr > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            sr -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        postSpecialTimer = postSpecialLock;
        currentState = "stomp hit";
        currentAction = ActionState.None;
        activeAbility = null;
        currentState = "idle";
        yield break;
    }

    // CHARGE
    private bool CanDoCharge()
    {
        if (IsBusy) return false;
        if (postSpecialTimer > 0f) return false;
        if (isCharging) return false;
        
        if (Time.time - lastChargeTime < chargeCooldown) return false;
        var t = bb.Get<Transform>("target");
        if (t == null) return false;
        float d = Vector3.Distance(transform.position, t.position);
        // prefer charge using inspector-set band
        float min = Mathf.Max(0.05f, preferChargeMin);
        bool canCharge = d >= min && (preferChargeMax <= 0f || d <= preferChargeMax);
        if (canCharge)
        {
            string maxStr = preferChargeMax > 0f ? preferChargeMax.ToString("F2") : "∞";
            Debug.Log($"[TikbalangAI] prefer CHARGE: dist {d:F2} in [{min:F2},{maxStr}]");
        }
        return canCharge;
    }

    private NodeState DoCharge()
    {
        if (IsBusy) return NodeState.Running; // Extra safety check
        if (activeAbility != null) return NodeState.Running;
        // mark cooldown at start to avoid any re-entry edge cases
        lastChargeTime = Time.time;
        Debug.Log($"[TikbalangAI] Charge cooldown started at {Time.time}");
        activeAbility = StartCoroutine(ChargeRoutine());
        return NodeState.Success; // Return success immediately to prevent re-evaluation
    }

    private System.Collections.IEnumerator ChargeRoutine()
    {
        // windup
        if (animator != null && HasTrigger(chargeWindupTrigger)) animator.SetTrigger(chargeWindupTrigger);
        var t = bb.Get<Transform>("target");
        if (t != null)
        {
            var look = new Vector3(t.position.x, transform.position.y, t.position.z);
            transform.LookAt(look);
        }
        // freeze in windup
        float wind = chargeWindup;
        float timeout = abilityHardTimeout;
        currentAction = ActionState.Charge;
        while (wind > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            wind -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        if (animator != null && HasTrigger(chargeTrigger)) animator.SetTrigger(chargeTrigger);

        // dash forward for duration
        isCharging = true;
        chargeDidHit = false;
        // track already-damaged players this charge
        var chargeVictims = new System.Collections.Generic.HashSet<GameObject>();
        chargeTimer = chargeDuration;
        // lock facing forward during charge
        Vector3 chargeForward = transform.forward;
        while (chargeTimer > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            currentState = chargeDidHit ? "charge (hit)" : "charging";
            chargeTimer -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(chargeForward * chargeSpeed);

            // damage any players touched during this frame (each once per charge)
            bool anyHitThisFrame = false;
            var cols = Physics.OverlapSphere(transform.position, chargeHitRadius, LayerMask.GetMask("Player"));
            if (cols != null && cols.Length > 0)
            {
                foreach (var c in cols)
                {
                    var ps = c.GetComponentInParent<PlayerStats>();
                    if (ps == null) continue;
                    var root = ps.gameObject;
                    if (chargeVictims.Add(root))
                    {
                        DamageRelay.ApplyToPlayer(root, chargeDamage);
                        anyHitThisFrame = true;
                    }
                }
            }
            else
            {
                // fallback by distance if Player layer not configured
                var players = FindAllPlayers();
                foreach (var ps in players)
                {
                    if (ps == null) continue;
                    float d = Vector3.Distance(transform.position, ps.transform.position);
                    if (d <= chargeHitRadius && chargeVictims.Add(ps.gameObject))
                    {
                        DamageRelay.ApplyToPlayer(ps.gameObject, chargeDamage);
                        anyHitThisFrame = true;
                    }
                }
            }
            if (anyHitThisFrame) chargeDidHit = true;
            yield return null;
        }
        isCharging = false;
        // small recover window
        float rec = chargeRecoverDuration;
        while (rec > 0f && (timeout -= Time.deltaTime) > 0f)
        {
            rec -= Time.deltaTime;
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            yield return null;
        }
        postSpecialTimer = postSpecialLock;
        currentState = "charge recover";
        currentAction = ActionState.None;
        activeAbility = null;
        currentState = "idle";
        yield break;
    }

    // IEnemyDamageable impl + RPC
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
        if (animator != null)
        {
            if (HasBool(isDeadBool)) animator.SetBool(isDeadBool, true);
            if (HasTrigger(dieTrigger)) animator.SetTrigger(dieTrigger);
        }
        if (controller != null) controller.enabled = false;
        RefreshHealthBar(true);
        // optional: destroy after animation
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

    // rotate to face a target on the horizontal plane
    private void FaceTarget(Transform t)
    {
        if (t == null) return;
        var look = new Vector3(t.position.x, transform.position.y, t.position.z);
        transform.LookAt(look);
    }

    private System.Collections.IEnumerator HideHealthBarLater()
    {
        yield return new WaitForSeconds(healthbarHideDelay);
        if (!isDead && healthBarRoot != null) healthBarRoot.SetActive(false);
        hbHideCo = null;
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
