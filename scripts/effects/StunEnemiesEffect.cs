using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 持续区域眩晕效果。
    /// 效果存活期间，Area2D 范围内所有 Enemies 层的敌人持续被冻结；
    /// 效果到期时自动解除全部眩晕。
    /// </summary>
    [GlobalClass]
    public partial class StunEnemiesEffect : ActorEffect
    {
        private const uint EnemiesLayerMask = 2u;

        /// <summary>
        /// 由 SpawnThrowDestroyEffects 在应用前设置，将 Area2D 定位到抛物落点。
        /// </summary>
        public Vector2 WorldSpawnPosition { get; set; } = Vector2.Zero;

        private Area2D? _area;
        private readonly HashSet<GameActor> _stunnedEnemies = new();
        private bool _cleaned = false;
        // 每个实例唯一前缀，便于精确移除 FreezeEffect
        private string _idPrefix = "";

        protected override void OnApply()
        {
            base.OnApply();
            _idPrefix = $"area_stun_{GetInstanceId()}";

            _area = GetNodeOrNull<Area2D>("Area2D");
            if (_area == null)
            {
                return;
            }

            _area.CollisionMask = EnemiesLayerMask;
            _area.Monitoring = true;
            _area.BodyEntered += OnBodyEntered;

            // 等物理帧同步后扫描已在范围内的敌人
            CallDeferred(MethodName.InitialScan);
        }

        private void InitialScan()
        {
            if (_area == null || !IsInstanceValid(_area)) return;

            // 修正 Area2D 到世界坐标落点（ActorEffect 挂在玩家树下，默认跟随玩家）
            if (WorldSpawnPosition != Vector2.Zero)
                _area.GlobalPosition = WorldSpawnPosition;

            // GetOverlappingBodies() 依赖物理帧，移动 Area2D 后立刻调用结果为空。
            // 改用直接空间查询，立即得到落点处的所有敌人。
            var shapeNode = _area.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shapeNode?.Shape == null) return;

            var spaceState = _area.GetWorld2D().DirectSpaceState;
            if (spaceState == null) return;

            Vector2 center = WorldSpawnPosition != Vector2.Zero ? WorldSpawnPosition : _area.GlobalPosition;
            var queryParams = new PhysicsShapeQueryParameters2D
            {
                Shape = shapeNode.Shape,
                Transform = new Transform2D(0f, center),
                CollisionMask = EnemiesLayerMask,
                CollideWithBodies = true,
                CollideWithAreas = false
            };

            foreach (var result in spaceState.IntersectShape(queryParams))
            {
                if (!result.TryGetValue("collider", out var colliderVar)) continue;
                if (colliderVar.As<GodotObject>() is GameActor enemy)
                    StunEnemy(enemy);
            }
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body is GameActor enemy)
                StunEnemy(enemy);
        }

        private void StunEnemy(GameActor enemy)
        {
            if (_cleaned) return;
            if (!IsInstanceValid(enemy)) return;
            if (_stunnedEnemies.Contains(enemy)) return;

            _stunnedEnemies.Add(enemy);
            var freeze = new FreezeEffect
            {
                Duration = this.Duration,   // 与区域眩晕效果同步倒计时
                EffectId = $"{_idPrefix}_{enemy.GetInstanceId()}"
            };
            enemy.ApplyEffect(freeze);
        }

        protected override void OnExpire()
        {
            Cleanup();
            base.OnExpire();
        }

        public override void OnRemoved()
        {
            Cleanup();
            base.OnRemoved();
        }

        private void Cleanup()
        {
            if (_cleaned) return;
            _cleaned = true;

            if (_area != null && IsInstanceValid(_area))
                _area.BodyEntered -= OnBodyEntered;

            foreach (var enemy in _stunnedEnemies)
            {
                if (!IsInstanceValid(enemy)) continue;
                
                // 移除应用的 FreezeEffect
                enemy.RemoveEffect($"{_idPrefix}_{enemy.GetInstanceId()}");
                
                // 清空残留的 Frozen 状态恢复时长，防止后续 Hit 状态错误恢复 Frozen
                enemy.FrozenStateRemainingTime = 0f;
            }
            _stunnedEnemies.Clear();
        }
    }
}