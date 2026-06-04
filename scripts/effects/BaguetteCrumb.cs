using Godot;

namespace Kuros.Effects
{
    /// <summary>
    /// 抛物线飞行碎块：从生成位置按参数化抛物线飞向玩家，同时自旋。
    /// 公式：lerp(start, target, t) - sin(t * PI) * PeakHeight
    /// 参考 EnemyWaiterAThrowProjectile。
    /// </summary>
    [GlobalClass]
    public partial class BaguetteCrumb : Node2D
    {
        [ExportCategory("Flight")]
        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float Duration { get; set; } = 0.8f;

        [Export(PropertyHint.Range, "50,800,10")]
        public float PeakHeight { get; set; } = 300f;

        [Export(PropertyHint.Range, "0,3600,10")]
        public float RotationDegreesPerSecond { get; set; } = 360f;

        [ExportCategory("Landing")]
        [Export(PropertyHint.Range, "0,30,0.5")]
        public float StayDuration { get; set; } = 3f;

        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _elapsed;
        private bool _launched;
        private bool _landed;
        private Vector2? _overrideTarget;

        public void SetTarget(Vector2 target)
        {
            _overrideTarget = target;
        }

        public override void _Ready()
        {
            if (_overrideTarget.HasValue)
            {
                _targetPos = _overrideTarget.Value;
            }
            else
            {
                var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                _targetPos = player?.GlobalPosition ?? GlobalPosition + new Vector2(200, 0);
            }

            SetPhysicsProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_landed) return;

            if (!_launched)
            {
                _startPos = GlobalPosition;
                _launched = true;
                return;
            }

            _elapsed += (float)delta;
            float t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);

            float upDown = Mathf.Sin(t * Mathf.Pi);
            float x = Mathf.Lerp(_startPos.X, _targetPos.X, t);
            float y = Mathf.Lerp(_startPos.Y, _targetPos.Y, t) - upDown * PeakHeight;
            GlobalPosition = new Vector2(x, y);
            RotationDegrees += RotationDegreesPerSecond * (float)delta;

            if (t >= 1f)
            {
                _landed = true;
                if (StayDuration > 0f)
                {
                    var timer = new Timer { OneShot = true, WaitTime = StayDuration };
                    timer.Timeout += () => { timer.QueueFree(); QueueFree(); };
                    AddChild(timer);
                    timer.Start();
                }
                else
                {
                    QueueFree();
                }
            }
        }
    }
}
