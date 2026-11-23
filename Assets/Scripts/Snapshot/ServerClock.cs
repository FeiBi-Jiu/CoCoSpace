using UnityEngine;

namespace Game.Snapshot
{
    public static class ServerClock
    {
        static bool initialized;
        static double offset;
        static double renderTime;

        public static bool IsReady => initialized;

        public static double ServerTimeNow => Time.unscaledTimeAsDouble + offset;

        public static double RenderTime => renderTime;

        public static void Observe(double serverTime, double localArrival)
        {
            double sample = serverTime - localArrival;

            if (!initialized)
            {
                offset = sample;
                renderTime = serverTime;
                initialized = true;
                return;
            }

            double diff = sample - offset;
            offset += diff > 0 ? diff * 0.05 : diff * 0.005;
        }

        public static void Tick(float deltaTime)
        {
            if (!initialized) return;

            double error = ServerTimeNow - renderTime;

            if (Mathf.Abs((float)error) > 0.5f)
            {
                renderTime = ServerTimeNow;
                return;
            }

            renderTime += deltaTime * (1f + Mathf.Clamp((float)error * 2f, -0.2f, 0.2f));
        }

        public static void Reset()
        {
            initialized = false;
            offset = 0;
            renderTime = 0;
        }
    }
}