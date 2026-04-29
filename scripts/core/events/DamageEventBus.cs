using System;
using System.Collections.Generic;
using Kuros.Core;

namespace Kuros.Core.Events
{
    /// <summary>
    /// 伤害来源类型，用于区分不同来源的伤害事件。
    /// </summary>
    public enum DamageSource
    {
        /// <summary>近战/武器直接攻击</summary>
        DirectAttack,
        /// <summary>持续区域效果（如 SpikeAttackEffect）</summary>
        AreaEffect,
        /// <summary>投掷物命中</summary>
        ThrowImpact,
        /// <summary>暴击追加伤害（如 MechGloveEffect 全中暴击）</summary>
        CritBonus,
        /// <summary>效果/构筑追加伤害（如 BuildMachineLevel3），不触发武器主动词条如暴击</summary>
        EffectBonus,
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
                handler?.Invoke(attacker, target, damage);
            }

            foreach (var handler in sourcedSnapshot)
            {
                handler?.Invoke(attacker, target, damage, source);
            }
        }
    }
}


