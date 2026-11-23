using Game.Combat;
using Game.Proto;
using Google.Protobuf;
using UnityEngine;
using PatternDef = Game.Table.BulletPatternDef;

namespace Game.Net
{
    public static class BulletPatternNet
    {
        public const int SpawnPatternMsgId = 2016;
        public const int StopPatternMsgId = 2017;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Bootstrap()
        {
            MsgDispatcher.UnregisterRaw((MsgId)SpawnPatternMsgId);
            MsgDispatcher.UnregisterRaw((MsgId)StopPatternMsgId);
            MsgDispatcher.RegisterRaw((MsgId)SpawnPatternMsgId, OnSpawnPattern);
            MsgDispatcher.RegisterRaw((MsgId)StopPatternMsgId, OnStopPattern);

            MsgDispatcher.Unregister<S2CGameOver>();
            MsgDispatcher.Register<S2CGameOver>(OnGameOver);
        }

        static void OnGameOver(S2CGameOver msg) => BulletPatternManager.StopAll();

        static void OnSpawnPattern(ByteString payload)
        {
            if (!TryParseSpawn(payload, out string patternId, out ulong emitterId, out PatternDef def))
                return;

            if (string.IsNullOrEmpty(patternId) && def != null) patternId = def.Id;
            BulletPatternManager.SpawnPattern(patternId, emitterId, def);
        }

        static void OnStopPattern(ByteString payload)
        {
            if (!TryParseStop(payload, out string patternId)) return;
            BulletPatternManager.StopPattern(patternId);
        }

        static bool TryParseSpawn(ByteString payload, out string patternId, out ulong emitterId, out PatternDef def)
        {
            patternId = string.Empty;
            emitterId = 0;
            def = null;

            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    if ((tag & 7) == 4) break;

                    switch (tag)
                    {
                        case 10: patternId = input.ReadString(); break;
                        case 16: emitterId = input.ReadUInt64(); break;
                        case 26: def = ParseDef(input.ReadBytes()); break;
                        default: input.SkipLastField(); break;
                    }
                }
            }
            catch (InvalidProtocolBufferException e)
            {
                Debug.LogWarning($"[Pattern] S2C_SpawnPattern 解析失败: {e.Message}");
                return false;
            }

            return true;
        }

        static bool TryParseStop(ByteString payload, out string patternId)
        {
            patternId = string.Empty;

            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    if ((tag & 7) == 4) break;

                    switch (tag)
                    {
                        case 10: patternId = input.ReadString(); break;
                        default: input.SkipLastField(); break;
                    }
                }
            }
            catch (InvalidProtocolBufferException e)
            {
                Debug.LogWarning($"[Pattern] S2C_StopPattern 解析失败: {e.Message}");
                return false;
            }

            return !string.IsNullOrEmpty(patternId);
        }

        static PatternDef ParseDef(ByteString payload)
        {
            var def = new PatternDef();
            var input = payload.CreateCodedInput();

            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                if ((tag & 7) == 4) break;

                switch (tag)
                {
                    case 10: def.Id = input.ReadString(); break;
                    case 18: def.Shape = input.ReadString(); break;
                    case 29: def.Interval = input.ReadFloat(); break;
                    case 32: def.Count = input.ReadInt32(); break;
                    case 45: def.Speed = input.ReadFloat(); break;
                    case 53: def.SpreadDeg = input.ReadFloat(); break;
                    case 58: def.BulletVisualId = input.ReadString(); break;
                    case 69: def.Duration = input.ReadFloat(); break;
                    case 77: def.AngularSpeedDeg = input.ReadFloat(); break;
                    case 85: def.Acceleration = input.ReadFloat(); break;
                    case 93: def.BulletLifetime = input.ReadFloat(); break;
                    case 101: def.BulletRadius = input.ReadFloat(); break;
                    case 109: def.Damage = input.ReadFloat(); break;
                    case 117: def.StartAngleDeg = input.ReadFloat(); break;
                    case 125: def.LineSpacing = input.ReadFloat(); break;
                    case 130: def.ColorHex = input.ReadString(); break;
                    default: input.SkipLastField(); break;
                }
            }

            return def;
        }
    }
}