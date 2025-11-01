using UnityEngine;

[CreateAssetMenu(fileName = "EnemyData", menuName = "AlbuRIOT/Enemies/Enemy Data")]
public class EnemyData : ScriptableObject
{
    [Header("Info")]
    public EnemyType enemyType = EnemyType.Aswang;
    
    [Header("Basic Stats")]
    public string enemyName = "Enemy";
    public int maxHealth = 100;
    public int basicDamage = 10;
    // Only globalized stats here:
    [Header("Movement/Combat")]
    public float moveSpeed = 3.5f;
    public float chaseSpeed = 2.5f;
    public float rotationSpeedDegrees = 360f;
    public float backoffSpeedMultiplier = 0.6f;
    [Header("Combat")]
    public float attackRange = 2.2f;
    public float attackCooldown = 1.25f;
    public float attackMoveLock = 0.35f;
    public float detectionRange = 12f;
    public float chaseLoseRange = 15f;
    [Header("Patrol")]
    public bool enablePatrol = true;
    public float patrolRadius = 8f;
    public float patrolWait = 1.5f;
    [Header("AI Selection")]
    public float specialFacingAngle = 20f;
    
    [Header("AI Behavior")]
    [Range(0f, 1f)]
    public float aggressionLevel = 0.5f; // How likely to attack vs patrol
    [Range(0f, 1f)]
    public float intelligenceLevel = 0.5f; // How smart the AI decisions are
    
    [Header("Visual & Audio")]
    public GameObject deathVFXPrefab;
    public AudioClip deathSFX;
    public GameObject hitVFXPrefab;
    public AudioClip hitSFX;
    
    [Header("Networking")]
    public bool syncOverNetwork = true;
    public float networkUpdateRate = 10f; // Updates per second
    
    [Header("Debug")]
    public bool showDebugGizmos = false;
    public Color debugColor = Color.red;
}
