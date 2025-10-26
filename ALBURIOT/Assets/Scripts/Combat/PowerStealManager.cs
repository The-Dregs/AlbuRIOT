using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class PowerStealManager : MonoBehaviourPun
{
    [Header("Power Steal Configuration")]
    public PowerStealData[] powerStealData;
    public float defaultStealDuration = 30f;
    
    [Header("Visual Effects")]
    public GameObject powerStealVFXPrefab;
    public Transform vfxSpawnPoint;
    
    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip powerStealSound;
    public AudioClip powerLostSound;
    
    [Header("UI")]
    public TMPro.TextMeshProUGUI powerStealTimerText;
    public UnityEngine.UI.Image powerStealIcon;
    
    // Active power steal tracking
    private Dictionary<string, PowerStealInstance> activePowers = new Dictionary<string, PowerStealInstance>();
    
    // Events
    public System.Action<string> OnPowerStolen;
    public System.Action<string> OnPowerLost;
    public System.Action<string> OnPowerExpired;
    
    // Components
    private MovesetManager movesetManager;
    private VFXManager vfxManager;
    private QuestManager questManager;
    
    void Awake()
    {
        movesetManager = GetComponent<MovesetManager>();
        vfxManager = GetComponent<VFXManager>();
        questManager = FindFirstObjectByType<QuestManager>();
        
        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }
    
    void Update()
    {
        if (photonView != null && !photonView.IsMine) return;
        
        UpdatePowerStealTimers();
        UpdateUI();
    }
    
    public void StealPowerFromEnemy(string enemyName, Vector3 position)
    {
        PowerStealData powerData = GetPowerStealData(enemyName);
        if (powerData == null)
        {
            Debug.LogWarning($"No power steal data found for enemy: {enemyName}");
            return;
        }
        
        // Check if power can be stolen
        if (!CanStealPower(enemyName))
        {
            Debug.Log($"Cannot steal power from {enemyName}");
            return;
        }
        
        // Create power steal instance
        PowerStealInstance powerInstance = new PowerStealInstance
        {
            powerData = powerData,
            timeRemaining = powerData.duration,
            isActive = true,
            stolenAt = Time.time
        };
        
        // Add to active powers
        activePowers[enemyName] = powerInstance;
        
        // Apply power effects
        ApplyPowerEffects(powerInstance);
        
        // Play VFX and audio
        PlayPowerStealVFX(enemyName, position);
        PlayPowerStealAudio();
        
        // Update quest progress
        if (questManager != null)
        {
            questManager.AddProgress_PowerSteal(enemyName);
        }
        
        Debug.Log($"Power stolen from {enemyName}: {powerData.powerName}");
        OnPowerStolen?.Invoke(enemyName);
        
        // Sync with other players
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("RPC_StealPower", RpcTarget.Others, enemyName, position);
        }
    }
    
    [PunRPC]
    public void RPC_StealPower(string enemyName, Vector3 position)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        StealPowerFromEnemy(enemyName, position);
    }
    
    private void UpdatePowerStealTimers()
    {
        List<string> expiredPowers = new List<string>();
        
        foreach (var kvp in activePowers)
        {
            PowerStealInstance powerInstance = kvp.Value;
            
            if (powerInstance.isActive)
            {
                powerInstance.timeRemaining -= Time.deltaTime;
                
                if (powerInstance.timeRemaining <= 0f)
                {
                    expiredPowers.Add(kvp.Key);
                }
            }
        }
        
        // Remove expired powers
        foreach (string enemyName in expiredPowers)
        {
            RemovePower(enemyName);
        }
    }
    
    private void RemovePower(string enemyName)
    {
        if (!activePowers.ContainsKey(enemyName)) return;
        
        PowerStealInstance powerInstance = activePowers[enemyName];
        
        // Remove power effects
        RemovePowerEffects(powerInstance);
        
        // Play power lost audio
        PlayPowerLostAudio();
        
        // Remove from active powers
        activePowers.Remove(enemyName);
        
        Debug.Log($"Power lost from {enemyName}: {powerInstance.powerData.powerName}");
        OnPowerLost?.Invoke(enemyName);
        OnPowerExpired?.Invoke(enemyName);
        
        // Sync with other players
        if (photonView != null && photonView.IsMine)
        {
            photonView.RPC("RPC_RemovePower", RpcTarget.Others, enemyName);
        }
    }
    
    [PunRPC]
    public void RPC_RemovePower(string enemyName)
    {
        if (photonView != null && !photonView.IsMine) return;
        
        RemovePower(enemyName);
    }
    
    private void ApplyPowerEffects(PowerStealInstance powerInstance)
    {
        PowerStealData powerData = powerInstance.powerData;
        
        // Apply moveset changes
        if (movesetManager != null && powerData.movesetData != null)
        {
            movesetManager.SetMoveset(powerData.movesetData);
        }
        
        // Apply stat modifications
        var playerStats = GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            playerStats.baseDamage += powerData.damageBonus;
            playerStats.baseSpeed += powerData.speedBonus;
            playerStats.maxHealth += powerData.healthBonus;
            playerStats.maxStamina += powerData.staminaBonus;
        }
        
        // Apply special abilities
        if (powerData.specialAbilities != null)
        {
            foreach (var ability in powerData.specialAbilities)
            {
                ApplySpecialAbility(ability);
            }
        }
    }
    
    private void RemovePowerEffects(PowerStealInstance powerInstance)
    {
        PowerStealData powerData = powerInstance.powerData;
        
        // Remove stat modifications
        var playerStats = GetComponent<PlayerStats>();
        if (playerStats != null)
        {
            playerStats.baseDamage -= powerData.damageBonus;
            playerStats.baseSpeed -= powerData.speedBonus;
            playerStats.maxHealth -= powerData.healthBonus;
            playerStats.maxStamina -= powerData.staminaBonus;
        }
        
        // Remove special abilities
        if (powerData.specialAbilities != null)
        {
            foreach (var ability in powerData.specialAbilities)
            {
                RemoveSpecialAbility(ability);
            }
        }
        
        // Return to default moveset
        if (movesetManager != null)
        {
            movesetManager.SetMoveset(movesetManager.availableMovesets[0]); // Default moveset
        }
    }
    
    private void ApplySpecialAbility(SpecialAbilityData ability)
    {
        // Apply special ability effects
        switch (ability.abilityType)
        {
            case SpecialAbilityType.Passive:
                // Apply passive effects
                break;
            case SpecialAbilityType.Active:
                // Add active ability to player
                break;
            case SpecialAbilityType.Trigger:
                // Set up trigger conditions
                break;
        }
    }
    
    private void RemoveSpecialAbility(SpecialAbilityData ability)
    {
        // Remove special ability effects
        switch (ability.abilityType)
        {
            case SpecialAbilityType.Passive:
                // Remove passive effects
                break;
            case SpecialAbilityType.Active:
                // Remove active ability from player
                break;
            case SpecialAbilityType.Trigger:
                // Remove trigger conditions
                break;
        }
    }
    
    private void PlayPowerStealVFX(string enemyName, Vector3 position)
    {
        if (vfxManager != null)
        {
            vfxManager.PlayPowerStealVFX(enemyName, position, Quaternion.identity);
        }
    }
    
    private void PlayPowerStealAudio()
    {
        if (audioSource != null && powerStealSound != null)
        {
            audioSource.PlayOneShot(powerStealSound);
        }
    }
    
    private void PlayPowerLostAudio()
    {
        if (audioSource != null && powerLostSound != null)
        {
            audioSource.PlayOneShot(powerLostSound);
        }
    }
    
    private void UpdateUI()
    {
        if (powerStealTimerText != null)
        {
            if (activePowers.Count > 0)
            {
                // Show total time remaining
                float totalTime = 0f;
                foreach (var kvp in activePowers)
                {
                    totalTime += kvp.Value.timeRemaining;
                }
                powerStealTimerText.text = $"Power: {totalTime:F1}s";
            }
            else
            {
                powerStealTimerText.text = "";
            }
        }
        
        if (powerStealIcon != null)
        {
            powerStealIcon.gameObject.SetActive(activePowers.Count > 0);
        }
    }
    
    private PowerStealData GetPowerStealData(string enemyName)
    {
        if (powerStealData == null) return null;
        
        foreach (var data in powerStealData)
        {
            if (data.enemyName == enemyName)
            {
                return data;
            }
        }
        return null;
    }
    
    private bool CanStealPower(string enemyName)
    {
        // Check if power is already stolen
        if (activePowers.ContainsKey(enemyName))
        {
            return false;
        }
        
        // Check if enemy has power to steal
        PowerStealData powerData = GetPowerStealData(enemyName);
        if (powerData == null)
        {
            return false;
        }
        
        // Check if power can be stolen
        if (!powerData.canBeStolen)
        {
            return false;
        }
        
        return true;
    }
    
    // Public getters
    public bool HasPower(string enemyName) => activePowers.ContainsKey(enemyName);
    public float GetPowerTimeRemaining(string enemyName)
    {
        if (activePowers.ContainsKey(enemyName))
        {
            return activePowers[enemyName].timeRemaining;
        }
        return 0f;
    }
    
    public Dictionary<string, PowerStealInstance> GetActivePowers() => activePowers;
    
    // Clear all powers
    public void ClearAllPowers()
    {
        List<string> powersToRemove = new List<string>(activePowers.Keys);
        foreach (string enemyName in powersToRemove)
        {
            RemovePower(enemyName);
        }
    }
}

[System.Serializable]
public class PowerStealInstance
{
    public PowerStealData powerData;
    public float timeRemaining;
    public bool isActive;
    public float stolenAt;
}

[System.Serializable]
public class PowerStealData
{
    [Header("Power Information")]
    public string enemyName;
    public string powerName;
    [TextArea] public string description;
    public Sprite icon;
    
    [Header("Power Properties")]
    public float duration = 30f;
    public bool canBeStolen = true;
    public int stealChance = 100; // Percentage
    
    [Header("Stat Modifications")]
    public int damageBonus = 0;
    public float speedBonus = 0f;
    public int healthBonus = 0;
    public int staminaBonus = 0;
    
    [Header("Moveset")]
    public MovesetData movesetData;
    
    [Header("Special Abilities")]
    public SpecialAbilityData[] specialAbilities;
    
    [Header("VFX")]
    public GameObject stealVFX;
    public GameObject activeVFX;
    public GameObject lostVFX;
    
    [Header("Audio")]
    public AudioClip stealSound;
    public AudioClip activeSound;
    public AudioClip lostSound;
    
    [Header("Quest Integration")]
    public bool isQuestObjective = false;
    public string questId = "";
}

[System.Serializable]
public class SpecialAbilityData
{
    [Header("Ability Information")]
    public string abilityName;
    [TextArea] public string description;
    public SpecialAbilityType abilityType;
    
    [Header("Effects")]
    public float effectMagnitude = 1f;
    public float effectDuration = 0f;
    
    [Header("Conditions")]
    public string triggerCondition = "";
    public float triggerChance = 100f;
    
    [Header("VFX")]
    public GameObject vfxPrefab;
    
    [Header("Audio")]
    public AudioClip soundEffect;
}

public enum SpecialAbilityType
{
    Passive,
    Active,
    Trigger
}

