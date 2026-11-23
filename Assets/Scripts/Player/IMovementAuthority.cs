using System;
using UnityEngine;

namespace Game.Player
{
    [Serializable]
    public class MovementSettings
    {
        public float speed = 6f;
        public float gravity = -20f;
        public float acceleration = 40f;
        public float deceleration = 60f;
    }

    public interface IMovementAuthority
    {
        void Tick(float dt, Vector2 inputDir);
        Vector3 Position { get; }
        float CurrentSpeed { get; }
        // 预留：升级为服务器权威时实现纠偏回调
        event Action<Vector3, double> OnServerCorrection;
    }
}