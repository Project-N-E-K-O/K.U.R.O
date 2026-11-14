using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Fx;

namespace Kuros.Effects
{
    /// <summary>
    /// 地面持续伤害 + 减速区域效果（世界空间 Node2D 效果，阵营无关）。
    /// 任何进入 Area2D 的 GameActor 都会受到持续伤害和减速。
    /// TargetCollisionMask 决定影响哪些物理层（支持多选）。
    ///
    /// 使用 Node2D 而非 ActorEffect：投掷落点的世界效果，不参与 EffectId 去重与
    /// Actor 生命周期绑定——多投掷各自独立区域。
    /// </summary>
    [GlobalClass]
    public partial class SlowHitAreaEffect : Node2D, IAttackerProvider
    {
        // ── 伤害 ─────────────────────────────────────────────────────────────
        [Export(PropertyHint.Range, "1,999,1")]
        public int DamagePerTick { get; set; } = 5;

        [Export(PropertyHint.Range, "0.1,10,0.1")]
        public float DamageInterval { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0,600,0.1")] public float Duration { get; set; } = 5.0f;

        // ── 减速 ─────────────────────────────────────────────────────────────
        [Export(PropertyHint.Range, "0,100,1")]
        public float SpeedSlowPercent { get; set; } = 30f;

        // ── 目标层 ───────────────────────────────────────────────────────────
        /// <summary>
        /// 受影响的碰撞层掩码。Layer 2 (enemies) = 2, Layer 3 (player) = 4。
        /// 同时影响两层：2 | 4 = 6。
        /// </summary>
        [Export(PropertyHint.Layers2DPhysics)]
        public uint TargetCollisionMask { get; set; } = 2u;

        // ── 视觉 ─────────────────────────────────────────────────────────────
        /// <summary>到期前淡出时长（秒）。0 = 不淡出。</summary>
        [Export(PropertyHint.Range, "0,30,0.1")]
        public float FadeOutDuration { get; set; } = 0f;

        /// <summary>投掷者（由投掷系统 IAttackerProvider 注入）。</summary>
        public GameActor? Attacker { get; set; }

        /// <summary>
        /// 每次本区域对该目标造成伤害后触发——通用扩展点，供外部组件附加命中效果（如流血）。
        /// 未订阅时零成本，复用本脚本的场景不受影响。
        /// </summary>
        public event Action<GameActor>? DamageDealtTo;

        // ── 私有状态 ─────────────────────────────────────────────────────────
        private Area2D? _area;
        private Sprite2D? _sprite;
        private float _speedMultiplier;
        private bool _initialized;
        private bool _areaSignalsConnected;

        private readonly Dictionary<GameActor, float> _actorTimers = new();
        private readonly Dictionary<GameActor, float> _appliedMultipliers = new();

        private float _elapsed;

        // ── 生命周期 ─────────────────────────────────────────────────────────
        public override void _Ready()
        {
            if (!_initialized)
                Setup();
        }

        public override void _Process(double delta)
        {
            if (!_initialized)
                return;

            _elapsed += (float)delta;
            TickAllActors((float)delta);

            if (Duration > 0f)
            {
                UpdateFade(Mathf.Max(Duration - _elapsed, 0f));
                if (_elapsed >= Duration)
                {
                    Cleanup();
                    QueueFree();
                }
            }
        }

        public override void _ExitTree()
        {
            Cleanup();
            base._ExitTree();
        }

        // ── 初始化 ───────────────────────────────────────────────────────────
        private void Setup()
        {
            _initialized = true;
            _speedMultiplier = 1f - SpeedSlowPercent / 100f;

            _area = GetNodeOrNull<Area2D>("Area2D");
            if (_area == null) return;

            _sprite = _area.GetNodeOrNull<Sprite2D>("Sprite2D");

            // 根已由投掷系统定位到落点，Area2D 相对根自动就位
            _area.CollisionMask = TargetCollisionMask;
            _area.Monitoring = true;
            _area.BodyEntered += OnBodyEntered;
            _area.BodyExited += OnBodyExited;
            _areaSignalsConnected = true;
        }

        // ── 碰撞回调 ─────────────────────────────────────────────────────────
        private void OnBodyEntered(Node2D body)
        {
            if (body is not GameActor actor) return;
            if (_actorTimers.ContainsKey(actor)) return;

            _actorTimers[actor] = 0f;

            if (!actor.IsDeadOrDying)
            {
                actor.TakeDamage(DamagePerTick, _area?.GlobalPosition, Attacker,
                    DamageSource.AreaEffect);
                DamageDealtTo?.Invoke(actor);
            }

            if (!actor.ActiveImmunities.HasFlag(ImmunityFlags.SpeedSlow))
                ApplySpeedMultiplier(actor, _speedMultiplier);
        }

        private void OnBodyExited(Node2D body)
        {
            if (body is not GameActor actor) return;
            RemoveActor(actor);
        }

        // ── 伤害计时 ─────────────────────────────────────────────────────────
        private void TickAllActors(float delta)
        {
            if (_actorTimers.Count == 0) return;

            var toRemove = new List<GameActor>();
            foreach (var kvp in _actorTimers)
            {
                var actor = kvp.Key;
                if (!IsInstanceValid(actor) || actor.IsDeadOrDying)
                {
                    toRemove.Add(actor);
                    continue;
                }

                _actorTimers[actor] = kvp.Value + delta;
                if (_actorTimers[actor] >= DamageInterval)
                {
                    _actorTimers[actor] = 0f;
                    actor.TakeDamage(DamagePerTick, _area?.GlobalPosition, Attacker,
                        DamageSource.AreaEffect);
                    DamageDealtTo?.Invoke(actor);
                }
            }

            foreach (var a in toRemove)
                RemoveActor(a);
        }

        private void RemoveActor(GameActor actor)
        {
            _actorTimers.Remove(actor);

            if (_appliedMultipliers.TryGetValue(actor, out float mult))
            {
                _appliedMultipliers.Remove(actor);
                SharedSpeedSlowManager.Remove(actor, mult);
            }
        }

        // ── 减速(全局共享叠加,最小乘数生效,见 SharedSpeedSlowManager)────────────
        private void ApplySpeedMultiplier(GameActor actor, float multiplier)
        {
            _appliedMultipliers[actor] = multiplier;
            SharedSpeedSlowManager.Apply(actor, multiplier);
        }

        // ── 视觉 ─────────────────────────────────────────────────────────────
        private void UpdateFade(float remaining)
        {
            if (_sprite == null || !IsInstanceValid(_sprite)) return;
            if (FadeOutDuration <= 0f || Duration <= 0f) return;

            float alpha = remaining <= FadeOutDuration
                ? remaining / FadeOutDuration
                : 1f;
            _sprite.Modulate = new Color(1f, 1f, 1f, alpha);
        }

        // ── 清理 ─────────────────────────────────────────────────────────────
        private void Cleanup()
        {
            if (_areaSignalsConnected && _area != null && IsInstanceValid(_area))
            {
                _area.BodyEntered -= OnBodyEntered;
                _area.BodyExited -= OnBodyExited;
            }
            _areaSignalsConnected = false; // 到期与 ExitTree 都会调用，重复清理不能再次断开信号。

            foreach (var actor in _appliedMultipliers.Keys.ToList())
            {
                if (_appliedMultipliers.TryGetValue(actor, out float mult))
                    SharedSpeedSlowManager.Remove(actor, mult);
            }

            _actorTimers.Clear();
            _appliedMultipliers.Clear();
        }
    }
}
