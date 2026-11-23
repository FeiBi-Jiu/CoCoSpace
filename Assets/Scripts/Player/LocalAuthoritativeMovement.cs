using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Player
{
    // 本地移动预测：与后端 systems/movement.go 对齐。
    //
    // 后端每 tick 把意图换算成速度（MoveIntent*PlayerSpeed），再按 60Hz 子步
    // 积分，子步内 X/Y 分轴判定地形碰撞——撞墙的那根轴停下、另一根轴继续走，
    // 即贴墙滑动。这里用固定 1/60 子步 + 两次 CharacterController.Move（先 x
    // 后 z）复现同一顺序，保证同样的输入得到同样的位移与滑墙行为。
    public class LocalAuthoritativeMovement : IMovementAuthority
    {
        const float SubStepDt = 1f / 60f;
        const float MaxSubStepBurst = SubStepDt * 8f;
        const float MaxReplayStepDt = 0.2f;
        const float CorrectionLogDistance = 0.05f;

        readonly CharacterController cc;
        readonly MovementSettings settings;

        Vector3 horizontalVelocity;
        float verticalVelocity;
        float currentSpeed;
        float accumulator;

        public event Action<Vector3, double> OnServerCorrection;

        public Vector3 Position => cc.transform.position;
        public float CurrentSpeed => currentSpeed;

        public LocalAuthoritativeMovement(CharacterController cc, MovementSettings settings = null)
        {
            this.cc = cc;
            this.settings = settings ?? new MovementSettings();
        }

        public void Tick(float dt, Vector2 inputDir)
        {
            if (dt <= 0f) return;

            // 后端 DecodeMove：长度最多为 1（短推保留比例），零输入即零速度，
            // 没有加速/减速惯性。这里保持同一语义，避免预测速度领先于服务器。
            Vector2 move = inputDir;
            float magnitude = move.magnitude;
            if (magnitude > 1f) move /= magnitude;

            horizontalVelocity = new Vector3(move.x, 0f, move.y) * settings.speed;
            currentSpeed = horizontalVelocity.magnitude;

            if (settings.gravity == 0f)
            {
                verticalVelocity = 0f;
            }
            else
            {
                if (cc.isGrounded && verticalVelocity < 0f)
                    verticalVelocity = -2f;
                verticalVelocity += settings.gravity * dt;
            }

            accumulator += dt;
            if (accumulator > MaxSubStepBurst) accumulator = MaxSubStepBurst;

            while (accumulator >= SubStepDt)
            {
                accumulator -= SubStepDt;
                Step(SubStepDt);
            }
        }

        // 子步积分：先 x 后 z，各自独立 Move，撞墙只停一根轴。
        void Step(float dt)
        {
            Vector3 delta = horizontalVelocity * dt;
            if (delta.x != 0f) cc.Move(new Vector3(delta.x, 0f, 0f));
            if (delta.z != 0f) cc.Move(new Vector3(0f, 0f, delta.z));
            if (verticalVelocity != 0f) cc.Move(new Vector3(0f, verticalVelocity * dt, 0f));
        }

        // 服务器确认（AckedInputTick）后校正本地预测：
        // 1. 回到服务器权威位置（该 tick 已处理完的输入结果）；
        // 2. 按固定 tick 时长回放服务器尚未确认的输入帧，得到本地应处的位置。
        //
        // 回放位移 = 速度 × 经过时间，与连续预测的总位移一致；区别只在碰撞
        // 采样点，与后端同为 60Hz 子步，所以残余误差只有位置量化（1/32 m）。
        public void Reconcile(
            Vector3 serverPosition,
            uint ackedTick,
            IReadOnlyList<(uint Tick, InputSnapshot Input)> frames,
            uint tickRate)
        {
            if (tickRate == 0) tickRate = 20;
            float tickDt = 1f / tickRate;

            Vector3 before = cc.transform.position;
            before.y = 0f;

            Warp(serverPosition);

            uint last = ackedTick;
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                if (!IsAfter(frame.Tick, ackedTick)) continue;

                float dt = (int)(frame.Tick - last) * tickDt;
                if (dt <= 0f) dt = tickDt;
                if (dt > MaxReplayStepDt) dt = MaxReplayStepDt;

                ReplayStep(dt, frame.Input.move);
                last = frame.Tick;
            }

            verticalVelocity = 0f;
            accumulator = 0f;
            currentSpeed = 0f;

            Vector3 after = cc.transform.position;
            after.y = 0f;

            if ((after - before).sqrMagnitude >= CorrectionLogDistance * CorrectionLogDistance)
                OnServerCorrection?.Invoke(after, Time.unscaledTimeAsDouble);
        }

        void ReplayStep(float dt, Vector2 inputDir)
        {
            Vector2 move = inputDir;
            float magnitude = move.magnitude;
            if (magnitude > 1f) move /= magnitude;

            Vector3 delta = new Vector3(move.x, 0f, move.y) * (settings.speed * dt);
            if (delta.x != 0f) cc.Move(new Vector3(delta.x, 0f, 0f));
            if (delta.z != 0f) cc.Move(new Vector3(0f, 0f, delta.z));
        }

        void Warp(Vector3 position)
        {
            cc.enabled = false;
            cc.transform.position = new Vector3(position.x, cc.transform.position.y, position.z);
            cc.enabled = true;
            horizontalVelocity = Vector3.zero;
            currentSpeed = 0f;
        }

        public void ResetVelocity()
        {
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            currentSpeed = 0f;
            accumulator = 0f;
        }

        static bool IsAfter(uint a, uint b)
        {
            return (int)(a - b) > 0;
        }
    }
}