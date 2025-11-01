using UnityEngine;

public class RegenerateOnSceneLoad : MonoBehaviour {
    void Start() {
        var terrainGen = FindObjectOfType<TerrainGenerator>();
        if (terrainGen != null) {
            terrainGen.GenerateTerrain();
        }
    }
}
