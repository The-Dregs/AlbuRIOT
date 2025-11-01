using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class VFXManager : MonoBehaviourPun
{
    [Header("VFX Configuration")]
    public Transform vfxSpawnPoint;
    public float vfxLifetime = 5f;
    
    [Header("Moveset VFX")]
    public MovesetVFXData[] movesetVFXData;
    
    [Header("Power Steal VFX")]
    public PowerStealVFXData[] powerStealVFXData;
    
    [Header("Debuff VFX")]
    public DebuffVFXData[] debuffVFXData;
    
    [Header("Audio")]
    public AudioSource audioSource;
    
    // Active VFX tracking
    private List<GameObject> activeVFX = new List<GameObject>();
    
    // Events
    public System.Action<string> OnVFXPlayed;
    public System.Action<string> OnVFXStopped;
    
    // Components
    private MovesetManager movesetManager;
    private DebuffManager debuffManager;
    
    void Awake()
    {
        movesetManager = GetComponent<MovesetManager>();
        debuffManager = GetComponent<DebuffManager>();
        
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }
    
    void Update()
    {
        // Clean up expired VFX
        CleanupExpiredVFX();
    }
    
    #region Moveset VFX
    
    public void PlayMovesetVFX(string movesetName, string moveName, Vector3 position, Quaternion rotation)
    {
        MovesetVFXData vfxData = GetMovesetVFXData(movesetName, moveName);
        if (vfxData == null) return;
        
        GameObject vfx = InstantiateVFX(vfxData.vfxPrefab, position, rotation);
        if (vfx != null)
        {
            // Set up VFX properties
            SetupVFX(vfx, vfxData);
            
            // Play audio
            PlayVFXAudio(vfxData.audioClip);
            
            Debug.Log($"Playing moveset VFX: {movesetName} - {moveName}");
            OnVFXPlayed?.Invoke($"{movesetName}_{moveName}");
            
            // Sync with other players
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("RPC_PlayMovesetVFX", RpcTarget.Others, movesetName, moveName, position, rotation);
            }
        }
    }
    
    [PunRPC]
    public void RPC_PlayMovesetVFX(string movesetName, string moveName, Vector3 position, Quaternion rotation)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        PlayMovesetVFX(movesetName, moveName, position, rotation);
    }
    
    #endregion
    
    #region Power Steal VFX
    
    public void PlayPowerStealVFX(string enemyName, Vector3 position, Quaternion rotation)
    {
        PowerStealVFXData vfxData = GetPowerStealVFXData(enemyName);
        if (vfxData == null) return;
        
        GameObject vfx = InstantiateVFX(vfxData.vfxPrefab, position, rotation);
        if (vfx != null)
        {
            // Set up VFX properties
            SetupVFX(vfx, vfxData);
            
            // Play audio
            PlayVFXAudio(vfxData.audioClip);
            
            Debug.Log($"Playing power steal VFX: {enemyName}");
            OnVFXPlayed?.Invoke($"PowerSteal_{enemyName}");
            
            // Sync with other players
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("RPC_PlayPowerStealVFX", RpcTarget.Others, enemyName, position, rotation);
            }
        }
    }
    
    [PunRPC]
    public void RPC_PlayPowerStealVFX(string enemyName, Vector3 position, Quaternion rotation)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        PlayPowerStealVFX(enemyName, position, rotation);
    }
    
    #endregion
    
    #region Debuff VFX
    
    public void PlayDebuffVFX(string debuffName, Vector3 position, Quaternion rotation)
    {
        DebuffVFXData vfxData = GetDebuffVFXData(debuffName);
        if (vfxData == null) return;
        
        GameObject vfx = InstantiateVFX(vfxData.vfxPrefab, position, rotation);
        if (vfx != null)
        {
            // Set up VFX properties
            SetupVFX(vfx, vfxData);
            
            // Play audio
            PlayVFXAudio(vfxData.audioClip);
            
            Debug.Log($"Playing debuff VFX: {debuffName}");
            OnVFXPlayed?.Invoke($"Debuff_{debuffName}");
            
            // Sync with other players
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("RPC_PlayDebuffVFX", RpcTarget.Others, debuffName, position, rotation);
            }
        }
    }
    
    [PunRPC]
    public void RPC_PlayDebuffVFX(string debuffName, Vector3 position, Quaternion rotation)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        PlayDebuffVFX(debuffName, position, rotation);
    }
    
    #endregion
    
    #region VFX Management
    
    private GameObject InstantiateVFX(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;
        
        GameObject vfx = Instantiate(prefab, position, rotation);
        activeVFX.Add(vfx);
        
        return vfx;
    }
    
    private void SetupVFX(GameObject vfx, VFXDataBase vfxData)
    {
        if (vfx == null || vfxData == null) return;
        
        // Set lifetime
        if (vfxData.lifetime > 0f)
        {
            Destroy(vfx, vfxData.lifetime);
        }
        
        // Set scale
        if (vfxData.scale != Vector3.one)
        {
            vfx.transform.localScale = vfxData.scale;
        }
        
        // Set color
        if (vfxData.color != Color.white)
        {
            var renderers = vfx.GetComponentsInChildren<Renderer>();
            foreach (var renderer in renderers)
            {
                renderer.material.color = vfxData.color;
            }
        }
        
        // Set up particle systems
        var particleSystems = vfx.GetComponentsInChildren<ParticleSystem>();
        foreach (var ps in particleSystems)
        {
            if (vfxData.color != Color.white)
            {
                var main = ps.main;
                main.startColor = vfxData.color;
            }
        }
    }
    
    private void PlayVFXAudio(AudioClip audioClip)
    {
        if (audioSource != null && audioClip != null)
        {
            audioSource.PlayOneShot(audioClip);
        }
    }
    
    private void CleanupExpiredVFX()
    {
        for (int i = activeVFX.Count - 1; i >= 0; i--)
        {
            if (activeVFX[i] == null)
            {
                activeVFX.RemoveAt(i);
            }
        }
    }
    
    #endregion
    
    #region Data Lookup
    
    private MovesetVFXData GetMovesetVFXData(string movesetName, string moveName)
    {
        if (movesetVFXData == null) return null;
        
        foreach (var data in movesetVFXData)
        {
            if (data.movesetName == movesetName && data.moveName == moveName)
            {
                return data;
            }
        }
        return null;
    }
    
    private PowerStealVFXData GetPowerStealVFXData(string enemyName)
    {
        if (powerStealVFXData == null) return null;
        
        foreach (var data in powerStealVFXData)
        {
            if (data.enemyName == enemyName)
            {
                return data;
            }
        }
        return null;
    }
    
    private DebuffVFXData GetDebuffVFXData(string debuffName)
    {
        if (debuffVFXData == null) return null;
        
        foreach (var data in debuffVFXData)
        {
            if (data.debuffName == debuffName)
            {
                return data;
            }
        }
        return null;
    }
    
    #endregion
    
    #region Public Methods
    
    public void StopAllVFX()
    {
        foreach (var vfx in activeVFX)
        {
            if (vfx != null)
            {
                Destroy(vfx);
            }
        }
        activeVFX.Clear();
    }
    
    public void StopVFX(string vfxName)
    {
        // This would need to be implemented based on your VFX naming system
        Debug.Log($"Stopping VFX: {vfxName}");
        OnVFXStopped?.Invoke(vfxName);
    }
    
    #endregion
}

[System.Serializable]
public class VFXDataBase
{
    public GameObject vfxPrefab;
    public float lifetime = 5f;
    public Vector3 scale = Vector3.one;
    public Color color = Color.white;
    public AudioClip audioClip;
}

[System.Serializable]
public class MovesetVFXData : VFXDataBase
{
    [Header("Moveset Information")]
    public string movesetName;
    public string moveName;
    
    [Header("VFX Properties")]
    public float duration = 1f;
    public bool followPlayer = false;
    public bool rotateWithPlayer = false;
}

[System.Serializable]
public class PowerStealVFXData : VFXDataBase
{
    [Header("Power Steal Information")]
    public string enemyName;
    public string powerName;
    
    [Header("VFX Properties")]
    public float duration = 2f;
    public bool followPlayer = true;
    public bool rotateWithPlayer = true;
}

[System.Serializable]
public class DebuffVFXData : VFXDataBase
{
    [Header("Debuff Information")]
    public string debuffName;
    public DebuffType debuffType;
    
    [Header("VFX Properties")]
    public float duration = 3f;
    public bool followPlayer = true;
    public bool rotateWithPlayer = false;
}

