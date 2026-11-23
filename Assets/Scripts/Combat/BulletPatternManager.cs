using Game.Table;
using UnityEngine;

namespace Game.Combat
{
    public static class BulletPatternManager
    {
        public static int ActiveCount => BulletPatternPlayer.Active.Count;

        public static BulletPatternPlayer SpawnPattern(string patternId, ulong emitterEntityId)
        {
            return SpawnPattern(patternId, emitterEntityId, null);
        }

        public static BulletPatternPlayer SpawnPattern(string patternId, ulong emitterEntityId, BulletPatternDef patternDef)
        {
            var def = patternDef ?? BulletPatternTable.Get(patternId);
            if (def == null)
            {
                Debug.LogWarning($"[Pattern] 找不到配置 pattern_id={patternId}");
                return null;
            }

            if (BulletPool.Instance == null)
            {
                Debug.LogWarning("[Pattern] BulletPool 尚未初始化，子弹不会显示");
            }

            return BulletPatternPlayer.Create(patternId, emitterEntityId, def);
        }

        public static void StopPattern(string patternId) => BulletPatternPlayer.Stop(patternId);

        public static void StopAll()
        {
            BulletPatternPlayer.StopAll();

            if (BulletPool.Instance != null)
                BulletPool.Instance.RecycleAll();
        }
    }
}