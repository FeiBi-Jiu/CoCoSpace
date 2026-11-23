using System.Collections.Generic;
using UnityEngine;

namespace Game.World
{
    public class DropDemoSpawner : MonoBehaviour
    {
        [Header("演示掉落（用于灰盒验收，入局后自动停用）")]
        [SerializeField] bool spawnEnabled = true;
        [SerializeField] bool onlyBeforeLogin = true;
        [SerializeField] float interval = 4f;
        [SerializeField] float firstDelay = 1.5f;
        [SerializeField] float scatterRadius = 2.5f;
        [SerializeField] float lifetime = 10f;
        [SerializeField] int maxDrops = 6;

        class Entry
        {
            public DropView View;
            public float ExpireAt;
        }

        readonly List<Entry> spawned = new();

        float timer;
        uint nextDropId = 900001;
        int typeCursor;

        void Start()
        {
            timer = firstDelay;
        }

        void Update()
        {
            ExpireOld();

            timer -= Time.deltaTime;
            if (timer > 0f) return;
            timer = Mathf.Max(1f, interval);

            if (!spawnEnabled) return;
            if (onlyBeforeLogin && GameRoot.Instance != null && GameRoot.Instance.State == GameRoot.GameState.InGame) return;

            var manager = DropManager.Instance;
            if (manager == null) return;
            if (spawned.Count >= maxDrops) return;

            uint type = (uint)(typeCursor % 4 + 1);
            typeCursor++;

            Vector2 offset = Random.insideUnitCircle * scatterRadius;
            Vector3 pos = transform.position + new Vector3(offset.x, 0f, offset.y);

            var view = manager.Spawn(nextDropId++, type, pos, type == (uint)DropType.Potion);
            if (view == null) return;

            spawned.Add(new Entry { View = view, ExpireAt = Time.time + lifetime });
        }

        void ExpireOld()
        {
            for (int i = spawned.Count - 1; i >= 0; i--)
            {
                var entry = spawned[i];

                if (entry.View == null)
                {
                    spawned.RemoveAt(i);
                    continue;
                }

                if (Time.time < entry.ExpireAt) continue;

                entry.View.PlayDecay();
                spawned.RemoveAt(i);
            }
        }
    }
}