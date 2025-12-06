using System;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;
using Kuros.Items.Attributes;
using Kuros.Systems.Inventory;

namespace Kuros.Effects
{
    /// <summary>
    /// 每次攻击命中后为指定武器永久追加攻击力。
    /// </summary>
    [GlobalClass]
    public partial class IncrementAttackPowerEffect : ActorEffect
    {
        [Export] public string TargetItemId { get; set; } = "weapon_a0_neiku";
        [Export(PropertyHint.Range, "0.1,50,0.1")] public float AttackPowerPerAttack { get; set; } = 1f;
        [Export(PropertyHint.Range, "0,1000,1")] public float MaxBonusAttackPower { get; set; } = 0f;

        private PlayerInventoryComponent? _inventory;
        private ulong _lastAttackFrame;

        protected override void OnApply()
        {
            base.OnApply();
            _inventory = ResolveInventory();
            if (_inventory == null)
            {
                GD.PushWarning($"{Name}: 未找到玩家背包组件，移除 {nameof(IncrementAttackPowerEffect)}。");
                Controller?.RemoveEffect(this);
                return;
            }

            DamageEventBus.Subscribe(OnDamageResolved);
        }

        public override void OnRemoved()
        {
            DamageEventBus.Unsubscribe(OnDamageResolved);
            base.OnRemoved();
        }

        private PlayerInventoryComponent? ResolveInventory()
        {
            if (Actor is SamplePlayer player)
            {
                return player.InventoryComponent ?? player.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            }

            return Actor.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage)
        {
            if (AttackPowerPerAttack <= 0f) return;
            if (attacker != Actor) return;
            if (!IsTargetWeaponSelected()) return;

            ulong frame = Engine.GetProcessFrames();
            if (_lastAttackFrame == frame) return;
            _lastAttackFrame = frame;

            ApplyIncrement();
        }

        private bool IsTargetWeaponSelected()
        {
            var stack = _inventory?.GetSelectedBackpackStack();
            if (stack == null || stack.Item == null) return false;
            return string.Equals(stack.Item.ItemId, TargetItemId, StringComparison.OrdinalIgnoreCase);
        }

        private InventoryItemStack? ResolveTargetStack()
        {
            if (_inventory?.Backpack == null) return null;

            var stack = _inventory.GetSelectedBackpackStack();
            if (stack != null && stack.Item != null &&
                string.Equals(stack.Item.ItemId, TargetItemId, StringComparison.OrdinalIgnoreCase))
            {
                return stack;
            }

            foreach (var candidate in _inventory.Backpack.Slots)
            {
                if (candidate == null || candidate.Item == null) continue;
                if (string.Equals(candidate.Item.ItemId, TargetItemId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }

        private void ApplyIncrement()
        {
            var stack = ResolveTargetStack();
            if (stack == null) return;

            float newValue = stack.AddRuntimeAttributeValue(ItemAttributeIds.AttackPower, AttackPowerPerAttack);

            if (MaxBonusAttackPower > 0f && newValue > MaxBonusAttackPower)
            {
                newValue = MaxBonusAttackPower;
                stack.SetRuntimeAttributeValue(ItemAttributeIds.AttackPower, MaxBonusAttackPower);
            }
        }
    }
}

