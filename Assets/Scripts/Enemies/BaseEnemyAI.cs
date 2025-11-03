using UnityEngine;
using Photon.Pun;
using AlbuRIOT.AI.BehaviorTree;
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public abstract class BaseEnemyAI : MonoBehaviourPun, IEnemyDamageable
{
    [Header("Enemy Data")]
    public EnemyData enemyData;
    
    [Header("Animation")]
    public Animator animator;
    public string speedParam = "Speed";
    public string attackTrigger = "Attack"; // Legacy: single trigger OR use separate windup/impact triggers
    public string attackWindupTrigger = "AttackWindup"; // Optional: separate windup trigger
    public string attackImpactTrigger = "AttackImpact"; // Optional: separate impact trigger
    public string hitTrigger = "Hit";
    public string dieTrigger = "Die";
    public string isDeadBool = "IsDead";
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    
    // Core components
    protected CharacterController controller;
    protected int currentHealth;
    protected bool isDead = false;
    protected bool isAttacking = false;
    protected bool isBusy = false;
    // After any attack/ability completes, we apply a short global busy window
    // so the enemy can only do one thing at a time and won't chain attacks instantly.
    protected float globalBusyTimer = 0f;
    
    // AI State
    protected Blackboard blackboard;
    protected Node behaviorTree;
    protected Transform targetPlayer;
    protected Vector3 spawnPoint;
    protected float lastAttackTime;
    protected float attackLockTimer = 0f;
    
    
    // Movement
    protected Vector3 patrolTarget;
    protected float patrolWaitTimer;
    // Note: Speed Settings and Chase/Orbit properties are now in EnemyData

    [Header("Buff VFX Prefabs (optional)")]
    public GameObject buffDamageVFXPrefab;
    public GameObject buffSpeedVFXPrefab;
    public GameObject buffStaminaVFXPrefab;
    public GameObject buffHealthVFXPrefab;

    [Header("Buff VFX Settings")]
    public Vector3 buffVfxOffset = Vector3.zero;
    public float buffVfxScale = 1.5f;

    private readonly Dictionary<BuffType, GameObject> activeBuffVfx = new Dictionary<BuffType, GameObject>();
    
    // Events
    public System.Action<BaseEnemyAI> OnEnemyDied;
    public System.Action<BaseEnemyAI, int> OnEnemyTookDamage;
    
    #region Unity Lifecycle
    
#if UNITY_EDITOR
    void Reset()
    {
        // Unity should automatically apply default references when Reset() is called
        // This happens when you add the component or click Reset in inspector
        // If defaults still aren't applied, use Resources fallback in OnValidate
    }
    
    void OnValidate()
    {
        // In editor, try to load default prefabs from Resources if fields are null
        // This provides a fallback if Unity's default references weren't applied
        if (buffDamageVFXPrefab == null)
            buffDamageVFXPrefab = Resources.Load<GameObject>("BuffVFX/DamageBuff");
        if (buffSpeedVFXPrefab == null)
            buffSpeedVFXPrefab = Resources.Load<GameObject>("BuffVFX/SpeedBuff");
        if (buffStaminaVFXPrefab == null)
            buffStaminaVFXPrefab = Resources.Load<GameObject>("BuffVFX/StaminaBuff");
        if (buffHealthVFXPrefab == null)
            buffHealthVFXPrefab = Resources.Load<GameObject>("BuffVFX/HealthBuff");
    }
#endif
    
    protected virtual void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (animator == null) animator = GetComponentInChildren<Animator>();
        
        spawnPoint = transform.position;
        currentHealth = enemyData != null ? enemyData.maxHealth : 100;
        
        blackboard = new Blackboard { owner = gameObject };
        BuildBehaviorTree();
        
        InitializeEnemy();
        
        // Runtime fallback: try Resources if defaults weren't applied (works in editor and builds)
        if (buffDamageVFXPrefab == null)
            buffDamageVFXPrefab = Resources.Load<GameObject>("BuffVFX/DamageBuff");
        if (buffSpeedVFXPrefab == null)
            buffSpeedVFXPrefab = Resources.Load<GameObject>("BuffVFX/SpeedBuff");
        if (buffStaminaVFXPrefab == null)
            buffStaminaVFXPrefab = Resources.Load<GameObject>("BuffVFX/StaminaBuff");
        if (buffHealthVFXPrefab == null)
            buffHealthVFXPrefab = Resources.Load<GameObject>("BuffVFX/HealthBuff");
    }
    
    protected virtual void Update()
    {
        if (isDead) return;
        
        // Network authority: only master client (or offline) drives AI
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode && !PhotonNetwork.IsMasterClient)
            return;
            
        UpdateAnimation();
        UpdateAttackLock();
        // decay global busy timer
        if (globalBusyTimer > 0f)
            globalBusyTimer -= Time.deltaTime;
        
        if (behaviorTree != null)
        {
            behaviorTree.Tick();
            // Debug log removed to prevent console spam
            // var result = behaviorTree.Tick();
            // if (showDebugInfo)
            // {
            //     Debug.Log($"[{gameObject.name}] Behavior Tree Result: {result}");
            // }
        }
    }
    
    #endregion

    #region State Machine
    public enum AIState { Idle, Patrol, ReturnToPatrol, Chase, BasicAttack, Special1, Special2 }
    protected AIState aiState = AIState.Idle;

    protected void BeginAction(AIState state, bool setAnimatorBusy = true)
    {
        isBusy = true;
        aiState = state;
        if (setAnimatorBusy && animator != null && HasBool("Busy"))
        {
            animator.SetBool("Busy", true);
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetBool", RpcTarget.Others, "Busy", true);
            }
        }
    }

    protected void EndAction(bool clearAnimatorBusy = true)
    {
        if (clearAnimatorBusy && animator != null && HasBool("Busy"))
        {
            animator.SetBool("Busy", false);
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetBool", RpcTarget.Others, "Busy", false);
            }
        }
        isBusy = false;
        aiState = AIState.Idle;
        // Apply a post-action busy equal to basic attack cooldown
        // to prevent immediate follow-up actions.
        if (enemyData != null)
        {
            float postBusy = Mathf.Max(0f, enemyData.attackCooldown);
            if (postBusy > globalBusyTimer) globalBusyTimer = postBusy;
        }
    }
    #endregion
    
    #region Abstract Methods (Override in specific enemy types)
    
    /// <summary>
    /// Initialize enemy-specific data and behaviors
    /// </summary>
    protected abstract void InitializeEnemy();
    
    /// <summary>
    /// Build the behavior tree for this specific enemy type
    /// </summary>
    protected abstract void BuildBehaviorTree();
    
    /// <summary>
    /// Perform the basic attack
    /// </summary>
    protected abstract void PerformBasicAttack();
    
    /// <summary>
    /// Check if special abilities are available and execute them
    /// </summary>
    protected abstract bool TrySpecialAbilities();
    
    // (generic skill helpers removed per request)
    
    #endregion
    
    #region Common AI Behaviors
    
    protected virtual void UpdateAnimation()
    {
        if (animator != null && HasFloatParam(speedParam))
        {
            // Always check GetMoveSpeed() first - it returns 0 when AI should be stopped
            // This catches: isBusy, globalBusyTimer, activeAbility, basicRoutine, aiState==Idle (in derived classes)
            float expectedSpeed = GetMoveSpeed();
            if (expectedSpeed <= 0f)
            {
                animator.SetFloat(speedParam, 0f);
                return;
            }
            
            // If AI should be moving, use actual velocity for smooth animation
            Vector3 velocity = controller != null ? controller.velocity : Vector3.zero;
            float planarSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            
            // If actual velocity is very low or AI is idle, clamp to 0
            if (planarSpeed < 0.01f || aiState == AIState.Idle)
            {
                animator.SetFloat(speedParam, 0f);
            }
            else
            {
                animator.SetFloat(speedParam, planarSpeed);
            }
        }
    }
    
    protected virtual void UpdateAttackLock()
    {
        if (attackLockTimer > 0f)
            attackLockTimer -= Time.deltaTime;
    }

    
    
    protected NodeState UpdateTarget()
    {
        // Drop target if out of chase range
        if (targetPlayer != null && enemyData != null)
        {
            float dist = Vector3.Distance(transform.position, targetPlayer.position);
            if (dist > enemyData.chaseLoseRange)
            {
                targetPlayer = null;
            }
        }

        // Acquire nearest only if within detection range
        if (targetPlayer == null || !IsPlayerValid(targetPlayer))
        {
            Transform nearest = FindNearestPlayer();
            if (nearest != null && enemyData != null)
            {
                float d = Vector3.Distance(transform.position, nearest.position);
                if (d <= enemyData.detectionRange)
                {
                    targetPlayer = nearest;
                }
                else
                {
                    targetPlayer = null;
                }
            }
            else
            {
                targetPlayer = nearest;
            }
        }
        
        if (targetPlayer != null)
            blackboard.Set("target", targetPlayer);
        else
            blackboard.Remove("target");
        
        return NodeState.Success;
    }
    
    protected bool HasTarget()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || !IsPlayerValid(target)) return false;
        if (enemyData != null)
        {
            float dist = Vector3.Distance(transform.position, target.position);
            // hard drop if beyond chase lose range
            if (dist > enemyData.chaseLoseRange)
            {
                targetPlayer = null;
                blackboard.Remove("target");
                return false;
            }
        }
        return true;
    }
    
    protected bool TargetInDetectionRange()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || enemyData == null) return false;
        return Vector3.Distance(transform.position, target.position) <= enemyData.detectionRange;
    }
    
    protected bool TargetInAttackRange()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || enemyData == null) return false;
        return Vector3.Distance(transform.position, target.position) <= enemyData.attackRange;
    }
    
    protected NodeState MoveTowardsTarget()
    {
        var target = blackboard.Get<Transform>("target");
        if (target == null || controller == null || enemyData == null) return NodeState.Failure;
        if (attackLockTimer > 0f || isBusy || globalBusyTimer > 0f) return NodeState.Running;
        // lose aggro and bail if target got too far
        float currentDistance = Vector3.Distance(transform.position, target.position);
        if (currentDistance > enemyData.chaseLoseRange)
        {
            targetPlayer = null;
            blackboard.Remove("target");
            return NodeState.Failure;
        }
        
        Vector3 direction = (target.position - transform.position);
        direction.y = 0f;
        float distance = direction.magnitude;
        
        // If within attack range but still on cooldown, optionally orbit around target
        if (distance <= enemyData.attackRange)
        {
            bool cooldownReady = (Time.time - lastAttackTime) >= enemyData.attackCooldown;
            float desired = Mathf.Clamp01(DesiredAttackDistanceFraction) * enemyData.attackRange;
            // push closer before attacking to avoid edge-of-range stalls
            if (distance > desired)
            {
                Vector3 dirNorm = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
                if (controller != null && controller.enabled)
                {
                    float speed = GetMoveSpeed();
                    if (speed <= 0f) speed = 0f; // Ensure movement respects busy state
                    controller.SimpleMove(dirNorm * speed);
                }
                Vector3 pushLookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
                Vector3 pushDirToLook = (pushLookTarget - transform.position);
                if (pushDirToLook.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(pushDirToLook);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
                }
                aiState = AIState.Chase;
                return NodeState.Running;
            }
            if (!cooldownReady && OrbitWhenOnCooldown)
            {
                Vector3 fwd = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
                Vector3 tangent = Vector3.Cross(Vector3.up, fwd);
                float speed = GetMoveSpeed() * Mathf.Clamp(OrbitSpeedMultiplier, 0.1f, 2f);
                if (speed <= 0f) speed = 0f; // Ensure orbit respects busy state
                if (controller != null && controller.enabled)
                    controller.SimpleMove(tangent * speed);
                // face target while orbiting
                Vector3 orbitLookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
                Vector3 orbitDirToLook = (orbitLookTarget - transform.position);
                if (orbitDirToLook.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(orbitDirToLook);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
                }
                aiState = AIState.Chase;
                return NodeState.Running;
            }
            // in range and cooldown ready: signal that movement is done so attack selector can run
            aiState = AIState.Chase;
            return NodeState.Failure;
        }
        
        direction.Normalize();
        if (controller != null && controller.enabled)
        {
            float speed = GetMoveSpeed();
            if (speed <= 0f) speed = 0f; // Ensure movement respects busy state
            controller.SimpleMove(direction * speed);
        }
            
        // Rotate towards target
        Vector3 lookTarget = new Vector3(target.position.x, transform.position.y, target.position.z);
        Vector3 dirToLook = (lookTarget - transform.position);
        if (dirToLook.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dirToLook);
            float rotSpeed = enemyData != null ? enemyData.rotationSpeedDegrees : 360f;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, rotSpeed * Time.deltaTime);
        }
        
        aiState = AIState.Chase;
        return NodeState.Running;
    }
    
    protected NodeState AttackTarget()
    {
        if (enemyData == null) return NodeState.Failure;
        if (Time.time - lastAttackTime < enemyData.attackCooldown) return NodeState.Running;
        if (isBusy || globalBusyTimer > 0f) return NodeState.Running;
        
        var target = blackboard.Get<Transform>("target");
        if (target == null) return NodeState.Failure;
        
        // Try special abilities first
        if (TrySpecialAbilities())
        {
            return NodeState.Success;
        }
        
        // Fall back to basic attack
        PerformBasicAttack();
        lastAttackTime = Time.time;
        attackLockTimer = enemyData.attackMoveLock;
        
        aiState = AIState.BasicAttack;
        return NodeState.Success;
    }
    
    protected NodeState Patrol()
    {
        if (enemyData == null || !enemyData.enablePatrol) return NodeState.Failure;
        if (isBusy) return NodeState.Running;
        
        // Pick a target point if none or reached
        Vector3 flatPos = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 flatTarget = new Vector3(patrolTarget.x, 0f, patrolTarget.z);
        
        if (patrolTarget == Vector3.zero || Vector3.Distance(flatPos, flatTarget) < 0.5f)
        {
            // During wait phase, stop movement and set state to Idle
            if (controller != null && controller.enabled)
                controller.SimpleMove(Vector3.zero);
            
            if (patrolWaitTimer <= 0f)
            {
                patrolTarget = spawnPoint + Random.insideUnitSphere * enemyData.patrolRadius;
                patrolTarget.y = transform.position.y;
                patrolWaitTimer = enemyData.patrolWait;
            }
            else
            {
                patrolWaitTimer -= Time.deltaTime;
                aiState = AIState.Idle; // Set to Idle during wait so speed is 0
                return NodeState.Running;
            }
        }
        
        if (controller != null)
        {
            Vector3 direction = (patrolTarget - transform.position);
            direction.y = 0f;
            if (direction.magnitude > 0.1f)
            {
                direction.Normalize();
                if (controller != null && controller.enabled)
                {
                    float pSpeed = GetMoveSpeed() * 0.75f;
                    if (pSpeed <= 0f) 
                    {
                        pSpeed = 0f; // Ensure patrol respects busy state
                        controller.SimpleMove(Vector3.zero); // Explicitly stop if speed is 0
                    }
                    else
                    {
                        if (PatrolSpeed > 0f) pSpeed = Mathf.Min(pSpeed, PatrolSpeed); // Use patrolSpeed as max if set
                        controller.SimpleMove(direction * pSpeed);
                    }
                }
                Vector3 look = new Vector3(patrolTarget.x, transform.position.y, patrolTarget.z);
                Vector3 lookDir = (look - transform.position);
                if (lookDir.sqrMagnitude > 0.0001f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, RotationSpeed * Time.deltaTime);
                }
                aiState = AIState.Patrol;
                return NodeState.Running;
            }
            else
            {
                // Reached target, stop movement
                if (controller != null && controller.enabled)
                    controller.SimpleMove(Vector3.zero);
                aiState = AIState.Idle; // Set to Idle when at destination
            }
        }
        return NodeState.Success;
    }
    
    #endregion
    
    #region Damage and Death
    
    public virtual void TakeEnemyDamage(int amount, GameObject source)
    {
        if (isDead) return;
        
        currentHealth -= amount;
        OnEnemyTookDamage?.Invoke(this, amount);
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Took {amount} damage. Health: {currentHealth}/{enemyData.maxHealth}");
        }
        
        // Trigger hit animation (sync to all clients)
        if (animator != null && HasTrigger(hitTrigger))
        {
            animator.SetTrigger(hitTrigger);
            // Sync animation to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetTrigger", RpcTarget.Others, hitTrigger);
            }
        }
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    [PunRPC]
    public void RPC_EnemyTakeDamage(int amount, int sourceViewId)
    {
        // RPC entry point for network damage
        GameObject source = sourceViewId >= 0 && PhotonView.Find(sourceViewId) != null ? PhotonView.Find(sourceViewId).gameObject : null;
        TakeEnemyDamage(amount, source);
    }
    
    protected virtual void Die()
    {
        if (isDead) return;
        
        isDead = true;
        currentHealth = 0;
        
        if (showDebugInfo)
        {
            Debug.Log($"[{gameObject.name}] Died");
        }
        
        // Trigger death animation (sync to all clients)
        if (animator != null)
        {
            if (HasBool(isDeadBool))
            {
                animator.SetBool(isDeadBool, true);
                // Sync bool to other clients
                if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    photonView.RPC("RPC_SetBool", RpcTarget.Others, isDeadBool, true);
                }
            }
            if (HasTrigger(dieTrigger))
            {
                animator.SetTrigger(dieTrigger);
                // Sync trigger to other clients
                if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    photonView.RPC("RPC_SetTrigger", RpcTarget.Others, dieTrigger);
                }
            }
        }
        
        // Disable movement
        if (controller != null)
            controller.enabled = false;
        
        OnEnemyDied?.Invoke(this);
        
        // Destroy after delay
        StartCoroutine(DestroyAfterDelay(5f));
    }
    
    private IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            PhotonNetwork.Destroy(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    #endregion
    
    #region Helper Methods
    
    protected Transform FindNearestPlayer()
    {
        PlayerStats[] players = FindObjectsByType<PlayerStats>(FindObjectsSortMode.None);
        Transform nearest = null;
        float bestDistance = float.MaxValue;
        
        foreach (var player in players)
        {
            if (player == null) continue;
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = player.transform;
            }
        }
        
        return nearest;
    }
    
    protected bool IsPlayerValid(Transform player)
    {
        if (player == null) return false;
        PlayerStats stats = player.GetComponent<PlayerStats>();
        // Fallback: if PlayerStats does not expose an alive flag, treat presence as valid
        return stats != null;
    }
    
    protected bool HasFloatParam(string param)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.name == param) return true;
        return false;
    }
    
    protected bool HasTrigger(string param)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == param) return true;
        return false;
    }
    
    protected bool HasBool(string param)
    {
        if (animator == null || string.IsNullOrEmpty(param)) return false;
        foreach (var p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Bool && p.name == param) return true;
        return false;
    }

    // Helper properties for common EnemyData values
    protected float RotationSpeed => enemyData != null ? enemyData.rotationSpeedDegrees : 360f;
    protected float SpecialFacingAngle => enemyData != null ? enemyData.specialFacingAngle : 20f;
    protected float PreferredDistance => enemyData != null ? enemyData.preferredDistance : 3.0f;
    protected float BackoffSpeedMultiplier => enemyData != null ? enemyData.backoffSpeedMultiplier : 0.6f;
    protected float PatrolSpeed => enemyData != null ? enemyData.patrolSpeed : 2f;
    protected bool OrbitWhenOnCooldown => enemyData != null ? enemyData.orbitWhenOnCooldown : true;
    protected float OrbitSpeedMultiplier => enemyData != null ? enemyData.orbitSpeedMultiplier : 0.7f;
    protected float DesiredAttackDistanceFraction => enemyData != null ? enemyData.desiredAttackDistanceFraction : 0.85f;

    protected virtual float GetMoveSpeed()
    {
        // Use chaseSpeed from EnemyData, fallback to moveSpeed if chaseSpeed is 0 or not set
        if (enemyData != null)
        {
            float chaseSpd = enemyData.chaseSpeed;
            return chaseSpd > 0f ? chaseSpd : enemyData.moveSpeed;
        }
        return 3.5f;
    }

    protected void SetBuffVfx(BuffType type, bool enabled, Vector3 localOffset, float scale = 1f)
    {
        if (!enabled)
        {
            if (activeBuffVfx.TryGetValue(type, out var inst) && inst != null)
            {
                Destroy(inst);
            }
            activeBuffVfx.Remove(type);
            return;
        }

        if (activeBuffVfx.ContainsKey(type) && activeBuffVfx[type] != null)
        {
            return; // already active
        }

        GameObject prefab = null;
        switch (type)
        {
            case BuffType.Damage: prefab = buffDamageVFXPrefab; break;
            case BuffType.Speed: prefab = buffSpeedVFXPrefab; break;
            case BuffType.Stamina: prefab = buffStaminaVFXPrefab; break;
            case BuffType.Health: prefab = buffHealthVFXPrefab; break;
        }
        if (prefab == null) return;
        // Parent to enemy so VFX follows the enemy (for persistent buffs)
        var go = Instantiate(prefab, transform);
        go.transform.localPosition = localOffset + buffVfxOffset;
        go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale * Mathf.Max(0.01f, buffVfxScale));
        activeBuffVfx[type] = go;
    }
    
    #endregion
    
    #region Public Properties
    
    public bool IsDead => isDead;
    public int CurrentHealth => currentHealth;
    public int MaxHealth => enemyData != null ? enemyData.maxHealth : 100;
    public float HealthPercentage => (float)currentHealth / MaxHealth;
    public Transform Target => targetPlayer;
    public bool IsAttacking => isAttacking;
    public bool IsBusy => isBusy;
    public float BasicCooldownRemaining => Mathf.Max(0f, attackLockTimer);
    public float BasicCooldownTime => enemyData != null ? Mathf.Max(0f, enemyData.attackCooldown - (Time.time - lastAttackTime)) : 0f;
    public AIState CurrentState => aiState;
    
    #endregion
    
    #region Network Synchronization RPCs
    
    /// <summary>
    /// RPC to sync animator trigger across network
    /// </summary>
    [PunRPC]
    public void RPC_SetTrigger(string triggerName)
    {
        if (animator != null && HasTrigger(triggerName))
        {
            animator.SetTrigger(triggerName);
        }
    }
    
    /// <summary>
    /// Helper method to trigger animation and sync to network. Use this for attack animations.
    /// </summary>
    protected void SetTriggerSync(string triggerName)
    {
        if (animator != null && HasTrigger(triggerName))
        {
            animator.SetTrigger(triggerName);
            // Sync to other clients
            if (photonView != null && PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
            {
                photonView.RPC("RPC_SetTrigger", RpcTarget.Others, triggerName);
            }
        }
    }
    
    /// <summary>
    /// RPC to sync animator bool across network
    /// </summary>
    [PunRPC]
    public void RPC_SetBool(string boolName, bool value)
    {
        if (animator != null && HasBool(boolName))
        {
            animator.SetBool(boolName, value);
        }
    }
    
    /// <summary>
    /// Helper method to spawn VFX locally and sync spawn command to other clients.
    /// Usage: After Instantiate, call this to notify other clients to spawn the same VFX.
    /// NOTE: This requires enemy AIs to override or implement VFX spawning with resource paths or shared prefab references.
    /// For immediate VFX sync, spawn VFX locally then call photonView.RPC("RPC_SpawnVFX", RpcTarget.Others, ...) with appropriate parameters.
    /// </summary>
    protected void SpawnVFXSync(GameObject vfxPrefab, Vector3 localOffset, Vector3 scale, bool parentToTransform = true)
    {
        if (vfxPrefab == null) return;
        
        // Spawn locally first
        GameObject vfxInstance;
        if (parentToTransform)
        {
            vfxInstance = Instantiate(vfxPrefab, transform);
            vfxInstance.transform.localPosition = localOffset;
        }
        else
        {
            vfxInstance = Instantiate(vfxPrefab, transform.position + localOffset, transform.rotation);
        }
        if (scale != Vector3.zero && scale != Vector3.one)
        {
            vfxInstance.transform.localScale = scale;
        }
        
        // Notify other clients - they will need to spawn locally using the same prefab reference
        // NOTE: This only works if the prefab is accessible to all clients (e.g., in Resources or shared reference)
        // For now, individual enemy AIs should implement their own VFX sync RPCs if needed
        // Example pattern: Store VFX prefab reference, spawn locally, then RPC with identifier to spawn on others
    }
    
    #endregion
}

public enum BuffType { Damage, Speed, Stamina, Health }
