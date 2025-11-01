using Photon.Pun;
using UnityEngine;

// routes damage to enemies in a network-safe way
public static class EnemyDamageRelay
{
    public static void Apply(GameObject enemy, int amount, GameObject source)
    {
        if (enemy == null) return;
        var pv = enemy.GetComponent<PhotonView>();
        int sourceViewId = -1;
        var srcPv = source != null ? source.GetComponent<PhotonView>() : null;
        if (srcPv != null) sourceViewId = srcPv.ViewID;

        bool isInRoom = PhotonNetwork.OfflineMode || (PhotonNetwork.IsConnected && PhotonNetwork.InRoom);

        if (!isInRoom)
        {
            // local/offline execution when not in a room
            var dmg = enemy.GetComponent<IEnemyDamageable>();
            if (dmg != null)
            {
                dmg.TakeEnemyDamage(amount, source);
            }
            else
            {
                // fallback: try a common method name
                var mi = enemy.GetType().GetMethod("TakeDamage", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                if (mi != null) mi.Invoke(enemy, new object[] { amount });
            }
            return;
        }

        // in network room: route to master client (server authority for AI)
        if (PhotonNetwork.IsMasterClient)
        {
            var dmg = enemy.GetComponent<IEnemyDamageable>();
            if (dmg != null)
            {
                dmg.TakeEnemyDamage(amount, source);
            }
        }
        else if (pv != null)
        {
            pv.RPC("RPC_EnemyTakeDamage", RpcTarget.MasterClient, amount, sourceViewId);
        }
    }
}
