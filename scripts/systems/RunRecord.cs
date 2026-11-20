using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items;
using Kuros.Managers;
using Dictionary = Godot.Collections.Dictionary;

namespace Kuros.Systems;

public enum RunOutcome { None, Cleared, NotCleared }

internal sealed record RunCard(string Id, string Name, string Description, string IconPath, BuildRarity Rarity, int Stacks, bool Active)
{
    internal static RunCard FromEffect(BuildEffectDefinition effect, int stacks, bool active) =>
        new(effect.EffectId, effect.DisplayName, effect.BuildDescriptionWithValues(stacks - 1), effect.Icon?.ResourcePath ?? "", effect.Rarity, stacks, active);
}

internal sealed record RunItem(string Id, string Name, string DefinitionPath, string IconPath, int Quantity);

internal sealed record RunRecord(string Id, long FinishedAt, int Seconds, RunOutcome Outcome, int Gold,
    IReadOnlyList<RunCard> Cards, IReadOnlyList<RunItem> Items)
{
    internal Dictionary Encode()
    {
        var cards = new Godot.Collections.Array();
        foreach (var card in Cards)
            cards.Add(new Dictionary { ["id"] = card.Id, ["name"] = card.Name, ["text"] = card.Description, ["icon"] = card.IconPath,
                ["rarity"] = (int)card.Rarity, ["stacks"] = card.Stacks, ["active"] = card.Active });

        var items = new Godot.Collections.Array();
        foreach (var item in Items)
            items.Add(new Dictionary { ["id"] = item.Id, ["name"] = item.Name, ["definition"] = item.DefinitionPath,
                ["icon"] = item.IconPath, ["quantity"] = item.Quantity });

        return new Dictionary { ["id"] = Id, ["time"] = FinishedAt, ["seconds"] = Seconds,
            ["outcome"] = (int)Outcome, ["gold"] = Gold, ["cards"] = cards, ["items"] = items };
    }

    internal static RunRecord? Decode(Dictionary data)
    {
        string id = data.GetValueOrDefault("id", "").AsString();
        var outcome = (RunOutcome)data.GetValueOrDefault("outcome", 0).AsInt32();
        long finishedAt = data.GetValueOrDefault("time", 0).AsInt64();
        if (string.IsNullOrEmpty(id) || finishedAt is < 0 or > 253402300799L
            || outcome is not (RunOutcome.Cleared or RunOutcome.NotCleared)) return null;

        var cards = new List<RunCard>();
        foreach (var entry in ReadEntries(data, "cards"))
            cards.Add(new RunCard(entry.GetValueOrDefault("id", "").AsString(), entry.GetValueOrDefault("name", "").AsString(),
                entry.GetValueOrDefault("text", "").AsString(),
                entry.GetValueOrDefault("icon", "").AsString(), (BuildRarity)entry.GetValueOrDefault("rarity", 0).AsInt32(),
                entry.GetValueOrDefault("stacks", 1).AsInt32(), entry.GetValueOrDefault("active", true).AsBool()));

        var items = new List<RunItem>();
        foreach (var entry in ReadEntries(data, "items"))
            items.Add(new RunItem(entry.GetValueOrDefault("id", "").AsString(), entry.GetValueOrDefault("name", "").AsString(),
                entry.GetValueOrDefault("definition", "").AsString(), entry.GetValueOrDefault("icon", "").AsString(),
                entry.GetValueOrDefault("quantity", 0).AsInt32()));

        return new RunRecord(id, finishedAt, Math.Max(0, data.GetValueOrDefault("seconds", 0).AsInt32()),
            outcome, data.GetValueOrDefault("gold", 0).AsInt32(), cards, items);
    }

    private static IEnumerable<Dictionary> ReadEntries(Dictionary data, string key)
    {
        if (!data.TryGetValue(key, out var list) || list.VariantType != Variant.Type.Array) yield break;
        foreach (var value in list.AsGodotArray())
            if (value.VariantType == Variant.Type.Dictionary) yield return value.AsGodotDictionary();
    }
}

internal sealed class RunSession(double startedAt)
{
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, RunItem> _items = new(StringComparer.Ordinal);
    private int _gold;

    internal RunRecord? FinalRecord { get; private set; }
    internal bool CountDeath { get; private set; }

    internal void AddGold(int amount)
    {
        if (FinalRecord == null) _gold += amount;
    }

    internal void AddItem(ItemDefinition item, int quantity)
    {
        if (FinalRecord != null || quantity <= 0 || string.IsNullOrEmpty(item.ItemId) || item.ItemId == "empty_item") return;
        _items.TryGetValue(item.ItemId, out var previous);
        _items[item.ItemId] = new RunItem(item.ItemId, item.DisplayName, item.ResourcePath,
            item.Icon?.ResourcePath ?? "", (previous?.Quantity ?? 0) + quantity);
    }

    internal RunRecord Finish(RunOutcome outcome, bool died, double endTime)
    {
        if (FinalRecord is { } record) return record;
        var cards = new List<RunCard>();
        var builds = BuildSelectionManager.Instance;
        if (builds != null)
        {
            if (builds.SelectedCore is { } core)
                cards.Add(new RunCard(core.CoreId, core.DisplayName, core.Description, core.Icon?.ResourcePath ?? "",
                    BuildRarity.Core, 1, true));
            foreach (var pick in builds.PickHistory)
                if (builds.FindEffectById(pick.EffectId) is { } effect)
                    cards.Add(RunCard.FromEffect(effect, pick.Stacks, pick.Active));
        }

        CountDeath = died;
        return FinalRecord = new RunRecord(_id, DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            (int)Math.Max(0, endTime - startedAt), outcome, _gold, cards, new List<RunItem>(_items.Values));
    }
}
