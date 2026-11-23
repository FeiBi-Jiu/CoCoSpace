using System;
using UnityEngine;

namespace Game.Player
{
    public class ServerAuthoritativeMovement : IMovementAuthority
    {
        const float SnapDistance = 2f;
        const float ReconcileRate = 12f;

        readonly CharacterController cc;
        readonly LocalAuthoritativeMovement predictor;

        Vector3 authorityPosition;
        bool hasAuthority;

        public event Action<Vector3, double> OnServerCorrection;

        public Vector3 Position => predictor.Position;
        public float CurrentSpeed => predictor.CurrentSpeed;

        public ServerAuthoritativeMovement(CharacterController cc, MovementSettings settings)
        {
            this.cc = cc;
            predictor = new LocalAuthoritativeMovement(cc, settings);
        }

        public void Tick(float dt, Vector2 inputDir)
        {
            predictor.Tick(dt, inputDir);

            if (!hasAuthority) return;

            Vector3 error = authorityPosition - cc.transform.position;
            error.y = 0f;

            float dist = error.magnitude;
            if (dist < 0.02f) return;

            if (dist >= SnapDistance)
            {
                Warp(authorityPosition);
                OnServerCorrection?.Invoke(authorityPosition, Time.unscaledTimeAsDouble);
                return;
            }

            cc.Move(error * Mathf.Min(1f, ReconcileRate * dt / dist));
        }

        public void ApplyServerState(Vector3 position, double serverTime)
        {
            authorityPosition = position;
            hasAuthority = true;
        }

        void Warp(Vector3 position)
        {
            cc.enabled = false;
            cc.transform.position = position;
            cc.enabled = true;
            predictor.ResetVelocity();
        }
    }
}