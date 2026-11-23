using System.Collections.Generic;
using Game.Net;
using Game.Player;
using Game.Proto;
using Game.Snapshot;
using Game.UI;
using Google.Protobuf;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Interaction
{
    public class AltarRevive : MonoBehaviour
    {
        [SerializeField] uint altarId;
        [SerializeField] float holdTime = 2f;
        [SerializeField] float interactRadius = 2.5f;
        [SerializeField] float serverTimeout = 3f;
        [SerializeField] Image progressRing;
        [SerializeField] ParticleSystem reviveFx;
        [SerializeField] Color guideColor = new(0.30f, 0.72f, 1f, 1f);
        [SerializeField] Color interruptColor = new(0.90f, 0.28f, 0.26f, 1f);

        // 全队任意客户端都能按 altarId 找到祭坛，用于全队引导环同步
        static readonly Dictionary<uint, AltarRevive> registry = new();

        // 与背包引导共用「计时 + 受击打断」状态机
        readonly BackpackUI.HoldGuide guide = new();

        IntentSender sender;
        Transform localPlayer;
        bool near;
        bool awaitingServer;
        float waitTimer;
        float serverProgress = -1f;
        float serverProgressTimer;

        public uint AltarId => altarId;

        public static AltarRevive Find(uint id)
            => registry.TryGetValue(id, out var altar) ? altar : null;

        void Awake()
        {
            guide.HoldSeconds = holdTime;
            if (altarId != 0) registry[altarId] = this;
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CReviveProgress>(OnProgress);
            MsgDispatcher.Register<S2CRevived>(OnRevived);
            MsgDispatcher.Register<S2CReviveInterrupted>(OnInterrupted);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CReviveProgress>();
            MsgDispatcher.Unregister<S2CRevived>();
            MsgDispatcher.Unregister<S2CReviveInterrupted>();

            if (altarId != 0 && registry.TryGetValue(altarId, out var altar) && altar == this)
                registry.Remove(altarId);

            if (sender != null) sender.SetInteraction(0, 0f);
        }

        void Start()
        {
            sender = FindObjectOfType<IntentSender>();

            var controller = FindObjectOfType<PlayerController>();
            if (controller != null) localPlayer = controller.transform;
        }

        void Update()
        {
            UpdateNear();
            UpdateLocalGuide();
            TickServerProgress();
            RefreshRing();
        }

        // 鬼魂的 CharacterController 被禁用，触发器事件不会触发，改用距离判定
        void UpdateNear()
        {
            if (localPlayer == null)
            {
                near = false;
                return;
            }

            Vector3 delta = localPlayer.position - transform.position;
            delta.y = 0f;
            near = delta.sqrMagnitude <= interactRadius * interactRadius;
        }

        void UpdateLocalGuide()
        {
            bool isGhost = DeathGhostTransition.IsGhost;
            bool holding = near && isGhost && !awaitingServer && Input.GetKey(KeyCode.E);

            if (guide.Tick(holding, Time.deltaTime))
                SendRevive();

            if (awaitingServer)
            {
                waitTimer += Time.deltaTime;
                if (waitTimer >= serverTimeout)
                {
                    awaitingServer = false;
                    waitTimer = 0f;
                    guide.Reset();
                }
            }

            // 交互意图上行：服务器据此推进自己的引导状态机并判定打断
            if (sender != null)
                sender.SetInteraction(holding ? altarId : 0, guide.Progress);
        }

        void SendRevive()
        {
            awaitingServer = true;
            waitTimer = 0f;

            var net = KcpNetClient.Instance;
            if (net == null)
            {
                awaitingServer = false;
                guide.Reset();
                return;
            }

            net.SendEnvelope(new Envelope
            {
                MsgId = MsgId.C2SRevive,
                Payload = ByteString.CopyFrom(new C2SRevive { AltarId = altarId }.ToByteArray()),
            });
        }

        void TickServerProgress()
        {
            if (serverProgress < 0f) return;

            serverProgressTimer -= Time.deltaTime;
            if (serverProgressTimer <= 0f) serverProgress = -1f;
        }

        void RefreshRing()
        {
            if (progressRing == null) return;

            // 服务器进度优先，本地进度只作灰盒兜底
            float target = serverProgress >= 0f ? serverProgress : guide.Progress;
            bool active = serverProgress >= 0f
                || (near && DeathGhostTransition.IsGhost && guide.Progress > 0f);

            progressRing.gameObject.SetActive(active);
            if (!active) return;

            progressRing.fillAmount = Mathf.Lerp(progressRing.fillAmount, target, Time.deltaTime * 10f);
            progressRing.color = guide.Interrupted ? interruptColor : guideColor;
        }

        // 任意玩家在任意祭坛引导，所有客户端都能收到 → 全队可见
        void OnProgress(S2CReviveProgress msg)
        {
            if (msg == null || msg.AltarId != altarId) return;

            serverProgress = Mathf.Clamp01(msg.Progress);
            serverProgressTimer = 0.5f;
        }

        void OnRevived(S2CRevived msg)
        {
            if (msg == null || msg.AltarId != altarId) return;

            serverProgress = -1f;

            if (msg.EntityId == LocalEntityId())
            {
                awaitingServer = false;
                waitTimer = 0f;
                guide.Reset();
            }

            PlayReviveFx(msg.EntityId);
        }

        void OnInterrupted(S2CReviveInterrupted msg)
        {
            if (msg == null || msg.AltarId != altarId) return;

            serverProgress = 0f;
            serverProgressTimer = 0.5f;

            if (msg.EntityId == LocalEntityId())
            {
                awaitingServer = false;
                waitTimer = 0f;
            }

            guide.Break();
        }

        void PlayReviveFx(ulong entityId)
        {
            if (reviveFx == null) return;

            Vector3 position = transform.position;
            if (entityId == LocalEntityId())
            {
                if (localPlayer != null) position = localPlayer.position;
            }
            else if (SnapshotDirector.Instance != null
                     && SnapshotDirector.Instance.TryGetEntityPosition(entityId, out var entityPos))
            {
                position = entityPos;
            }

            var fx = Instantiate(reviveFx, position, Quaternion.identity);
            fx.Play();

            float life = fx.main.duration + fx.main.startLifetime.constantMax;
            Destroy(fx.gameObject, life);
        }

        ulong LocalEntityId()
            => GameRoot.Instance != null ? GameRoot.Instance.LocalEntityId : 0;
    }
}