using System.Collections.Generic;
using Game.Player;
using UnityEngine;

namespace Game.Net
{
    public static class NetxProto
    {
        public const uint ProtocolVersion = 1;
        public const uint DefaultTickRate = 20;

        public const double PosScale = 32.0;
        public const double VelScale = 256.0;
        public const double RadiusScale = 256.0;
        public const double TwoPi = 6.283185307179586;
        public const double AimScale = 65536.0 / TwoPi;

        public const int MaxInputMagnitude = 1000;

        public const uint ButtonAttack = 1u << 0;
        public const uint ButtonSkill1 = 1u << 1;
        public const uint ButtonSkill2 = 1u << 2;
        public const uint ButtonInteract = 1u << 3;

        const double Clamp = 1 << 30;

        public static int QuantizePos(float v) => Round(v * PosScale);

        public static int QuantizeVel(float v) => Round(v * VelScale);

        public static uint QuantizeRadius(float v)
        {
            if (v <= 0f) return 0;
            double q = v * RadiusScale + 0.5;
            return q > 65535 ? 65535u : (uint)q;
        }

        public static uint QuantizeAim(float rad)
        {
            double r = rad;
            while (r < 0) r += TwoPi;
            while (r >= TwoPi) r -= TwoPi;

            double v = r * AimScale + 0.5;
            return v >= 65536 ? 0u : (uint)v;
        }

        public static float DequantizePos(int q) => (float)(q / PosScale);

        public static float DequantizeVel(int q) => (float)(q / VelScale);

        public static float DequantizeAim(uint q) => (float)(q / AimScale);

        public static float DequantizeAimDegrees(uint q) => DequantizeAim(q) * Mathf.Rad2Deg;

        public static float AimToYawDegrees(uint q)
        {
            float yaw = 90f - DequantizeAimDegrees(q);
            while (yaw > 180f) yaw -= 360f;
            while (yaw <= -180f) yaw += 360f;
            return yaw;
        }

        static int Round(double q)
        {
            if (q > Clamp) q = Clamp;
            else if (q < -Clamp) q = -Clamp;
            return q < 0 ? (int)(q - 0.5) : (int)(q + 0.5);
        }

        public static Game.Proto.Backend.Envelope BuildHello(string playerName, string token)
        {
            return new Game.Proto.Backend.Envelope
            {
                Hello = new Game.Proto.Backend.Hello
                {
                    ProtocolVersion = ProtocolVersion,
                    PlayerName = playerName ?? string.Empty,
                    Token = token ?? string.Empty,
                },
            };
        }

        public static Game.Proto.Backend.Envelope BuildJoinRoom(string roomId)
        {
            return new Game.Proto.Backend.Envelope
            {
                JoinRoom = new Game.Proto.Backend.JoinRoom
                {
                    RoomId = roomId ?? string.Empty,
                },
            };
        }

        public static Game.Proto.Backend.Envelope BuildPing(ulong clientTimeNs)
        {
            return new Game.Proto.Backend.Envelope
            {
                Ping = new Game.Proto.Backend.Ping { ClientTimeNs = clientTimeNs },
            };
        }

        public static Game.Proto.Backend.InputFrame BuildInputFrame(InputSnapshot input, uint tick)
        {
            Vector2 move = input.move;
            if (move.sqrMagnitude > 1f) move.Normalize();

            float yawRad = input.yaw * Mathf.Deg2Rad;

            uint buttons = 0;
            if (input.firePressed) buttons |= ButtonAttack;
            if (input.interactTargetId != 0) buttons |= ButtonInteract;

            return new Game.Proto.Backend.InputFrame
            {
                Tick = tick,
                MoveX = (int)(move.x * MaxInputMagnitude),
                MoveY = (int)(move.y * MaxInputMagnitude),
                AimX = (int)(Mathf.Sin(yawRad) * MaxInputMagnitude),
                AimY = (int)(Mathf.Cos(yawRad) * MaxInputMagnitude),
                Buttons = buttons,
            };
        }

        public static Game.Proto.Backend.Envelope BuildInput(InputSnapshot input, uint tick)
        {
            return BuildInput(new List<(uint, InputSnapshot)> { (tick, input) }, tick);
        }

        public static Game.Proto.Backend.Envelope BuildInput(
            List<(uint Tick, InputSnapshot Input)> frames, uint clientTick)
        {
            var env = new Game.Proto.Backend.Envelope
            {
                Input = new Game.Proto.Backend.Input { ClientTick = clientTick },
            };

            for (int i = 0; i < frames.Count; i++)
                env.Input.Frames.Add(BuildInputFrame(frames[i].Input, frames[i].Tick));

            return env;
        }
    }
}