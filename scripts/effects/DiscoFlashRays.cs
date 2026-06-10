using Godot;
using System.Collections.Generic;

namespace Kuros.Effects
{
    [GlobalClass]
    public partial class DiscoFlashRays : Node2D
    {
        [Export(PropertyHint.Range, "50,500,10")]
        public float RayLength { get; set; } = 120f;

        [Export(PropertyHint.Range, "20,500,10")]
        public float RayLengthMin { get; set; } = 50f;

        [Export(PropertyHint.Range, "5,36,1")]
        public int RayCount { get; set; } = 17;

        [Export(PropertyHint.Range, "0.1,8,0.1")]
        public float RayWidth { get; set; } = 3f;

        [Export(PropertyHint.Range, "0.2,2,0.05")]
        public float ColorIntensity { get; set; } = 1f;

        [Export(PropertyHint.Range, "0.1,10,0.05")]
        public float Duration { get; set; } = 0.45f;

        [Export(PropertyHint.Range, "1,2000,1")]
        public float SpeedMin { get; set; } = 200f;

        [Export(PropertyHint.Range, "1,2000,1")]
        public float SpeedMax { get; set; } = 600f;

        [Export(PropertyHint.Range, "0.5,10,0.1")]
        public float ExpandScale { get; set; } = 1.4f;

        [Export(PropertyHint.Range, "0,360,10")]
        public float RotationSweep { get; set; } = 45f;

        public bool FlipX { get; set; }

        private readonly List<RayState> _rays = new();
        private GlowState? _glowH;
        private GlowState? _glowV;
        private float _elapsed;

        public override void _Ready()
        {
            for (int i = 0; i < RayCount; i++)
            {
                float angle = (i / (float)RayCount) * Mathf.Tau;
                float hue = i / (float)RayCount;
                float minLen = (float)GD.RandRange(RayLengthMin * 0.6f, RayLengthMin);
                float maxLen = (float)GD.RandRange(RayLengthMin, RayLength);
                float target = (float)GD.RandRange(minLen, maxLen);

                var ray = new Line2D
                {
                    Width = RayWidth,
                    DefaultColor = Color.FromHsv(hue, 0.9f, ColorIntensity, 0.9f)
                };
                ray.AddPoint(Vector2.Zero);
                ray.AddPoint(Vector2.Right);
                ray.Rotation = angle;
                ray.Scale = new Vector2(0f, 1f);
                AddChild(ray);

                _rays.Add(new RayState
                {
                    Node = ray,
                    MinLen = minLen,
                    MaxLen = maxLen,
                    Target = target,
                    Current = 0f
                });
            }

            _glowH = CreateGlow(Vector2.Right, Vector2.Left);
            _glowV = CreateGlow(Vector2.Down, Vector2.Up);

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

        public override void _Process(double delta)
        {
            _elapsed += (float)delta;

            foreach (var r in _rays)
            {
                if (!IsInstanceValid(r.Node)) continue;

                float dir = r.Target - r.Current;
                float dist = Mathf.Abs(dir);
                float speed = (float)GD.RandRange(SpeedMin, SpeedMax);
                float step = speed * (float)delta;

                if (step >= dist)
                {
                    r.Current = r.Target;
                    r.Target = (float)GD.RandRange(r.MinLen, r.MaxLen);
                }
                else
                {
                    r.Current += Mathf.Sign(dir) * step;
                }

                r.Node.Scale = new Vector2(r.Current, 1f);
            }

            UpdateGlow(_glowH);
            UpdateGlow(_glowV);
        }

        private void UpdateGlow(GlowState? glow)
        {
            if (glow == null || !IsInstanceValid(glow.Node)) return;

            float t = _elapsed / Duration;
            if (t >= 1f)
            {
                glow.Node.Scale = new Vector2(0f, 1f);
                return;
            }

            float pulse = Mathf.Sin(t * Mathf.Pi * 3f) * 0.5f + 0.5f;
            glow.Node.Scale = new Vector2(glow.Max * pulse, 1f);
        }

        private GlowState? CreateGlow(Vector2 dirA, Vector2 dirB)
        {
            float cx = RayLength * 0.03f;
            var glow = new Line2D
            {
                Width = RayWidth * 2.5f,
                DefaultColor = new Color(1f, 1f, 1f, 0.95f)
            };
            glow.AddPoint(dirB);
            glow.AddPoint(dirA);
            glow.Scale = new Vector2(0f, 1f);
            AddChild(glow);

            return new GlowState { Node = glow, Max = cx };
        }

        private class RayState
        {
            public Line2D Node = null!;
            public float MinLen;
            public float MaxLen;
            public float Target;
            public float Current;
        }

        private class GlowState
        {
            public Line2D Node = null!;
            public float Max;
        }
    }
}
