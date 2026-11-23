using System.Collections.Generic;
using Game.Net;
using Game.Proto;
using UnityEngine;

namespace Game.Snapshot
{
    public class SnapshotDirector : MonoBehaviour
    {
        public static SnapshotDirector Instance { get; private set; }

        [SerializeField] RemoteEntityView entityPrefab;
        [SerializeField] Transform entityRoot;
        [SerializeField] int despawnFrames = 15;

        readonly Dictionary<ulong, RemoteEntityView> views = new();
        readonly Dictionary<ulong, int> missing = new();
        readonly HashSet<ulong> seen = new();
        readonly List<ulong> stale = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (Instance != null) return;
            var go = new GameObject("SnapshotDirector");
            go.AddComponent<SnapshotDirector>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (transform.parent == null) DontDestroyOnLoad(gameObject);
        }

        void Start() => MsgDispatcher.Register<S2C_Snapshot>(OnSnapshot);

        void OnDestroy()
        {
            MsgDispatcher.Unregister<S2C_Snapshot>();
            if (Instance == this) Instance = null;
        }

        void Update() => ServerClock.Tick(Time.unscaledDeltaTime);

        void OnSnapshot(S2C_Snapshot msg)
        {
            ServerClock.Observe(msg.ServerTime, Time.unscaledTimeAsDouble);

            ulong selfId = GameRoot.Instance != null ? GameRoot.Instance.LocalEntityId : 0;

            seen.Clear();
            foreach (var e in msg.Entities) seen.Add(e.EntityId);

            foreach (var e in msg.Entities)
            {
                if (e.EntityId == selfId) continue;

                // 子弹由客户端本地预测（FirePrediction + BulletPool）表现，
                // 后端快照里的子弹只用于判定，重复渲染会出现双份弹道
                if (e.Type == EntityType.EntityBullet) continue;

                if (!views.TryGetValue(e.EntityId, out var view) || view == null)
                {
                    view = Spawn(e);
                    views[e.EntityId] = view;
                }

                missing.Remove(e.EntityId);
                view.OnSnapshot(msg.ServerTime, e);
            }

            stale.Clear();
            foreach (var pair in views)
            {
                if (seen.Contains(pair.Key)) continue;

                missing.TryGetValue(pair.Key, out int n);
                n++;
                if (n >= despawnFrames) stale.Add(pair.Key);
                else missing[pair.Key] = n;
            }

            foreach (var id in stale) Despawn(id);
        }

        public void ResetFromSnapshot(S2C_Snapshot msg)
        {
            foreach (var view in views.Values)
                if (view != null) Destroy(view.gameObject);

            views.Clear();
            missing.Clear();
            ServerClock.Reset();
            OnSnapshot(msg);
        }

        public bool TryGetEntityPosition(ulong entityId, out Vector3 position)
        {
            if (views.TryGetValue(entityId, out var view) && view != null)
            {
                position = view.transform.position;
                return true;
            }

            position = Vector3.zero;
            return false;
        }

        public Transform GetEntityTransform(ulong entityId)
        {
            return views.TryGetValue(entityId, out var view) && view != null ? view.transform : null;
        }

        public bool TryGetEntityTransform(ulong entityId, out Vector3 position, out Quaternion rotation)
        {
            if (views.TryGetValue(entityId, out var view) && view != null)
            {
                position = view.transform.position;
                rotation = view.transform.rotation;
                return true;
            }

            position = Vector3.zero;
            rotation = Quaternion.identity;
            return false;
        }

        RemoteEntityView Spawn(Entity e)
        {
            RemoteEntityView view = entityPrefab != null
                ? Instantiate(entityPrefab, entityRoot)
                : CreateFallbackView();

            view.name = $"Remote_{e.Type}_{e.EntityId}";
            view.Init(e.EntityId, e.Type);
            return view;
        }

        RemoteEntityView CreateFallbackView()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            if (entityRoot != null) go.transform.SetParent(entityRoot, false);

            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.localScale = new Vector3(0.8f, 1.6f, 0.8f);
            return go.AddComponent<RemoteEntityView>();
        }

        void Despawn(ulong id)
        {
            if (views.TryGetValue(id, out var view) && view != null)
                Destroy(view.gameObject);

            views.Remove(id);
            missing.Remove(id);
        }
    }
}