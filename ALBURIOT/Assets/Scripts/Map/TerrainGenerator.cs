using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    [Header("Terrain Settings")]
    public int width = 256;
    public int height = 256;
    public float scale = 50f;
    public float heightMultiplier = 10f;

    [Header("Biome Settings")]
    public float biomeScale = 120f;
    [Range(0f, 1f)] public float mountainBiomeThreshold = 0.65f; // higher = less mountains
    [Range(1, 8)] public int mountainOctaves = 6;
    public float mountainHeightMultiplier = 18f;

    [Header("Island Shape Tuning")]
    [Range(0.001f, 0.1f)] public float sandThreshold = 0.02f; // tweak for more/less water
    [Tooltip("controls falloff curve; lower A/B = gentler falloff (more land)")] public float falloffA = 1.5f;
    [Tooltip("controls falloff curve; lower A/B = gentler falloff (more land)")] public float falloffB = 2.2f;
    [Header("Auto-tune Land % (optional)")]
    [Tooltip("when enabled, adjusts sand threshold used for painting/metrics to move land percent toward target")]
    public bool autoTuneLand = true;
    [Range(0.2f, 0.9f)] public float targetLandPercent = 0.6f;

    [Header("Noise Settings")]
    [Range(1, 8)] public int octaves = 4;
    [Range(0.1f, 1f)] public float persistence = 0.5f;
    [Range(1f, 4f)] public float lacunarity = 2f;
    public bool useRidgedNoise = false;

    [Header("Noise Valley Depth")]
    [Range(1f, 6f)] public float noisePower = 2.5f; // higher = deeper valleys, more holes

    [Header("Terrain Layers")]
    public TerrainLayer sandLayer;
    public TerrainLayer grassLayer1;
    public TerrainLayer grassLayer2;
    public TerrainLayer grassLayer3;

    [Header("Grass Probabilities (0-1)")]
    [Range(0f, 1f)] public float grass1Prob = 0.4f;
    [Range(0f, 1f)] public float grass2Prob = 0.4f;
    [Range(0f, 1f)] public float grass3Prob = 0.2f;

    [Header("Object Spawning")]
    public GameObject[] treePrefabs;
    public GameObject[] rockPrefabs;
    [Range(0, 10000)] public int treeCount = 300;
    [Range(0, 10000)] public int rockCount = 120;

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

        // enforce mountain profile makes sense: mountains should be at least 25% taller than base
        if (mountainHeightMultiplier <= heightMultiplier)
        {
            mountainHeightMultiplier = Mathf.Max(heightMultiplier * 1.35f, mountainHeightMultiplier);
        }

        int seed = Random.Range(int.MinValue, int.MaxValue);
    float[,] falloffMap = GenerateFalloffMap(width, height);
        float[,] biomeMap = new float[width, height];
        System.Random prng = new System.Random(seed + 1000);
        float biomeOffsetX = prng.Next(-100000, 100000);
        float biomeOffsetY = prng.Next(-100000, 100000);

        float[,] finalHeightMap = new float[width, height];
        int landBlocks = 0;
        int sandBlocks = 0;
        int mountainPixels = 0;
        double sumH = 0.0; double sumH2 = 0.0;
        double sumHMountain = 0.0; int countHMountain = 0; double sumHNonMountain = 0.0; int countHNonMountain = 0;

        sw.Restart();
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float bx = (x + biomeOffsetX) / biomeScale;
                float by = (y + biomeOffsetY) / biomeScale;
                float biomeValue = Mathf.PerlinNoise(bx, by);
                biomeMap[x, y] = biomeValue;

                // choose parameters based on biome
                int localOctaves = octaves;
                float localHeightMultiplier = heightMultiplier;
                bool isMountain = biomeValue > mountainBiomeThreshold;
                if (isMountain)
                {
                    localOctaves = mountainOctaves;
                    localHeightMultiplier = mountainHeightMultiplier;
                    mountainPixels++;
                }
                float nx = (x + biomeOffsetX) / scale;
                float ny = (y + biomeOffsetY) / scale;
                float value = FractalNoise(nx, ny, localOctaves, persistence, lacunarity);
                value = Mathf.Pow(value, noisePower);
                if (useRidgedNoise)
                {
                    value = 1f - Mathf.Abs(value * 2f - 1f);
                }
                float h = Mathf.Clamp01(value - falloffMap[x, y]);
                h *= localHeightMultiplier / heightMultiplier;
                if (h < sandThreshold)
                {
                    finalHeightMap[x, y] = h * 0.3f;
                    sandBlocks++;
                }
                else
                {
                    finalHeightMap[x, y] = h;
                    landBlocks++;
                }
                // accumulate stats
                sumH += finalHeightMap[x, y];
                sumH2 += finalHeightMap[x, y] * finalHeightMap[x, y];
                if (isMountain) { sumHMountain += finalHeightMap[x, y]; countHMountain++; }
                else { sumHNonMountain += finalHeightMap[x, y]; countHNonMountain++; }
            }
        }
        sw.Stop();
        double msCompute = sw.Elapsed.TotalMilliseconds;

    Debug.Log($"Island's Mass: {landBlocks + sandBlocks} Blocks | Land: {landBlocks} | Sand: {sandBlocks} | SandThreshold: {sandThreshold}");

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

        sw.Restart();
        terrainData.SetHeights(0, 0, finalHeightMap);
        sw.Stop();
        double msApplyHeight = sw.Elapsed.TotalMilliseconds;

        // optionally adjust classification threshold to move land% toward target for painting/metrics
        float usedSandThreshold = sandThreshold;
        int totalBlocks = width * height;
        float initialLandPercent = (float)landBlocks / Mathf.Max(1, totalBlocks);
        if (autoTuneLand && targetLandPercent > 0f)
        {
            // scale threshold inversely to land ratio; clamp to a sensible range
            float scale = Mathf.Clamp(initialLandPercent / targetLandPercent, 0.5f, 1.5f);
            usedSandThreshold = Mathf.Clamp(sandThreshold * scale, 0.001f, 0.2f);
        }

        sw.Restart();
        float[,,] splatmap = new float[width, width, 4];
        for (int y = 0; y < width; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = finalHeightMap[x, y];
                float[] weights = new float[4];
                if (h < usedSandThreshold)
                {
                    weights[0] = 1f; // sand
                }
                else if (h < 0.8f)
                {
                    // smooth transition between sand and grass
                    float t = Mathf.InverseLerp(usedSandThreshold + 0.01f, usedSandThreshold + 0.06f, h);
                    weights[0] = 1f - t; // sand
                    float r = Random.value;
                    if (r < grass1Prob)
                        weights[1] = t;
                    else if (r < grass1Prob + grass2Prob)
                        weights[2] = t;
                    else
                        weights[3] = t;
                }
                else
                {
                    float r = Random.value;
                    if (r < grass1Prob)
                        weights[1] = 1f;
                    else if (r < grass1Prob + grass2Prob)
                        weights[2] = 1f;
                    else
                        weights[3] = 1f;
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

        // generate grass detail layer based on grass splatmap
        sw.Restart();
        int detailRes = terrainData.detailResolution;
        int detailPrototypes = terrainData.detailPrototypes.Length;
        for (int layer = 0; layer < detailPrototypes; layer++)
        {
            int[,] grassMap = new int[detailRes, detailRes];
            for (int y = 0; y < detailRes; y++)
            {
                for (int x = 0; x < detailRes; x++)
                {
                    // map detail coordinates to splatmap coordinates
                    int tx = Mathf.FloorToInt((float)x / detailRes * width);
                    int ty = Mathf.FloorToInt((float)y / detailRes * width);

                    // only place grass if not sand and not grass layer 3
                    float sandWeight = splatmap[tx, ty, 0];
                    float grass1Weight = splatmap[tx, ty, 1];
                    float grass2Weight = splatmap[tx, ty, 2];
                    float grass3Weight = splatmap[tx, ty, 3];

                    // adding grass
                    if (sandWeight < 0.5f && grass3Weight < 0.5f && (grass1Weight > 0.3f || grass2Weight > 0.3f))
                    {
                        grassMap[x, y] = 256;
                    }
                    else
                    {
                        grassMap[x, y] = 0;
                    }
                }
            }
            terrainData.SetDetailLayer(0, 0, layer, grassMap);
        }
        sw.Stop(); double msDetails = sw.Elapsed.TotalMilliseconds;

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
                if (h < usedSandThreshold) sandCount++; else landCount++;
            }
        }
        float landPercent = (float)landCount / Mathf.Max(1, totalBlocks2);
        float sandPercent = (float)sandCount / Mathf.Max(1, totalBlocks2);
    double mean = sumH / totalBlocks2;
    double variance = Mathf.Max(0f, (float)(sumH2 / totalBlocks2 - mean * mean));
        float stdDev = Mathf.Sqrt((float)variance);
        float mountainFraction = (float)mountainPixels / totalBlocks;
        float meanMount = countHMountain > 0 ? (float)(sumHMountain / countHMountain) : 0f;
        float meanNonMount = countHNonMountain > 0 ? (float)(sumHNonMountain / countHNonMountain) : 0f;

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
            msDetails = msDetails,
            msApplyHeight = msApplyHeight,
            msApplySplat = msApplySplat,
            totalMs = swTotal.Elapsed.TotalMilliseconds
        };

        Debug.Log($"terrain metrics: total {lastMetrics.totalMs:F1}ms | height {lastMetrics.msHeightCompute:F1}ms + apply {lastMetrics.msApplyHeight:F1}ms | splat {lastMetrics.msSplatCompute:F1}ms + apply {lastMetrics.msApplySplat:F1}ms | objects {lastMetrics.msObjects:F1}ms | details {lastMetrics.msDetails:F1}ms | land {lastMetrics.landPercent:P1} sand {lastMetrics.sandPercent:P1} corr {lastMetrics.radialCorrelation:F2} mountain meanΔ {(lastMetrics.meanHeightMountain - lastMetrics.meanHeightNonMountain):F3}");
    }

    // fractal noise (octaves) and ridged noise implementation
    float[,] GenerateFractalNoiseMap(int width, int height, float scale, int seed, int octaves, float persistence, float lacunarity, bool ridged)
    {
        float[,] map = new float[width, height];
        System.Random prng = new System.Random(seed);
        float offsetX = prng.Next(-100000, 100000);
        float offsetY = prng.Next(-100000, 100000);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float nx = (x + offsetX) / scale;
                float ny = (y + offsetY) / scale;
                float value = FractalNoise(nx, ny, octaves, persistence, lacunarity);
                // apply power curve to deepen holes and valleys
                value = Mathf.Pow(value, noisePower);
                if (ridged)
                {
                    value = 1f - Mathf.Abs(value * 2f - 1f); // ridged noise
                }
                map[x, y] = value;
            }
        }
        return map;
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
    float[,] GenerateFalloffMap(int width, int height)
    {
        float[,] map = new float[width, height];
        float a = Mathf.Max(0.5f, falloffA);
        float b = Mathf.Max(0.5f, falloffB);

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