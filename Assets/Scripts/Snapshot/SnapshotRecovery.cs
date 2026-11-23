using Game.Proto;
using UnityEngine;

namespace Game.Snapshot
{
    public static class SnapshotRecovery
    {
        public static void Apply(S2C_Snapshot snap)
        {
            var director = SnapshotDirector.Instance;
            if (director == null)
            {
                Debug.LogWarning("[SnapshotRecovery] SnapshotDirector 未就绪，忽略重连快照");
                return;
            }

            director.ResetFromSnapshot(snap);
            Debug.Log($"[SnapshotRecovery] 应用重连快照 time={snap.ServerTime} entities={snap.Entities.Count}");
        }
    }
}