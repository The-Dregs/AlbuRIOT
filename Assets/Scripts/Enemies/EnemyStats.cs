using UnityEngine;

// Minimal stats container used by EnemyAIController. Create assets via Create > AlbuRIOT > Enemy Stats.
[CreateAssetMenu(fileName = "EnemyStats", menuName = "AlbuRIOT/Enemies/Enemy Stats")] 
public class EnemyStats : ScriptableObject
{
    [Header("combat")]
    public int maxHealth = 100;
    public int damage = 10;
    public float attackCooldown = 1.25f;

    [Header("ranges")]
    public float detectionRange = 12f;
    public float attackRange = 2.2f;

    [Header("movement")]
    public float moveSpeed = 3.5f;
}
