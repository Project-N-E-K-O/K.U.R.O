using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 声波效果：攻击命中敌人时在其身后生成锥形扩散声波，范围内敌人受伤并减速。
    /// 锥形视觉由 sound_wave_cone.gdshader 全权处理，脚本仅负责命中检测和伤害/减速。
    /// </summary>
    [GlobalClass]
    public partial class SoundWaveEffect : ActorEffect
    {
        [ExportGroup("Cone")]
        [Export(PropertyHint.Range, "10,180,1")] public float ConeAngle { get; set; } = 45f;
        [Export(PropertyHint.Range, "100,2000,10")] public float ConeRange { get; set; } = 500f;

        [ExportGroup("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public int Damage { get; set; } = 20;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float DamageInterval { get; set; } = 1f;

        [ExportGroup("Visual")]
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float FadeDuration { get; set; } = 1.0f;

        [ExportGroup("Slow")]
        [Export(PropertyHint.Range, "0.1,1,0.01")] public float SlowMultiplier { get; set; } = 0.5f;

        [ExportGroup("Debug")]
        [Export] public bool ShowDebugCone { get; set; } = false;

        private bool _subscribed;
        private readonly Dictionary<GameActor, float> _slowedEnemies = new();
        private readonly List<Node2D> _activeVisuals = new();
        private Node2D? _coneTemplate;

        private Vector2 _coneOrigin;
        private Vector2 _coneDir;
        private bool _coneActive;
        private float _damageTickTimer;

        public SoundWaveEffect()
        {
            EffectId = "sound_wave";
            DisplayName = "声波";
            Description = "攻击命中时在敌人身后产生锥形声波，造成伤害和减速。";
            MaxStacks = 1;
        }

        public override void _Ready()
        {
            base._Ready();
            _coneTemplate = GetNodeOrNull<Node2D>("ConeContainer");
        }

        protected override void OnApply()
        {
            base.OnApply();
            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _subscribed = true;
            }
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _subscribed = false;
            }
            RestoreSlowedEnemies();
            ClearAllVisuals();
            base.OnRemoved();
        }

        protected override void OnTick(double delta)
        {
            if (!_coneActive) return;

            _damageTickTimer += (float)delta;
            if (_damageTickTimer >= DamageInterval)
            {
                _damageTickTimer -= DamageInterval;
                ApplyPeriodicDamage();
            }
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor) return;
            if (damage <= 0) return;
            if (target.IsDead) return;

            var origin = GetEnemyHitCenter(target);
            var coneDir = (origin - Actor.GlobalPosition).Normalized();

            _coneOrigin = origin;
            _coneDir = coneDir;
            _coneActive = true;
            _damageTickTimer = 0f;

            SpawnConeVisual(origin, coneDir);
            ApplyPeriodicDamage();
            ApplyConeSlow(origin, coneDir);
        }

        private void ApplyPeriodicDamage()
        {
            var tree = GetTree();
            if (tree == null) return;

            float halfAngleRad = Mathf.DegToRad(ConeAngle * 0.5f);

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || !IsInstanceValid(enemy) || enemy.IsDead)
                    continue;

                if (!IsEnemyInCone(enemy, _coneOrigin, _coneDir, halfAngleRad))
                    continue;

                if (Damage > 0)
                    enemy.TakeDamage(Damage, _coneOrigin);
            }
        }

        private void ApplyConeSlow(Vector2 coneOrigin, Vector2 coneDir)
        {
            var tree = GetTree();
            if (tree == null) return;

            float halfAngleRad = Mathf.DegToRad(ConeAngle * 0.5f);

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || !IsInstanceValid(enemy) || enemy.IsDead)
                    continue;
                if (_slowedEnemies.ContainsKey(enemy))
                    continue;

                if (!IsEnemyInCone(enemy, coneOrigin, coneDir, halfAngleRad))
                    continue;

                ApplySlow(enemy);
            }
        }

        private bool IsEnemyInCone(GameActor enemy, Vector2 origin, Vector2 dir, float halfAngleRad)
        {
            var toTarget = GetEnemyHitCenter(enemy) - origin;
            float dist = toTarget.Length();
            if (dist > ConeRange) return false;
            float angle = dir.AngleTo(toTarget);
            return Mathf.Abs(angle) <= halfAngleRad;
        }

        private void SpawnConeVisual(Vector2 origin, Vector2 coneDir)
        {
            if (_coneTemplate == null) return;

            var clone = _coneTemplate.Duplicate() as Node2D;
            if (clone == null) return;

            clone.Visible = true;
            clone.GlobalPosition = origin;
            clone.Rotation = coneDir.Angle();

            var coneRect = clone.GetNodeOrNull<ColorRect>("ConeRect");
            if (coneRect?.Material is ShaderMaterial mat)
            {
                var uniqueMat = (ShaderMaterial)mat.Duplicate();
                coneRect.Material = uniqueMat;

                if (Duration > 0f && FadeDuration > 0f)
                {
                    float ringAlpha = ((Color)uniqueMat.GetShaderParameter("ring_color")).A;
                    float intensityBase = (float)uniqueMat.GetShaderParameter("intensity");
                    float fadeStart = Mathf.Max(Duration - FadeDuration, 0f);

                    var tween = clone.CreateTween();
                    if (fadeStart > 0f)
                        tween.TweenInterval(fadeStart);
                    tween.TweenMethod(Callable.From<float>(f =>
                    {
                        if (!IsInstanceValid(uniqueMat)) return;
                        var rc = (Color)uniqueMat.GetShaderParameter("ring_color");
                        rc.A = f * ringAlpha;
                        uniqueMat.SetShaderParameter("ring_color", rc);
                        uniqueMat.SetShaderParameter("intensity", f * intensityBase);
                    }), 1.0f, 0.0f, FadeDuration);
                }
            }

            if (ShowDebugCone)
            {
                var debug = new DebugConeOverlay(ConeAngle, ConeRange);
                clone.AddChild(debug);
            }

            GetTree()?.CurrentScene?.AddChild(clone);
            _activeVisuals.Add(clone);

            if (Duration > 0f)
            {
                var timer = new Timer { OneShot = true, WaitTime = Duration };
                timer.Timeout += () =>
                {
                    _activeVisuals.Remove(clone);
                    clone.QueueFree();
                    timer.QueueFree();
                };
                clone.AddChild(timer);
                timer.Start();
            }
        }

        private static Vector2 GetEnemyHitCenter(GameActor enemy)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? enemy.GlobalPosition;
        }

        private void ApplySlow(GameActor enemy)
        {
            _slowedEnemies[enemy] = enemy.Speed;
            enemy.Speed *= SlowMultiplier;

            if (Duration > 0f)
            {
                var timer = new Timer { OneShot = true, WaitTime = Duration };
                var capturedEnemy = enemy;
                timer.Timeout += () =>
                {
                    if (_slowedEnemies.Remove(capturedEnemy, out float originalSpeed)
                        && IsInstanceValid(capturedEnemy))
                    {
                        capturedEnemy.Speed = originalSpeed;
                    }
                    timer.QueueFree();
                };
                enemy.AddChild(timer);
                timer.Start();
            }
        }

        private void RestoreSlowedEnemies()
        {
            foreach (var (enemy, originalSpeed) in _slowedEnemies)
            {
                if (IsInstanceValid(enemy))
                    enemy.Speed = originalSpeed;
            }
            _slowedEnemies.Clear();
        }

        private void ClearAllVisuals()
        {
            foreach (var visual in _activeVisuals)
            {
                visual.QueueFree();
            }
            _activeVisuals.Clear();
        }

        private partial class DebugConeOverlay : Node2D
        {
            private readonly float _angle;
            private readonly float _range;

            public DebugConeOverlay(float angle, float range)
            {
                _angle = angle;
                _range = range;
                ZIndex = 100;
                ZAsRelative = false;
            }

            public override void _Draw()
            {
                float half = Mathf.DegToRad(_angle * 0.5f);
                var origin = Vector2.Zero;
                var left = new Vector2(Mathf.Cos(-half), Mathf.Sin(-half)) * _range;
                var right = new Vector2(Mathf.Cos(half), Mathf.Sin(half)) * _range;

                DrawLine(origin, left, Colors.Red, 3f);
                DrawLine(origin, right, Colors.Red, 3f);

                const int segments = 24;
                var arcPts = new Vector2[segments + 1];
                for (int i = 0; i <= segments; i++)
                {
                    float a = -half + Mathf.DegToRad(_angle) / segments * i;
                    arcPts[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * _range;
                }
                DrawPolyline(arcPts, Colors.Red, 2f);
            }
        }
    }
}
