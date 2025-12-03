using System;

namespace Kuros.Items.Durability
{
    /// <summary>
    /// 运行时耐久度数据，用于跟踪当前剩余耐久度以及损毁状态。
    /// </summary>
    public class ItemDurabilityState
    {
        public ItemDurabilityConfig Config { get; }
        public int CurrentDurability { get; private set; }
        public bool IsBroken { get; private set; }
        public event Action<ItemDurabilityState>? DurabilityDepleted;

        public ItemDurabilityState(ItemDurabilityConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            CurrentDurability = Config.MaxDurability;
        }

        public void Reset()
        {
            CurrentDurability = Config.MaxDurability;
            IsBroken = false;
        }

        public bool ApplyDamage(int amount)
        {
            if (IsBroken || amount <= 0) return false;

            int previous = CurrentDurability;
            CurrentDurability = Math.Max(0, CurrentDurability - amount);
            bool brokeNow = previous > 0 && CurrentDurability == 0;

            if (brokeNow)
            {
                IsBroken = Config.BreakBehavior == DurabilityBreakBehavior.BecomeBroken;
                DurabilityDepleted?.Invoke(this);
            }

            return brokeNow;
        }

        public void Repair(int amount)
        {
            if (!Config.IsRepairable) return;
            if (amount <= 0) return;

            CurrentDurability = Math.Min(Config.MaxDurability, CurrentDurability + amount);
            if (CurrentDurability > 0)
            {
                IsBroken = false;
            }
        }
    }
}

