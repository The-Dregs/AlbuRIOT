using UnityEngine;
using Photon.Pun;

public class PlayerStats : MonoBehaviourPun, IPunObservable
{
    public int maxHealth = 100;
    public int currentHealth;
    public int maxStamina = 100;
    public int currentStamina;
    public float staminaRegenRate = 10f;
    public int baseDamage = 25;
    public float baseSpeed = 6f;
    public float speedModifier = 0f;
    public int staminaCostModifier = 0;

    // when true, stamina will not regenerate (e.g., while running)
    private bool staminaRegenBlocked = false;
    // delay before stamina starts regenerating after use or when unblocking
    [Header("stamina regen")]
    public float staminaRegenDelay = 1.0f;
    private float staminaRegenDelayTimer = 0f;
    private float staminaRegenAccumulator = 0f;

    // --- health regeneration ---
    [Header("health regen")]
    [Tooltip("enable or disable passive health regeneration")] public bool enableHealthRegen = true;
    [Tooltip("health regenerated per second (use small values for slow regen, e.g., 0.25..2.0)")] public float healthRegenPerSecond = 0.5f;
    [Tooltip("delay after taking damage before regen starts")] public float healthRegenDelay = 5.0f;
    [Tooltip("when true, health regen pauses while bleeding is active")] public bool blockRegenWhileBleeding = true;
    private float healthRegenDelayTimer = 0f;
    private float healthRegenAccumulator = 0f;
    private bool healthRegenWasActive = false; // for debug logs

    // --- status effects / debuffs ---
    [Header("status effects")]
    [Tooltip("applies a percent slowdown (0..1) to movement speed")] public float slowPercent = 0f; // cumulative (max wins)
    [Tooltip("when > 0, player cannot move (rooted)")] public float rootRemaining = 0f;
    [Tooltip("when > 0, player cannot use abilities/attacks")] public float silenceRemaining = 0f;
    [Tooltip("when > 0, player cannot move or act")] public float stunRemaining = 0f;
    [Tooltip("damage taken multiplier bonus, e.g., 0.2 => take +20% damage")] public float defenseDownBonus = 0f; // cumulative (max wins)
    [Tooltip("bleed damage per 0.5s tick while active")] public float bleedPerTick = 0f; public float bleedRemaining = 0f; private float bleedAcc = 0f;
    [Tooltip("stamina burn per 0.5s tick while active")] public float staminaBurnPerTick = 0f; public float staminaBurnRemaining = 0f; private float staminaBurnAcc = 0f;

    void Awake()
    {
        currentHealth = maxHealth;
        currentStamina = maxStamina;
    animator = GetComponent<Animator>();
    controller = GetComponent<ThirdPersonController>();
    combat = GetComponent<PlayerCombat>();
    }

    void Update()
    {
        if (photonView != null && !photonView.IsMine) return;
        TickDebuffs();
        // countdown delay timer
        if (staminaRegenDelayTimer > 0f)
        {
            staminaRegenDelayTimer -= Time.deltaTime;
        }
        // regenerate only if not blocked and delay timer elapsed; use accumulator for smooth, framerate-independent regen
        if (currentStamina < maxStamina && !staminaRegenBlocked && staminaRegenDelayTimer <= 0f)
        {
            staminaRegenAccumulator += staminaRegenRate * Time.deltaTime;
            if (staminaRegenAccumulator >= 1f)
            {
                int toAdd = Mathf.FloorToInt(staminaRegenAccumulator);
                int room = maxStamina - currentStamina;
                int applied = Mathf.Min(toAdd, room);
                currentStamina += applied;
                staminaRegenAccumulator -= applied;
            }
        }

        // health regen timers
        if (healthRegenDelayTimer > 0f)
        {
            healthRegenDelayTimer -= Time.deltaTime;
        }

        // slow, configurable health regeneration (owner only)
        bool canRegenHealth = enableHealthRegen && !isDead && currentHealth < maxHealth && healthRegenDelayTimer <= 0f && (!blockRegenWhileBleeding || bleedRemaining <= 0f);
        if (canRegenHealth && healthRegenPerSecond > 0f)
        {
            healthRegenAccumulator += Mathf.Max(0f, healthRegenPerSecond) * Time.deltaTime;
            if (healthRegenAccumulator >= 1f)
            {
                int toAdd = Mathf.FloorToInt(healthRegenAccumulator);
                int room = maxHealth - currentHealth;
                int applied = Mathf.Min(toAdd, room);
                if (applied > 0)
                {
                    currentHealth += applied;
                    healthRegenAccumulator -= applied;
                    if (!healthRegenWasActive)
                    {
                        Debug.Log($"health regen started ({healthRegenPerSecond}/s)");
                        healthRegenWasActive = true;
                    }
                }
            }
        }
        else
        {
            // if regen was active but now blocked/full/dead, log once
            if (healthRegenWasActive)
            {
                Debug.Log("health regen paused");
                healthRegenWasActive = false;
            }
        }
    }

    public void TakeDamage(int amount)
    {
        if (isDead) return;
        // invulnerable while rolling/dashing
        var controllerCmp = controller != null ? controller : GetComponent<ThirdPersonController>();
        if (controllerCmp != null && controllerCmp.IsRolling)
        {
            Debug.Log("damage ignored: rolling");
            return;
        }
    // defense down increases incoming damage
    float mult = 1f + Mathf.Max(0f, defenseDownBonus);
    int finalAmount = Mathf.RoundToInt(amount * mult);
    currentHealth -= finalAmount;
        if (currentHealth < 0) currentHealth = 0;

        // reset health regen delay when taking damage
        if (finalAmount > 0)
        {
            healthRegenDelayTimer = healthRegenDelay;
            healthRegenAccumulator = 0f;
            // also mark as not currently regenerating for debug
            if (healthRegenWasActive)
            {
                Debug.Log($"health regen delayed for {healthRegenDelay:F1}s due to damage");
                healthRegenWasActive = false;
            }
        }

        // play hit reaction if still alive
        if (currentHealth > 0 && finalAmount > 0)
        {
            PlayHitFX();
            ShowDamageIndicator(finalAmount);
            PulseDamageOverlay(finalAmount);
        }
        else if (currentHealth <= 0)
        {
            // trigger death
            Die();
        }
    }

    // network entry point for enemy damage; invoked on the owning client
    [PunRPC]
    public void RPC_TakeDamage(int amount)
    {
        TakeDamage(amount);
    }

    public bool UseStamina(int amount)
    {
        if (currentStamina >= amount)
        {
            currentStamina -= amount;
            // reset regen delay on stamina spend
            staminaRegenDelayTimer = staminaRegenDelay;
            staminaRegenAccumulator = 0f;
            return true;
        }
        return false;
    }

    public void Heal(int amount)
    {
        currentHealth += amount;
        if (currentHealth > maxHealth) currentHealth = maxHealth;
    }

    public void RestoreStamina(int amount)
    {
        currentStamina += amount;
        if (currentStamina > maxStamina) currentStamina = maxStamina;
    }

    // debuff ticking and application
    private void TickDebuffs()
    {
        float dt = Time.deltaTime;
        if (rootRemaining > 0f) rootRemaining = Mathf.Max(0f, rootRemaining - dt);
        if (silenceRemaining > 0f) silenceRemaining = Mathf.Max(0f, silenceRemaining - dt);
        if (stunRemaining > 0f) stunRemaining = Mathf.Max(0f, stunRemaining - dt);

        if (bleedRemaining > 0f)
        {
            bleedRemaining = Mathf.Max(0f, bleedRemaining - dt);
            bleedAcc += dt;
            if (bleedAcc >= 0.5f)
            {
                bleedAcc -= 0.5f;
                int dmg = Mathf.RoundToInt(Mathf.Max(0f, bleedPerTick));
                if (dmg > 0) TakeDamage(dmg);
            }
            if (bleedRemaining <= 0f) bleedPerTick = 0f;
        }
        if (staminaBurnRemaining > 0f)
        {
            staminaBurnRemaining = Mathf.Max(0f, staminaBurnRemaining - dt);
            staminaBurnAcc += dt;
            if (staminaBurnAcc >= 0.5f)
            {
                staminaBurnAcc -= 0.5f;
                int burn = Mathf.RoundToInt(Mathf.Max(0f, staminaBurnPerTick));
                if (burn > 0)
                {
                    currentStamina = Mathf.Max(0, currentStamina - burn);
                    staminaRegenDelayTimer = Mathf.Max(staminaRegenDelayTimer, 0.5f);
                }
            }
            if (staminaBurnRemaining <= 0f) staminaBurnPerTick = 0f;
        }

        // sanitize slow
        if (slowPercent < 0f) slowPercent = 0f;
        if (slowPercent > 0.9f) slowPercent = 0.9f;
    }

    [PunRPC]
    public void RPC_ApplyDebuff(int type, float magnitude, float duration)
    {
        ApplyDebuff(type, magnitude, duration);
    }

    public void ApplyDebuff(int type, float magnitude, float duration)
    {
        var t = (StatusEffectRelay.EffectType)type;
        switch (t)
        {
            case StatusEffectRelay.EffectType.Slow:
                slowPercent = Mathf.Max(slowPercent, Mathf.Clamp01(magnitude));
                StartCoroutine(ClearAfter(duration, () => slowPercent = 0f));
                break;
            case StatusEffectRelay.EffectType.Root:
                rootRemaining = Mathf.Max(rootRemaining, duration);
                break;
            case StatusEffectRelay.EffectType.Silence:
                silenceRemaining = Mathf.Max(silenceRemaining, duration);
                break;
            case StatusEffectRelay.EffectType.Stun:
                stunRemaining = Mathf.Max(stunRemaining, duration);
                break;
            case StatusEffectRelay.EffectType.DefenseDown:
                defenseDownBonus = Mathf.Max(defenseDownBonus, Mathf.Max(0f, magnitude));
                StartCoroutine(ClearAfter(duration, () => defenseDownBonus = 0f));
                break;
            case StatusEffectRelay.EffectType.Bleed:
                bleedPerTick = Mathf.Max(bleedPerTick, magnitude);
                bleedRemaining = Mathf.Max(bleedRemaining, duration);
                bleedAcc = 0f;
                break;
            case StatusEffectRelay.EffectType.StaminaBurn:
                staminaBurnPerTick = Mathf.Max(staminaBurnPerTick, magnitude);
                staminaBurnRemaining = Mathf.Max(staminaBurnRemaining, duration);
                staminaBurnAcc = 0f;
                break;
        }
    }

    private System.Collections.IEnumerator ClearAfter(float seconds, System.Action onClear)
    {
        if (seconds <= 0f) { onClear?.Invoke(); yield break; }
        yield return new WaitForSeconds(seconds);
        onClear?.Invoke();
    }

    public void ApplyEquipment(ItemData item)
    {
        maxHealth += item.healthModifier;
        maxStamina += item.staminaModifier;
        baseDamage += item.damageModifier;
        speedModifier += item.speedModifier;
        staminaCostModifier += item.staminaCostModifier;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        currentStamina = Mathf.Min(currentStamina, maxStamina);
    }

    public void RemoveEquipment(ItemData item)
    {
        maxHealth -= item.healthModifier;
        maxStamina -= item.staminaModifier;
        baseDamage -= item.damageModifier;
        speedModifier -= item.speedModifier;
        staminaCostModifier -= item.staminaCostModifier;
        currentHealth = Mathf.Min(currentHealth, maxHealth);
        currentStamina = Mathf.Min(currentStamina, maxStamina);
    }

    // external controllers can toggle stamina regen based on movement state
    public void SetStaminaRegenBlocked(bool blocked)
    {
        if (staminaRegenBlocked == blocked) return;
        // when unblocking, start delay so regen doesn't kick in immediately
        if (staminaRegenBlocked && !blocked)
        {
            staminaRegenDelayTimer = staminaRegenDelay;
        }
        staminaRegenBlocked = blocked;
        if (blocked)
        {
            // clear accumulator so regen resumes cleanly later
            staminaRegenAccumulator = 0f;
        }
    }

    // ui/debug helpers
    public bool IsStaminaRegenBlocked => staminaRegenBlocked;
    public bool IsStaminaRegenerating => currentStamina < maxStamina && !staminaRegenBlocked && staminaRegenDelayTimer <= 0f;
    public float StaminaRegenDelayRemaining => Mathf.Max(0f, staminaRegenDelayTimer);

    // health regen status
    public bool IsHealthRegenerating => enableHealthRegen && !isDead && currentHealth < maxHealth && healthRegenDelayTimer <= 0f && (!blockRegenWhileBleeding || bleedRemaining <= 0f);
    public float HealthRegenDelayRemaining => Mathf.Max(0f, healthRegenDelayTimer);

    // --- hit / death animation integration ---
    [Header("hit/death animation")]
    public string hitTriggerName = "Hit";
    public string deathTriggerName = "Die";
    public string isDeadBoolName = "IsDead";

    private Animator animator;
    private ThirdPersonController controller;
    private PlayerCombat combat;
    private bool isDead = false;
    public bool IsDead => isDead;

    private bool AnimatorHasParameter(string name)
    {
        if (animator == null) return false;
        foreach (var p in animator.parameters)
        {
            if (p.name == name) return true;
        }
        return false;
    }

    private void PlayHitFX()
    {
        if (photonView != null && (Photon.Pun.PhotonNetwork.IsConnected || Photon.Pun.PhotonNetwork.OfflineMode))
        {
            photonView.RPC(nameof(RPC_PlayHit), RpcTarget.All);
        }
        else
        {
            // not connected: play locally
            RPC_PlayHit();
        }
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;
        if (photonView != null && (Photon.Pun.PhotonNetwork.IsConnected || Photon.Pun.PhotonNetwork.OfflineMode))
        {
            photonView.RPC(nameof(RPC_PlayDeath), RpcTarget.All);
        }
        else
        {
            // not connected: play locally
            RPC_PlayDeath();
        }
        // clear debuffs on death
        slowPercent = 0f; rootRemaining = 0f; silenceRemaining = 0f; stunRemaining = 0f; defenseDownBonus = 0f; bleedPerTick = 0f; bleedRemaining = 0f; staminaBurnPerTick = 0f; staminaBurnRemaining = 0f;
    }

    [PunRPC]
    private void RPC_PlayHit()
    {
        if (animator != null && AnimatorHasParameter(hitTriggerName))
        {
            animator.SetTrigger(hitTriggerName);
        }
    }

    [PunRPC]
    private void RPC_PlayDeath()
    {
        // animation flags
        if (animator != null)
        {
            if (AnimatorHasParameter(isDeadBoolName)) animator.SetBool(isDeadBoolName, true);
            if (AnimatorHasParameter(deathTriggerName)) animator.SetTrigger(deathTriggerName);
        }
        // disable control for local player so they stop moving/attacking
        if (photonView == null || photonView.IsMine)
        {
            if (controller != null)
            {
                controller.SetCanControl(false);
                controller.SetCanMove(false);
            }
            if (combat != null)
            {
                combat.SetCanControl(false);
            }
            // block stamina regen and spending
            SetStaminaRegenBlocked(true);
        }
    }

    [Header("ui damage indicator")]
    public GameObject damageTextPrefab; // optional, shows numbers like enemies
    public Transform damageTextSpawnPoint;

    private void ShowDamageIndicator(int amount)
    {
        if (damageTextPrefab == null) return;
        Vector3 spawnPos = damageTextSpawnPoint != null ? damageTextSpawnPoint.position : transform.position + Vector3.up * 2f;
        var go = Instantiate(damageTextPrefab, spawnPos, Quaternion.identity);
        var dmg = go.GetComponent<DamageText>();
        if (dmg != null) dmg.ShowDamage(amount);
    }

    // pulse local screen overlay (if present) when damaged
    private void PulseDamageOverlay(int amount)
    {
        if (photonView != null && !photonView.IsMine) return; // local screen only
    var overlay = FindFirstObjectByType<DamageOverlayUI>();
        if (overlay != null)
        {
            float proportion = maxHealth > 0 ? (float)amount / (float)maxHealth : 0.2f;
            overlay.Pulse(Mathf.Clamp01(proportion * 3f)); // scale up a bit
        }
    }

    // convenience accessors for controllers
    public bool IsStunned => stunRemaining > 0f;
    public bool IsRooted => rootRemaining > 0f || IsStunned;
    public bool IsSilenced => silenceRemaining > 0f || IsStunned;

    // network sync for essential stat values
    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext(currentHealth);
            stream.SendNext(maxHealth);
            stream.SendNext(currentStamina);
            stream.SendNext(maxStamina);
        }
        else
        {
            currentHealth = (int)stream.ReceiveNext();
            maxHealth = (int)stream.ReceiveNext();
            currentStamina = (int)stream.ReceiveNext();
            maxStamina = (int)stream.ReceiveNext();
        }
    }
}
