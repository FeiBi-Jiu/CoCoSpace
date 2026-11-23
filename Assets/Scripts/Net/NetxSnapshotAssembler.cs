using System.Collections.Generic;

namespace Game.Net
{
    public sealed class NetxSnapshotAssembler
    {
        readonly Dictionary<uint, Game.Proto.Entity> cache = new();
        readonly List<uint> stale = new();

        public uint SelfEntityId { get; private set; }
        public uint SelfGold { get; private set; }
        public uint LastServerTick { get; private set; }
        public uint LastAckedInputTick { get; private set; }

        public void Reset()
        {
            cache.Clear();
            SelfEntityId = 0;
            SelfGold = 0;
            LastServerTick = 0;
            LastAckedInputTick = 0;
        }

        public Game.Proto.S2C_Snapshot Apply(Game.Proto.Backend.Snapshot delta, uint tickRate)
        {
            if (delta == null) return null;

            LastServerTick = delta.ServerTick;
            LastAckedInputTick = delta.AckedInputTick;

            foreach (uint id in delta.Removes) cache.Remove(id);

            foreach (var d in delta.Adds) Upsert(d);
            foreach (var d in delta.Updates) Upsert(d);

            if (delta.Self != null)
            {
                SelfEntityId = delta.Self.EntityId;
                SelfGold = delta.Self.Gold;
                UpsertSelf(delta.Self);
            }

            double serverTime = tickRate == 0 ? 0.0 : (double)delta.ServerTick / tickRate;

            var full = new Game.Proto.S2C_Snapshot { ServerTime = serverTime };
            full.Entities.AddRange(cache.Values);
            return full;
        }

        public Game.Proto.Entity Find(ulong entityId)
        {
            return cache.TryGetValue((uint)entityId, out var e) ? e : null;
        }

        void Upsert(Game.Proto.Backend.EntityDelta d)
        {
            if (d == null || d.EntityId == 0) return;

            if (!cache.TryGetValue(d.EntityId, out var e))
            {
                e = new Game.Proto.Entity { EntityId = d.EntityId };
                cache[d.EntityId] = e;
            }

            e.EntityId = d.EntityId;
            e.Type = ToType(d.Kind);
            e.X = NetxProto.DequantizePos(d.Pos != null ? d.Pos.X : 0);
            e.Z = NetxProto.DequantizePos(d.Pos != null ? d.Pos.Y : 0);
            e.Yaw = NetxProto.AimToYawDegrees(d.Aim);
            e.Hp = d.Hp;
            e.HpMax = d.MaxHp;
            e.Flags = ToFlags(d.Flags);
        }

        void UpsertSelf(Game.Proto.Backend.PlayerSelf s)
        {
            if (s.EntityId == 0) return;

            if (!cache.TryGetValue(s.EntityId, out var e))
            {
                e = new Game.Proto.Entity { EntityId = s.EntityId };
                cache[s.EntityId] = e;
            }

            e.EntityId = s.EntityId;
            e.Type = Game.Proto.EntityType.EntityPlayer;
            e.X = NetxProto.DequantizePos(s.Pos != null ? s.Pos.X : 0);
            e.Z = NetxProto.DequantizePos(s.Pos != null ? s.Pos.Y : 0);
            e.Yaw = NetxProto.AimToYawDegrees(s.Aim);
            e.Hp = s.Hp;
            e.HpMax = s.MaxHp;
            e.Flags = ToFlags(s.Flags);
        }

        static Game.Proto.EntityFlag ToFlags(uint flags)
        {
            if (flags == 0) return null;
            return new Game.Proto.EntityFlag
            {
                IsGhost = (flags & 1u) != 0,
                IsEnraged = (flags & 2u) != 0,
                ArmorBroken = (flags & 4u) != 0,
            };
        }

        static Game.Proto.EntityType ToType(Game.Proto.Backend.EntityKind kind)
        {
            switch (kind)
            {
                case Game.Proto.Backend.EntityKind.Player: return Game.Proto.EntityType.EntityPlayer;
                case Game.Proto.Backend.EntityKind.Monster: return Game.Proto.EntityType.EntityMonster;
                case Game.Proto.Backend.EntityKind.Bullet: return Game.Proto.EntityType.EntityBullet;
                default: return Game.Proto.EntityType.EntityNone;
            }
        }
    }
}