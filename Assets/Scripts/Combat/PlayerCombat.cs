using UnityEngine;
using Photon.Pun;

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

    [Header("VFX Integration")]
    public VFXManager vfxManager;
    public PowerStealManager powerStealManager;
    
    [Header("Managers")]
    public MovesetManager movesetManager;
    
    private float nextAttackTime = 0f;
    private PlayerStats stats;
    private Animator animator;
    private float isAttackingTimer = 0f;
    public bool IsAttacking => isAttackingTimer > 0f;

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
    if (stats != null && (stats.IsSilenced || stats.IsStunned)) return; // debuff-gated
        // update attack cooldown timer
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;
        if (isAttackingTimer > 0f)
            isAttackingTimer -= Time.deltaTime;

        if (Time.time >= nextAttackTime)
        {
            // Only allow attack if CanAttack is true (i.e., grounded) and cooldown is over
            var controller = GetComponent<ThirdPersonController>();
            bool groundedOk = controller != null && controller.CanAttack;
            bool cdOk = attackCooldownTimer <= 0f;
            bool inputOk = Input.GetMouseButtonDown(0);
            if (groundedOk && cdOk && inputOk)
            {
                int finalStaminaCost = attackStaminaCost;
                if (stats != null)
                    finalStaminaCost = Mathf.Max(1, attackStaminaCost + stats.staminaCostModifier);
                if (stats.UseStamina(finalStaminaCost))
                {
                    Debug.Log("Player attacking!");
                    if (animator != null)
                    {
                        animator.SetTrigger("Attack");
                    }
                    Attack();
                    nextAttackTime = Time.time + 1f / attackRate;
                    attackCooldownTimer = attackCooldown;
                    isAttackingTimer = 0.25f; // brief flag for UI/debug
                }
                else
                {
                    Debug.Log("Not enough stamina to attack!");
                }
            }
            else if (inputOk)
            {
                Debug.Log($"Attack blocked - groundedOk:{groundedOk} cdOk:{cdOk} canControl:{canControl} nextTime:{nextAttackTime:F2} now:{Time.time:F2}");
            }
        }
    }

    void Attack()
    {
        // Play attack animation here if you have one
        Collider[] hitEnemies = Physics.OverlapSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f, enemyLayers);
        Debug.Log($"Attack! Enemies in range: {hitEnemies.Length}");
        
        // deduplicate targets so multiple colliders on the same enemy don't apply damage more than once
        var uniqueEnemies = new System.Collections.Generic.HashSet<GameObject>();
        foreach (var enemy in hitEnemies)
        {
            var dmg = enemy.GetComponentInParent<IEnemyDamageable>();
            var mb = dmg as MonoBehaviour;
            if (mb != null) uniqueEnemies.Add(mb.gameObject);
        }

        // Calculate damage based on moveset
        int damage = stats.baseDamage;
        if (movesetManager != null && movesetManager.CurrentMoveset != null)
        {
            damage = movesetManager.CurrentMoveset.baseDamage;
        }

        foreach (var go in uniqueEnemies)
        {
            Debug.Log($"Hit enemy: {go.name}");
            EnemyDamageRelay.Apply(go, damage, gameObject);
            LastHitEnemyRoot = go.transform;
        }
    }
    
    // power stealing is granted by enemy death logic (PowerDropOnDeath) and quest updates handled there

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f);
    }
}
