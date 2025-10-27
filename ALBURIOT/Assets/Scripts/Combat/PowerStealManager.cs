using UnityEngine;
using Photon.Pun;
using System;
using System.Collections.Generic;

public class PowerStealManager : MonoBehaviourPun
{
    [Header("Power Steal Configuration")]
    public PowerStealData[] powerStealData;

    // Example inspector setup for Amomongo's Berserk Frenzy power (set these fields in Unity Inspector)
    // Add this to the powerStealData array in the Inspector:
    // enemyName: "Amomongo"
    // powerName: "Berserk Frenzy"
    // description: "Gain Amomongo's Berserk Frenzy: temporarily boosts your damage and speed."
    // icon: [Assign Berserk icon sprite]
    // duration: 30
    // canBeStolen: true
    // stealChance: 100
    // damageBonus: 10
    // speedBonus: 2.0
    // healthBonus: 0
    // staminaBonus: 0
    // movesetData: [Optional: assign moveset for Berserk]
    // specialAbilities: [Add a SpecialAbilityData with abilityName "Berserk Frenzy", type Active, effectMagnitude 1.3, effectDuration 4.0]
    // stealVFX: [Assign VFX prefab]
    // activeVFX: [Assign VFX prefab]
    // lostVFX: [Assign VFX prefab]
    // stealSound: [Assign audio clip]
    // activeSound: [Assign audio clip]
    // lostSound: [Assign audio clip]
    // isQuestObjective: false
    // questId: ""
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

    // Active power steal tracking (no duration, just tracks what powers are granted)
    private HashSet<string> grantedPowers = new HashSet<string>();

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

        // Only allow granting once per enemy
        if (grantedPowers.Contains(enemyName))
        {
            Debug.Log($"[PowerStealManager] Power from {enemyName} already granted.");
            return;
        }
        grantedPowers.Add(enemyName);

        // Play VFX and audio
        PlayPowerStealVFX(enemyName, position);
        PlayPowerStealAudio();

        // Update quest progress
        if (questManager != null)
        {
            questManager.AddProgress_PowerSteal(enemyName);
        }

        Debug.Log($"[PowerStealManager] Power stolen from {enemyName}: {powerData.powerName} for player: {gameObject.name}");
        // Only assign to local player in multiplayer
        var pv = GetComponent<PhotonView>();
        if (pv == null || pv.IsMine)
        {
            var skillSlots = GetComponent<PlayerSkillSlots>();
            if (skillSlots != null)
            {
                Debug.Log($"[PowerStealManager] Assigning {powerData.powerName} to skill slots for player: {gameObject.name}");
                skillSlots.OnPowerStolen(powerData, position);
            }
            else
            {
                Debug.LogWarning($"[PowerStealManager] PlayerSkillSlots not found on {gameObject.name}");
            }
        }
        else
        {
            Debug.Log($"[PowerStealManager] Skipping skill slot assignment for remote player: {gameObject.name}");
        }
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
        // No timer logic; powers persist until used
    }

    private void RemovePower(string enemyName)
    {
        // No removal logic; powers persist until used
    }

    [PunRPC]
    // RemovePower RPC not needed; powers persist until used



    private void PlayPowerStealVFX(string enemyName, Vector3 position)
    {
        // if a uniform Power Steal VFX prefab is assigned on this manager, instantiate it here
        if (powerStealVFXPrefab != null)
        {
            Vector3 spawnPos = (vfxSpawnPoint != null) ? vfxSpawnPoint.position : position;
            var vfx = Instantiate(powerStealVFXPrefab, spawnPos, Quaternion.identity);
            // destroy after a short lifetime (use effectDuration if provided, otherwise 4s)
            float life =  (/* try to use a reasonable default */ 4f);
            if (vfx != null) Destroy(vfx, life);

            // sync to other clients by RPC so they also play the same uniform prefab
            if (photonView != null && photonView.IsMine)
            {
                photonView.RPC("RPC_PlayUniformPowerStealVFX", RpcTarget.Others, spawnPos);
            }
            return;
        }

        // fallback to VFXManager per-enemy vfx if no uniform prefab assigned
        if (vfxManager != null)
        {
            vfxManager.PlayPowerStealVFX(enemyName, position, Quaternion.identity);
        }
    }

    [PunRPC]
    public void RPC_PlayUniformPowerStealVFX(Vector3 position)
    {
        if (powerStealVFXPrefab == null) return;
        Vector3 spawnPos = (vfxSpawnPoint != null) ? vfxSpawnPoint.position : position;
        var vfx = Instantiate(powerStealVFXPrefab, spawnPos, Quaternion.identity);
        if (vfx != null) Destroy(vfx, 4f);
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
            powerStealTimerText.text = "";
        }
        if (powerStealIcon != null)
        {
            powerStealIcon.gameObject.SetActive(false);
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
        // Check if power is already granted
        if (grantedPowers.Contains(enemyName))
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
    public bool HasPower(string enemyName) => grantedPowers.Contains(enemyName);
    // No timer or expiration logic; powers persist until used
    public HashSet<string> GetGrantedPowers() => grantedPowers;

    // Removed unused method GetActivePowers

    // Clear all powers
    public void ClearAllPowers()
    {
        grantedPowers.Clear();
        // Optionally clear skill slots here if needed
    }
}

// PowerStealInstance removed; powers are now one-time use active skills

[System.Serializable]
public class PowerStealData
{
    [Header("Skill Effects")]
    public bool stopPlayerOnActivate = false; // if true, player movement is stopped when skill is used
    public string[] animationTriggers; // list of animation trigger names to activate on player
    [Header("Power Information")]
    public string enemyName;
    public string powerName;
    // PowerType enum and powerType field defined only once below
    [TextArea] public string description;
    public Sprite icon;

    [Header("Power Properties")]
    public bool canBeStolen = true;
    public int stealChance = 100; // Percentage

    [Header("Stat Modifications")]
    public int damageBonus = 0;
    public float speedBonus = 0f;
    public int healthBonus = 0;
    public int staminaBonus = 0;

    public enum PowerType { Attack, Buff, Utility }
    public PowerType powerType = PowerType.Attack;
    public float attackRadius = 0f; // for AOE
    public float projectileSpeed = 0f; // for projectiles
    public float effectDuration = 0f; // for buffs/DoT
    [Header("Stop / Timing")]
    public float stopDuration = 0.5f; // duration to stop player when activating this power

    [Header("Moveset")]
    public MovesetData movesetData;

    // All powers are now active skills; no special abilities or passive/trigger logic

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


