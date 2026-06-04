using Godot;
using System.Collections.Generic;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 流血效果：攻击命中敌人后附加持续伤害，每 TickInterval 秒造成 DamagePerTick 点伤害，
    /// 持续 BleedDuration 秒。同一敌人重复命中刷新持续时间。
    /// 流血期间敌人身上显示血滴特效。切换武器后已存在的流血继续生效。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class DotBleedEffect : ActorEffect
    {
        [ExportGroup("Bleed")]
        [Export(PropertyHint.Range, "1,500,1")]
        public int DamagePerTick { get; set; } = 5;

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
            Description = "攻击命中后对敌人造成持续流血伤害。";
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
            if (target.IsDead) return;

            ApplyBleed(target);
        }

        private void ApplyBleed(GameActor enemy)
        {
            if (_bleeds.TryGetValue(enemy, out var existing))
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
                    visual.Position = GetHitCenterLocal(enemy) + VisualOffset;
                    if (visual is GpuParticles2D particles)
                        particles.Emitting = true;
                    enemy.AddChild(visual);
                }
            }

            var capturedEnemy = enemy;
            tickTimer.Timeout += () =>
            {
                if (!IsInstanceValid(capturedEnemy) || capturedEnemy.IsDead)
                {
                    CleanupBleed(capturedEnemy);
                    return;
                }
                capturedEnemy.TakeDamage(DamagePerTick, Vector2.Zero);
                if (capturedEnemy.IsDead)
                    CleanupBleed(capturedEnemy);
            };

            expiryTimer.Timeout += () => CleanupBleed(capturedEnemy);
            enemy.TreeExiting += () => CleanupBleed(capturedEnemy);
            enemy.DamageTaken += _ =>
            {
                if (capturedEnemy.IsDead)
                    CleanupBleed(capturedEnemy);
            };

            enemy.AddChild(tickTimer);
            enemy.AddChild(expiryTimer);

            _bleeds[enemy] = new BleedState { TickTimer = tickTimer, ExpiryTimer = expiryTimer, Visual = visual };

            enemy.TakeDamage(DamagePerTick, Vector2.Zero);
        }

        private void CleanupBleed(GameActor enemy)
        {
            if (!_bleeds.Remove(enemy, out var state)) return;

            if (IsInstanceValid(state.TickTimer))
                state.TickTimer.QueueFree();
            if (IsInstanceValid(state.ExpiryTimer))
                state.ExpiryTimer.QueueFree();
            KillVisual(state.Visual);
        }

        private void ClearAllBleeds()
        {
            foreach (var enemy in _bleeds.Keys)
            {
                if (_bleeds.TryGetValue(enemy, out var state))
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

        private static Vector2 GetHitCenterLocal(GameActor enemy)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            var globalCenter = hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? enemy.GlobalPosition;
            return enemy.ToLocal(globalCenter);
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
