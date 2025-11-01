using UnityEngine;
using UnityEngine.UI;
// add this if PowerType is in PowerStealManager.cs
using static PowerStealManager;

public class PlayerSkillSlots : MonoBehaviour
{
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            UseSkillSlot(0);
        }
        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            UseSkillSlot(1);
        }
        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            UseSkillSlot(2);
        }
    }

    void Awake()
    {
        // Set all backgrounds to empty on start
        for (int i = 0; i < skillSlotBgImages.Length; i++)
        {
            if (skillSlotBgImages[i] != null)
                skillSlotBgImages[i].sprite = bgEmptySprite;
        }

        // Hide all skill images on start
        for (int i = 0; i < skillSlotSkillImages.Length; i++)
        {
            if (skillSlotSkillImages[i] != null)
                skillSlotSkillImages[i].gameObject.SetActive(false); // hide in game until skill is assigned
        }
    }
    [Header("Skill Slot Data")]
    public PowerStealData[] skillSlots = new PowerStealData[3];

    [Header("Skill Slot UI")]
    public Image[] skillSlotBgImages = new Image[3]; // assign in inspector
    public Image[] skillSlotSkillImages = new Image[3]; // assign in inspector

    [Header("Background Sprites")]
    public Sprite bgEmptySprite; // assign in inspector
    public Sprite bgFilledSprite; // assign in inspector

    // Assign a stolen power to the first available slot
    public void AssignPowerToSlot(PowerStealData powerData)
    {
        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (skillSlots[i] == null || skillSlots[i].powerName == null || skillSlots[i].powerName == "")
            {
                skillSlots[i] = powerData;
                Debug.Log($"[PlayerSkillSlots] Assigned {powerData.powerName} to slot {i + 1}");
                UpdateSkillSlotUI(i);
                for (int j = 0; j < skillSlots.Length; j++)
                {
                    Debug.Log($"[PlayerSkillSlots] Slot {j + 1}: {(skillSlots[j] != null ? skillSlots[j].powerName : "<empty>")}");
                }
                return;
            }
        }
        Debug.LogWarning("[PlayerSkillSlots] No empty skill slots available!");
    }

    // Use a skill from a slot (call from UI button)
    public void UseSkillSlot(int slotIndex)
    {
    var power = skillSlots[slotIndex];
    if (power != null)
    {
        Debug.Log($"Using skill: {power.powerName}");
        var player = gameObject;
        var stats = player.GetComponent<PlayerStats>();
        var animator = player.GetComponent<Animator>();
        // stop player movement if requested (temporary)
        ThirdPersonController controller = null;
        if (power.stopPlayerOnActivate)
        {
            controller = player.GetComponent<ThirdPersonController>();
            if (controller != null)
            {
                controller.enabled = false;
                Debug.Log("[PlayerSkillSlots] Player movement stopped for skill activation.");
            }
        }
        // trigger animation parameters
        if (animator != null && power.animationTriggers != null)
        {
            foreach (var trigger in power.animationTriggers)
            {
                animator.SetTrigger(trigger);
                Debug.Log($"[PlayerSkillSlots] Triggered animation: {trigger}");
            }
        }
        // play VFX on skill activation (auto-destroy after stopDuration or effectDuration)
        GameObject activeVfxInstance = null;
        if (power.activeVFX != null)
        {
            activeVfxInstance = Instantiate(power.activeVFX, player.transform.position, Quaternion.identity);
            float vfxLifetime = power.effectDuration > 0f ? power.effectDuration : power.stopDuration > 0f ? power.stopDuration : 2f;
            if (activeVfxInstance != null) Destroy(activeVfxInstance, vfxLifetime);
            Debug.Log("[PlayerSkillSlots] Played active VFX on skill use.");
        }
        switch (power.powerType)
        {
            case PowerStealData.PowerType.Attack:
                // If AOE, apply damage in radius
                // If projectile, spawn projectile
                break;
            case PowerStealData.PowerType.Buff:
                // apply stat buffs to player
                if (stats != null)
                {
                    stats.maxHealth += power.healthBonus;
                    stats.maxStamina += power.staminaBonus;
                    stats.baseDamage += power.damageBonus;
                    stats.speedModifier += power.speedBonus;
                    Debug.Log($"[PlayerSkillSlots] Applied buff: +{power.healthBonus} health, +{power.staminaBonus} stamina, +{power.damageBonus} damage, +{power.speedBonus} speed");
                }
                else
                {
                    Debug.LogWarning("[PlayerSkillSlots] PlayerStats not found for buff application!");
                }
                break;
            case PowerStealData.PowerType.Utility:
                // Custom logic
                break;
        }
        // restore movement after duration if we stopped it
        if (controller != null)
        {
            float restoreDelay = power.stopDuration > 0f ? power.stopDuration : 0.5f;
            StartCoroutine(RestoreMovementAfter(controller, restoreDelay));
        }

        // Clear slot after use if one-time
        ClearSkillSlot(slotIndex);
    }
    else
    {
        Debug.Log($"Skill slot {slotIndex + 1} is empty.");
    }
}

    // Clear a slot (e.g., when power expires)
    public void ClearSkillSlot(int slotIndex)
    {
        skillSlots[slotIndex] = null;
        UpdateSkillSlotUI(slotIndex);
    }

    // Update UI icon for a slot
    public void UpdateSkillSlotUI(int slotIndex)
    {
        // Update background image
        if (skillSlotBgImages != null && slotIndex < skillSlotBgImages.Length)
        {
            var bgImg = skillSlotBgImages[slotIndex];
            if (bgImg != null)
            {
                if (skillSlots[slotIndex] != null)
                    bgImg.sprite = bgFilledSprite;
                else
                    bgImg.sprite = bgEmptySprite;
            }
        }
        else
        {
            Debug.LogWarning($"[PlayerSkillSlots] skillSlotBgImages array not assigned or index out of range: {slotIndex}");
        }

        // Update skill image
        if (skillSlotSkillImages != null && slotIndex < skillSlotSkillImages.Length)
        {
            var skillImg = skillSlotSkillImages[slotIndex];
            var slot = skillSlots[slotIndex];
            if (skillImg == null)
            {
                Debug.LogWarning($"[PlayerSkillSlots] Skill slot skill image {slotIndex + 1} is not assigned!");
                return;
            }
            if (slot != null && slot.icon != null)
            {
                skillImg.sprite = slot.icon;
                skillImg.enabled = true;
                skillImg.gameObject.SetActive(true);
                Debug.Log($"[PlayerSkillSlots] Updated skill image for slot {slotIndex + 1}: icon={slot.icon.name}, enabled=true");
            }
            else
            {
                skillImg.sprite = null;
                skillImg.enabled = false;
                skillImg.gameObject.SetActive(true);
                Debug.Log($"[PlayerSkillSlots] Updated skill image for slot {slotIndex + 1}: icon=<none>, enabled=false");
            }
        }
        else
        {
            Debug.LogWarning($"[PlayerSkillSlots] skillSlotSkillImages array not assigned or index out of range: {slotIndex}");
        }
    }

    // Call this after stealing a power (from PowerStealManager)
    public void OnPowerStolen(PowerStealData powerData, Vector3 enemyPosition)
    {
        // steal VFX is spawned centrally by PowerStealManager (uniform prefab). Do not spawn here to avoid duplicates.
        AssignPowerToSlot(powerData);
    }

    private System.Collections.IEnumerator RestoreMovementAfter(ThirdPersonController controller, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (controller != null)
        {
            controller.enabled = true;
            Debug.Log("[PlayerSkillSlots] Player movement restored after skill activation.");
        }
    }
}
