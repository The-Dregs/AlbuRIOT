using System.Collections.Generic;
using UnityEngine;
using Photon.Pun;

// Generates level-1 map resources and points of interest over the procedurally generated island
// - Places 3 ship-remnant piles inland (not sand) and guards each with a distinct enemy type
// - Scatters herb pickups using a Perlin mask
// - Places a broken ship quest area on the island edge (land just above sand)
// - Chooses up to 4 randomized player spawn points on the island edge and spawns Nuno + starting boat near the chosen spawn
// Networking: Only MasterClient performs placement; state is synchronized via buffered RPCs. Offline works with local instantiation.
[RequireComponent(typeof(PhotonView))]
public class MapResourcesGenerator : MonoBehaviourPunCallbacks
{
    [Header("terrain")]
    [SerializeField] private Terrain terrain;
    [SerializeField] private TerrainGenerator terrainGenerator;

    [Header("remnants & guards")]
    [Tooltip("Three distinct remnant prefabs (index-aligned with guards): 0 Tikbalang, 1 Berberoka, 2 Bungisngis")] 
    [SerializeField] private string[] remnantPhotonPrefabs = new string[3];
    [SerializeField] private GameObject[] remnantLocalPrefabs = new GameObject[3];
    [Tooltip("Photon prefab names for the three distinct guards: Tikbalang, Berberoka, Bungisngis (in this order)")]
    [SerializeField] private string tikbalangPhotonPrefab;
    [SerializeField] private string berberokaPhotonPrefab;
    [SerializeField] private string bungisngisPhotonPrefab;
    [SerializeField] private GameObject tikbalangLocalPrefab;
    [SerializeField] private GameObject berberokaLocalPrefab;
    [SerializeField] private GameObject bungisngisLocalPrefab;
    [SerializeField, Range(2f, 30f)] private float guardOffset = 4.5f;

    [Header("herb resources")]
    [SerializeField] private string herbPhotonPrefab;
    [SerializeField] private GameObject herbLocalPrefab;
    [SerializeField, Range(0, 200)] private int herbDesiredCount = 60;
    [SerializeField] private float herbPerlinScale = 0.05f;
    [SerializeField, Range(0f, 1f)] private float herbPerlinThreshold = 0.6f;
    [SerializeField, Range(0.02f, 10f)] private float minHerbSpacing = 6f;

    [Header("broken ship quest area (edge)")]
    [SerializeField] private string brokenShipPhotonPrefab;
    [SerializeField] private GameObject brokenShipLocalPrefab;
    [SerializeField, Range(0.5f, 8f)] private float edgeBandAboveSand = 0.03f; // how far above sand to consider edge
    [SerializeField, Range(0.01f, 0.2f)] private float edgeBandWidth = 0.04f;

    [Header("spawn points (up to 4 players)")]
    [SerializeField, Range(1, 4)] private int maxPlayers = 4;
    [SerializeField] private string playerSpawnMarkerPhotonPrefab; // optional marker prefab to visualize spawn points
    [SerializeField] private GameObject playerSpawnMarkerLocalPrefab;
    [SerializeField] private string nunoPhotonPrefab;
    [SerializeField] private GameObject nunoLocalPrefab;
    [SerializeField] private string boatPhotonPrefab;
    [SerializeField] private GameObject boatLocalPrefab;
    [SerializeField, Range(1f, 15f)] private float spawnSpacing = 10f; // spacing between spawn points
    [SerializeField, Range(1f, 20f)] private float nunoBoatOffset = 6f; // placed beside first spawn

    [Header("filters & misc")] 
    [SerializeField, Range(0f, 60f)] private float maxSlope = 28f;
    [SerializeField] private bool logSummary = true;

    private readonly List<Transform> spawned = new List<Transform>();
    private Vector2 perlinOffset;

    void Start()
    {
        if (terrain == null) terrain = FindFirstObjectByType<Terrain>();
        if (terrainGenerator == null) terrainGenerator = FindFirstObjectByType<TerrainGenerator>();

        // Master only spawns; others receive buffered RPC state
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient)
        {
            if (logSummary) Debug.Log("MapResourcesGenerator: not master, skipping local generation");
            enabled = false;
            return;
        }

        int seed = Random.Range(int.MinValue, int.MaxValue);
        var prng = new System.Random(seed);
        perlinOffset = new Vector2(prng.Next(-100000, 100000), prng.Next(-100000, 100000));

        StartCoroutine(GenerateWhenTerrainReady());
    }

    private System.Collections.IEnumerator GenerateWhenTerrainReady()
    {
        // Wait until terrain data is applied by TerrainGenerator. Prefer its metrics; fallback to a short frame wait.
        float timeout = 3f; // seconds
        float waited = 0f;
        while (true)
        {
            bool ready = terrain != null && terrain.terrainData != null;
            if (ready && terrainGenerator != null)
            {
                ready = terrainGenerator.lastMetrics.totalMs > 0.01; // generated at least once
            }
            if (ready) break;
            if (waited > timeout) break;
            yield return null; waited += Time.deltaTime;
        }
        GenerateAll();
    }

    public void GenerateAll()
    {
        ApplyGenerateAll();
        // if no PhotonView on this object (or not connected), skip RPC; still works offline
        if (PhotonNetwork.IsMasterClient && photonView != null)
        {
            photonView.RPC("RPC_GenerateAll", RpcTarget.AllBuffered);
        }
    }

    [PunRPC]
    private void RPC_GenerateAll()
    {
        ApplyGenerateAll();
    }

    private void ApplyGenerateAll()
    {
        if (terrain == null || terrain.terrainData == null || terrainGenerator == null)
        {
            Debug.LogWarning("MapResourcesGenerator: missing Terrain/TerrainGenerator");
            return;
        }

        spawned.Clear();

        // 1) Place three ship remnants inland with guards
        var inlandPositions = FindInlandPositions(3, 0.08f, 18f);
        string[] guardPhoton = { tikbalangPhotonPrefab, berberokaPhotonPrefab, bungisngisPhotonPrefab };
        GameObject[] guardLocal = { tikbalangLocalPrefab, berberokaLocalPrefab, bungisngisLocalPrefab };
        for (int i = 0; i < inlandPositions.Count; i++)
        {
            Vector3 pos = inlandPositions[i];
            string remPhoton = i < remnantPhotonPrefabs.Length ? remnantPhotonPrefabs[i] : null;
            GameObject remLocal = i < remnantLocalPrefabs.Length ? remnantLocalPrefabs[i] : null;
            var rem = SpawnPrefab(remPhoton, remLocal, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (rem != null) spawned.Add(rem);
            // guard: position a short offset facing the remnant
            Vector3 dir = Random.insideUnitSphere; dir.y = 0f; if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            Vector3 gpos = pos + dir.normalized * guardOffset; gpos.y = GroundY(gpos);
            var guard = SpawnPrefab(guardPhoton[i], guardLocal[i], gpos, Quaternion.LookRotation(-dir.normalized, Vector3.up));
            if (guard != null) spawned.Add(guard);
        }

        // 2) Herbs scattered by perlin
        ScatterHerbs();

        // 3) Broken ship quest area on the island edge
        Vector3 edge = FindEdgePosition(spawnSpacing, maxSlope);
        var broken = SpawnPrefab(brokenShipPhotonPrefab, brokenShipLocalPrefab, edge, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        if (broken != null) spawned.Add(broken);

        // 4) Player spawn points (clustered side-by-side at a single edge site)
        var spawns = GenerateClusteredEdgeSpawns(maxPlayers, spawnSpacing, maxSlope);
        for (int i = 0; i < spawns.Count; i++)
        {
            var marker = SpawnPrefab(playerSpawnMarkerPhotonPrefab, playerSpawnMarkerLocalPrefab, spawns[i], Quaternion.identity);
            if (marker == null)
            {
                // create a simple builtin marker if nothing was provided
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.transform.position = spawns[i];
                go.transform.localScale = new Vector3(0.6f, 1.2f, 0.6f);
                go.name = $"SpawnMarker_{i+1}";
                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null) mr.material.color = new Color(0.2f, 0.8f, 1f, 0.9f);
                marker = go.transform;
            }
            if (marker != null) spawned.Add(marker);
        }
        // place Nuno + starting boat beside the first spawn
        if (spawns.Count > 0)
        {
            Vector3 basePos = spawns[0];
            Vector3 rightDir = Vector3.Cross(Vector3.up, ApproxTerrainNormal(basePos)).normalized;
            Vector3 beside = basePos + rightDir * nunoBoatOffset; beside.y = GroundY(beside);
            var nuno = SpawnPrefab(nunoPhotonPrefab, nunoLocalPrefab, beside, Quaternion.LookRotation(-rightDir));
            if (nuno != null) spawned.Add(nuno);
            Vector3 boatPos = basePos - rightDir * (nunoBoatOffset * 0.8f); boatPos.y = GroundY(boatPos);
            var boat = SpawnPrefab(boatPhotonPrefab, boatLocalPrefab, boatPos, Quaternion.LookRotation(rightDir));
            if (boat != null) spawned.Add(boat);
        }

        if (logSummary)
        {
            Debug.Log($"MapResourcesGenerator: placed {spawned.Count} objects (remnants+guards+herbs+quest+spawns)");
        }
    }

    private void ScatterHerbs()
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        var positions = new List<Vector3>(herbDesiredCount);
        int attempts = 0; int maxAttempts = herbDesiredCount * 60;
        while (positions.Count < herbDesiredCount && attempts < maxAttempts)
        {
            attempts++;
            float nx = Random.value; float nz = Random.value;
            float vx = (nx * size.x) * herbPerlinScale + perlinOffset.x;
            float vz = (nz * size.z) * herbPerlinScale + perlinOffset.y;
            if (Mathf.PerlinNoise(vx, vz) < herbPerlinThreshold) continue;
            float hNorm = td.GetInterpolatedHeight(nx, nz) / size.y;
            if (hNorm <= sand + 0.01f) continue; // avoid sand
            float slope = td.GetSteepness(nx, nz);
            if (slope > maxSlope) continue;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundY(world);
            bool tooClose = false;
            foreach (var p in positions) { if ((p - world).sqrMagnitude < minHerbSpacing * minHerbSpacing) { tooClose = true; break; } }
            if (tooClose) continue;
            positions.Add(world);
            var herb = SpawnPrefab(herbPhotonPrefab, herbLocalPrefab, world, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            if (herb != null) spawned.Add(herb);
        }
    }

    private List<Vector3> FindInlandPositions(int count, float spacing, float minDistanceFromEdge)
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        var positions = new List<Vector3>(count);
        int attempts = 0; int maxAttempts = count * 800;
        while (positions.Count < count && attempts < maxAttempts)
        {
            attempts++;
            float nx = Random.value; float nz = Random.value;
            float hNorm = td.GetInterpolatedHeight(nx, nz) / size.y;
            if (hNorm <= sand + 0.08f) continue; // ensure clearly inland grass
            float slope = td.GetSteepness(nx, nz); if (slope > maxSlope) continue;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundY(world);
            bool tooClose = false; foreach (var p in positions) { if ((p - world).sqrMagnitude < spacing * spacing) { tooClose = true; break; } }
            if (tooClose) continue;
            positions.Add(world);
        }
        // Fallback: if still short, relax constraints and try again to guarantee placement
        while (positions.Count < count)
        {
            float nx = Random.value; float nz = Random.value;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundY(world);
            positions.Add(world);
        }
        return positions;
    }

    private Vector3 FindEdgePosition(float spacing, float slopeLimit)
    {
        var list = FindMultipleEdgePositions(1, spacing, slopeLimit);
        return list.Count > 0 ? list[0] : terrain.transform.position;
    }

    private List<Vector3> FindMultipleEdgePositions(int count, float spacing, float slopeLimit)
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        float sand = Mathf.Max(0.001f, terrainGenerator.sandThreshold);
        float minH = sand + Mathf.Max(0.005f, edgeBandAboveSand);
        float maxH = minH + Mathf.Max(0.01f, edgeBandWidth);
        var positions = new List<Vector3>(count);
        int attempts = 0; int maxAttempts = count * 1200;
        while (positions.Count < count && attempts < maxAttempts)
        {
            attempts++;
            float nx = Random.value; float nz = Random.value;
            float hNorm = td.GetInterpolatedHeight(nx, nz) / size.y;
            if (hNorm < minH || hNorm > maxH) continue; // edge band
            float slope = td.GetSteepness(nx, nz); if (slope > slopeLimit) continue;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundY(world);
            bool tooClose = false; foreach (var p in positions) { if ((p - world).sqrMagnitude < spacing * spacing) { tooClose = true; break; } }
            if (tooClose) continue;
            positions.Add(world);
        }
        // Fallback: if none found, just pick near-terrain center edge-ish ring using minH
        while (positions.Count < count)
        {
            float nx = Random.value; float nz = Random.value;
            Vector3 world = new Vector3(nx * size.x, 0f, nz * size.z) + terrain.transform.position;
            world.y = GroundY(world);
            positions.Add(world);
        }
        return positions;
    }

    // Produces a clustered set of edge spawn points placed side-by-side along the shoreline band
    private List<Vector3> GenerateClusteredEdgeSpawns(int count, float spacing, float slopeLimit)
    {
        var result = new List<Vector3>(count);
        var basePos = FindEdgePosition(spacing, slopeLimit);
        Vector3 normal = ApproxTerrainNormal(basePos);
        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 1e-3f) tangent = Vector3.right;
        tangent.Normalize();
        float step = Mathf.Max(1f, spacing * 0.6f);
        // center the cluster around basePos
        int leftCount = (count - 1) / 2;
        int rightCount = count - 1 - leftCount;
        for (int i = leftCount; i > 0; i--)
        {
            Vector3 p = basePos - tangent * (i * step); p.y = GroundY(p);
            result.Add(p);
        }
        result.Add(basePos);
        for (int i = 1; i <= rightCount; i++)
        {
            Vector3 p = basePos + tangent * (i * step); p.y = GroundY(p);
            result.Add(p);
        }
        return result;
    }

    private float GroundY(Vector3 world)
    {
        float y = terrain.SampleHeight(world) + terrain.transform.position.y;
        Ray ray = new Ray(new Vector3(world.x, y + 50f, world.z), Vector3.down);
        if (Physics.Raycast(ray, out var hit, 120f, ~0, QueryTriggerInteraction.Ignore))
            y = hit.point.y;
        return y;
    }

    private Vector3 ApproxTerrainNormal(Vector3 world)
    {
        var td = terrain.terrainData; Vector3 size = td.size;
        Vector3 local = world - terrain.transform.position;
        float nx = Mathf.Clamp01(local.x / size.x);
        float nz = Mathf.Clamp01(local.z / size.z);
        Vector3 normal = td.GetInterpolatedNormal(nx, nz);
        if (normal.sqrMagnitude < 1e-3f) normal = Vector3.up;
        return normal;
    }

    private Transform SpawnPrefab(string photonName, GameObject localPrefab, Vector3 position, Quaternion rotation)
    {
        GameObject go = null;
        if (PhotonNetwork.IsConnected && PhotonNetwork.InRoom)
        {
            if (!string.IsNullOrEmpty(photonName)) go = PhotonNetwork.Instantiate(photonName, position, rotation);
            else if (localPrefab != null) go = Instantiate(localPrefab, position, rotation);
        }
        else
        {
            if (localPrefab != null) go = Instantiate(localPrefab, position, rotation);
            else if (!string.IsNullOrEmpty(photonName))
            {
                var res = Resources.Load<GameObject>(photonName);
                if (res != null) go = Instantiate(res, position, rotation);
            }
        }
        return go != null ? go.transform : null;
    }
}


