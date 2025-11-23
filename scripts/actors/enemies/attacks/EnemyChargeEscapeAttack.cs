using Godot;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺抓取攻击示例：玩家需在 2 秒内左右移动各 4 次才能逃脱。
    /// </summary>
    public partial class EnemyChargeEscapeAttack : EnemyChargeGrabAttack
    {
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float EscapeWindowSeconds = 2.0f;

        [Export(PropertyHint.Range, "1,20,1")]
        public int RequiredLeftInputs = 4;

        [Export(PropertyHint.Range, "1,20,1")]
        public int RequiredRightInputs = 4;

        private float _escapeTimer = 0f;
        private int _leftCount = 0;
        private int _rightCount = 0;
        private bool _trackingInputs = false;

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            StartTrackingInputs();
        }

        protected override bool EvaluateEscapeSequence(SamplePlayer player)
        {
            if (!_trackingInputs)
            {
                StartTrackingInputs();
            }

            _escapeTimer = EscapeWindowSeconds;
            _leftCount = 0;
            _rightCount = 0;

            while (_escapeTimer > 0)
            {
                UpdateEscapeTimer(GetPhysicsProcessDeltaTime());

                if (_leftCount >= RequiredLeftInputs && _rightCount >= RequiredRightInputs)
                {
                    StopTrackingInputs();
                    return true;
                }
            }

            StopTrackingInputs();
            return false;
        }

        private void StartTrackingInputs()
        {
            _trackingInputs = true;
            _escapeTimer = EscapeWindowSeconds;
            _leftCount = 0;
            _rightCount = 0;
        }

        private void StopTrackingInputs()
        {
            _trackingInputs = false;
        }

        private void UpdateEscapeTimer(double delta)
        {
            _escapeTimer -= (float)delta;

            if (Input.IsActionJustPressed("move_left"))
            {
                _leftCount++;
            }

            if (Input.IsActionJustPressed("move_right"))
            {
                _rightCount++;
            }
        }
    }
}

