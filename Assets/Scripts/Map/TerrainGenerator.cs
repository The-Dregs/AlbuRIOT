using UnityEngine;
using System.Collections.Generic;

public class TerrainGenerator : MonoBehaviour
{
    [Header("Terrain Size")]
    public int width = 256;
    public int height = 256;
    public float heightMultiplier = 30f;

    [Header("Simple Controls")]
    [Tooltip("0 = random each run")] public int seed = 0;
    [Range(40f, 400f)] public float islandScale = 160f; // feature size – bigger = fewer, larger landmasses
    [Range(0.25f, 0.85f)] public float targetLandPercent = 0.6f; // desired land coverage
    [Range(0.01f, 0.2f)] public float beachWidth = 0.08f; // thickness of the sand band in normalized height space
    [Range(0f, 1f)] public float mountainAmount = 0.35f; // fraction of land that becomes mountain
    [Range(0f, 1f)] public float roughness = 0.55f; // higher = more fragmented terrain

    [Header("Elevation Controls")]
    [Range(0f, 0.2f)] public float underwaterDepth = 0.02f; // height mapped for underwater sand
    [Range(0f, 0.6f)] public float shorelineLow = 0.05f; // start of beach ramp
    [Range(0.05f, 0.6f)] public float shorelineHigh = 0.3f; // end of beach ramp (before grass)
    [Range(0.15f, 0.7f)] public float inlandBase = 0.38f; // inland base elevation

    [Header("Beach Tuning")]
    [Range(0f, 0.12f)] public float duneAmplitude = 0.06f;
    [Range(0.05f, 0.6f)] public float duneScale = 0.25f; // relative scale factor vs islandScale
    [Range(0f, 0.1f)] public float shoreDrop = 0.03f; // small lowering near water line

    [Header("Shape & Smoothness")]
    [Range(0f, 1f)] public float centerBias = 0.65f; // 1 = strong island center, 0 = no bias
    [Range(0, 6)] public int smoothIterations = 3; // stronger blur for smoother shoreline
    [Range(0f, 0.35f)] public float cavityAmount = 0.08f; // depth of inland holes/lakes
    [Range(0.2f, 2.5f)] public float cavityScale = 0.9f; // relative to islandScale

    [Header("Compatibility (read-only where possible)")]
    [Range(0.001f, 0.2f)] public float sandThreshold = 0.02f; // kept for other scripts; auto-derived from beachWidth

    [Header("Terrain Layers")]
    public TerrainLayer sandLayer;
    public TerrainLayer grassLayer1;
    public TerrainLayer grassLayer2;
    public TerrainLayer grassLayer3;

    [Header("Grass Probabilities (0-1)")]
    [Range(0f, 1f)] public float grass1Prob = 0.4f;
    [Range(0f, 1f)] public float grass2Prob = 0.4f;
    [Range(0f, 1f)] public float grass3Prob = 0.2f;
    [Range(0.01f, 4f)] public float grassNoiseScale = 1.2f; // higher = more variation tiles

    [Header("Object Spawning")]
    public GameObject[] treePrefabs;
    public GameObject[] rockPrefabs;
    [Range(0, 10000)] public int treeCount = 300;
    [Range(0, 10000)] public int rockCount = 120;
    [Range(1f, 40f)] public float treeMinSpacing = 6f;
    [Range(1f, 60f)] public float rockMinSpacing = 8f;
    [Range(0f, 60f)] public float maxObjectSlope = 28f;
    [Range(0.01f, 0.4f)] public float minGrassHeight = 0.02f; // minimal height above sand threshold for objects

    public Terrain terrain;

    [System.Serializable]
    public struct TerrainMetrics
    {
        public int seed;
        public int width; public int height;
        public int landBlocks; public int sandBlocks; public int totalBlocks;
        public float landPercent; public float sandPercent;
        public float meanHeight; public float stdDevHeight;
        public float mountainFraction; public float meanHeightMountain; public float meanHeightNonMountain;
        public float radialCorrelation; // correlation between height and radial island core factor
        public double msHeightCompute; public double msSplatCompute; public double msObjects; public double msDetails; public double msApplyHeight; public double msApplySplat; public double totalMs;
    }

    public TerrainMetrics lastMetrics;

    void Start()
    {
        GenerateTerrain();
    }

    // generate terrain heightmap and paint textures and grass details
    public void GenerateTerrain()
    {
        var swTotal = System.Diagnostics.Stopwatch.StartNew();
        var sw = new System.Diagnostics.Stopwatch();
        int useSeed = seed != 0 ? seed : Random.Range(int.MinValue, int.MaxValue);
        System.Random prng = new System.Random(useSeed);
        float offX = prng.Next(-100000, 100000);
        float offY = prng.Next(-100000, 100000);

        float[,] finalHeightMap = new float[width, height];
        int landBlocks = 0;
        int sandBlocks = 0;
        double sumH = 0.0; double sumH2 = 0.0;
        // Precompute simple octaves based on roughness
        int baseOctaves = Mathf.RoundToInt(Mathf.Lerp(2, 5, Mathf.Clamp01(roughness)));
        float persistence = Mathf.Lerp(0.35f, 0.6f, Mathf.Clamp01(roughness));
        float lacunarity = Mathf.Lerp(1.8f, 2.6f, Mathf.Clamp01(roughness));

        // Falloff produces an islandy shape (square mask softened towards the edges)
        // derive falloff sharpness from centerBias to avoid harsh edges
        float aFall = Mathf.Lerp(0.9f, 1.8f, Mathf.Clamp01(centerBias));
        float bFall = Mathf.Lerp(1.4f, 2.6f, Mathf.Clamp01(centerBias));
        float[,] falloffMap = GenerateFalloffMap(width, height, aFall, bFall);

        sw.Restart();
        // First pass: create a continuous height field
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x + offX) / islandScale;
                float ny = (y + offY) / islandScale;
                float baseNoise = FractalNoise(nx, ny, baseOctaves, persistence, lacunarity);
                // reduce center bias by scaling falloff influence
                float fall = Mathf.Lerp(0f, falloffMap[x, y], Mathf.Clamp01(centerBias));
                float h = Mathf.Clamp01(baseNoise - fall);
                // pre-smooth the primary height signal to avoid harsh terraces
                h = Mathf.SmoothStep(0f, 1f, h);
                // carve inland cavities/holes
                if (cavityAmount > 0f)
                {
                    float cx = (x + offX * 0.21f) / (islandScale * Mathf.Max(0.05f, cavityScale));
                    float cy = (y + offY * 0.21f) / (islandScale * Mathf.Max(0.05f, cavityScale));
                    float cav = Mathf.PerlinNoise(cx, cy);
                    float cavity = Mathf.Max(0f, (cav - 0.5f) * 2f) * cavityAmount;
                    h = Mathf.Clamp01(h - cavity * (1f - fall)); // less cavities near border
                }
                finalHeightMap[x, y] = h;
            }
        }
        sw.Stop();
        double msCompute = sw.Elapsed.TotalMilliseconds;

        // Optional smoothing to improve transitions
        if (smoothIterations > 0)
        {
            SmoothHeightsInPlace(finalHeightMap, width, height, Mathf.Clamp(smoothIterations, 3, 6));
            // relax edges where falloff is high to guarantee a wide shoreline ramp
            EdgeRelax(finalHeightMap, falloffMap, width, height, 0.55f, 3, 0.6f);
        }

        // Auto-threshold to hit target land percent; compute classification threshold in height space
        float thresh = FindHeightThreshold(finalHeightMap, targetLandPercent);
        // beach (sand) sits just below land threshold using beachWidth
        sandThreshold = Mathf.Clamp(thresh, 0.01f, 0.3f);
        float grassStart = sandThreshold + beachWidth;

        TerrainData terrainData = terrain.terrainData;
        terrainData.heightmapResolution = width + 1;
        terrainData.alphamapResolution = width;
        terrainData.size = new Vector3(width, heightMultiplier, height);

        // assign terrain layers only if they are non-null to avoid engine issues
        var layerList = new System.Collections.Generic.List<TerrainLayer>(4);
        if (sandLayer != null) layerList.Add(sandLayer);
        if (grassLayer1 != null) layerList.Add(grassLayer1);
        if (grassLayer2 != null) layerList.Add(grassLayer2);
        if (grassLayer3 != null) layerList.Add(grassLayer3);
        if (layerList.Count > 0)
        {
            terrainData.terrainLayers = layerList.ToArray();
        }
        else
        {
            Debug.LogWarning("terrain generator: no TerrainLayers assigned; skipping layer assignment");
        }

        // Compose final physical heights: continuous C1 mapping around shoreline to avoid steps
        float[,] physical = new float[width, height];
        sw.Restart();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = finalHeightMap[x, y];
                // base elevation curve
                float baseCurve = Mathf.SmoothStep(0f, 1f, h);
                float edgeFactor = Mathf.Clamp01(falloffMap[x, y]);

                // signed distance to shoreline in noise space
                float d = h - sandThreshold;
                float heightNorm;
                if (d < 0f)
                {
                    // underwater: smooth step from deep to shoreline with C1 continuity
                    float t = Mathf.SmoothStep(-beachWidth, 0f, d);
                    heightNorm = Mathf.Lerp(0f, underwaterDepth, t);
                }
                else if (d < beachWidth)
                {
                    // beach band: smooth ramp
                    float t = Mathf.SmoothStep(0f, 1f, d / Mathf.Max(1e-5f, beachWidth));
                    heightNorm = Mathf.Lerp(shorelineLow, shorelineHigh, t);

                    // dunes near edge
                    float duneNoise = Mathf.PerlinNoise((x + offX * 0.5f) / (islandScale * Mathf.Max(0.05f, duneScale)), (y + offY * 0.5f) / (islandScale * Mathf.Max(0.05f, duneScale)));
                    float dune = (duneNoise - 0.5f) * 2f;
                    heightNorm += dune * (duneAmplitude * Mathf.SmoothStep(0f, 1f, edgeFactor));

                    float drop = shoreDrop * Mathf.SmoothStep(0.6f, 1.0f, edgeFactor);
                    heightNorm = Mathf.Max(0f, heightNorm - drop * (1f - t));
                }
                else
                {
                    // inland: continuous extension from beach using base curve, plus subtle highland modulation
                    float t = Mathf.InverseLerp(grassStart, 1f, h);
                    t = t * t * (3f - 2f * t); // smootherstep
                    heightNorm = Mathf.Lerp(inlandBase, 1.0f, t * baseCurve);

                    // add micro-terrain variation only in highlands so it doesn't affect beaches
                    float hx = (x + offX * 0.63f) / (islandScale * 0.42f);
                    float hy = (y + offY * 0.63f) / (islandScale * 0.42f);
                    float highland = FractalNoise(hx, hy, 4, 0.53f, 2.15f) - 0.5f; // [-0.5,0.5]
                    float amp = Mathf.Lerp(0.02f, 0.12f, Mathf.Clamp01(roughness));
                    heightNorm = Mathf.Clamp01(heightNorm + highland * amp * t);
                }

                // Enforce a world-edge ramp using the falloff map (prevents cliffs regardless of noise)
                // fall=0 center, 1 at edges. Blend targetEdge curve as fall approaches 1
                float fall = edgeFactor;
                // earlier start + stronger blend for a wider, smoother coastal ramp
                float rampT = Mathf.SmoothStep(0.45f, 0.985f, fall);
                // target edge curve widens the shallow shelf then rises toward shorelineLow
                float edgeInner = Mathf.SmoothStep(0.35f, 0.95f, 1f - fall);
                float targetEdge = Mathf.Lerp(underwaterDepth, shorelineLow, edgeInner);
                heightNorm = Mathf.Lerp(heightNorm, targetEdge, Mathf.Clamp01(rampT));

                physical[x, y] = Mathf.Clamp01(heightNorm);
            }
        }
        // extra shoreline softening pass: blur only low-to-mid elevations to avoid sharp cliffs
        ShorelineSoftenInPlace(physical, width, height, grassStart, 2);
        AdaptiveLowlandSmoothing(physical, width, height, Mathf.Max(shorelineHigh + 0.02f, grassStart + 0.04f), 6, 0.5f);
        // ensure continuity exactly around elevation control thresholds regardless of values
        IsocontourSmoothing(physical, finalHeightMap, width, height, sandThreshold, grassStart, beachWidth * 1.25f, 2);
        terrainData.SetHeights(0, 0, physical);
        sw.Stop();
        double msApplyHeight = sw.Elapsed.TotalMilliseconds;
        int totalBlocks = width * height;

        sw.Restart();
        float[,,] splatmap = new float[width, width, 4];
        for (int y = 0; y < width; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = finalHeightMap[x, y];
                float[] weights = new float[4];
                if (h < sandThreshold)
                {
                    weights[0] = 1f; // sand
                }
                else if (h < grassStart)
                {
                    // smooth beach->grass transition; keep sand visible and pick grass via probabilities
                    float t = Mathf.InverseLerp(sandThreshold, grassStart, h);
                    float edgeFactor = Mathf.Clamp01(falloffMap[x, y]);
                    float elevatedSandBoost = 0.12f * Mathf.SmoothStep(0.6f, 1.0f, edgeFactor);
                    weights[0] = Mathf.Clamp01((1f - t) * 0.9f + elevatedSandBoost);

                    // stochastic, non-tiled selector by fBm noise in world scale
                    float gnx = (x + offX * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                    float gny = (y + offY * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                    float r = FractalNoise(gnx, gny, 2, 0.55f, 2.15f);
                    float p1 = Mathf.Clamp01(grass1Prob);
                    float p2 = Mathf.Clamp01(grass2Prob);
                    float p3 = Mathf.Clamp01(grass3Prob);
                    float sumP = Mathf.Max(0.0001f, p1 + p2 + p3);
                    r *= sumP;
                    if (r < p1) weights[1] = t; else if (r < p1 + p2) weights[2] = t; else weights[3] = t;
                }
                else
                {
                    // interior: random grass with bias, plus a small fraction of rocky patches
                    float mx = (x + offX * 0.37f) / (islandScale * 0.6f);
                    float my = (y + offY * 0.37f) / (islandScale * 0.6f);
                    float mNoise = FractalNoise(mx, my, baseOctaves + 1, persistence, lacunarity);
                    // rocky zones appear sparsely and mainly in mid/highlands, not by a large height band
                    bool rocky = (mNoise > 0.82f) && (finalHeightMap[x, y] > grassStart + 0.05f);
                    float p1 = Mathf.Clamp01(grass1Prob);
                    float p2 = Mathf.Clamp01(grass2Prob);
                    float p3 = Mathf.Clamp01(grass3Prob * 0.6f); // slightly reduce rock/grass3 dominance
                    float sumP = Mathf.Max(0.0001f, p1 + p2 + p3);
                    if (rocky) { weights[3] = 1f; }
                    else {
                        float gnx = (x + offX * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                        float gny = (y + offY * 0.71f) / (islandScale * Mathf.Max(0.15f, grassNoiseScale));
                        float r = FractalNoise(gnx, gny, 2, 0.55f, 2.15f) * sumP;
                        if (r < p1) weights[1] = 1f; else if (r < p1 + p2) weights[2] = 1f; else weights[3] = 1f;
                    }
                }
                float total = weights[0] + weights[1] + weights[2] + weights[3];
                for (int i = 0; i < 4; i++)
                    splatmap[x, y, i] = weights[i] / total;
            }
        }
        sw.Stop(); double msSplatCompute = sw.Elapsed.TotalMilliseconds;
        sw.Restart(); terrainData.SetAlphamaps(0, 0, splatmap); sw.Stop(); double msApplySplat = sw.Elapsed.TotalMilliseconds;
        double msObjects = 0.0;

        // disable terrain tree/rock prototypes and instances to avoid collider creation and native crashes
        // this clears any existing prototypes/instances and skips placement entirely
        sw.Restart();
        terrainData.treePrototypes = System.Array.Empty<TreePrototype>();
        terrainData.treeInstances = System.Array.Empty<TreeInstance>();
        sw.Stop(); msObjects = sw.Elapsed.TotalMilliseconds;

        // GameObject-based tree/rock placement (safer than Terrain trees; supports any prefab)
        var terrainRoot = terrain.transform;
        Transform treesRoot = terrainRoot.Find("_Trees"); if (treesRoot == null) { var go = new GameObject("_Trees"); go.transform.SetParent(terrainRoot, false); treesRoot = go.transform; }
        Transform rocksRoot = terrainRoot.Find("_Rocks"); if (rocksRoot == null) { var go = new GameObject("_Rocks"); go.transform.SetParent(terrainRoot, false); rocksRoot = go.transform; }
        // clear previous
        for (int i = treesRoot.childCount - 1; i >= 0; i--) DestroyImmediate(treesRoot.GetChild(i).gameObject);
        for (int i = rocksRoot.childCount - 1; i >= 0; i--) DestroyImmediate(rocksRoot.GetChild(i).gameObject);

        // sampling helper
        bool IsValidObjectSpot(int px, int py)
        {
            float hVal = finalHeightMap[px, py];
            if (hVal < sandThreshold + minGrassHeight) return false; // avoid sand/shore
            float nx2 = px / (float)width; float ny2 = py / (float)height;
            float slope = terrainData.GetSteepness(nx2, ny2);
            if (slope > maxObjectSlope) return false;
            // must be on grass (sum of grass layers sufficiently large)
            float sandW = splatmap[px, py, 0];
            float g1 = splatmap[px, py, 1]; float g2 = splatmap[px, py, 2]; float g3 = splatmap[px, py, 3];
            if (sandW > 0.05f) return false; // not on sand
            if (g1 + g2 + g3 < 0.6f) return false;
            return true;
        }

        // Place with simple rejection sampling + spacing in world space
        Vector3 tSize = terrainData.size;
        System.Random objRng = new System.Random(useSeed + 777);
        List<Vector3> placedTrees = new List<Vector3>(treeCount);
        List<Vector3> placedRocks = new List<Vector3>(rockCount);

        if (treePrefabs != null && treePrefabs.Length > 0 && treeCount > 0)
        {
            int attempts = 0, maxAttempts = treeCount * 60;
            while (placedTrees.Count < treeCount && attempts < maxAttempts)
            {
                attempts++;
                int px = objRng.Next(0, width);
                int py = objRng.Next(0, height);
                if (!IsValidObjectSpot(px, py)) continue;
                Vector3 world = new Vector3(px / (float)width * tSize.x, 0f, py / (float)height * tSize.z) + terrainRoot.position;
                world.y = terrain.SampleHeight(world) + terrainRoot.position.y;
                bool tooClose = false; foreach (var p in placedTrees) { if ((p - world).sqrMagnitude < treeMinSpacing * treeMinSpacing) { tooClose = true; break; } }
                if (tooClose) continue;
                int idx = objRng.Next(0, treePrefabs.Length);
                var prefab = treePrefabs[idx]; if (prefab == null) continue;
                var go = Instantiate(prefab, world, Quaternion.Euler(0f, (float)objRng.NextDouble() * 360f, 0f), treesRoot);
                placedTrees.Add(world);
            }
        }
        if (rockPrefabs != null && rockPrefabs.Length > 0 && rockCount > 0)
        {
            int attempts = 0, maxAttempts = rockCount * 60;
            while (placedRocks.Count < rockCount && attempts < maxAttempts)
            {
                attempts++;
                int px = objRng.Next(0, width);
                int py = objRng.Next(0, height);
                if (!IsValidObjectSpot(px, py)) continue;
                Vector3 world = new Vector3(px / (float)width * tSize.x, 0f, py / (float)height * tSize.z) + terrainRoot.position;
                world.y = terrain.SampleHeight(world) + terrainRoot.position.y;
                bool tooClose = false; foreach (var p in placedRocks) { if ((p - world).sqrMagnitude < rockMinSpacing * rockMinSpacing) { tooClose = true; break; } }
                if (tooClose) continue;
                int idx = objRng.Next(0, rockPrefabs.Length);
                var prefab = rockPrefabs[idx]; if (prefab == null) continue;
                var go = Instantiate(prefab, world, Quaternion.Euler(0f, (float)objRng.NextDouble() * 360f, 0f), rocksRoot);
                placedRocks.Add(world);
            }
        }


        swTotal.Stop();
        // compute summary stats
        int totalBlocks2 = width * height;
        // recompute land/sand percent for metrics using the tuned threshold and final heights
        int landCount = 0, sandCount = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = finalHeightMap[x, y];
                if (h < sandThreshold) sandCount++; else landCount++;
            }
        }
        float landPercent = (float)landCount / Mathf.Max(1, totalBlocks2);
        float sandPercent = (float)sandCount / Mathf.Max(1, totalBlocks2);
        double mean = sumH / totalBlocks2;
        double variance = Mathf.Max(0f, (float)(sumH2 / totalBlocks2 - mean * mean));
        float stdDev = Mathf.Sqrt((float)variance);
        float mountainFraction = mountainAmount;
        float meanMount = 0f;
        float meanNonMount = 0f;

        // radial correlation between height and island-core factor r (1 at center -> 0 at border)
        double sumX=0, sumY=0, sumXX=0, sumYY=0, sumXY=0; int n=0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx2 = x / (float)width * 2 - 1;
                float ny2 = y / (float)height * 2 - 1;
                float rCore = 1f - Mathf.Max(Mathf.Abs(nx2), Mathf.Abs(ny2)); // 1 center .. 0 edges
                float hv = finalHeightMap[x, y];
                double X = rCore; double Y = hv;
                sumX += X; sumY += Y; sumXX += X*X; sumYY += Y*Y; sumXY += X*Y; n++;
            }
        }
        double denom = System.Math.Sqrt(System.Math.Max(1e-6, (n*sumXX - sumX*sumX) * (n*sumYY - sumY*sumY)));
        float corr = denom > 0 ? (float)((n*sumXY - sumX*sumY) / denom) : 0f;

        lastMetrics = new TerrainMetrics
        {
            seed = seed,
            width = width, height = height,
            landBlocks = landBlocks, sandBlocks = sandBlocks, totalBlocks = totalBlocks,
            landPercent = landPercent, sandPercent = sandPercent,
            meanHeight = (float)mean, stdDevHeight = stdDev,
            mountainFraction = mountainFraction, meanHeightMountain = meanMount, meanHeightNonMountain = meanNonMount,
            radialCorrelation = corr,
            msHeightCompute = msCompute,
            msSplatCompute = msSplatCompute,
            msObjects = msObjects,
            msApplyHeight = msApplyHeight,
            msApplySplat = msApplySplat,
            totalMs = swTotal.Elapsed.TotalMilliseconds
        };

        Debug.Log($"terrain metrics: total {lastMetrics.totalMs:F1}ms | height {lastMetrics.msHeightCompute:F1}ms + apply {lastMetrics.msApplyHeight:F1}ms | splat {lastMetrics.msSplatCompute:F1}ms + apply {lastMetrics.msApplySplat:F1}ms | objects {lastMetrics.msObjects:F1}ms | details {lastMetrics.msDetails:F1}ms | land {lastMetrics.landPercent:P1} sand {lastMetrics.sandPercent:P1} corr {lastMetrics.radialCorrelation:F2}");
    }

    // Helper: compute threshold for desired land % over [0..1] height map
    float FindHeightThreshold(float[,] heights, float targetLand)
    {
        // sample histogram to choose threshold
        int buckets = 256;
        int[] hist = new int[buckets];
        int total = heights.GetLength(0) * heights.GetLength(1);
        for (int y = 0; y < heights.GetLength(1); y++)
            for (int x = 0; x < heights.GetLength(0); x++)
                hist[Mathf.Clamp(Mathf.FloorToInt(heights[x, y] * (buckets - 1)), 0, buckets - 1)]++;
        int landTarget = Mathf.RoundToInt(total * targetLand);
        int running = 0;
        for (int i = buckets - 1; i >= 0; i--)
        {
            running += hist[i];
            if (running >= landTarget)
            {
                return (float)i / (buckets - 1);
            }
        }
        return 0.5f;
    }

    // In-place separable 5-tap Gaussian-like smoothing to soften steps/cliffs
    void SmoothHeightsInPlace(float[,] map, int w, int h, int iterations)
    {
        float[,] temp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            // horizontal pass (kernel [1,4,6,4,1]/16)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a2 = map[Mathf.Max(0, x - 2), y];
                    float a1 = map[Mathf.Max(0, x - 1), y];
                    float b0 = map[x, y];
                    float c1 = map[Mathf.Min(w - 1, x + 1), y];
                    float c2 = map[Mathf.Min(w - 1, x + 2), y];
                    temp[x, y] = (a2 + 4f * a1 + 6f * b0 + 4f * c1 + c2) / 16f;
                }
            }
            // vertical pass back into map (same kernel)
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float a2 = temp[x, Mathf.Max(0, y - 2)];
                    float a1 = temp[x, Mathf.Max(0, y - 1)];
                    float b0 = temp[x, y];
                    float c1 = temp[x, Mathf.Min(h - 1, y + 1)];
                    float c2 = temp[x, Mathf.Min(h - 1, y + 2)];
                    map[x, y] = (a2 + 4f * a1 + 6f * b0 + 4f * c1 + c2) / 16f;
                }
            }
        }
    }

    // Light blur focused on shoreline and lowlands to remove cliffy look
    void ShorelineSoftenInPlace(float[,] map, int w, int h, float grassStart, int iterations)
    {
        if (iterations <= 0) return;
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = map[x, y];
                    // only soften shoreline/lowlands
                    if (v <= grassStart + 0.05f)
                    {
                        float sum = 0f;
                        sum += map[x - 1, y]; sum += map[x + 1, y];
                        sum += map[x, y - 1]; sum += map[x, y + 1];
                        sum += v * 4f;
                        tmp[x, y] = sum / 8f; // gentle
                    }
                    else tmp[x, y] = v;
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    map[x, y] = tmp[x, y];
        }
    }

    // Diffusion-like smoothing focused on lowlands and shoreline with adjustable strength
    void AdaptiveLowlandSmoothing(float[,] map, int w, int h, float maxHeight, int iterations, float strength)
    {
        strength = Mathf.Clamp01(strength);
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = map[x, y];
                    if (v <= maxHeight)
                    {
                        float avg = (map[x - 1, y] + map[x + 1, y] + map[x, y - 1] + map[x, y + 1] + v) / 5f;
                        tmp[x, y] = Mathf.Lerp(v, avg, strength);
                    }
                    else tmp[x, y] = v;
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    map[x, y] = tmp[x, y];
        }
    }

    // Smoothing constrained around specific isocontours of the source noise (pre-threshold map)
    void IsocontourSmoothing(float[,] outHeights, float[,] srcNoise, int w, int h, float t1, float t2, float band, int iterations)
    {
        if (iterations <= 0) return;
        band = Mathf.Max(0.001f, band);
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    float v = outHeights[x, y];
                    float s = srcNoise[x, y];
                    float d1 = Mathf.Abs(s - t1);
                    float d2 = Mathf.Abs(s - t2);
                    float near = Mathf.Min(d1, d2);
                    if (near <= band)
                    {
                        float avg = (outHeights[x - 1, y] + outHeights[x + 1, y] + outHeights[x, y - 1] + outHeights[x, y + 1] + v) / 5f;
                        float wgt = Mathf.SmoothStep(band, 0f, near); // stronger right at the contour
                        tmp[x, y] = Mathf.Lerp(v, avg, 0.6f * wgt);
                    }
                    else tmp[x, y] = v;
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    outHeights[x, y] = tmp[x, y];
        }
    }

    // Specifically relax steep edges near the border defined by falloff
    void EdgeRelax(float[,] map, float[,] fall, int w, int h, float threshold, int iterations, float strength)
    {
        strength = Mathf.Clamp01(strength);
        float[,] tmp = new float[w, h];
        for (int it = 0; it < iterations; it++)
        {
            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    if (fall[x, y] >= threshold)
                    {
                        float v = map[x, y];
                        float avg = (map[x - 1, y] + map[x + 1, y] + map[x, y - 1] + map[x, y + 1] + v) / 5f;
                        tmp[x, y] = Mathf.Lerp(v, avg, strength);
                    }
                    else tmp[x, y] = map[x, y];
                }
            }
            for (int y = 1; y < h - 1; y++)
                for (int x = 1; x < w - 1; x++)
                    map[x, y] = tmp[x, y];
        }
    }

    // fractal noise (fBm) == richer terrain details
    float FractalNoise(float x, float y, int octaves, float persistence, float lacunarity)
    {
        float total = 0f;
        float frequency = 1f;
        float amplitude = 1f;
        float maxValue = 0f;
        for (int i = 0; i < octaves; i++)
        {
            total += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= lacunarity;
        }
        return total / maxValue;
    }

    // generates a falloff map to create island shapes
    float[,] GenerateFalloffMap(int width, int height, float a, float b)
    {
        float[,] map = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = x / (float)width * 2 - 1;
                float ny = y / (float)height * 2 - 1;
                float value = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(ny));
                float falloff = Mathf.Pow(value, a) / (Mathf.Pow(value, a) + Mathf.Pow(b - b * value, a));
                map[x, y] = falloff;
            }
        }
        return map;
    }
}