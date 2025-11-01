using UnityEngine;
using Photon.Pun;
using System.Collections.Generic;
using System.Collections;

public class EnemyManager : MonoBehaviourPun
{
    [Header("Enemy Management")]
    public Transform[] spawnPoints;
    public float spawnRadius = 2f;
    public int maxEnemiesPerSpawn = 3;
    public float spawnCooldown = 5f;
    
    [Header("Enemy Prefabs")]
    public GameObject[] enemyPrefabs;
    
    [Header("Debug")]
    public bool showDebugInfo = false;
    public bool enableSpawning = true;
    
    // Runtime data
    private List<BaseEnemyAI> activeEnemies = new List<BaseEnemyAI>();
    private float lastSpawnTime;
    private Dictionary<string, GameObject> enemyPrefabLookup = new Dictionary<string, GameObject>();
    
    // Events
    public System.Action<BaseEnemyAI> OnEnemySpawned;
    public System.Action<BaseEnemyAI> OnEnemyDied;
    public System.Action<int> OnEnemyCountChanged;
    
    #region Unity Lifecycle
    
    void Start()
    {
        InitializeEnemyLookup();
        SetupEnemyEvents();
    }
    
    void Update()
    {
        if (showDebugInfo)
        {
            Debug.Log($"[EnemyManager] Active Enemies: {activeEnemies.Count}");
        }
    }
    
    #endregion
    
    #region Initialization
    
    private void InitializeEnemyLookup()
    {
        enemyPrefabLookup.Clear();
        foreach (var prefab in enemyPrefabs)
        {
            if (prefab != null)
            {
                string enemyName = prefab.name.Replace("(Clone)", "").Trim();
                enemyPrefabLookup[enemyName] = prefab;
            }
        }
    }
    
    private void SetupEnemyEvents()
    {
        // Subscriptions are added per-enemy when they spawn
    }
    
    #endregion
    
    #region Public Spawning Methods
    
    public void SpawnEnemy(string enemyName, Vector3 position, Quaternion rotation)
    {
        if (!enableSpawning) return;
        if (!enemyPrefabLookup.ContainsKey(enemyName))
        {
            Debug.LogWarning($"[EnemyManager] Enemy prefab '{enemyName}' not found!");
            return;
        }
        
        GameObject enemyPrefab = enemyPrefabLookup[enemyName];
        SpawnEnemy(enemyPrefab, position, rotation);
    }
    
    public void SpawnEnemy(GameObject enemyPrefab, Vector3 position, Quaternion rotation)
    {
        if (!enableSpawning) return;
        
        GameObject enemyInstance = null;
        
        if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
        {
            // Network spawn
            enemyInstance = PhotonNetwork.Instantiate(enemyPrefab.name, position, rotation);
        }
        else
        {
            // Local spawn
            enemyInstance = Instantiate(enemyPrefab, position, rotation);
        }
        
        if (enemyInstance != null)
        {
            BaseEnemyAI enemyAI = enemyInstance.GetComponent<BaseEnemyAI>();
            if (enemyAI != null)
            {
                activeEnemies.Add(enemyAI);
                // subscribe to this instance's death event
                enemyAI.OnEnemyDied += HandleEnemyDied;
                OnEnemySpawned?.Invoke(enemyAI);
                OnEnemyCountChanged?.Invoke(activeEnemies.Count);
                
                if (showDebugInfo)
                {
                    Debug.Log($"[EnemyManager] Spawned {enemyAI.name} at {position}");
                }
            }
        }
    }
    
    public void SpawnEnemyAtRandomPoint(string enemyName)
    {
        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            Debug.LogWarning("[EnemyManager] No spawn points available!");
            return;
        }
        
        Transform spawnPoint = spawnPoints[Random.Range(0, spawnPoints.Length)];
        Vector3 randomOffset = Random.insideUnitSphere * spawnRadius;
        randomOffset.y = 0f; // Keep on ground level
        
        Vector3 spawnPosition = spawnPoint.position + randomOffset;
        SpawnEnemy(enemyName, spawnPosition, Quaternion.identity);
    }
    
    public void SpawnMultipleEnemies(string enemyName, int count)
    {
        StartCoroutine(SpawnMultipleEnemiesCoroutine(enemyName, count));
    }
    
    private IEnumerator SpawnMultipleEnemiesCoroutine(string enemyName, int count)
    {
        for (int i = 0; i < count; i++)
        {
            SpawnEnemyAtRandomPoint(enemyName);
            yield return new WaitForSeconds(spawnCooldown);
        }
    }
    
    #endregion
    
    #region Enemy Management
    
    public void ClearAllEnemies()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            if (activeEnemies[i] != null)
            {
                if (PhotonNetwork.IsConnected && !PhotonNetwork.OfflineMode)
                {
                    PhotonNetwork.Destroy(activeEnemies[i].gameObject);
                }
                else
                {
                    Destroy(activeEnemies[i].gameObject);
                }
            }
        }
        activeEnemies.Clear();
        OnEnemyCountChanged?.Invoke(0);
    }
    
    public void KillAllEnemies()
    {
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null && !enemy.IsDead)
            {
                enemy.TakeEnemyDamage(enemy.MaxHealth, gameObject);
            }
        }
    }
    
    public List<BaseEnemyAI> GetEnemiesInRange(Vector3 position, float range)
    {
        List<BaseEnemyAI> enemiesInRange = new List<BaseEnemyAI>();
        
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null && !enemy.IsDead)
            {
                float distance = Vector3.Distance(position, enemy.transform.position);
                if (distance <= range)
                {
                    enemiesInRange.Add(enemy);
                }
            }
        }
        
        return enemiesInRange;
    }
    
    #endregion
    
    #region Event Handlers
    
    private void HandleEnemyDied(BaseEnemyAI enemy)
    {
        if (activeEnemies.Contains(enemy))
        {
            activeEnemies.Remove(enemy);
            OnEnemyDied?.Invoke(enemy);
            OnEnemyCountChanged?.Invoke(activeEnemies.Count);
            
            if (showDebugInfo)
            {
                Debug.Log($"[EnemyManager] Enemy {enemy.name} died. Remaining: {activeEnemies.Count}");
            }
        }
    }
    
    #endregion
    
    #region Public Properties
    
    public int ActiveEnemyCount => activeEnemies.Count;
    public List<BaseEnemyAI> ActiveEnemies => new List<BaseEnemyAI>(activeEnemies);
    
    #endregion
    
    #region Debug
    
    void OnDrawGizmos()
    {
        if (spawnPoints != null)
        {
            Gizmos.color = Color.green;
            foreach (var spawnPoint in spawnPoints)
            {
                if (spawnPoint != null)
                {
                    Gizmos.DrawWireSphere(spawnPoint.position, spawnRadius);
                }
            }
        }
    }
    
    #endregion
    
    #region Cleanup
    
    void OnDestroy()
    {
        // Unsubscribe from instance events
        foreach (var enemy in activeEnemies)
        {
            if (enemy != null)
            {
                enemy.OnEnemyDied -= HandleEnemyDied;
            }
        }
    }
    
    #endregion
}
