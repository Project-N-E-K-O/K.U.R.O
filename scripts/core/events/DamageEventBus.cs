using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;

namespace Kuros.Core.Events
{
    /// <summary>
    /// 伤害来源类型，用于区分不同来源的伤害事件。
    /// </summary>
    public enum DamageSource
    {
        /// <summary>普通武器直接攻击（!IsThrowable）</summary>
        DirectAttack,
        /// <summary>投掷武器直接攻击（IsThrowable 武器近战）</summary>
        ThrowableDirectAttack,
        /// <summary>持续区域效果（如 SpikeAttackEffect）</summary>
        AreaEffect,
        /// <summary>投掷物命中</summary>
        ThrowImpact,
        /// <summary>暴击追加伤害（如 MechGloveEffect 全中暴击）</summary>
        CritBonus,
        /// <summary>效果/构筑追加伤害（如 BuildMachineLevel3），不触发武器主动词条如暴击</summary>
        EffectBonus,
        /// <summary>深坑固定比例惩罚，不经过普通攻击免伤与伤害拦截。</summary>
        EnvironmentalFall,
    }

    /// <summary>
    /// 伤害来源过滤（Flags）：按位匹配 <see cref="DamageSource"/> 分类，
    /// 用于"仅特定伤害类型触发"的开关（如攻击眩晕打断只吃投掷伤害）。
    /// </summary>
    [Flags]
    public enum DamageSourceFilter
    {
        None = 0,
        /// <summary>普通近战（DirectAttack）</summary>
        DirectAttack = 1 << 0,
        /// <summary>投掷类（ThrowableDirectAttack + ThrowImpact：投掷武器近战 + 投掷物命中）</summary>
        Throwable = 1 << 1,
        /// <summary>区域效果（AreaEffect）</summary>
        AreaEffect = 1 << 2,
        /// <summary>暴击追加（CritBonus）</summary>
        CritBonus = 1 << 3,
        /// <summary>效果/构筑追加（EffectBonus）</summary>
        EffectBonus = 1 << 4,
        All = DirectAttack | Throwable | AreaEffect | CritBonus | EffectBonus,
    }

    public static class DamageSourceFilterExtensions
    {
        /// <summary>判断某伤害来源是否命中过滤开关（All 恒命中，保持默认全放行）。</summary>
        public static bool Matches(this DamageSourceFilter filter, DamageSource source)
        {
            if (filter == DamageSourceFilter.All) return true;
            return source switch
            {
                DamageSource.DirectAttack => filter.HasFlag(DamageSourceFilter.DirectAttack),
                DamageSource.ThrowableDirectAttack or DamageSource.ThrowImpact => filter.HasFlag(DamageSourceFilter.Throwable),
                DamageSource.AreaEffect => filter.HasFlag(DamageSourceFilter.AreaEffect),
                DamageSource.CritBonus => filter.HasFlag(DamageSourceFilter.CritBonus),
                DamageSource.EffectBonus => filter.HasFlag(DamageSourceFilter.EffectBonus),
                _ => true,
            };
        }
    }

    /// <summary>
    /// 简单的受击事件总线，用于在 GameActor.TakeDamage 后广播命中结果。
    /// </summary>
    public static class DamageEventBus
    {
        public delegate void DamageResolvedHandler(GameActor attacker, GameActor target, int damage);

        /// <summary>包含伤害来源的订阅委托。</summary>
        public delegate void DamageResolvedWithSourceHandler(GameActor attacker, GameActor target, int damage, DamageSource source);

        private static readonly List<DamageResolvedHandler> Subscribers = new();
        private static readonly List<DamageResolvedWithSourceHandler> SourcedSubscribers = new();
        private static readonly object SubscribersLock = new();

        public static void Subscribe(DamageResolvedHandler handler)
        {
            if (handler == null) return;
            lock (SubscribersLock)
            {
                if (!Subscribers.Contains(handler))
                {
                    Subscribers.Add(handler);
                }
            }
        }

        public static void Unsubscribe(DamageResolvedHandler handler)
        {
            if (handler == null) return;
            lock (SubscribersLock)
            {
                Subscribers.Remove(handler);
            }
        }

        /// <summary>订阅包含伤害来源的事件，适合需要区分伤害类型的效果。</summary>
        public static void SubscribeWithSource(DamageResolvedWithSourceHandler handler)
        {
            if (handler == null) return;
            lock (SubscribersLock)
            {
                if (!SourcedSubscribers.Contains(handler))
                {
                    SourcedSubscribers.Add(handler);
                }
            }
        }

        /// <summary>取消订阅包含伤害来源的事件。</summary>
        public static void UnsubscribeWithSource(DamageResolvedWithSourceHandler handler)
        {
            if (handler == null) return;
            lock (SubscribersLock)
            {
                SourcedSubscribers.Remove(handler);
            }
        }

        public static void Publish(GameActor attacker, GameActor target, int damage, DamageSource source = DamageSource.DirectAttack)
        {
            if (attacker == null || target == null) return;

            List<DamageResolvedHandler> snapshot;
            List<DamageResolvedWithSourceHandler> sourcedSnapshot;
            lock (SubscribersLock)
            {
                snapshot = Subscribers.Count > 0
                    ? new List<DamageResolvedHandler>(Subscribers)
                    : new List<DamageResolvedHandler>();
                sourcedSnapshot = SourcedSubscribers.Count > 0
                    ? new List<DamageResolvedWithSourceHandler>(SourcedSubscribers)
                    : new List<DamageResolvedWithSourceHandler>();
            }

            foreach (var handler in snapshot)
            {
                try { handler?.Invoke(attacker, target, damage); }
                catch (System.Exception ex)
                {
                    GD.PushWarning($"[DamageEventBus] 订阅者异常（已隔离，不影响其他订阅者）: {ex.Message}");
                }
            }

            foreach (var handler in sourcedSnapshot)
            {
                try { handler?.Invoke(attacker, target, damage, source); }
                catch (System.Exception ex)
                {
                    GD.PushWarning($"[DamageEventBus] 带来源订阅者异常（已隔离，不影响其他订阅者）: {ex.Message}");
                }
            }
        }
    }
}


