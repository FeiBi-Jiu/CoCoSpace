using System;
using Game.Combat;
using Game.Net;
using UnityEngine;

namespace Game.Player
{
    public enum MovementMode
    {
        Local,
        Server,
    }

    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [SerializeField] MovementMode mode = MovementMode.Local;
        [SerializeField] MovementSettings settings = new();
        [SerializeField] uint defaultSkillId = 1;
        [SerializeField] Animator animator;
        [SerializeField] FirePrediction firePrediction;

        IMovementAuthority movement;
        IntentSender sender;
        Camera mainCam;
        Animator anim;
        Vector2 facingDir = Vector2.down;

        public IMovementAuthority Movement => movement;
        public MovementSettings Settings => settings;

        void Awake()
        {
            settings ??= new MovementSettings();

            var cc = GetComponent<CharacterController>();
            movement = mode == MovementMode.Server
                ? new ServerAuthoritativeMovement(cc, settings)
                : new LocalAuthoritativeMovement(cc, settings);
            movement.OnServerCorrection += OnServerCorrection;

            anim = animator != null ? animator : GetComponentInChildren<Animator>();
            sender = FindObjectOfType<IntentSender>();
            if (firePrediction == null) firePrediction = GetComponent<FirePrediction>();
            if (firePrediction != null) firePrediction.SetWeapon(defaultSkillId);
            mainCam = Camera.main;

            if (sender == null)
                Debug.LogError("[Player] 场景中缺少 IntentSender，输入无法上报");
        }

        void OnDestroy()
        {
            movement.OnServerCorrection -= OnServerCorrection;
            MsgDispatcher.Unregister<Game.Proto.S2C_Snapshot>(OnSnapshot);
        }

        void OnEnable()
        {
            MsgDispatcher.Register<Game.Proto.S2C_Snapshot>(OnSnapshot);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<Game.Proto.S2C_Snapshot>(OnSnapshot);
        }

        // 把后端权威位置喂给移动权威层：服务端模式下做平滑纠偏，
        // 本地模式下用 AckedInputTick + 未确认输入回放做预测校正。
        void OnSnapshot(Game.Proto.S2C_Snapshot msg)
        {
            if (msg == null || GameRoot.Instance == null) return;
            if (!(movement is ServerAuthoritativeMovement) && !(movement is LocalAuthoritativeMovement))
                return;

            ulong selfId = GameRoot.Instance.LocalEntityId;
            if (selfId == 0) return;

            foreach (var e in msg.Entities)
            {
                if (e == null || e.EntityId != selfId) continue;

                Vector3 pos = new(e.X, transform.position.y, e.Z);

                if (movement is ServerAuthoritativeMovement server)
                {
                    server.ApplyServerState(pos, msg.ServerTime);
                }
                else if (movement is LocalAuthoritativeMovement local && sender != null)
                {
                    var net = KcpNetClient.Instance;
                    uint acked = net != null ? net.AckedInputTick : 0;
                    uint tickRate = net != null ? net.TickRate : 20;

                    local.Reconcile(pos, acked, sender.UnackedFrames, tickRate);
                    sender.Acknowledge(acked);
                }

                return;
            }
        }

        void Update()
        {
            if (sender == null) return;
            if (movement == null) return;

            Vector2 dir = new(
                Input.GetAxisRaw("Horizontal"),
                Input.GetAxisRaw("Vertical"));
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            if (dir.sqrMagnitude > 0.01f) facingDir = dir;

            // 后端 Integrate 只移动 PlayerAlive 的玩家，鬼魂速度恒为零。
            // 本地预测同样不移动，否则每帧都会与服务器位置对不上。
            if (DeathGhostTransition.IsGhost) dir = Vector2.zero;

            movement.Tick(Time.deltaTime, dir);
            UpdateAnimator();

            sender.LatestInput.pos = movement.Position;
            sender.LatestInput.move = dir;
            sender.LatestInput.yaw = AimYaw();
            sender.LatestInput.skillId = defaultSkillId;

            bool firing = Input.GetMouseButton(0) && !DeathGhostTransition.IsGhost;
            sender.LatestInput.firePressed = firing;
            if (firePrediction != null) firePrediction.SetAttackHeld(firing);
        }

        void UpdateAnimator()
        {
            if (anim == null) return;
            anim.SetFloat("Horizontal", facingDir.x);
            anim.SetFloat("Vertical", facingDir.y);
            anim.SetFloat("moveSpeed", movement.CurrentSpeed);
        }

        float AimYaw()
        {
            if (mainCam == null) return transform.eulerAngles.y;

            var ground = new Plane(Vector3.up, new Vector3(0f, transform.position.y, 0f));
            Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
            if (!ground.Raycast(ray, out float dist)) return mainCam.transform.eulerAngles.y;

            Vector3 aim = ray.GetPoint(dist);
            Vector3 dir = aim - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return mainCam.transform.eulerAngles.y;

            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }

        void OnServerCorrection(Vector3 position, double serverTime)
        {
            Debug.Log($"[Player] 服务器纠偏 → ({position.x:F2},{position.z:F2}) t={serverTime:F2}");
        }
    }
}