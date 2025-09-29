using UnityEngine;

public class ProceduralTerrainPainter : MonoBehaviour
{
    public Terrain terrain;
    public TerrainLayer grassLayer;
    public TerrainLayer dirtLayer;
    public TerrainLayer sandLayer;
    public TerrainLayer cliffLayer;

    public int width = 512;
    public int height = 512;
    public float scale = 50f;
    public float heightMultiplier = 20f;

    void Start()
    {
        GenerateTerrain();
    }

    void GenerateTerrain()
    {
        TerrainData terrainData = terrain.terrainData;
        terrainData.heightmapResolution = width + 1;
        terrainData.size = new Vector3(width, heightMultiplier, height);

        // Generate heightmap
        float[,] heights = new float[width, height];
        int seed = Random.Range(int.MinValue, int.MaxValue);
        float[,] noiseMap = GeneratePerlinNoiseMap(width, height, scale, seed);

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                heights[y, x] = noiseMap[x, y];

        terrainData.SetHeights(0, 0, heights);

        // Assign terrain layers
        terrainData.terrainLayers = new TerrainLayer[] { grassLayer, dirtLayer, sandLayer, cliffLayer };

        // Generate splatmap
        float[,,] splatmap = new float[width, height, 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                float h = heights[y, x];
                float slope = terrainData.GetSteepness((float)x / width, (float)y / height);

                // Example rules:
                // Sand for lowest heights
                // Grass for mid heights and gentle slopes
                // Dirt for mid-high heights
                // Cliff for steep slopes

                float[] weights = new float[4];

                if (h < 0.15f)
                    weights[2] = 1f; // sand
                else if (slope > 35f)
                    weights[3] = 1f; // cliff
                else if (h < 0.5f)
                    weights[0] = 1f; // grass
                else
                    weights[1] = 1f; // dirt

                // Normalize weights
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
            for (int x = 0; x < width; x++)
            {
                float sampleX = (x + offsetX) / scale;
                float sampleY = (y + offsetY) / scale;
                float value = Mathf.PerlinNoise(sampleX, sampleY);
                map[x, y] = value;
            }
        return map;
    }
}