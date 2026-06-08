using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using System;

namespace Kuros.Effects
{
    [GlobalClass]
    public partial class BunnySwardFloatingCannon : ActorEffect
    {
        [Export] public PackedScene? LaserEffect { get; set; }

        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float FireInterval { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "100,2000,10")]
        public float DetectionRange { get; set; } = 2000f;

        [Export(PropertyHint.Range, "0.1,2,0.05")]
        public float SpawnAnimDuration { get; set; } = 0.4f;

        [Export(PropertyHint.Range, "0.1,2,0.05")]
        public float DespawnAnimDuration { get; set; } = 0.3f;

        private Marker2D? _laserSpawnPoint;
        private float _fireTimer;
        private ShaderMaterial? _outlineMat;
        private ShaderMaterial? _spriteMat;
        private bool _despawning;
        private float _lifeElapsed;

        protected override void OnApply()
        {
            base.OnApply();
            _laserSpawnPoint = GetNode<Marker2D>("Marker2D");
            _fireTimer = 0f;
            _despawning = false;
            _lifeElapsed = 0f;

            if (Actor != null)
            {
                Reparent(Actor.GetParent());
                Set("global_position", Actor.GlobalPosition);
            }

            var outline = GetNode<Sprite2D>("outline");
            _outlineMat = outline.Material as ShaderMaterial;
            _spriteMat = outline.GetNode<Sprite2D>("Sprite2D").Material as ShaderMaterial;

            PlayScanlineAnim(false, SpawnAnimDuration, () =>
            {
                DisableScanline();
            });
        }

        protected override void OnStackRefreshed()
        {
            // 玩家再次攻击时不应刷新 Duration——保持原有生命周期
        }

        protected override void OnExpire()
        {
            if (_despawning) return;
            _despawning = true;

            PlayScanlineAnim(true, DespawnAnimDuration, () =>
            {
                base.OnExpire();
                QueueFree();
            });
        }

        public override void OnRemoved()
        {
            if (!_despawning)
            {
                PlayScanlineAnim(true, DespawnAnimDuration, () =>
                {
                    base.OnRemoved();
                    QueueFree();
                });
                _despawning = true;
            }
            else
            {
                base.OnRemoved();
            }
        }

        protected override void OnTick(double delta)
        {
            if (_despawning) return;

            _lifeElapsed += (float)delta;

            var remaining = Duration - _lifeElapsed;
            if (Duration > 0 && remaining <= DespawnAnimDuration)
            {
                _despawning = true;
                PlayScanlineAnim(true, DespawnAnimDuration, () =>
                {
                    Controller?.RemoveEffect(this);
                });
                return;
            }

            var nearestEnemy = FindNearestEnemy();
            if (nearestEnemy != null)
            {
                RotateToward(GetEnemyAimCenter(nearestEnemy));
            }

            _fireTimer += (float)delta;
            if (_fireTimer >= FireInterval && nearestEnemy != null)
            {
                _fireTimer -= FireInterval;
                FireLaser();
            }
        }

        private void PlayScanlineAnim(bool reverse, float duration, Action onDone)
        {
            if (_outlineMat == null || _spriteMat == null) return;
            var tree = GetTree();
            if (tree == null) return;

            SetScanlinePos(0f);
            SetReverseScan(reverse);

            var tween = tree.CreateTween();
            tween.SetParallel(true);
            tween.TweenMethod(Callable.From<float>(pos =>
            {
                if (_outlineMat != null && GodotObject.IsInstanceValid(_outlineMat))
                    _outlineMat.SetShaderParameter("scanline_pos", pos);
                if (_spriteMat != null && GodotObject.IsInstanceValid(_spriteMat))
                    _spriteMat.SetShaderParameter("scanline_pos", pos);
            }), 0f, 1f, duration);
            tween.SetParallel(false);
            tween.TweenCallback(Callable.From(onDone));
        }

        private void SetScanlinePos(float pos)
        {
            _outlineMat?.SetShaderParameter("scanline_pos", pos);
            _spriteMat?.SetShaderParameter("scanline_pos", pos);
        }

        private void SetReverseScan(bool reverse)
        {
            _outlineMat?.SetShaderParameter("reverse_scan", reverse);
            _spriteMat?.SetShaderParameter("reverse_scan", reverse);
        }

        private void DisableScanline()
        {
            SetReverseScan(false);
            SetScanlinePos(-1f);
        }

        private void RotateToward(Vector2 target)
        {
            var direction = target - GetGlobalPos();
            Set("rotation", direction.Angle());
        }

        private GameActor? FindNearestEnemy()
        {
            var tree = GetTree();
            if (tree == null) return null;

            GameActor? nearest = null;
            float nearestDistSq = float.MaxValue;
            var myPos = GetGlobalPos();
            float rangeSq = DetectionRange * DetectionRange;

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || !IsInstanceValid(enemy) || enemy.IsDead)
                    continue;

                float distSq = myPos.DistanceSquaredTo(enemy.GlobalPosition);
                if (distSq > rangeSq)
                    continue;

                if (distSq < nearestDistSq)
                {
                    nearestDistSq = distSq;
                    nearest = enemy;
                }
            }

            return nearest;
        }

        private void FireLaser()
        {
            if (LaserEffect == null || _laserSpawnPoint == null) return;

            var laser = LaserEffect.Instantiate<Node2D>();
            if (laser == null) return;

            GetTree()?.CurrentScene?.AddChild(laser);
            laser.GlobalPosition = _laserSpawnPoint.GlobalPosition;
            laser.GlobalRotation = _laserSpawnPoint.GlobalRotation;
        }

        private static Vector2 GetEnemyAimCenter(Node2D enemy)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? enemy.GlobalPosition;
        }

        private Vector2 GetGlobalPos()
        {
            return Get("global_position").AsVector2();
        }
    }
}
