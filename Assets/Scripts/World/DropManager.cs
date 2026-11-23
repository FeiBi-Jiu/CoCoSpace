using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using Google.Protobuf;
using UnityEngine;

namespace Game.World
{
    public class DropManager : MonoBehaviour
    {
        [Header("消息 ID（后端 proto 落地后可替换为 MsgId 枚举）")]
        [SerializeField] int pickedMsgId = 2023;
        [SerializeField] int decayedMsgId = 2024;

        [Header("生成")]
        [SerializeField] Transform dropRoot;
        [SerializeField] float groundY = 0.05f;
        [SerializeField] uint defaultSkillElementId = 1;
        [SerializeField] bool logDrops = true;

        public static DropManager Instance { get; private set; }

        readonly Dictionary<uint, DropView> drops = new();
        readonly List<uint> staleIds = new();

        float nextCleanup;

        void Awake()
        {
            Instance = this;

            if (dropRoot != null) return;

            var existing = transform.Find("DropRoot");
            if (existing != null)
            {
                dropRoot = existing;
                return;
            }

            var root = new GameObject("DropRoot");
            root.transform.SetParent(transform, false);
            dropRoot = root.transform;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            MsgDispatcher.Register<S2CDropSpawn>(OnDropSpawn);
            MsgDispatcher.Register<S2CItemCarried>(OnItemCarried);
            MsgDispatcher.RegisterRaw((MsgId)pickedMsgId, OnDropPickedRaw);
            MsgDispatcher.RegisterRaw((MsgId)decayedMsgId, OnDropDecayedRaw);
        }

        void OnDisable()
        {
            MsgDispatcher.Unregister<S2CDropSpawn>(OnDropSpawn);
            MsgDispatcher.Unregister<S2CItemCarried>(OnItemCarried);
            MsgDispatcher.UnregisterRaw((MsgId)pickedMsgId);
            MsgDispatcher.UnregisterRaw((MsgId)decayedMsgId);
            ClearAll();
        }

        void Update()
        {
            if (drops.Count == 0) return;
            if (Time.time < nextCleanup) return;

            nextCleanup = Time.time + 0.5f;
            staleIds.Clear();

            foreach (var pair in drops)
            {
                if (pair.Value == null) staleIds.Add(pair.Key);
            }

            for (int i = 0; i < staleIds.Count; i++)
                drops.Remove(staleIds[i]);
        }

        public DropView Spawn(uint dropId, uint type, Vector3 position, bool ghostOnly)
        {
            Vector3 pos = new Vector3(position.x, groundY, position.z);

            if (drops.TryGetValue(dropId, out var existing) && existing != null)
            {
                existing.gameObject.SetActive(true);
                existing.transform.position = pos;
                existing.Setup(type, ghostOnly, defaultSkillElementId);
                return existing;
            }

            var go = new GameObject($"Drop_{dropId}");
            go.transform.SetParent(dropRoot != null ? dropRoot : transform, false);
            go.transform.position = pos;

            var view = go.AddComponent<DropView>();
            view.DropId = dropId;
            view.Setup(type, ghostOnly, defaultSkillElementId);
            drops[dropId] = view;

            if (logDrops)
                Debug.Log($"[Drop] spawn id={dropId} type={(DropType)type} ghostOnly={ghostOnly} pos={pos}");

            return view;
        }

        public void HandlePicked(uint dropId, ulong picker)
        {
            if (!drops.TryGetValue(dropId, out var view) || view == null)
            {
                drops.Remove(dropId);
                return;
            }

            drops.Remove(dropId);

            Transform pickerTransform = GameRoot.Instance != null
                ? GameRoot.Instance.GetEntityTransform(picker)
                : null;

            view.PlayPickup(pickerTransform);

            if (logDrops)
                Debug.Log($"[Drop] picked id={dropId} picker={picker}");
        }

        public void HandleDecayed(uint dropId)
        {
            if (drops.TryGetValue(dropId, out var view) && view != null)
                view.PlayDecay();

            drops.Remove(dropId);

            if (logDrops)
                Debug.Log($"[Drop] decayed id={dropId}");
        }

        public void ClearAll()
        {
            foreach (var pair in drops)
            {
                if (pair.Value != null) Destroy(pair.Value.gameObject);
            }

            drops.Clear();
        }

        void OnDropSpawn(S2CDropSpawn msg)
        {
            Spawn(msg.DropId, msg.Type, new Vector3(msg.X, 0f, msg.Z), msg.GhostOnly);
        }

        void OnItemCarried(S2CItemCarried msg)
        {
            uint dropId = (uint)msg.ItemId;

            if (!drops.TryGetValue(dropId, out var view) || view == null) return;

            bool carried = !msg.Dropped && msg.CarrierEntityId != 0;
            view.gameObject.SetActive(!carried);

            if (logDrops && carried)
                Debug.Log($"[Drop] carried id={dropId} by={msg.CarrierEntityId} type={msg.ItemType}");
        }

        void OnDropPickedRaw(ByteString payload)
        {
            if (!TryReadDropId(payload, out uint dropId, out ulong picker)) return;
            HandlePicked(dropId, picker);
        }

        void OnDropDecayedRaw(ByteString payload)
        {
            if (!TryReadDropId(payload, out uint dropId, out _)) return;
            HandleDecayed(dropId);
        }

        static bool TryReadDropId(ByteString payload, out uint dropId, out ulong picker)
        {
            dropId = 0;
            picker = 0;

            try
            {
                var input = payload.CreateCodedInput();
                uint tag;
                while ((tag = input.ReadTag()) != 0)
                {
                    switch (tag)
                    {
                        case 8:
                            dropId = input.ReadUInt32();
                            break;
                        case 16:
                            picker = input.ReadUInt64();
                            break;
                        default:
                            input.SkipLastField();
                            break;
                    }
                }
            }
            catch (System.Exception)
            {
                return false;
            }

            return dropId != 0;
        }
    }
}