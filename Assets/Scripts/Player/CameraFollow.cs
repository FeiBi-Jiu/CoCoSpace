using UnityEngine;

namespace Game.Player
{
    public class CameraFollow : MonoBehaviour
    {
        [SerializeField] Transform target;
        [SerializeField] Vector3 offset = new(0f, 6f, -6f);
        [SerializeField] Vector3 lookOffset = new(0f, 0.5f, 0f);
        [SerializeField] float smoothTime = 0.15f;

        Vector3 velocity;

        void Awake()
        {
            if (target != null) return;
            var player = FindObjectOfType<PlayerController>();
            if (player != null) target = player.transform;
        }

        void LateUpdate()
        {
            if (target == null) return;

            Vector3 goal = target.position + offset;
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref velocity, smoothTime);
            transform.LookAt(target.position + lookOffset);
        }
    }
}