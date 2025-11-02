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

    [Header("Combo System")]
    [SerializeField] private float comboWindow = 0.8f; // Time to continue combo after hit
    [SerializeField] private float comboInputDelay = 0.3f; // Minimum delay before next hit input can be registered
    [SerializeField] private float[] comboDamageMultipliers = { 1.0f, 1.2f, 1.5f }; // Damage multiplier per hit
    [SerializeField] private float[] attackDurations = { 0.4f, 0.4f, 0.5f }; // Duration of each attack
    
    [Header("VFX Integration")]
    public VFXManager vfxManager;
    public PowerStealManager powerStealManager;
    
    [Header("Managers")]
    public MovesetManager movesetManager;
    private EquipmentManager equipmentManager;
    
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

    // track last damaged enemy root to attribute kills for power stealing
    public Transform LastHitEnemyRoot { get; private set; }

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
        currentComboIndex++;
        int maxCombo = GetMaxComboCount();
        if (currentComboIndex >= maxCombo)
        {
            // Combo complete - this will be the final hit, then reset
            currentComboIndex = maxCombo - 1; // Keep at max index for final hit
        }
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
        
        // Set combo index parameter for animator to track which attack in combo
        int hitNumber = currentComboIndex + 1;
        int maxCombo = GetMaxComboCount();
        bool isArmed = equipmentManager != null && equipmentManager.equippedItem != null;
        
        Debug.Log($"[Combo] Performing hit {hitNumber}/{maxCombo} - ComboIndex: {currentComboIndex}, Armed: {isArmed}");
        
        // Set animator parameters for combo system
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
        
        // Get attack duration for this combo hit
        float attackDuration = (currentComboIndex < attackDurations.Length) 
            ? attackDurations[currentComboIndex] 
            : attackDurations[0];
            
        isAttackingTimer = attackDuration;
        nextAttackTime = Time.time + attackDuration;
        
        // Wait for attack animation to complete (blocks next attack until this finishes)
        yield return new WaitForSeconds(attackDuration);
        
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
    }
    
    private void ApplyComboDamage()
    {
        Collider[] hitEnemies = Physics.OverlapSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f, enemyLayers);
        
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

        foreach (var go in uniqueEnemies)
        {
            EnemyDamageRelay.Apply(go, finalDamage, gameObject);
            LastHitEnemyRoot = go.transform;
        }
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
        // Legacy method - kept for backwards compatibility, but combo system handles attacks now
        StartComboAttack();
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
        Gizmos.DrawWireSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f);
    }
}
