using Godot;

namespace Kuros.Effects
{
    /// <summary>
    /// 迪斯科球风格闪光射线——放射状彩色射线，自动播放动画后销毁。
    /// 由 DiscoFlashStunEffect 触发时实例化。
    /// </summary>
    [GlobalClass]
    public partial class DiscoFlashRays : Node2D
    {
        [Export(PropertyHint.Range, "50,500,10")]
        public float RayLength { get; set; } = 120f;

        [Export(PropertyHint.Range, "5,36,1")]
        public int RayCount { get; set; } = 17;

        [Export(PropertyHint.Range, "2,8,0.5")]
        public float RayWidth { get; set; } = 3f;

        [Export(PropertyHint.Range, "0.1,1,0.05")]
        public float Duration { get; set; } = 0.45f;

        [Export(PropertyHint.Range, "0.5,10,0.1")]
        public float ExpandScale { get; set; } = 1.4f;

        [Export(PropertyHint.Range, "30,3600,10")]
        public float RotationSweep { get; set; } = 45f;

        /// <summary>
        /// 在 AddChild 前由外部设置，控制射线是否 X 轴翻转。
        /// </summary>
        public bool FlipX { get; set; }

        public override void _Ready()
        {
            for (int i = 0; i < RayCount; i++)
            {
                float angle = (i / (float)RayCount) * Mathf.Tau;
                float hue = i / (float)RayCount;

                var ray = new Line2D
                {
                    Width = RayWidth,
                    DefaultColor = Color.FromHsv(hue, 0.9f, 1f, 0.9f)
                };
                ray.AddPoint(Vector2.Zero);
                ray.AddPoint(Vector2.Right * RayLength);
                ray.Rotation = angle;
                AddChild(ray);
            }

            float cx = RayLength * 0.03f;

            var centerGlow = new Line2D
            {
                Width = RayWidth * 2.5f,
                DefaultColor = new Color(1f, 1f, 1f, 0.95f)
            };
            centerGlow.AddPoint(Vector2.Left * cx);
            centerGlow.AddPoint(Vector2.Right * cx);
            AddChild(centerGlow);

            var centerGlowV = new Line2D
            {
                Width = RayWidth * 2.5f,
                DefaultColor = new Color(1f, 1f, 1f, 0.95f)
            };
            centerGlowV.AddPoint(Vector2.Up * cx);
            centerGlowV.AddPoint(Vector2.Down * cx);
            AddChild(centerGlowV);

            float dirX = FlipX ? -1f : 1f;
            Scale = new Vector2(dirX, 1f);

            var tween = CreateTween();
            tween.SetParallel(true);
            tween.TweenProperty(this, "scale", new Vector2(dirX * ExpandScale, ExpandScale), Duration * 0.5f);
            tween.TweenProperty(this, "rotation", dirX * Mathf.DegToRad(RotationSweep), Duration);
            tween.TweenProperty(this, "modulate:a", 0f, Duration);
            tween.SetParallel(false);
            tween.TweenCallback(Callable.From(QueueFree));
        }
    }
}
