using Photon.Pun;
using UnityEngine;

// attach to the player prefab so server/master can attribute quest progress to the correct client
public class PlayerQuestRelay : MonoBehaviourPun
{
    [PunRPC]
    public void RPC_AddKillProgress(string enemyName)
    {
        // only execute on owning client
        if (photonView != null && !photonView.IsMine) return;
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm != null)
        {
            qm.AddProgress_Kill(enemyName);
            Debug.Log($"quest kill progress (rpc): {enemyName}");
        }
    }

    [PunRPC]
    public void RPC_AddReachProgress(string areaId)
    {
        if (photonView != null && !photonView.IsMine) return;
        var qm = FindFirstObjectByType<QuestManager>();
        if (qm != null)
        {
            qm.AddProgress_ReachArea(areaId);
            Debug.Log($"quest reach progress (rpc): {areaId}");
        }
    }
}
