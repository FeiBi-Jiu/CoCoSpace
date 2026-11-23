using Game.Proto;
using Game.Proto.Backend;
using UnityEngine;

namespace Game.Net
{
    public static class BackendBridge
    {
        static bool hasSelfGhost;
        static bool selfGhost;

        public static void Apply(Game.Proto.Backend.Snapshot snap, NetxSnapshotAssembler assembler)
        {
            if (snap == null || assembler == null) return;

            SyncSelfGhost(assembler);
            TranslateEvents(snap, assembler);
        }

        public static void Reset()
        {
            hasSelfGhost = false;
            selfGhost = false;
        }

        // 后端死亡只置 ghost 标记位、不发 S2CDeath，这里把它翻译成客户端的死亡/复活消息
        static void SyncSelfGhost(NetxSnapshotAssembler assembler)
        {
            uint selfId = assembler.SelfEntityId;
            if (selfId == 0) return;

            var self = assembler.Find(selfId);
            if (self == null) return;

            bool ghost = self.Flags != null && self.Flags.IsGhost;
            if (hasSelfGhost && ghost == selfGhost) return;

            hasSelfGhost = true;
            selfGhost = ghost;

            if (ghost)
            {
                MsgDispatcher.Dispatch(new S2CDeath { EntityId = selfId });
            }
            else
            {
                MsgDispatcher.Dispatch(new S2CRevived { EntityId = selfId });
            }
        }

        // 后端 GameEvent：A=来源 B=目标，value 为 ×1000 定点
        static void TranslateEvents(Game.Proto.Backend.Snapshot snap, NetxSnapshotAssembler assembler)
        {
            var events = snap.Events;
            if (events == null) return;

            for (int i = 0; i < events.Count; i++)
            {
                var ev = events[i];
                if (ev == null) continue;

                bool isDamage = ev.Kind == GameEventKind.Damage;
                bool isKill = ev.Kind == GameEventKind.Kill;
                bool isDeath = ev.Kind == GameEventKind.Death;
                if (!isDamage && !isKill && !isDeath) continue;

                ulong victimId = ev.B;
                if (victimId == 0) continue;

                var victim = assembler.Find(victimId);

                MsgDispatcher.Dispatch(new S2CHitApply
                {
                    AttackerEntityId = ev.A,
                    HasVictimEntityId = true,
                    VictimEntityId = victimId,
                    DamageDealt = (uint)Mathf.Max(0f, ev.Value / 1000f),
                    HpAfter = victim != null ? victim.Hp : 0f,
                    HpMaxAfter = victim != null ? victim.HpMax : 0f,
                });

                if (isKill || isDeath)
                {
                    MsgDispatcher.Dispatch(new S2CDeath { EntityId = victimId });
                }
            }
        }
    }
}