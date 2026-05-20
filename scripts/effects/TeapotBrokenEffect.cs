using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 茶壶破碎落地效果（WaiterA 的投掷物命中后生成）。
    /// 玩家进入 Area2D 后受到 DamagePerTick 点持续伤害，并降低 SpeedSlowPercent% 移动速度；
    /// 离开区域后恢复速度。效果在 Duration 秒后自动消失。
    ///
    /// 支持两种工作模式：
    ///   独立模式：由 EnemyWaiterAThrowProjectile.OnArrived() 直接 AddChild 到场景树，
    ///             _Ready() 完成初始化，_Process() 驱动计时与到期销毁。
    ///   ActorEffect 模式：通过 actor.ApplyEffect() 应用到玩家身上，生命周期由
    ///                     EffectController 管理（与 SpikeAttackEffect 相同路径）。
    /// </summary>
    [GlobalClass]
    public partial class TeapotBrokenEffect : ActorEffect, IWorldSpawnable
    {
        /// <summary>玩家所在碰撞层掩码（Layer 3 in Godot editor = bit 2 = value 4）。</summary>
        private const uint PlayersLayerMask = 4u;

        // ── IWorldSpawnable ──────────────────────────────────────────────────
        /// <summary>由外部在 AddChild 前设置，将 Area2D 定位到落点；未设置时保持默认位置。</summary>
        public Vector2? WorldSpawnPosition { get; set; }

        // ── Exports ──────────────────────────────────────────────────────────
        /// <summary>每 DamageInterval 秒造成的伤害量。</summary>
        [Export(PropertyHint.Range, "1,999,1")]
        public int DamagePerTick { get; set; } = 5;

        /// <summary>伤害间隔（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float DamageInterval { get; set; } = 1.0f;

        /// <summary>移动速度降低百分比（0~100）。</summary>
        [Export(PropertyHint.Range, "0,100,1")]
        public float SpeedSlowPercent { get; set; } = 30f;

        /// <summary>到期前开始淡出的时长（秒）。0 = 不淡出，直接消失。</summary>
        [Export(PropertyHint.Range, "0,30,0.1")]
        public float FadeOutDuration { get; set; } = 2.0f;

        // ── Private state ────────────────────────────────────────────────────
        private Area2D? _area;
        private Sprite2D? _sprite;
        private float _speedMultiplier;

        // 独立模式计时（与基类私有 _elapsed 独立）
        private float _standaloneElapsed;
        private float _damageTimer;
        private bool _initialized;

        // 当前受影响的玩家（通常只有 1 名）
        private GameActor? _affectedPlayer;
        private bool _speedApplied;

        // 全局计数器：玩家 → (活跃实例数, 原始速度)
        // 确保多个 TeapotBrokenEffect 同时存在时只应用一次减速
        private static readonly Dictionary<GameActor, (int Count, float OriginalSpeed)> GlobalSlowState = new();

        // ── Godot lifecycle（独立模式入口）───────────────────────────────────
        public override void _Ready()
        {
            // 直接 AddChild 到场景时（Actor 尚未由 Initialize 赋值），走独立初始化
            if (!_initialized)
                Setup();
        }

        public override void _Process(double delta)
        {
            // 仅在独立模式下（Actor == null）自行驱动计时
            if (Actor != null) return;

            // 对区域内玩家造成持续伤害
            if (_affectedPlayer != null && IsInstanceValid(_affectedPlayer) && !_affectedPlayer.IsDead)
            {
                _damageTimer += (float)delta;
                if (_damageTimer >= DamageInterval)
                {
                    _damageTimer = 0f;
                    _affectedPlayer.TakeDamage(DamagePerTick, _area?.GlobalPosition, null, DamageSource.AreaEffect);
                }
            }

            // 到期自销毁 + 淡出
            if (Duration > 0f)
            {
                _standaloneElapsed += (float)delta;
                UpdateFade(Mathf.Max(Duration - _standaloneElapsed, 0f));
                if (_standaloneElapsed >= Duration)
                    SelfExpire();
            }
        }

        // ── ActorEffect lifecycle（ActorEffect 模式入口）─────────────────────
        protected override void OnApply()
        {
            base.OnApply();
            if (!_initialized)
                Setup();
        }

        protected override void OnTick(double delta)
        {
            // ActorEffect 模式：EffectController 调用 Tick → OnTick
            UpdateFade(GetRemainingDuration());

            if (_affectedPlayer == null || !IsInstanceValid(_affectedPlayer) || _affectedPlayer.IsDead) return;

            _damageTimer += (float)delta;
            if (_damageTimer >= DamageInterval)
            {
                _damageTimer = 0f;
                _affectedPlayer.TakeDamage(DamagePerTick, _area?.GlobalPosition, Actor, DamageSource.AreaEffect);
            }
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

        // ── 核心逻辑 ─────────────────────────────────────────────────────────
        private void Setup()
        {
            _initialized = true;
            EffectId = $"teapot_{Guid.NewGuid()}";
            _speedMultiplier = 1f - SpeedSlowPercent / 100f;

            _area = GetNodeOrNull<Area2D>("Area2D");
            if (_area == null) return;

            _sprite = _area.GetNodeOrNull<Sprite2D>("Sprite2D");

            if (WorldSpawnPosition.HasValue)
                _area.GlobalPosition = WorldSpawnPosition.Value;

            _area.CollisionMask = PlayersLayerMask;
            _area.Monitoring = true;
            _area.BodyEntered += OnBodyEntered;
            _area.BodyExited += OnBodyExited;
        }

        private void OnBodyEntered(Node2D body)
        {
            if (body is not GameActor player) return;
            if (_affectedPlayer == player) return;

            _affectedPlayer = player;
            _damageTimer = 0f;

            // 立即造成首次伤害
            if (!player.IsDead)
                player.TakeDamage(DamagePerTick, _area?.GlobalPosition, Actor, DamageSource.AreaEffect);

            // 施加减速（跳过免疫玩家）
            if (!player.ActiveImmunities.HasFlag(ImmunityFlags.SpeedSlow))
            {
                if (!GlobalSlowState.TryGetValue(player, out var state))
                {
                    // 第一个实例：记录真正的原始速度并施加减速
                    GlobalSlowState[player] = (1, player.Speed);
                    player.Speed = player.Speed * _speedMultiplier;
                }
                else
                {
                    // 已有其他实例正在减速：只增加计数，不再叠加减速
                    GlobalSlowState[player] = (state.Count + 1, state.OriginalSpeed);
                }
                _speedApplied = true;
            }
        }

        private void OnBodyExited(Node2D body)
        {
            if (body is not GameActor player || player != _affectedPlayer) return;
            RestorePlayerSpeed();
            _affectedPlayer = null;
        }

        private void RestorePlayerSpeed()
        {
            if (!_speedApplied || _affectedPlayer == null) return;
            _speedApplied = false;

            if (!GlobalSlowState.TryGetValue(_affectedPlayer, out var state)) return;

            if (state.Count <= 1)
            {
                // 最后一个实例移除：恢复原始速度
                GlobalSlowState.Remove(_affectedPlayer);
                if (IsInstanceValid(_affectedPlayer) && !_affectedPlayer.IsDead)
                    _affectedPlayer.Speed = state.OriginalSpeed;
            }
            else
            {
                // 还有其他实例存活：只减少计数，保持减速
                GlobalSlowState[_affectedPlayer] = (state.Count - 1, state.OriginalSpeed);
            }
        }

        private void UpdateFade(float remaining)
        {
            if (_sprite == null || !IsInstanceValid(_sprite)) return;
            if (FadeOutDuration <= 0f || Duration <= 0f) return;

            float alpha = remaining <= FadeOutDuration
                ? remaining / FadeOutDuration
                : 1f;
            _sprite.Modulate = new Color(1f, 1f, 1f, alpha);
        }

        private void SelfExpire()
        {
            Cleanup();
            QueueFree();
        }

        private void Cleanup()
        {
            if (_area != null && IsInstanceValid(_area))
            {
                _area.BodyEntered -= OnBodyEntered;
                _area.BodyExited -= OnBodyExited;
            }
            RestorePlayerSpeed();
        }
    }
}
