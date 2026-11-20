using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Kuros.Systems.Stage;

public static class RoomRoutePlanner
{
    public static List<RoomType>? Plan(StageConfig config, Dictionary<string, RoomType> rooms,
        RandomNumberGenerator rng, IReadOnlyDictionary<RoomType, int>? misses)
    {
        var capacity = rooms.Values.GroupBy(type => type).ToDictionary(group => group.Key, group => group.Count());
        var rules = new Dictionary<RoomType, RoomTypeRule>();
        foreach (var rule in config.MiddleRoomRules)
        {
            if (rule == null || !rules.TryAdd(rule.RoomType, rule) || rule.MinCount < 0
                || rule.MaxCount < -1 || !float.IsFinite(rule.Weight) || rule.Weight < 0
                || rule.RoomType is not (RoomType.Combat or RoomType.AdvancedCombat or RoomType.EliteCombat
                    or RoomType.Puzzle or RoomType.Shop or RoomType.Healing)
                || (rule.MaxCount >= 0 && rule.MinCount > rule.MaxCount))
                return Fail(config);
        }
        var minimum = rules.ToDictionary(pair => pair.Key, pair => pair.Value.MinCount);
        int guaranteeSlots = config.MiddleRoomCount - minimum.Values.Sum();
        // 同时到期时优先处理缺席更久的类型，容量不足的保底保留到下一层。
        foreach (var rule in rules.Values.OrderByDescending(rule => misses?.GetValueOrDefault(rule.RoomType) ?? 0)
                     .ThenBy(rule => rule.RoomType))
            if (rule.GuaranteeAfter > 0 && rule.MaxCount != 0 && capacity.ContainsKey(rule.RoomType)
                && misses != null && misses.TryGetValue(rule.RoomType, out int count)
                && count >= rule.GuaranteeAfter && minimum[rule.RoomType] == 0 && guaranteeSlots > 0)
            {
                minimum[rule.RoomType] = 1;
                guaranteeSlots--;
            }
        if (minimum.Any(pair => pair.Value > capacity.GetValueOrDefault(pair.Key))
            || minimum.Values.Sum() > config.MiddleRoomCount) return Fail(config);

        var result = new List<RoomType>();
        var counts = new Dictionary<RoomType, int>();
        int budget = 20000;
        bool Fill()
        {
            if (--budget <= 0) return false;
            int remaining = config.MiddleRoomCount - result.Count;
            if (minimum.Sum(pair => System.Math.Max(0, pair.Value - counts.GetValueOrDefault(pair.Key))) > remaining)
                return false;
            if (remaining == 0) return true;
            var options = new List<(RoomType Type, double Rank)>();
            foreach (var (type, available) in capacity)
            {
                int count = counts.GetValueOrDefault(type);
                rules.TryGetValue(type, out var rule);
                int max = rule?.MaxCount ?? -1;
                if (count >= available || (max >= 0 && count >= max)) continue;
                float weight = rule?.Weight ?? (rules.Count == 0 ? available - count : 1f);
                bool needed = count < minimum.GetValueOrDefault(type);
                if (weight <= 0 && !needed) continue;
                int limit = config.MaxConsecutiveRoomType;
                if (limit > 0 && result.Count >= limit && result.Skip(result.Count - limit).All(item => item == type))
                    continue;
                double rank = -System.Math.Log(System.Math.Max(0.000001, rng.Randf())) / System.Math.Max(weight, 0.000001f);
                options.Add((type, rank));
            }
            foreach (var option in options.OrderBy(item => item.Rank))
            {
                result.Add(option.Type);
                counts[option.Type] = counts.GetValueOrDefault(option.Type) + 1;
                if (Fill()) return true;
                counts[option.Type]--;
                result.RemoveAt(result.Count - 1);
            }
            return false;
        }
        return Fill() ? result : Fail(config);
    }

    private static List<RoomType>? Fail(StageConfig config)
    {
        GD.PushWarning($"[RoomRoutePlanner] {config.StageId} 的房间数量、候选容量、保底或连续类型限制无法满足。");
        return null;
    }
}
