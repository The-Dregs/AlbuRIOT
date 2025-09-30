using UnityEngine;

// terrain generator with fractal noise and ridged noise options
// fractal noise (octaves) adds hills, valleys, and more realistic detail
// ridged noise creates sharp peaks and mountain-like features
public class TerrainGenerator : MonoBehaviour
{
    [Header("Object Spawning")]
    public GameObject[] treePrefabs;
    public GameObject[] rockPrefabs;
    [Range(0, 10000)] public int treeCount = 300;
    [Range(0, 10000)] public int rockCount = 120;
    [Header("Biome Settings")]
    public float biomeScale = 120f;
    [Range(0f, 1f)] public float mountainBiomeThreshold = 0.65f; // higher = less mountains
    [Range(1, 8)] public int mountainOctaves = 6;
    public float mountainHeightMultiplier = 18f;
    [Header("Terrain Settings")]
    public int width = 256;
    public int height = 256;
    public float scale = 50f;
    public float heightMultiplier = 10f;

    [Header("Island Shape Tuning")]
    [Range(0.001f, 0.1f)] public float sandThreshold = 0.02f; // tweak for more/less water

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

    public Terrain terrain;

    void Start()
    {
        GenerateTerrain();
    }

    // generate terrain heightmap and paint textures and grass details
    void GenerateTerrain()
    {
        int seed = Random.Range(int.MinValue, int.MaxValue);
        float[,] falloffMap = GenerateFalloffMap(width, height);
        float[,] biomeMap = new float[width, height];
        System.Random prng = new System.Random(seed + 1000);
        float biomeOffsetX = prng.Next(-100000, 100000);
        float biomeOffsetY = prng.Next(-100000, 100000);

        float[,] finalHeightMap = new float[width, height];
        int landBlocks = 0;
        int sandBlocks = 0;
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
            }
        }

        Debug.Log($"Island's Mass: {landBlocks + sandBlocks} Blocks | Land: {landBlocks} | Sand: {sandBlocks} | SandThreshold: {sandThreshold}");

        TerrainData terrainData = terrain.terrainData;
        terrainData.heightmapResolution = width + 1;
        terrainData.alphamapResolution = width;
        terrainData.size = new Vector3(width, heightMultiplier, height);

        terrainData.terrainLayers = new TerrainLayer[] { sandLayer, grassLayer1, grassLayer2, grassLayer3 };

        terrainData.SetHeights(0, 0, finalHeightMap);

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
                else if (h < 0.8f)
                {
                    // smooth transition between sand and grass
                    float t = Mathf.InverseLerp(sandThreshold + 0.01f, sandThreshold + 0.06f, h);
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
        terrainData.SetAlphamaps(0, 0, splatmap);

        // randomly spawn trees and rocks
        var treeInstances = new System.Collections.Generic.List<TreeInstance>();
        System.Random objRand = new System.Random();
        float terrainWidth = terrainData.size.x;
        float terrainHeight = terrainData.size.z;
        float terrainY = terrainData.size.y;
        int splatRes = terrainData.alphamapResolution;
        float[,,] splat = terrainData.GetAlphamaps(0, 0, splatRes, splatRes);

        for (int i = 0; i < treeCount; i++)
        {
            float tx = (float)objRand.NextDouble();
            float tz = (float)objRand.NextDouble();
            int x = Mathf.FloorToInt(tx * splatRes);
            int z = Mathf.FloorToInt(tz * splatRes);
            float grassWeight = splat[x, z, 1] + splat[x, z, 2] + splat[x, z, 3];
            float sandWeight = splat[x, z, 0];
            if (grassWeight > 0.7f && sandWeight < 0.2f)
            {
                float terrainX = tx * terrainWidth;
                float terrainZ = tz * terrainHeight;
                float normX = tx;
                float normZ = tz;
                float y = terrainData.GetInterpolatedHeight(normX, normZ) / terrainY;
                TreeInstance tree = new TreeInstance();
                tree.position = new Vector3(normX, y, normZ);
                tree.prototypeIndex = objRand.Next(treePrefabs.Length);
                tree.widthScale = 1f;
                tree.heightScale = 1f;
                tree.color = Color.white;
                tree.lightmapColor = Color.white;
                treeInstances.Add(tree);
            }
        }

        for (int i = 0; i < rockCount; i++)
        {
            float tx = (float)objRand.NextDouble();
            float tz = (float)objRand.NextDouble();
            int x = Mathf.FloorToInt(tx * splatRes);
            int z = Mathf.FloorToInt(tz * splatRes);
            float grassWeight = splat[x, z, 1] + splat[x, z, 2] + splat[x, z, 3];
            float sandWeight = splat[x, z, 0];
            // spawn rocks on grass or sand, not water
            if ((grassWeight > 0.5f || sandWeight > 0.5f))
            {
                float terrainX = tx * terrainWidth;
                float terrainZ = tz * terrainHeight;
                float normX = tx;
                float normZ = tz;
                float y = terrainData.GetInterpolatedHeight(normX, normZ) / terrainY;
                TreeInstance rock = new TreeInstance();
                rock.position = new Vector3(normX, y, normZ);
                rock.prototypeIndex = objRand.Next(rockPrefabs.Length) + treePrefabs.Length;
                rock.widthScale = 1f;
                rock.heightScale = 1f;
                rock.color = Color.white;
                rock.lightmapColor = Color.white;
                treeInstances.Add(rock);
            }
        }
        terrainData.treeInstances = treeInstances.ToArray();

        // generate grass detail layer based on grass splatmap
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
                // apply power curve to deepen valleys and create more holes
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

    // generates a radial falloff map to create island shapes
    float[,] GenerateFalloffMap(int width, int height)
    {
        float[,] map = new float[width, height];
    float a = 1.8f; // more aggressive falloff for fragmented islands
    float b = 2.8f; // smaller island size, more water

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