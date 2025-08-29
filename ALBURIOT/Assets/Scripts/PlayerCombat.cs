using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    public float attackCooldown = 1.0f; // seconds
    private float attackCooldownTimer = 0f;
    public float AttackCooldownProgress => Mathf.Clamp01(attackCooldownTimer / attackCooldown);

    public float attackRange = 2f;
    public float attackRate = 1f;
    public int attackStaminaCost = 20;
    public LayerMask enemyLayers;

    private float nextAttackTime = 0f;
    private PlayerStats stats;
    private Animator animator;

    void Start()
    {
        stats = GetComponent<PlayerStats>();
        animator = GetComponent<Animator>();
    }

    private bool canControl = true;

    public void SetCanControl(bool value)
    {
        canControl = value;
    }

    void Update()
    {
        if (!canControl) return;
        // update attack cooldown timer
        if (attackCooldownTimer > 0f)
            attackCooldownTimer -= Time.deltaTime;

        if (Time.time >= nextAttackTime)
        {
            // Only allow attack if CanAttack is true (i.e., grounded) and cooldown is over
            var controller = GetComponent<ThirdPersonController>();
            if (controller != null && controller.CanAttack && attackCooldownTimer <= 0f && Input.GetMouseButtonDown(0))
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
                }
                else
                {
                    Debug.Log("Not enough stamina to attack!");
                }
            }
        }
    }

    void Attack()
    {
        // Play attack animation here if you have one
        Collider[] hitEnemies = Physics.OverlapSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f, enemyLayers);
        Debug.Log($"Attack! Enemies in range: {hitEnemies.Length}");
        foreach (Collider enemy in hitEnemies)
        {
            EnemyController enemyController = enemy.GetComponent<EnemyController>();
            if (enemyController != null)
            {
                Debug.Log($"Hit enemy: {enemyController.gameObject.name}");
                enemyController.TakeDamage(stats.baseDamage);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position + transform.forward * attackRange * 0.5f, attackRange * 0.5f);
    }
}
