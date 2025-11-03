using Photon.Pun;
using UnityEngine;

// routes damage to enemies in a network-safe way
public static class EnemyDamageRelay
{
    public static void Apply(GameObject enemy, int amount, GameObject source)
    {
        if (enemy == null) return;
        
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
        else
        {
            // Get PhotonView from the BaseEnemyAI component (MonoBehaviourPun provides photonView)
            var enemyAI = enemy.GetComponent<BaseEnemyAI>();
            if (enemyAI != null && enemyAI.photonView != null)
            {
                enemyAI.photonView.RPC("RPC_EnemyTakeDamage", RpcTarget.MasterClient, amount, sourceViewId);
            }
            else
            {
                // Fallback: try GetComponent if MonoBehaviourPun approach doesn't work
                var pv = enemy.GetComponent<PhotonView>();
                if (pv != null)
                {
                    pv.RPC("RPC_EnemyTakeDamage", RpcTarget.MasterClient, amount, sourceViewId);
                }
                else
                {
                    Debug.LogWarning($"[EnemyDamageRelay] No PhotonView found on enemy {enemy.name}. Cannot send damage RPC.");
                }
            }
        }
    }
}
