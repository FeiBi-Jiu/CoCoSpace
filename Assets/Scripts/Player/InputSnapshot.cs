using UnityEngine;

namespace Game.Player
{
    public struct InputSnapshot
    {
        public Vector3 pos;
        public Vector2 move;
        public float yaw;
        public bool firePressed;
        public uint skillId;
        public uint interactTargetId;
        public float interactProgress;
    }
}