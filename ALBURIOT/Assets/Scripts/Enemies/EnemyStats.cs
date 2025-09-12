using UnityEngine;
using TMPro;

[CreateAssetMenu(fileName = "NewEnemyStats", menuName = "Enemy/Stats")]
public class EnemyStats : ScriptableObject
{
    public string enemyName;
    public int maxHealth = 100;
    public int damage = 10;
    public float moveSpeed = 3f;
    public float detectionRange = 10f;
    public float attackRange = 2f;
    public float attackCooldown = 1.5f;
    public bool isMelee = true; // Checkbox in Inspector
    // Add more stats as needed
}
