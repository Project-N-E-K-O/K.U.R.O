using Godot;
using System.Collections.Generic;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 玩家激光束：从浮游炮沿直线发射，命中路径上的敌人并造成伤害与击退。
    /// BunnySwordFloatingCannon 已瞄准，激光无需追踪。
    /// </summary>
    public partial class LaserBeamPlayerWeapon : Node2D
    {
        [ExportCategory("Beam")]
        [Export] public float MaxLength = 3000f;
        [Export] public Color BeamColor = new Color(1f, 0.85f, 0.2f, 1f);
        [Export] public Color GlowColor = new Color(1f, 0.6f, 0.05f, 0.35f);
        [Export] public float BeamWidth = 8f;
        [Export] public float GlowWidth = 32f;

        [ExportCategory("Timing")]
        [Export] public float Lifetime = 0.45f;
        [Export] public float FadeDuration = 0.15f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 2;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 50f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 1000f;

        private RayCast2D? _ray;
        private Line2D? _glowLine;
        private Line2D? _beamLine;
        private float _timer;
        private Color _initBeamColor;
        private Color _initGlowColor;
        private bool _pendingFirstFrame;
        private readonly HashSet<GameActor> _damagedEnemies = new();
        private readonly Vector2[] _beamPoints = new Vector2[2];

        public override void _Ready()
        {
            _ray = GetNodeOrNull<RayCast2D>("RayCast2D");
            _glowLine = GetNodeOrNull<Line2D>("GlowLine");
            _beamLine = GetNodeOrNull<Line2D>("BeamLine");

            if (_ray == null || _glowLine == null || _beamLine == null)
            {
                GD.PushWarning("[LaserBeamPlayerWeapon] 缺少子节点，请检查场景结构。");
                QueueFree();
                return;
            }

            _beamLine.Width = BeamWidth;
            _beamLine.DefaultColor = BeamColor;
            _glowLine.Width = GlowWidth;
            _glowLine.DefaultColor = GlowColor;

            _initBeamColor = BeamColor;
            _initGlowColor = GlowColor;

            _ray.TargetPosition = new Vector2(MaxLength, 0f);
            _ray.Enabled = true;

            _timer = Lifetime;
            _pendingFirstFrame = true;
        }

        public override void _Process(double delta)
        {
            if (_pendingFirstFrame)
            {
                _pendingFirstFrame = false;
                UpdateBeam();
                TryDamageEnemies();
            }

            _timer -= (float)delta;

            if (_timer <= 0f)
            {
                QueueFree();
                return;
            }

            if (_timer < FadeDuration && FadeDuration > 0f)
            {
                float t = _timer / FadeDuration;
                if (_beamLine != null)
                    _beamLine.DefaultColor = new Color(
                        _initBeamColor.R, _initBeamColor.G, _initBeamColor.B, _initBeamColor.A * t);
                if (_glowLine != null)
                    _glowLine.DefaultColor = new Color(
                        _initGlowColor.R, _initGlowColor.G, _initGlowColor.B, _initGlowColor.A * t);
            }

            UpdateBeam();
        }

        private void TryDamageEnemies()
        {
            if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;

            var tree = GetTree();
            if (tree == null) return;

            Vector2 beamDir = new Vector2(Mathf.Cos(Rotation), Mathf.Sin(Rotation));

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || !IsInstanceValid(enemy) || enemy.IsDead)
                    continue;
                if (_damagedEnemies.Contains(enemy))
                    continue;

                if (TryDamageEnemy(enemy, beamDir))
                    _damagedEnemies.Add(enemy);
            }
        }

        /// <summary>
        /// 与 LaserBeam.TryDamagePlayer 相同几何距离检测法，命中了则造成伤害和击退。
        /// </summary>
        private bool TryDamageEnemy(GameActor enemy, Vector2 beamDir)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            Vector2 targetCenter = hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? enemy.GlobalPosition;

            Vector2 toTarget = targetCenter - GlobalPosition;

            float along = toTarget.Dot(beamDir);
            if (along < 0f || along > MaxLength) return false;

            float perp = Mathf.Abs(toTarget.X * beamDir.Y - toTarget.Y * beamDir.X);

            float detectionRadius = 150f;
            if (hitShape?.Shape is CapsuleShape2D cap)
            {
                float worldScale = Mathf.Abs(hitShape.GlobalTransform.Scale.X);
                detectionRadius = cap.Radius * worldScale;
            }

            if (perp > detectionRadius) return false;

            bool alreadyInvincible = enemy is Kuros.Actors.Heroes.MainCharacter mc && mc.IsHitInvincible;

            if (Damage > 0)
                enemy.TakeDamage(Damage, GlobalPosition);

            if (!alreadyInvincible)
            {
                float knockSpeed = KnockbackSpeed > 0f
                    ? KnockbackSpeed
                    : KnockbackDistance > 0f
                        ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f)
                        : 0f;
                if (knockSpeed > 0f)
                    enemy.Velocity = beamDir * knockSpeed;
            }

            return true;
        }

        private void UpdateBeam()
        {
            if (_ray == null || _beamLine == null || _glowLine == null) return;

            _ray.ForceRaycastUpdate();

            Vector2 endPt = _ray.IsColliding()
                ? ToLocal(_ray.GetCollisionPoint())
                : new Vector2(MaxLength, 0f);

            _beamPoints[0] = Vector2.Zero;
            _beamPoints[1] = endPt;
            _beamLine.Points = _beamPoints;
            _glowLine.Points = _beamPoints;
        }
    }
}
