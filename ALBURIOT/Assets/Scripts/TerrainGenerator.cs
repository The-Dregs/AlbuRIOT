using UnityEngine;
using System.Collections.Generic;

public class TerrainGenerator : MonoBehaviour
{
    public int width = 512;
    public int height = 512;
    public float scale = 40f;
    public float heightMultiplier = 18f; // Increased for more vertical variation
    public int seed = 0;
    public Material terrainMaterial;
    public GameObject treePrefab;
    public GameObject rockPrefab;
    public int treeCount = 100;
    public int rockCount = 50;
    public GameObject waterPrefab;
    public float waterHeight = 0.15f;
    public float biomeScale = 0.12f; // Controls biome size/frequency
    [Range(0f, 1f)] public float plainsThreshold = 0.33f;
    [Range(0f, 1f)] public float hillsThreshold = 0.66f;

    private Terrain terrain;
    private System.Random prng;

    void Start()
    {
        prng = new System.Random(seed);
        GenerateTerrain();
        PlaceProps();
        SpawnWater();
    }

    void GenerateTerrain()
    {
        TerrainData terrainData = new TerrainData();
        terrainData.heightmapResolution = Mathf.Max(width, height) + 1;
        terrainData.size = new Vector3(width, heightMultiplier, height);

        float[,] heights = new float[width, height];
        Vector2 center = new Vector2(width / 2f, height / 2f);
        float maxDist = width / 2f * 0.98f;
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                // Biome map: controls biome regions
                float biomeVal = Mathf.PerlinNoise((x + seed) * biomeScale, (z + seed) * biomeScale);
                // Height map: base terrain
                float xCoord = (float)x / width * scale + seed;
                float zCoord = (float)z / height * scale + seed;
                float baseHeight = Mathf.PerlinNoise(xCoord, zCoord);

                float finalHeight;
                if (biomeVal < plainsThreshold)
                {
                    // Plains: flatter
                    finalHeight = baseHeight * 0.5f;
                }
                else if (biomeVal < hillsThreshold)
                {
                    // Hills: moderate variation
                    finalHeight = Mathf.Lerp(baseHeight, Mathf.Pow(baseHeight, 1.5f), 0.5f);
                }
                else
                {
                    // Mountains: sharp peaks
                    finalHeight = Mathf.Lerp(baseHeight, Mathf.Pow(baseHeight, 2.5f), 0.8f);
                }

                // Gentle island mask (radial falloff)
                float dist = Vector2.Distance(new Vector2(x, z), center);
                float normDist = dist / maxDist;
                float islandFalloff = Mathf.SmoothStep(1f, 0.1f, normDist);
                finalHeight *= islandFalloff;

                heights[x, z] = Mathf.Clamp01(finalHeight);
            }
        }
        terrainData.SetHeights(0, 0, heights);

        GameObject terrainObj = Terrain.CreateTerrainGameObject(terrainData);
        terrain = terrainObj.GetComponent<Terrain>();
        if (terrainMaterial != null)
            terrain.materialTemplate = terrainMaterial;
        terrainObj.transform.parent = this.transform;

        // Vary grass color by height (if using material with color property)
        if (terrainMaterial != null && terrainMaterial.HasProperty("_Color"))
        {
            float avgHeight = 0.5f;
            Color baseColor = Color.Lerp(new Color(0.8f, 1f, 0.6f), new Color(0.1f, 0.4f, 0.1f), avgHeight);
            terrainMaterial.color = baseColor;
        }
    }

    void SpawnWater()
    {
        if (waterPrefab != null)
        {
            float y = waterHeight * heightMultiplier;
            GameObject water = Instantiate(waterPrefab);
            water.transform.localScale = new Vector3(width, 1f, height);
            water.transform.position = new Vector3(width / 2f, y, height / 2f);
            water.transform.parent = this.transform;
            Collider col = water.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
    }

    void PlaceProps()
    {
        if (treePrefab != null)
        {
            for (int i = 0; i < treeCount; i++)
            {
                Vector3 pos = GetRandomPositionOnTerrain();
                Instantiate(treePrefab, pos, Quaternion.identity, this.transform);
            }
        }
        if (rockPrefab != null)
        {
            for (int i = 0; i < rockCount; i++)
            {
                Vector3 pos = GetRandomPositionOnTerrain();
                Instantiate(rockPrefab, pos, Quaternion.identity, this.transform);
            }
        }
    }

    Vector3 GetRandomPositionOnTerrain()
    {
        float x = (float)prng.NextDouble() * width;
        float z = (float)prng.NextDouble() * height;
        float y = terrain.SampleHeight(new Vector3(x, 0, z));
        return new Vector3(x, y, z);
    }
}
