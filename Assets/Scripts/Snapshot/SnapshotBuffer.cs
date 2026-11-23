namespace Game.Snapshot
{
    public class SnapshotBuffer<T> where T : class
    {
        struct Snap
        {
            public double serverTime;
            public T payload;
        }

        public const int Capacity = 32;

        readonly Snap[] ring = new Snap[Capacity];
        int head;
        int count;

        public double interpDelay = 0.1;

        public int Count => count;

        int Index(int i) => (head + i) % Capacity;

        public void Push(double serverTime, T snap)
        {
            if (count > 0 && serverTime <= ring[Index(count - 1)].serverTime) return;

            if (count == Capacity)
            {
                ring[head].serverTime = serverTime;
                ring[head].payload = snap;
                head = (head + 1) % Capacity;
                return;
            }

            int tail = Index(count);
            ring[tail].serverTime = serverTime;
            ring[tail].payload = snap;
            count++;
        }

        public bool Sample(double renderTime, out T from, out T to, out float t)
        {
            from = null;
            to = null;
            t = 0f;
            if (count == 0) return false;

            int lo = 0;
            int hi = count - 1;
            int idx = -1;

            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (ring[Index(mid)].serverTime <= renderTime)
                {
                    idx = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }

            if (idx < 0)
            {
                from = to = ring[Index(0)].payload;
                return true;
            }

            if (idx >= count - 1)
            {
                from = to = ring[Index(count - 1)].payload;
                return true;
            }

            var a = ring[Index(idx)];
            var b = ring[Index(idx + 1)];
            from = a.payload;
            to = b.payload;

            double span = b.serverTime - a.serverTime;
            t = span > 0 ? (float)((renderTime - a.serverTime) / span) : 0f;
            return true;
        }

        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                ring[i].serverTime = 0;
                ring[i].payload = null;
            }

            head = 0;
            count = 0;
        }
    }
}