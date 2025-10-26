using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

// spawns enemies (e.g., Tikbalang) over a terrain using Perlin noise masks
// multiplayer-safe: only MasterClient spawns using PhotonNetwork.Instantiate; offline falls back to local Instantiate
public class PerlinEnemySpawner : MonoBehaviourPunCallbacks
{
    [Header("terrain")]
    public Terrain terrain; // assign the Terrain in FIRSTMAP
    public TerrainGenerator terrainGenerator; // optional; if assigned, will use its sandThreshold for water/beach avoidance

    [Header("enemy prefab (choose one)")]
    [Tooltip("Name of the enemy prefab under a Resources/ folder, for PhotonNetwork.Instantiate")] public string photonPrefabName = "Tikbalang";
    [Tooltip("Local prefab fallback used when offline/Play Mode without Photon")] public GameObject localPrefab;

    [Header("spawn distribution (perlin)")]
    [Tooltip("Desired number of enemies to spawn across the map")] public int desiredCount = 18;
    [Tooltip("Perlin frequency; smaller = larger features; tune to your island size")] public float perlinScale = 0.035f;
    [Range(0f, 1f)] public float perlinThreshold = 0.58f;
    public int seed = 0; // 0 = random
    [Tooltip("reduce threshold step-by-step if not enough valid spots found")] public float backoffStep = 0.05f;
    [Tooltip("minimum threshold when backing off")] public float minThreshold = 0.35f;

    [Header("placement filters")]
    [Tooltip("skip points with normalized height below this (water/beach). If TerrainGenerator is linked, this is auto-set")] public float minNormalizedHeight = 0.08f;
    [Tooltip("skip points with slope angle above this many degrees")] public float maxSlope = 28f;
    [Tooltip("enforce a minimum spacing between spawns (meters)")] public float minSpawnSpacing = 18f;

    [Header("debug")]
    public bool logSummary = true;
    public bool drawGizmos = false;
    [Range(0.1f, 2f)] public float gizmoSphere = 0.6f;

    private readonly List<Transform> _spawned = new List<Transform>();
    private Vector2 _offset;
    [System.NonSerialized] public int lastPlaced = 0;
    [System.NonSerialized] public int lastAttempts = 0;

    void Start()
    {
        if (terrain == null)
        {
            terrain = FindFirstObjectByType<Terrain>();
        }
        if (terrainGenerator == null)
        {
            terrainGenerator = FindFirstObjectByType<TerrainGenerator>();
        }
        if (terrainGenerator != null)
        {
            // use the terrain's sand threshold + a small buffer to keep spawns inland
            minNormalizedHeight = Mathf.Max(minNormalizedHeight, terrainGenerator.sandThreshold + 0.02f);
        }

        // only master spawns in multiplayer; otherwise offline we can spawn locally
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            if (logSummary) Debug.Log("perlin spawner: not master, skipping spawn on this client");
            enabled = false; // nothing to do
            return;
        }

        // initialize noise offset for seed variance
        int useSeed = seed != 0 ? seed : Random.Range(int.MinValue, int.MaxValue);
        var prng = new System.Random(useSeed);
        _offset = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

        SpawnAll();
    }

    public void ClearAll()
    {
        foreach (var t in _spawned)
        {
            if (t == null) continue;
            if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
            {
                var pv = t.GetComponent<PhotonView>();
                if (pv != null && pv.IsMine)
                {
                    PhotonNetwork.Destroy(t.gameObject);
                }
                else if (pv == null)
                {
                    Destroy(t.gameObject);
                }
            }
            else
            {
                Destroy(t.gameObject);
            }
        }
        _spawned.Clear();
    }

    public void SpawnAll()
    {
        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("perlin spawner: no terrain assigned");
            return;
        }

        ClearAll();

        var td = terrain.terrainData;
        Vector3 size = td.size; // x = width, y = height, z = length
        float threshold = perlinThreshold;
    int placed = 0;
    int attempts = 0;
        int maxAttempts = desiredCount * 50;
        var positions = new List<Vector3>();

        // sampling strategy: random rejection sampling with perlin mask + constraints
        while (placed < desiredCount && attempts < maxAttempts)
        {
            attempts++;
            // pick a random normalized point
            float nx = Random.value;
            float nz = Random.value;
            // sample perlin
            float vx = (nx * size.x) * perlinScale + _offset.x;
            float vz = (nz * size.z) * perlinScale + _offset.y;
            float p = Mathf.PerlinNoise(vx, vz);
            if (p < threshold) continue;

            float hNorm = td.GetInterpolatedHeight(nx, nz) / size.y;
            if (hNorm < minNormalizedHeight) continue;
            float slope = td.GetSteepness(nx, nz);
            if (slope > maxSlope) continue;

            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            // correct height using SampleHeight to align exactly on terrain
            world.y = terrain.SampleHeight(world) + terrain.transform.position.y;

            bool tooClose = false;
            foreach (var pt in positions)
            {
                if ((pt - world).sqrMagnitude < (minSpawnSpacing * minSpawnSpacing))
                { tooClose = true; break; }
            }
            if (tooClose) continue;

            // looks good, spawn it
            Transform spawned = DoSpawn(world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (spawned != null)
            {
                positions.Add(world);
                _spawned.Add(spawned);
                placed++;
                // adjust grounding after colliders initialize
                StartCoroutine(GroundAfterInit(spawned));
            }

            // if we can’t hit the target, relax threshold gradually
            if (attempts % 200 == 0 && placed < desiredCount && threshold > minThreshold)
            {
                threshold = Mathf.Max(minThreshold, threshold - backoffStep);
                if (logSummary) Debug.Log($"perlin spawner: backing off threshold to {threshold:F2} after {attempts} attempts, placed {placed}");
            }
        }

        lastPlaced = placed; lastAttempts = attempts;
        if (logSummary)
        {
            Debug.Log($"perlin spawner: placed {placed}/{desiredCount} enemies in {attempts} attempts");
        }
    }

    private Transform DoSpawn(Vector3 position, Quaternion rotation)
    {
        GameObject go = null;
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            // require Resources prefab for networked spawn
            if (!string.IsNullOrEmpty(photonPrefabName))
            {
                go = PhotonNetwork.Instantiate(photonPrefabName, position, rotation);
            }
            else if (localPrefab != null)
            {
                // fallback if no resource name configured (will not be networked)
                go = Instantiate(localPrefab, position, rotation);
            }
        }
        else
        {
            if (localPrefab != null)
            {
                go = Instantiate(localPrefab, position, rotation);
            }
            else if (!string.IsNullOrEmpty(photonPrefabName))
            {
                // allow offline Resources.Load as a convenience
                var res = Resources.Load<GameObject>(photonPrefabName);
                if (res != null) go = Instantiate(res, position, rotation);
            }
        }
        if (go == null)
        {
            Debug.LogWarning("perlin spawner: failed to spawn (no prefab configured or not found)");
            return null;
        }
        return go.transform;
    }

    private System.Collections.IEnumerator GroundAfterInit(Transform t)
    {
        // wait end of frame so colliders/skins report correct bounds
        yield return null; // 1 frame
        if (t == null || terrain == null) yield break;

        // compute accurate ground via terrain height and a safety raycast
        Vector3 pos = t.position;
        float groundY = terrain.SampleHeight(pos) + terrain.transform.position.y;
        // optional raycast to catch non-terrain colliders (rocks etc.) slightly above terrain
        Ray ray = new Ray(new Vector3(pos.x, groundY + 50f, pos.z), Vector3.down);
        if (Physics.Raycast(ray, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
        {
            groundY = hit.point.y;
        }

        // adjust so the bottom of the main collider sits on ground
        float bottomY = float.NaN;
        // prefer CharacterController bounds, then any Collider in children
        var cc = t.GetComponentInChildren<CharacterController>();
        if (cc != null)
        {
            bottomY = cc.bounds.min.y;
        }
        else
        {
            var col = t.GetComponentInChildren<Collider>();
            if (col != null) bottomY = col.bounds.min.y;
        }

        if (!float.IsNaN(bottomY))
        {
            float delta = groundY - bottomY;
            pos.y += delta + 0.02f; // tiny lift to avoid z-fighting
        }
        else
        {
            // no collider found: just place transform origin on ground
            pos.y = groundY;
        }

        // apply; if Rigidbody present and kinematic, move directly
        var rb = t.GetComponent<Rigidbody>();
        if (rb != null && rb.isKinematic)
            rb.MovePosition(pos);
        else
            t.position = pos;
    }

    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.red;
        foreach (var t in _spawned)
        {
            if (t == null) continue;
            Gizmos.DrawSphere(t.position, gizmoSphere);
        }
    }
}
