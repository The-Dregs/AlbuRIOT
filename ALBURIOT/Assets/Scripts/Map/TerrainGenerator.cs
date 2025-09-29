using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    [Header("Terrain Settings")]
    public int width = 256;
    public int height = 256;
    public float scale = 50f;
    public float heightMultiplier = 20f;

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

    void GenerateTerrain()
    {
        int seed = Random.Range(int.MinValue, int.MaxValue);
        float[,] noiseMap = GeneratePerlinNoiseMap(width, height, scale, seed);
        float[,] falloffMap = GenerateFalloffMap(width, height);

        float[,] finalHeightMap = new float[width, height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = Mathf.Clamp01(noiseMap[x, y] - falloffMap[x, y]);
                finalHeightMap[x, y] = h;
            }
        }

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
                if (h < 0.08f)
                {
                    weights[0] = 1f; // sand
                }
                else if (h < 0.12f)
                {
                    // smooth transition between sand and grass
                    float t = Mathf.InverseLerp(0.08f, 0.12f, h);
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
    }

    float[,] GeneratePerlinNoiseMap(int width, int height, float scale, int seed)
    {
        float[,] map = new float[width, height];
        System.Random prng = new System.Random(seed);
        float offsetX = prng.Next(-100000, 100000);
        float offsetY = prng.Next(-100000, 100000);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float sampleX = (x + offsetX) / scale;
                float sampleY = (y + offsetY) / scale;
                float value = Mathf.PerlinNoise(sampleX, sampleY);
                map[x, y] = value;
            }
        }
        return map;
    }

    float[,] GenerateFalloffMap(int width, int height)
    {
        float[,] map = new float[width, height];
        float a = 2.0f; // lower steepness for gradual edge
        float b = 2.5f; // larger island size

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