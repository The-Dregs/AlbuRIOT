using Photon.Pun;
using UnityEngine;

namespace AlbuRIOT.Abilities
{
    [DisallowMultipleComponent]
    public class PlayerAbilityController : MonoBehaviourPun
    {
        [Header("ability slots")]
        [Tooltip("Slot 1 ability (activated by key 1)")]
        public AbilityBase slot1;

        [Header("stamina costs")]
        public int slot1StaminaCost = 20;
        
            [Header("animation overrides (optional)")]
            [Tooltip("If set, this trigger will be used for Slot 1 ability animation instead of the ability's own setting.")]
            public string slot1AnimTriggerOverride = "";

        private PlayerStats stats;

        void Awake()
        {
            stats = GetComponent<PlayerStats>();
        }

        void Update()
        {
            if (photonView != null && !photonView.IsMine) return;
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                TryUseSlot1();
            }
        }

        public bool TryUseSlot1()
        {
            if (slot1 == null) { Debug.Log("ability slot 1 empty"); return false; }
            if (!slot1.IsReady) { Debug.Log("ability slot 1 cooldown"); return false; }

            int cost = slot1StaminaCost;
            if (stats != null) cost = Mathf.Max(0, slot1StaminaCost + stats.staminaCostModifier);
            if (stats != null && cost > 0 && !stats.UseStamina(cost))
            {
                Debug.Log("not enough stamina for ability 1");
                return false;
            }
            bool ok = slot1.Execute(gameObject, this);
            if (ok) Debug.Log($"used ability 1: {slot1.abilityName}");
            return ok;
        }

        // runtime assignment from drops
        public void AssignSlot1(AbilityBase ability)
        {
            if (ability != null)
            {
                // clone the ScriptableObject so runtime state (cooldown) is per-player, not shared
                var instanced = ScriptableObject.Instantiate(ability);
                // reset cooldown so it can be used immediately after grant
                instanced.lastUseTime = -999f;
                slot1 = instanced;
            }
            else
            {
                slot1 = null;
            }
            Debug.Log($"ability slot 1 assigned: {(slot1!=null?slot1.abilityName:"<null>")}");
        }

        [PunRPC]
        public void RPC_AssignAbilitySlot1ByPath(string resourcesPath)
        {
            var ability = Resources.Load<AbilityBase>(resourcesPath);
            if (ability != null)
            {
                AssignSlot1(ability);
            }
            else
            {
                Debug.LogWarning($"RPC_AssignAbilitySlot1ByPath: could not load ability at Resources/{resourcesPath}");
            }
        }

        // --- animation helpers for abilities ---
        public void PlayAbilityAnimation(string trigger)
        {
            if (string.IsNullOrEmpty(trigger)) return;
            var pv = photonView;
            if (pv != null && (PhotonNetwork.InRoom || PhotonNetwork.OfflineMode))
            {
                pv.RPC(nameof(RPC_PlayAnimTrigger), RpcTarget.All, trigger);
            }
            else
            {
                // not connected: play locally
                RPC_PlayAnimTrigger(trigger);
            }
        }

        [PunRPC]
        public void RPC_PlayAnimTrigger(string trigger)
        {
            var anim = GetComponent<Animator>();
            if (anim == null) return;
            if (AnimatorHasTrigger(anim, trigger))
            {
                anim.SetTrigger(trigger);
                Debug.Log($"ability animation trigger fired: {trigger}");
            }
            else
            {
                Debug.LogWarning($"Animator missing trigger '{trigger}' on {name}");
            }
        }

        private bool AnimatorHasTrigger(Animator anim, string trig)
        {
            foreach (var p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == trig)
                    return true;
            }
            return false;
        }
    }
}
