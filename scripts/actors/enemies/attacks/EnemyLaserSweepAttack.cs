using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Godot.Collections;
using Kuros.Core;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 激光横扫攻击：敌人腾空后在不同分段随机投射横向激光，伴随警示并对命中的目标造成伤害。
    /// </summary>
    public partial class EnemyLaserSweepAttack : EnemyAttackTemplate
    {
        [ExportGroup("Laser Routine")]
        [Export(PropertyHint.Range, "1,5,1")] public int BurstCount { get; set; } = 3;
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float TelegraphDuration { get; set; } = 1.5f;
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float LaserDuration { get; set; } = 1.0f;
        [Export(PropertyHint.Range, "0,5,0.1")] public float InterBurstDelay { get; set; } = 0.2f;
        [Export(PropertyHint.Range, "0,5,0.1")] public float PostRecoveryDuration { get; set; } = 1.0f;
        [Export(PropertyHint.Range, "1,10,1")] public int LaserDamage { get; set; } = 1;
        [Export(PropertyHint.Range, "0,5000,10")] public float LaserWidth { get; set; } = 1400f;
        [Export(PropertyHint.Range, "10,500,1")] public float LaserThickness { get; set; } = 80f;
        [Export] public Vector2 HoverOffset { get; set; } = new Vector2(0, -120f);
        [Export] public Color WarningColor { get; set; } = new Color(1f, 0.55f, 0.15f, 0.45f);
        [Export] public Color ActiveLaserColor { get; set; } = new Color(1f, 0.15f, 0.1f, 0.8f);
        [Export] public Array<NodePath> LaserAnchorPaths { get; set; } = new();

        private readonly List<Node2D> _anchorNodes = new();
        private Polygon2D? _warningPolygon;
        private Polygon2D? _activePolygon;
        private bool _sequenceRunning;
        private bool _sequenceCancelled;
        private Vector2 _originalPosition;
        private bool _invulnerabilityApplied;

        public EnemyLaserSweepAttack()
        {
            AttackName = "Laser1";
            WarmupDuration = 0f;
            ActiveDuration = 10f;
            RecoveryDuration = PostRecoveryDuration;
            CooldownDuration = 4f;
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            CollectAnchors();
            EnsureIndicatorPolygons();
            RecoveryDuration = PostRecoveryDuration;
            ActiveDuration = Math.Max((TelegraphDuration + LaserDuration + InterBurstDelay) * BurstCount + 0.1f, 0.5f);
        }

        public override bool CanStart()
        {
            if (!base.CanStart())
            {
                return false;
            }

            if (BurstCount <= 0)
            {
                return false;
            }

            return true;
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            _originalPosition = Enemy.GlobalPosition;
            AttachInvulnerability();
            _sequenceCancelled = false;
            _sequenceRunning = true;
            _ = RunSequenceAsync();
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            StopSequence();
        }

        private async Task RunSequenceAsync()
        {
            try
            {
                for (int burst = 0; burst < BurstCount && !_sequenceCancelled; burst++)
                {
                    Vector2 anchorPosition = ChooseAnchorPosition();
                    MoveEnemyToAnchor(anchorPosition);
                    UpdateIndicators(anchorPosition);

                    await DelaySeconds(TelegraphDuration);
                    if (_sequenceCancelled) break;

                    ActivateLaser(anchorPosition);
                    await DelaySeconds(LaserDuration);
                    DeactivateLaser();

                    if (_sequenceCancelled) break;

                    if (burst < BurstCount - 1 && InterBurstDelay > 0f)
                    {
                        await DelaySeconds(InterBurstDelay);
                    }
                }
            }
            finally
            {
                FinishSequence();
            }
        }

        private async Task DelaySeconds(float seconds)
        {
            if (seconds <= 0f || _sequenceCancelled)
            {
                return;
            }

            var timer = Enemy.GetTree().CreateTimer(seconds);
            await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        }

        private Vector2 ChooseAnchorPosition()
        {
            if (_anchorNodes.Count == 0 || _anchorNodes.TrueForAll(a => a == null || !GodotObject.IsInstanceValid(a)))
            {
                return Enemy.GlobalPosition;
            }

            var validAnchors = new List<Node2D>();
            foreach (var node in _anchorNodes)
            {
                if (node != null && node.IsInsideTree())
                {
                    validAnchors.Add(node);
                }
            }

            if (validAnchors.Count == 0)
            {
                return Enemy.GlobalPosition;
            }

            int index = GD.RandRange(0, validAnchors.Count - 1);
            return validAnchors[index].GlobalPosition;
        }

        private void MoveEnemyToAnchor(Vector2 anchorPosition)
        {
            Enemy.Velocity = Vector2.Zero;
            Enemy.GlobalPosition = anchorPosition + HoverOffset;
        }

        private void UpdateIndicators(Vector2 center)
        {
            var halfWidth = LaserWidth * 0.5f;
            var halfThickness = LaserThickness * 0.5f;
            var polygon = BuildRectanglePolygon(halfWidth, halfThickness);

            if (_warningPolygon != null)
            {
                _warningPolygon.Visible = true;
                _warningPolygon.GlobalPosition = center;
                _warningPolygon.Polygon = polygon;
                _warningPolygon.Color = WarningColor;
            }

            if (_activePolygon != null)
            {
                _activePolygon.Visible = false;
                _activePolygon.GlobalPosition = center;
                _activePolygon.Polygon = polygon;
                _activePolygon.Color = ActiveLaserColor;
            }
        }

        private void ActivateLaser(Vector2 center)
        {
            if (_warningPolygon != null)
            {
                _warningPolygon.Visible = false;
            }

            if (_activePolygon != null)
            {
                _activePolygon.Visible = true;
            }

            ApplyLaserDamage(center);
        }

        private void DeactivateLaser()
        {
            if (_activePolygon != null)
            {
                _activePolygon.Visible = false;
            }
        }

        private void ApplyLaserDamage(Vector2 center)
        {
            if (Player == null)
            {
                return;
            }

            var targetPos = Player.GlobalPosition;
            float halfWidth = LaserWidth * 0.5f;
            float halfThickness = LaserThickness * 0.5f;

            bool insideX = Mathf.Abs(targetPos.X - center.X) <= halfWidth;
            bool insideY = Mathf.Abs(targetPos.Y - center.Y) <= halfThickness;

            if (insideX && insideY)
            {
                Player.TakeDamage(LaserDamage, center, Enemy);
            }
        }

        private void StopSequence()
        {
            if (!_sequenceRunning)
            {
                return;
            }

            _sequenceCancelled = true;
            FinishSequence();
        }

        private void FinishSequence()
        {
            if (!_sequenceRunning)
            {
                return;
            }

            _sequenceRunning = false;
            if (_warningPolygon != null) _warningPolygon.Visible = false;
            if (_activePolygon != null) _activePolygon.Visible = false;

            Enemy.GlobalPosition = _originalPosition;
            Enemy.Velocity = Vector2.Zero;
            DetachInvulnerability();
            ForceEnterRecoveryPhase();
        }

        private void CollectAnchors()
        {
            _anchorNodes.Clear();
            if (LaserAnchorPaths == null) return;

            foreach (var path in LaserAnchorPaths)
            {
                if (path.IsEmpty) continue;
                var anchor = GetNodeOrNull<Node2D>(path) ?? Enemy.GetNodeOrNull<Node2D>(path);
                if (anchor != null)
                {
                    _anchorNodes.Add(anchor);
                }
            }
        }

        private void EnsureIndicatorPolygons()
        {
            _warningPolygon ??= CreatePolygonNode("LaserWarning", WarningColor);
            _activePolygon ??= CreatePolygonNode("LaserActive", ActiveLaserColor);
        }

        private Polygon2D CreatePolygonNode(string name, Color color)
        {
            var polygon = new Polygon2D
            {
                Name = name,
                Color = color,
                Visible = false
            };

            polygon.ZIndex = 10;
            polygon.TopLevel = true;
            Enemy.AddChild(polygon);
            return polygon;
        }

        private static Vector2[] BuildRectanglePolygon(float halfWidth, float halfHeight)
        {
            return new[]
            {
                new Vector2(-halfWidth, -halfHeight),
                new Vector2(halfWidth, -halfHeight),
                new Vector2(halfWidth, halfHeight),
                new Vector2(-halfWidth, halfHeight)
            };
        }

        private void AttachInvulnerability()
        {
            if (_invulnerabilityApplied)
            {
                return;
            }

            Enemy.DamageIntercepted += OnEnemyDamageIntercepted;
            _invulnerabilityApplied = true;
        }

        private void DetachInvulnerability()
        {
            if (!_invulnerabilityApplied)
            {
                return;
            }

            Enemy.DamageIntercepted -= OnEnemyDamageIntercepted;
            _invulnerabilityApplied = false;
        }

        private bool OnEnemyDamageIntercepted(GameActor.DamageEventArgs args)
        {
            args.IsBlocked = true;
            return true;
        }
    }
}

