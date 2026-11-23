using System.Collections.Generic;
using Game.Player;
using Game.Snapshot;
using UnityEngine;

namespace Game.Net
{
    public class IntentSender : MonoBehaviour
    {
        const int InputWindow = 3;
        const int MaxUnackedFrames = 256;

        [SerializeField] float sendHz = 20f;
        [SerializeField] bool logSends;

        uint localTick;
        uint lastSentTick;
        bool hasSent;
        float sendAccumulator;

        readonly List<(uint Tick, InputSnapshot Input)> frames = new(InputWindow);
        readonly List<(uint Tick, InputSnapshot Input)> unacked = new(MaxUnackedFrames);

        public InputSnapshot LatestInput;

        public uint LastIntentSeq => localTick;
        public int SentCount { get; private set; }

        // 已发送但还没被服务器确认的输入帧，供本地预测回放使用
        public IReadOnlyList<(uint Tick, InputSnapshot Input)> UnackedFrames => unacked;

        public void Acknowledge(uint ackedTick)
        {
            if (ackedTick == 0) return;

            int remove = 0;
            while (remove < unacked.Count && !IsAfter(unacked[remove].Tick, ackedTick))
                remove++;

            if (remove > 0) unacked.RemoveRange(0, remove);
        }

        static bool IsAfter(uint a, uint b)
        {
            return (int)(a - b) > 0;
        }

        public void SendLogin(string roomId, string playerName)
        {
            var net = KcpNetClient.Instance;
            if (net == null)
            {
                Debug.LogError("[Intent] KcpNetClient 不存在，登录无法发送");
                return;
            }

            net.Login(roomId, playerName);
        }

        public void SendReconnect(string token)
        {
            var net = KcpNetClient.Instance;
            if (net == null) return;

            net.ReconnectWithToken(token);
        }

        public void SetInteraction(uint targetId, float progress)
        {
            LatestInput.interactTargetId = targetId;
            LatestInput.interactProgress = Mathf.Clamp01(progress);
        }

        void Update()
        {
            float interval = 1f / Mathf.Max(1f, sendHz);
            sendAccumulator += Time.deltaTime;
            if (sendAccumulator < interval) return;

            var net = KcpNetClient.Instance;
            if (net == null || !net.IsConnected ||
                GameRoot.Instance == null || GameRoot.Instance.LocalEntityId == 0)
            {
                sendAccumulator = 0f;
                LatestInput.firePressed = false;
                frames.Clear();
                unacked.Clear();
                return;
            }
            sendAccumulator -= interval;

            var input = LatestInput;
            LatestInput.firePressed = false;

            uint tick = NextTick(net.TickRate);

            frames.Add((tick, input));
            if (frames.Count > InputWindow) frames.RemoveAt(0);

            unacked.Add((tick, input));
            if (unacked.Count > MaxUnackedFrames) unacked.RemoveAt(0);

            net.SendEnvelopeUnreliable(NetxProto.BuildInput(frames, tick));
            SentCount++;

            if (logSends)
            {
                Debug.Log($"[Intent] tick={tick} frames={frames.Count} " +
                          $"move=({input.move.x:F2},{input.move.y:F2}) yaw={input.yaw:F1} fire={input.firePressed}");
            }
        }

        uint NextTick(uint tickRate)
        {
            uint tick;
            if (ServerClock.IsReady && tickRate != 0)
            {
                tick = (uint)(ServerClock.ServerTimeNow * tickRate);
            }
            else
            {
                tick = ++localTick;
            }

            if (hasSent && (int)(tick - lastSentTick) <= 0)
                tick = lastSentTick + 1;

            lastSentTick = tick;
            hasSent = true;
            localTick = tick;
            return tick;
        }
    }
}