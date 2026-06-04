using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 浮游炮：装备光剑攻击时在玩家位置生成，朝范围内最近敌人旋转并发射激光。
    /// </summary>
    [GlobalClass]
    public partial class BunnySwardFloatingCannon : ActorEffect
    {
        [Export] public PackedScene? LaserEffect { get; set; }

        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float FireInterval { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "100,2000,10")]
        public float DetectionRange { get; set; } = 2000f;

        private Marker2D? _laserSpawnPoint;
        private float _fireTimer;

        protected override void OnApply()
        {
            base.OnApply();
            _laserSpawnPoint = GetNode<Marker2D>("Marker2D");
            _fireTimer = 0f;

            if (Actor != null)
            {
                Reparent(Actor.GetParent());
                Set("global_position", Actor.GlobalPosition);
            }
        }

        protected override void OnTick(double delta)
        {
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
