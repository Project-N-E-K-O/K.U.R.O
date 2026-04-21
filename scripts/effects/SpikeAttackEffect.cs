using Godot;
using System;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 尖刺区域效果。
    /// 敌人进入 Area2D 后，每隔 DamageInterval 秒受到 DamagePerTick 点伤害，
    /// 并持续降低 SpeedSlowPercent% 的移动速度；离开区域后恢复速度。
    /// </summary>
    [GlobalClass]
    public partial class SpikeAttackEffect : ActorEffect
    {
        private const uint EnemiesLayerMask = 2u;

        /// <summary>
        /// 由 SpawnThrowDestroyEffects 在应用前设置，将 Area2D 定位到抛物落点。
        /// </summary>
        public Vector2 WorldSpawnPosition { get; set; } = Vector2.Zero;

        /// <summary>每次造成的伤害量。</summary>
        [Export(PropertyHint.Range, "1,999,1")]
        public int DamagePerTick { get; set; } = 10;

        /// <summary>伤害间隔（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float DamageInterval { get; set; } = 1.0f;

        /// <summary>移动速度降低百分比（0~100）。</summary>
        [Export(PropertyHint.Range, "0,100,1")]
        public float SpeedSlowPercent { get; set; } = 30f;

        private Area2D? _area;

        // 区域内的敌人 → 独立计时器
        private readonly Dictionary<GameActor, float> _enemyTimers = new();
        // 记录每个敌人被减速前的原始速度
        private readonly Dictionary<GameActor, float> _originalSpeeds = new();

        protected override void OnApply()
        {
            base.OnApply();

            // 每次应用都生成唯一 ID，确保多次投掷时能创建多个独立的 SpikeAttackEffect
            EffectId = $"spike_{Guid.NewGuid()}";

            _area = GetNodeOrNull<Area2D>("Area2D");
            if (_area == null) return;

            if (WorldSpawnPosition != Vector2.Zero)
                _area.GlobalPosition = WorldSpawnPosition;

            _area.CollisionMask = EnemiesLayerMask;
            _area.Monitoring = true;
            _area.BodyEntered += OnBodyEntered;
            _area.BodyExited += OnBodyExited;
        }

        protected override void OnTick(double delta)
        {
            if (_enemyTimers.Count == 0) return;

            // 收集需要移除的无效敌人
            var toRemove = new List<GameActor>();

            foreach (var kvp in _enemyTimers)
            {
                var enemy = kvp.Key;
                if (!IsInstanceValid(enemy) || enemy!.IsDead)
                {
                    toRemove.Add(enemy!);
                    continue;
                }

                // 持续维持减速效果（防止其他系统改变速度）
                if (_originalSpeeds.TryGetValue(enemy, out float originalSpeed))
                {
                    float slowedSpeed = originalSpeed * (1f - SpeedSlowPercent / 100f);
                    if (Mathf.Abs(enemy.Speed - slowedSpeed) > 0.01f)
                        enemy.Speed = slowedSpeed;
                }

                _enemyTimers[enemy] = kvp.Value + (float)delta;
                if (_enemyTimers[enemy] >= DamageInterval)
                {
                    _enemyTimers[enemy] = 0f;
                    enemy.TakeDamage(DamagePerTick, Actor?.GlobalPosition, Actor);
                }
            }

            foreach (var e in toRemove)
                RemoveEnemy(e);
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

        private void OnBodyEntered(Node2D body)
        {
            if (body is not GameActor enemy) return;
            if (_enemyTimers.ContainsKey(enemy)) return;

            _enemyTimers[enemy] = 0f;

            // 立刻造成首次伤害
            if (!enemy.IsDead)
                enemy.TakeDamage(DamagePerTick, Actor?.GlobalPosition, Actor);

            // 施加减速
            if (!_originalSpeeds.ContainsKey(enemy))
            {
                _originalSpeeds[enemy] = enemy.Speed;
                enemy.Speed *= 1f - SpeedSlowPercent / 100f;
            }
        }

        private void OnBodyExited(Node2D body)
        {
            if (body is not GameActor enemy) return;
            RemoveEnemy(enemy);
        }

        private void RemoveEnemy(GameActor enemy)
        {
            _enemyTimers.Remove(enemy);

            if (_originalSpeeds.TryGetValue(enemy, out float originalSpeed))
            {
                _originalSpeeds.Remove(enemy);
                if (IsInstanceValid(enemy) && !enemy.IsDead)
                    enemy.Speed = originalSpeed;
            }
        }

        private void Cleanup()
        {
            if (_area != null && IsInstanceValid(_area))
            {
                _area.BodyEntered -= OnBodyEntered;
                _area.BodyExited -= OnBodyExited;
            }

            // 恢复所有仍在区域内的敌人速度
            foreach (var kvp in _originalSpeeds)
            {
                if (IsInstanceValid(kvp.Key) && !kvp.Key.IsDead)
                    kvp.Key.Speed = kvp.Value;
            }

            _enemyTimers.Clear();
            _originalSpeeds.Clear();
        }
    }
}
