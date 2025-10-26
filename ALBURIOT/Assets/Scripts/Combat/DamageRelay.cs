using Photon.Pun;
using UnityEngine;

// utility for applying damage to players in a network-safe way
public static class DamageRelay
{
    public static void ApplyToPlayer(GameObject player, int amount)
    {
        if (player == null) return;
        var pv = player.GetComponent<PhotonView>();
        var stats = player.GetComponent<PlayerStats>();
        // Only route via RPC when actually in a room (or offline mode). Otherwise fall back to local damage to
        // support single-player/editor testing where Photon may be connected but not joined.
        bool canUseRpc = pv != null && (PhotonNetwork.OfflineMode || (PhotonNetwork.IsConnected && PhotonNetwork.InRoom));
        if (canUseRpc)
        {
            if (pv.Owner != null)
            {
                Debug.Log($"DamageRelay RPC -> {player.name} owner {pv.Owner.ActorNumber}, dmg {amount}");
                pv.RPC("RPC_TakeDamage", pv.Owner, amount);
            }
            else
            {
                // no known owner (e.g., not properly networked); apply locally as a safe fallback
                if (stats != null)
                {
                    Debug.LogWarning($"DamageRelay: PhotonView on {player.name} has no Owner; applying damage locally. dmg {amount}");
                    stats.TakeDamage(amount);
                }
            }
        }
        else if (stats != null)
        {
            // local/offline execution path
            Debug.Log($"DamageRelay Local -> {player.name}, dmg {amount}");
            stats.TakeDamage(amount);
        }
    }
}
