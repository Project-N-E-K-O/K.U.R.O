using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 尖刺区域效果。
    /// 敌人进入 Area2D 后，每隔 DamageInterval 秒受到 DamagePerTick 点伤害，
    /// 并持续降低 SpeedSlowPercent% 的移动速度；离开区域后恢复速度。
    /// 支持多个尖刺效果重叠（使用倍数乘积管理减速）。
    /// </summary>
    [GlobalClass]
    public partial class SpikeAttackEffect : ActorEffect
    {
        private const uint EnemiesLayerMask = 2u;
        // 全局字典：敌人 → 减速倍数列表（来自所有活跃的 SpikeAttackEffect）
        private static readonly Dictionary<GameActor, List<float>> GlobalSpeedMultipliers = new();
        // 全局字典：敌人 → 原始速度（在第一个减速效果应用时记录）
        private static readonly Dictionary<GameActor, float> GlobalOriginalSpeeds = new();

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
        // 此效果的速度倍数（如 0.7 表示减速30%）
        private float _speedMultiplier;
        // 区域内的敌人 → 独立计时器
        private readonly Dictionary<GameActor, float> _enemyTimers = new();
        // 此效果在该敌人上应用的倍数（用于移除时恢复）
        private readonly Dictionary<GameActor, float> _appliedMultipliers = new();

        protected override void OnApply()
        {
            base.OnApply();

            // 每次应用都生成唯一 ID，确保多次投掷时能创建多个独立的 SpikeAttackEffect
            EffectId = $"spike_{Guid.NewGuid()}";

            // 计算此效果的速度倍数
            _speedMultiplier = 1f - SpeedSlowPercent / 100f;

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

            // 使用倍数乘积管理减速
            ApplySpeedMultiplier(enemy, _speedMultiplier);
        }

        private void OnBodyExited(Node2D body)
        {
            if (body is not GameActor enemy) return;
            RemoveEnemy(enemy);
        }

        private void RemoveEnemy(GameActor enemy)
        {
            _enemyTimers.Remove(enemy);

            // 移除此效果的速度倍数
            if (_appliedMultipliers.TryGetValue(enemy, out float appliedMult))
            {
                _appliedMultipliers.Remove(enemy);
                RemoveSpeedMultiplier(enemy, appliedMult);
            }
        }

        /// <summary>
        /// 为敌人应用一个速度倍数。
        /// 记录原始速度（第一个减速时），维护倍数列表，计算和应用总倍数。
        /// </summary>
        private void ApplySpeedMultiplier(GameActor enemy, float multiplier)
        {
            if (!GlobalOriginalSpeeds.ContainsKey(enemy))
            {
                // 第一个减速效果：记录原始速度
                GlobalOriginalSpeeds[enemy] = enemy.Speed;
                GlobalSpeedMultipliers[enemy] = new List<float>();
            }

            GlobalSpeedMultipliers[enemy].Add(multiplier);
            // 记录此效果在该敌人上应用的倍数（用于 Cleanup）
            _appliedMultipliers[enemy] = multiplier;
            RecalculateSpeed(enemy);
        }

        /// <summary>
        /// 为敌人移除一个速度倍数。
        /// </summary>
        private static void RemoveSpeedMultiplier(GameActor enemy, float multiplier)
        {
            if (!GlobalSpeedMultipliers.ContainsKey(enemy)) return;

            GlobalSpeedMultipliers[enemy].Remove(multiplier);

            if (GlobalSpeedMultipliers[enemy].Count == 0)
            {
                // 所有减速效果移除，恢复原始速度
                if (GlobalOriginalSpeeds.TryGetValue(enemy, out float originalSpeed))
                {
                    if (IsInstanceValid(enemy) && !enemy.IsDead)
                        enemy.Speed = originalSpeed;
                    GlobalOriginalSpeeds.Remove(enemy);
                }
                GlobalSpeedMultipliers.Remove(enemy);
            }
            else
            {
                // 仍有其他减速效果，重新计算
                RecalculateSpeed(enemy);
            }
        }

        /// <summary>
        /// 重新计算敌人的当前速度（原始速度 * 所有倍数的乘积）。
        /// </summary>
        private static void RecalculateSpeed(GameActor enemy)
        {
            if (!GlobalOriginalSpeeds.TryGetValue(enemy, out float originalSpeed)) return;
            if (!GlobalSpeedMultipliers.TryGetValue(enemy, out var multipliers)) return;

            // 计算倍数乘积
            float totalMultiplier = multipliers.Aggregate(1f, (a, b) => a * b);
            float finalSpeed = originalSpeed * totalMultiplier;

            if (IsInstanceValid(enemy) && !enemy.IsDead)
                enemy.Speed = finalSpeed;
        }

        private void Cleanup()
        {
            if (_area != null && IsInstanceValid(_area))
            {
                _area.BodyEntered -= OnBodyEntered;
                _area.BodyExited -= OnBodyExited;
            }

            // 移除此效果为所有敌人应用的减速倍数
            foreach (var enemy in _appliedMultipliers.Keys.ToList())
            {
                if (_appliedMultipliers.TryGetValue(enemy, out float mult))
                {
                    RemoveSpeedMultiplier(enemy, mult);
                }
            }

            _enemyTimers.Clear();
            _appliedMultipliers.Clear();
        }
    }
}
