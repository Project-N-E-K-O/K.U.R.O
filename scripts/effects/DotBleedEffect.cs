using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 流血效果：攻击命中目标后附加持续伤害，每 TickInterval 秒造成 DamagePercentPerSecond% 当前生命值的伤害，
    /// 持续 BleedDuration 秒。同一目标重复命中刷新持续时间。
    /// 流血期间目标身上显示血滴特效。切换武器后已存在的流血继续生效。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class DotBleedEffect : ActorEffect
    {
        [ExportGroup("Bleed")]
        /// <summary>每次 Tick 造成的伤害，按目标当前生命值的百分比（%）。</summary>
        [Export(PropertyHint.Range, "0.5,50,0.5")]
        public float DamagePercentPerSecond { get; set; } = 5f;

        [Export(PropertyHint.Range, "0.1,5,0.1")]
        public float TickInterval { get; set; } = 1f;

        [Export(PropertyHint.Range, "1,30,1")]
        public float BleedDuration { get; set; } = 3f;

        [ExportGroup("Visual")]
        [Export] public Vector2 VisualOffset { get; set; } = Vector2.Zero;

        private bool _subscribed;
        private Node2D? _bleedVisualTemplate;
        private readonly Dictionary<GameActor, BleedState> _bleeds = new();

        public DotBleedEffect()
        {
            EffectId = "dot_bleed";
            DisplayName = "流血";
            Description = "攻击命中后对目标造成基于当前生命值的持续流血伤害。";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        public override void _Ready()
        {
            base._Ready();
            _bleedVisualTemplate = GetNodeOrNull<Node2D>("BleedVisual");
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
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor) return;
            if (damage <= 0) return;
            if (target.IsDeathSequenceActive || target.IsDead) return;

            ApplyBleed(target);
        }

        private void ApplyBleed(GameActor target)
        {
            if (_bleeds.TryGetValue(target, out var existing))
            {
                existing.ExpiryTimer.Start(BleedDuration);
                return;
            }

            var tickTimer = new Timer { OneShot = false, WaitTime = TickInterval, Autostart = true };
            var expiryTimer = new Timer { OneShot = true, WaitTime = BleedDuration, Autostart = true };
            Node2D? visual = null;

            if (_bleedVisualTemplate != null)
            {
                visual = _bleedVisualTemplate.Duplicate() as Node2D;
                if (visual != null)
                {
                    visual.Visible = true;
                    visual.Position = GetHitCenterLocal(target) + VisualOffset;
                    if (visual is GpuParticles2D particles)
                        particles.Emitting = true;
                    target.AddChild(visual);
                }
            }

            var capturedTarget = target;
            tickTimer.Timeout += () =>
            {
                if (!IsInstanceValid(capturedTarget) || capturedTarget.IsDeathSequenceActive || capturedTarget.IsDead)
                {
                    CleanupBleed(capturedTarget);
                    return;
                }

                int bleedDamage = Mathf.Max(1,
                    Mathf.RoundToInt(capturedTarget.CurrentHealth * DamagePercentPerSecond / 100f * TickInterval));
                capturedTarget.TakeDamage(bleedDamage, Vector2.Zero);

                if (capturedTarget.IsDeathSequenceActive || capturedTarget.IsDead)
                    CleanupBleed(capturedTarget);
            };

            expiryTimer.Timeout += () => CleanupBleed(capturedTarget);
            target.TreeExiting += () => CleanupBleed(capturedTarget);
            target.DamageTaken += _ =>
            {
                if (capturedTarget.IsDeathSequenceActive || capturedTarget.IsDead)
                    CleanupBleed(capturedTarget);
            };

            target.AddChild(tickTimer);
            target.AddChild(expiryTimer);

            _bleeds[target] = new BleedState { TickTimer = tickTimer, ExpiryTimer = expiryTimer, Visual = visual };

            // 立即造成首次伤害
            int initialDamage = Mathf.Max(1,
                Mathf.RoundToInt(target.CurrentHealth * DamagePercentPerSecond / 100f * TickInterval));
            target.TakeDamage(initialDamage, Vector2.Zero);
        }

        private void CleanupBleed(GameActor target)
        {
            if (!_bleeds.Remove(target, out var state)) return;

            if (IsInstanceValid(state.TickTimer))
                state.TickTimer.QueueFree();
            if (IsInstanceValid(state.ExpiryTimer))
                state.ExpiryTimer.QueueFree();
            KillVisual(state.Visual);
        }

        private void ClearAllBleeds()
        {
            foreach (var target in _bleeds.Keys)
            {
                if (_bleeds.TryGetValue(target, out var state))
                {
                    if (IsInstanceValid(state.TickTimer))
                        state.TickTimer.QueueFree();
                    if (IsInstanceValid(state.ExpiryTimer))
                        state.ExpiryTimer.QueueFree();
                    KillVisual(state.Visual);
                }
            }
            _bleeds.Clear();
        }

        private static void KillVisual(Node2D? visual)
        {
            if (!IsInstanceValid(visual)) return;
            ClearAllParticles(visual);
            visual.QueueFree();
        }

        private static Vector2 GetHitCenterLocal(GameActor target)
        {
            var hitArea = target.GetNodeOrNull<Area2D>("HitArea")
                ?? target.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            var globalCenter = hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? target.GlobalPosition;
            return target.ToLocal(globalCenter);
        }

        private static void ClearAllParticles(Node node)
        {
            if (node is GpuParticles2D p)
            {
                p.Emitting = false;
                p.Amount = 0;
            }
            foreach (var child in node.GetChildren())
                ClearAllParticles(child);
        }

        private sealed class BleedState
        {
            public Timer TickTimer = null!;
            public Timer ExpiryTimer = null!;
            public Node2D? Visual;
        }
    }
}
