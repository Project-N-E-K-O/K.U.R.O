using System;
using System.Collections.Generic;
using Kuros.Core;

namespace Kuros.Core.Events
{
    /// <summary>
    /// 简单的受击事件总线，用于在 GameActor.TakeDamage 后广播命中结果。
    /// </summary>
    public static class DamageEventBus
    {
        public delegate void DamageResolvedHandler(GameActor attacker, GameActor target, int damage);

        private static readonly List<DamageResolvedHandler> Subscribers = new();

        public static void Subscribe(DamageResolvedHandler handler)
        {
            if (handler == null || Subscribers.Contains(handler)) return;
            Subscribers.Add(handler);
        }

        public static void Unsubscribe(DamageResolvedHandler handler)
        {
            if (handler == null) return;
            Subscribers.Remove(handler);
        }

        public static void Publish(GameActor attacker, GameActor target, int damage)
        {
            if (attacker == null || target == null) return;
            foreach (var handler in Subscribers.ToArray())
            {
                handler?.Invoke(attacker, target, damage);
            }
        }
    }
}


