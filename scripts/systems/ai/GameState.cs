using System;
using System.Collections.Generic;
using Godot;

namespace Kuros.Systems.AI
{
    public sealed class QuickBarSlotState
    {
        public int SlotIndex { get; init; }
        public bool IsSelected { get; init; }
        public bool IsOccupied { get; init; }
        public string ItemId { get; init; } = string.Empty;
        public string ItemName { get; init; } = string.Empty;
        public int Quantity { get; init; }

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["slot_index"] = SlotIndex,
                ["is_selected"] = IsSelected,
                ["is_occupied"] = IsOccupied,
                ["item_id"] = ItemId,
                ["item_name"] = ItemName,
                ["quantity"] = Quantity
            };
        }
    }

    public sealed class CompanionState
    {
        public string Name { get; init; } = string.Empty;
        public int CurrentHp { get; init; }
        public int MaxHp { get; init; }

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["name"] = Name,
                ["current_hp"] = CurrentHp,
                ["max_hp"] = MaxHp
            };
        }
    }

    /// <summary>
    /// Runtime game-state abstraction for AI decision making.
    /// </summary>
    public sealed class GameState
    {
        public ulong TimestampMs { get; init; }

        public int PlayerHp { get; init; }
        public int PlayerMaxHp { get; init; }
        public bool PlayerUnderAttack { get; init; }
        public string PlayerStateName { get; init; } = string.Empty;

        public int AliveEnemyCount { get; init; }
        public float NearestEnemyDistance { get; init; }
        public float AverageEnemyDistance { get; init; }

        public int BackpackItemCount { get; init; }
        public int BackpackOccupiedSlots { get; init; }
        public int QuickBarSlotCount { get; init; }
        public int QuickBarOccupiedSlots { get; init; }
        public int SelectedQuickBarSlotIndex { get; init; } = -1;
        public string SelectedQuickBarItemId { get; init; } = string.Empty;
        public string SelectedQuickBarItemName { get; init; } = string.Empty;
        public List<QuickBarSlotState> QuickBarSlots { get; init; } = new();

        public List<CompanionState> Companions { get; init; } = new();

        public int CompanionCount => Companions.Count;

        public Godot.Collections.Dictionary<string, Variant> ToAiInputDictionary()
        {
            var companions = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
            var quickBarSlots = new Godot.Collections.Array<Godot.Collections.Dictionary<string, Variant>>();
            int companionTotalHp = 0;
            int companionTotalMaxHp = 0;

            foreach (var companion in Companions)
            {
                companions.Add(companion.ToDictionary());
                companionTotalHp += companion.CurrentHp;
                companionTotalMaxHp += companion.MaxHp;
            }

            foreach (var slot in QuickBarSlots)
            {
                quickBarSlots.Add(slot.ToDictionary());
            }

            return new Godot.Collections.Dictionary<string, Variant>
            {
                ["timestamp_ms"] = TimestampMs,
                ["player"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["hp"] = PlayerHp,
                    ["max_hp"] = PlayerMaxHp,
                    ["under_attack"] = PlayerUnderAttack,
                    ["state"] = PlayerStateName
                },
                ["companions"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["count"] = CompanionCount,
                    ["total_hp"] = companionTotalHp,
                    ["total_max_hp"] = companionTotalMaxHp,
                    ["members"] = companions
                },
                ["enemies"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["alive_count"] = AliveEnemyCount,
                    ["nearest_distance"] = NearestEnemyDistance,
                    ["average_distance"] = AverageEnemyDistance
                },
                ["inventory"] = new Godot.Collections.Dictionary<string, Variant>
                {
                    ["backpack_item_count"] = BackpackItemCount,
                    ["backpack_occupied_slots"] = BackpackOccupiedSlots,
                    ["quickbar_slot_count"] = QuickBarSlotCount,
                    ["quickbar_occupied_slots"] = QuickBarOccupiedSlots,
                    ["selected_quickbar_slot_index"] = SelectedQuickBarSlotIndex,
                    ["selected_quickbar_item_id"] = SelectedQuickBarItemId,
                    ["selected_quickbar_item_name"] = SelectedQuickBarItemName,
                    ["quickbar_slots"] = quickBarSlots
                }
            };
        }

        public string ToAiInputJson(bool pretty = true)
        {
            return Json.Stringify(ToAiInputDictionary(), pretty ? "  " : string.Empty);
        }

        public string ToAiPromptText()
        {
            return string.Join("\n", new[]
            {
                "[GameState]",
                $"player.hp={PlayerHp}/{PlayerMaxHp}",
                $"player.under_attack={PlayerUnderAttack}",
                $"player.state={PlayerStateName}",
                $"companions.count={CompanionCount}",
                $"enemies.alive_count={AliveEnemyCount}",
                $"enemies.nearest_distance={NearestEnemyDistance:F2}",
                $"enemies.average_distance={AverageEnemyDistance:F2}",
                $"inventory.backpack_item_count={BackpackItemCount}",
                $"inventory.backpack_occupied_slots={BackpackOccupiedSlots}",
                $"inventory.quickbar_slot_count={QuickBarSlotCount}",
                $"inventory.quickbar_occupied_slots={QuickBarOccupiedSlots}",
                $"inventory.selected_quickbar_slot_index={SelectedQuickBarSlotIndex}",
                $"inventory.selected_quickbar_item_id={SelectedQuickBarItemId}",
                $"inventory.selected_quickbar_item_name={SelectedQuickBarItemName}",
                "output_format=json"
            });
        }
    }
}
