using UnityEngine;
using Photon.Pun;
using System.Collections;

public class PlayerCombat : MonoBehaviourPun
{
    [Header("Combat Configuration")]
    public float attackCooldown = 1.0f; // seconds
    private float attackCooldownTimer = 0f;
    public float AttackCooldownProgress => Mathf.Clamp01(attackCooldownTimer / attackCooldown);

    public float attackRange = 2f;
    public float attackRate = 1f;
    public int attackStaminaCost = 20;
    public LayerMask enemyLayers;
    [Tooltip("Optional origin for attack range (use this transform's position/forward instead of the player's)")]
    public Transform attackRangeOrigin;
    [Tooltip("Forward offset in meters from the origin to center the attack sphere (use to make range less deep)")]
    public float attackForwardOffset = 0.0f;

    [Header("Combo System")]
    [SerializeField] private float comboWindow = 0.8f; // Time to continue combo after hit
    [SerializeField] private float comboInputDelay = 0.3f; // Minimum delay before next hit input can be registered
    [SerializeField] private float[] comboDamageMultipliers = { 1.0f, 1.2f, 1.5f }; // Damage multiplier per hit
    
    [Header("Attack Durations")]
    [SerializeField] private float[] unarmedAttackDurations = { 0.4f, 0.4f }; // Duration of each unarmed attack (2 hits)
    [SerializeField] private float[] armedAttackDurations = { 0.4f, 0.4f, 0.5f }; // Duration of each armed attack (3 hits)
    
    [Header("Hit Stop Effect")]
    [SerializeField] private float hitStopDuration = 0.05f; // Duration of hit-stop effect when hitting enemies
    
    [Header("Attack Rotation")]
    [SerializeField] private float attackRotationSpeed = 720f; // Degrees per second - fast and snappy rotation towards camera
    
    [Header("VFX Integration")]
    public VFXManager vfxManager;
    public PowerStealManager powerStealManager;
    
    [Header("Managers")]
    public MovesetManager movesetManager;
    private EquipmentManager equipmentManager;
    
    [Header("Camera")]
    public Transform cameraPivot; // Camera pivot transform for camera-relative attacks
    
    private float nextAttackTime = 0f;
    private PlayerStats stats;
    private Animator animator;
    private float isAttackingTimer = 0f;
    public bool IsAttacking => isAttackingTimer > 0f;

    // Combo state
    private int currentComboIndex = 0;
    private float comboWindowTimer = 0f;
    private float comboInputDelayTimer = 0f;
    private bool isPerformingCombo = false;
    private Coroutine currentAttackCoroutine = null;
    // Ensures we never trigger attack animation without enough stamina
    private bool hasPaidForCurrentHit = false;

    // track last damaged enemy root to attribute kills for power stealing
    public Transform LastHitEnemyRoot { get; private set; }
    
    // Hit-stop state
    private Coroutine hitStopCoroutine = null;
    private ThirdPersonController playerController;
    
    // Store attack start position/direction to keep damage area fixed (not affected by root motion)
    private Vector3 attackStartPosition;
    private Vector3 attackStartForward;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        animator = GetComponent<Animator>();
        
        // Auto-find moveset manager
        if (movesetManager == null)
            movesetManager = GetComponent<MovesetManager>();
            
        // Auto-find VFX manager
        if (vfxManager == null)
            vfxManager = GetComponent<VFXManager>();
            
        // Auto-find power steal manager
        if (powerStealManager == null)
            powerStealManager = GetComponent<PowerStealManager>();
            
        // Auto-find equipment manager
        if (equipmentManager == null)
            equipmentManager = GetComponent<EquipmentManager>();
            
        // Get player controller for hit-stop
        playerController = GetComponent<ThirdPersonController>();
        
        // Auto-find camera pivot from ThirdPersonController
        if (cameraPivot == null && playerController != null)
        {
            var controllerType = playerController.GetType();
            var cameraPivotField = controllerType.GetField("cameraPivot", 
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cameraPivotField != null)
            {
                cameraPivot = cameraPivotField.GetValue(playerController) as Transform;
            }
        }
    }

    private bool canControl = true;

    public void SetCanControl(bool value)
    {
        canControl = value;
    }

    void Update()
    {
        var photonView = GetComponent<Photon.Pun.PhotonView>();
        if (photonView != null && !photonView.IsMine) return;
        if (!canControl) return;
        if (stats != null && (stats.IsSilenced || stats.IsStunned || stats.IsExhausted)) return;
        
        // Update timers
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;
        if (isAttackingTimer > 0f)
            isAttackingTimer -= Time.deltaTime;
            
        // Update combo window timer
        if (comboWindowTimer > 0f)
        {
            comboWindowTimer -= Time.deltaTime;
            if (comboWindowTimer <= 0f)
            {
                // Combo window expired, reset combo
                Debug.Log("[Combo] Combo window expired - Combo reset!");
                ResetCombo();
            }
        }
        
        // Update combo input delay timer
        if (comboInputDelayTimer > 0f)
        {
            comboInputDelayTimer -= Time.deltaTime;
        }

        // Handle attack input - can only trigger if not currently performing an attack and attack animation is complete
        bool inputOk = Input.GetMouseButtonDown(0);
        
        // CRITICAL: Block all attacks if currently attacking or attack animation hasn't finished
        bool canAttack = !isPerformingCombo && !IsAttacking && comboInputDelayTimer <= 0f && Time.time >= nextAttackTime;
        
        if (inputOk && canAttack)
        {
            var controller = GetComponent<ThirdPersonController>();
            bool groundedOk = controller != null && controller.CanAttack;
            
            if (groundedOk)
            {
                // Check if we're in combo window (continue combo) or starting new combo
                if (comboWindowTimer > 0f && attackCooldownTimer <= 0f)
                {
                    // Continue existing combo
                    int finalStaminaCost = attackStaminaCost;
                    if (stats != null)
                        finalStaminaCost = Mathf.Max(1, attackStaminaCost + stats.staminaCostModifier);
                    if (stats.UseStamina(finalStaminaCost))
                    {
                        hasPaidForCurrentHit = true;
                        ContinueCombo();
                    }
                }
                else if (attackCooldownTimer <= 0f)
                {
                    // Start new combo
                    int finalStaminaCost = attackStaminaCost;
                    if (stats != null)
                        finalStaminaCost = Mathf.Max(1, attackStaminaCost + stats.staminaCostModifier);
                    if (stats.UseStamina(finalStaminaCost))
                    {
                        hasPaidForCurrentHit = true;
                        StartComboAttack();
                    }
                    else
                    {
                        Debug.Log("Not enough stamina to attack!");
                    }
                }
            }
        }
    }

    private int GetMaxComboCount()
    {
        // Check if player is armed
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        return isArmed ? 3 : 2;
    }
    
    private void StartComboAttack()
    {
        // Clear stance bools when starting a new attack
        if (animator != null)
        {
            animator.SetBool("IsUnarmedStance", false);
            animator.SetBool("IsArmedStance", false);
        }
        
        currentComboIndex = 0;
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        int maxCombo = GetMaxComboCount();
        Debug.Log($"[Combo] Starting combo attack - Mode: {(isArmed ? "Armed" : "Unarmed")}, Max Hits: {maxCombo}");
        PerformComboHit();
    }
    
    private void ContinueCombo()
    {
        int maxCombo = GetMaxComboCount();
        
        // Only continue if we haven't reached max combo yet
        if (currentComboIndex >= maxCombo - 1)
        {
            Debug.Log($"[Combo] Cannot continue - combo already at max ({maxCombo} hits). Resetting.");
            ResetCombo();
            StartComboAttack(); // Start a new combo instead
            return;
        }
        
        currentComboIndex++;
        Debug.Log($"[Combo] Continuing combo - Hit {currentComboIndex + 1}/{maxCombo}");
        PerformComboHit();
    }
    
    private void PerformComboHit()
    {
        if (currentAttackCoroutine != null)
        {
            StopCoroutine(currentAttackCoroutine);
        }
        currentAttackCoroutine = StartCoroutine(CoPerformComboHit());
    }
    
    private IEnumerator CoPerformComboHit()
    {
        isPerformingCombo = true;
        if (!hasPaidForCurrentHit)
        {
            // Safety: never trigger animation if stamina wasn't paid for this swing
            isPerformingCombo = false;
            yield break;
        }
        
        // Set combo index parameter for animator to track which attack in combo
        int hitNumber = currentComboIndex + 1;
        int maxCombo = GetMaxComboCount();
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        
        Debug.Log($"[Combo] Performing hit {hitNumber}/{maxCombo} - ComboIndex: {currentComboIndex}, Armed: {isArmed}");
        
        // Rotate player to face camera direction (fast and snappy)
        Quaternion targetRotation = transform.rotation; // Default to current rotation
        if (cameraPivot != null)
        {
            Vector3 cameraForward = cameraPivot.forward;
            cameraForward.y = 0f;
            if (cameraForward.sqrMagnitude > 0.0001f)
            {
                cameraForward.Normalize();
                targetRotation = Quaternion.LookRotation(cameraForward, Vector3.up);
                
                // Fast and snappy rotation towards camera
                float rotationTime = 0f;
                float maxRotationTime = 0.15f; // Max time to complete rotation (very snappy)
                Quaternion startRotation = transform.rotation;
                
                while (rotationTime < maxRotationTime)
                {
                    rotationTime += Time.deltaTime;
                    float t = rotationTime / maxRotationTime;
                    // Use smoothstep for smooth but snappy rotation
                    t = t * t * (3f - 2f * t);
                    
                    transform.rotation = Quaternion.Slerp(startRotation, targetRotation, t);
                    
                    // Check if we're close enough to target (early exit for snappiness)
                    float angle = Quaternion.Angle(transform.rotation, targetRotation);
                    if (angle < 2f) break; // Close enough, snap to target
                    
                    yield return null;
                }
                
                // Ensure we're exactly facing the target
                transform.rotation = targetRotation;
            }
        }
        
        // Store attack start position and forward direction for damage detection
        attackStartPosition = transform.position;
        attackStartForward = transform.forward;
        
        // Store locked rotation to maintain camera-facing direction during entire attack
        Quaternion lockedRotation = transform.rotation;
        
        // Set animator parameters for combo system (only after stamina paid)
        if (animator != null)
        {
            // Set combo index (0, 1, 2 for 1st, 2nd, 3rd hit)
            if (AnimatorHasParameter(animator, "ComboIndex"))
                animator.SetInteger("ComboIndex", currentComboIndex);
            
            // Set armed state
            if (AnimatorHasParameter(animator, "IsArmed"))
                animator.SetBool("IsArmed", isArmed);
            
            // Single Attack trigger for all combo hits
            if (AnimatorHasParameter(animator, "Attack"))
                animator.SetTrigger("Attack");
        }
        
        // Get attack duration for this combo hit (use armed or unarmed durations)
        float[] durations = isArmed ? armedAttackDurations : unarmedAttackDurations;
        float attackDuration = (currentComboIndex < durations.Length) 
            ? durations[currentComboIndex] 
            : (durations.Length > 0 ? durations[0] : 0.4f);
            
        isAttackingTimer = attackDuration;
        nextAttackTime = Time.time + attackDuration;
        
        // Lock rotation during entire attack to prevent weird turning
        float elapsed = 0f;
        while (elapsed < attackDuration)
        {
            elapsed += Time.deltaTime;
            // Keep rotation locked to camera-facing direction throughout attack
            transform.rotation = lockedRotation;
            yield return null;
        }
        
        // Apply damage after attack duration
        ApplyComboDamage();
        
        // Set input delay timer before allowing next input
        comboInputDelayTimer = comboInputDelay;
        
        // Check if combo is complete
        if (currentComboIndex >= maxCombo - 1)
        {
            // Combo complete - set stance bool to allow transition to stance state
            // isArmed already declared at top of method
            if (animator != null)
            {
                if (isArmed)
                {
                    animator.SetBool("IsArmedStance", true);
                    animator.SetBool("IsUnarmedStance", false);
                }
                else
                {
                    animator.SetBool("IsUnarmedStance", true);
                    animator.SetBool("IsArmedStance", false);
                }
            }
            Debug.Log($"[Combo] Combo complete! Setting {(isArmed ? "Armed" : "Unarmed")} stance.");
            ResetCombo();
        }
        else
        {
            // Clear stance bools during combo (not final hit yet)
            if (animator != null)
            {
                animator.SetBool("IsUnarmedStance", false);
                animator.SetBool("IsArmedStance", false);
            }
            // Set combo window timer for next hit
            comboWindowTimer = comboWindow;
            Debug.Log($"[Combo] Combo window opened ({comboWindow}s) - Next hit available in {comboInputDelay}s");
        }
        
        // End combo performance only after attack timer fully expires (allows next input after delay)
        isPerformingCombo = false;
        currentAttackCoroutine = null;
        hasPaidForCurrentHit = false; // require payment for next hit
    }
    
    private void ApplyComboDamage()
    {
        // Use stored attack start position and direction to keep damage area fixed in front
        // This prevents the attack area from moving with root motion animations
        Vector3 originPos = attackStartPosition;
        Vector3 originFwd = attackStartForward;
        if (attackRangeOrigin != null)
        {
            originPos = attackRangeOrigin.position;
            originFwd = attackRangeOrigin.forward;
        }
        float radius = attackRange * 0.5f;
        Vector3 damageCenter = originPos + originFwd * (radius + attackForwardOffset);
        Collider[] hitEnemies = Physics.OverlapSphere(damageCenter, radius, enemyLayers);
        
        // Deduplicate targets
        var uniqueEnemies = new System.Collections.Generic.HashSet<GameObject>();
        foreach (var enemy in hitEnemies)
        {
            var dmg = enemy.GetComponentInParent<IEnemyDamageable>();
            var mb = dmg as MonoBehaviour;
            if (mb != null) uniqueEnemies.Add(mb.gameObject);
        }

        // Calculate base damage
        int baseDamage = stats.baseDamage;
        if (movesetManager != null && movesetManager.CurrentMoveset != null)
        {
            baseDamage = movesetManager.CurrentMoveset.baseDamage;
        }
        
        // Apply combo damage multiplier
        float multiplier = (currentComboIndex < comboDamageMultipliers.Length) 
            ? comboDamageMultipliers[currentComboIndex] 
            : 1.0f;
        int finalDamage = Mathf.RoundToInt(baseDamage * multiplier);

        Debug.Log($"[Combo] Hit {currentComboIndex + 1} - Base Damage: {baseDamage}, Multiplier: {multiplier}x, Final Damage: {finalDamage}, Targets: {uniqueEnemies.Count}");

        bool hitEnemy = false;
        foreach (var go in uniqueEnemies)
        {
            EnemyDamageRelay.Apply(go, finalDamage, gameObject);
            LastHitEnemyRoot = go.transform;
            hitEnemy = true;
        }
        
        // Trigger hit-stop effect if we hit an enemy
        if (hitEnemy && hitStopDuration > 0f && photonView != null && photonView.IsMine)
        {
            if (hitStopCoroutine != null)
                StopCoroutine(hitStopCoroutine);
            hitStopCoroutine = StartCoroutine(CoHitStop(uniqueEnemies));
        }
    }
    
    private IEnumerator CoHitStop(System.Collections.Generic.HashSet<GameObject> hitEnemies)
    {
        if (hitEnemies == null || hitEnemies.Count == 0) yield break;
        
        // Store original states
        float playerAnimatorSpeed = 1f;
        if (animator != null)
        {
            playerAnimatorSpeed = animator.speed;
            animator.speed = 0f; // Pause player animation
        }
        
        bool wasPlayerMovable = true;
        if (playerController != null)
        {
            wasPlayerMovable = true; // We'll check this via SetCanMove
            playerController.SetCanMove(false); // Stop player movement
        }
        
        // Pause enemy animations and movement
        System.Collections.Generic.List<BaseEnemyAI> pausedEnemies = new System.Collections.Generic.List<BaseEnemyAI>();
        System.Collections.Generic.List<Animator> enemyAnimators = new System.Collections.Generic.List<Animator>();
        System.Collections.Generic.List<CharacterController> enemyControllers = new System.Collections.Generic.List<CharacterController>();
        
        foreach (var enemyGo in hitEnemies)
        {
            if (enemyGo == null) continue;
            
            // Get enemy AI component
            var enemyAI = enemyGo.GetComponent<BaseEnemyAI>();
            if (enemyAI != null)
            {
                pausedEnemies.Add(enemyAI);
                
                // Pause enemy animator
                if (enemyAI.animator != null)
                {
                    enemyAnimators.Add(enemyAI.animator);
                    enemyAI.animator.speed = 0f;
                }
            }
            else
            {
                // Fallback: try to find animator directly
                var anim = enemyGo.GetComponentInChildren<Animator>();
                if (anim != null)
                {
                    enemyAnimators.Add(anim);
                    anim.speed = 0f;
                }
            }
            
            // Stop enemy movement
            var enemyController = enemyGo.GetComponent<CharacterController>();
            if (enemyController != null && enemyController.enabled)
            {
                enemyControllers.Add(enemyController);
                enemyController.SimpleMove(Vector3.zero);
            }
            else
            {
                // Try to find in children
                var childController = enemyGo.GetComponentInChildren<CharacterController>();
                if (childController != null && childController.enabled)
                {
                    enemyControllers.Add(childController);
                    childController.SimpleMove(Vector3.zero);
                }
            }
        }
        
        // Wait for hit-stop duration while keeping enemies stopped
        float elapsed = 0f;
        while (elapsed < hitStopDuration)
        {
            // Continuously stop enemy movement during hit-stop
            foreach (var enemyController in enemyControllers)
            {
                if (enemyController != null && enemyController.enabled)
                {
                    enemyController.SimpleMove(Vector3.zero);
                }
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Restore player state
        if (animator != null)
        {
            animator.speed = playerAnimatorSpeed;
        }
        
        if (playerController != null && wasPlayerMovable)
        {
            playerController.SetCanMove(true);
        }
        
        // Restore enemy animations
        foreach (var enemyAnim in enemyAnimators)
        {
            if (enemyAnim != null)
            {
                enemyAnim.speed = 1f;
            }
        }
        
        // Enemy movement will resume naturally through their AI update
        
        hitStopCoroutine = null;
    }
    
    private void ResetCombo()
    {
        if (currentComboIndex > 0 || comboWindowTimer > 0f)
        {
            Debug.Log("[Combo] Combo reset");
        }
        currentComboIndex = 0;
        comboWindowTimer = 0f;
        
        // Reset animator combo index
        if (animator != null && AnimatorHasParameter(animator, "ComboIndex"))
        {
            animator.SetInteger("ComboIndex", 0);
        }
    }
    
    void Attack()
    {
        // Legacy method - ensure stamina check before starting
        int finalStaminaCost = attackStaminaCost;
        if (stats != null)
            finalStaminaCost = Mathf.Max(1, attackStaminaCost + stats.staminaCostModifier);
        if (stats != null && stats.UseStamina(finalStaminaCost))
        {
            hasPaidForCurrentHit = true;
            StartComboAttack();
        }
    }
    
    // power stealing is granted by enemy death logic (PowerDropOnDeath) and quest updates handled there

    private bool AnimatorHasParameter(Animator anim, string paramName)
    {
        if (anim == null) return false;
        foreach (var p in anim.parameters)
        {
            if (p.name == paramName) return true;
        }
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 gPos = transform.position;
        Vector3 gFwd = transform.forward;
        if (attackRangeOrigin != null)
        {
            gPos = attackRangeOrigin.position;
            gFwd = attackRangeOrigin.forward;
        }
        float r = attackRange * 0.5f;
        Gizmos.DrawWireSphere(gPos + gFwd * (r + attackForwardOffset), r);
    }
}
