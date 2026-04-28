using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Actors.Heroes;
using Kuros.Companions;

namespace Kuros.Systems.AI
{
    /// <summary>
    /// Collects world/runtime data and exposes AI-friendly state snapshots.
    /// </summary>
    [GlobalClass]
    public partial class GameStateProvider : Node
    {
        [Export] public NodePath PlayerPath { get; set; } = new();
        [Export] public Godot.Collections.Array<NodePath> CompanionPaths { get; set; } = new();
        [Export] public string EnemyGroupName { get; set; } = "enemies";

        [ExportGroup("Under Attack")]
        [Export] public float UnderAttackWindowSeconds { get; set; } = 0.75f;
        [Export] public bool TreatHitStateAsUnderAttack { get; set; } = true;

        private SamplePlayer? _cachedPlayer;

        public GameState CaptureGameState()
        {
            var player = ResolvePlayer();
            if (player == null)
            {
                return new GameState
                {
                    TimestampMs = Time.GetTicksMsec(),
                    PlayerStateName = "missing_player",
                    NearestEnemyDistance = -1f,
                    AverageEnemyDistance = -1f
                };
            }

            var companions = ResolveCompanions(player);
            var (enemyCount, nearestDistance, averageDistance) = ResolveEnemyMetrics(player);
            var (backpackItemCount, backpackOccupiedSlots) = ResolveBackpackMetrics(player);
            var quickBarState = ResolveQuickBarState(player);

            return new GameState
            {
                TimestampMs = Time.GetTicksMsec(),
                PlayerHp = player.CurrentHealth,
                PlayerMaxHp = player.MaxHealth,
                PlayerUnderAttack = ResolvePlayerUnderAttack(player),
                PlayerStateName = player.StateMachine?.CurrentState?.Name ?? string.Empty,
                AliveEnemyCount = enemyCount,
                NearestEnemyDistance = nearestDistance,
                AverageEnemyDistance = averageDistance,
                BackpackItemCount = backpackItemCount,
                BackpackOccupiedSlots = backpackOccupiedSlots,
                QuickBarSlotCount = quickBarState.slotCount,
                QuickBarOccupiedSlots = quickBarState.occupiedSlots,
                SelectedQuickBarSlotIndex = quickBarState.selectedSlotIndex,
                SelectedQuickBarItemId = quickBarState.selectedItemId,
                SelectedQuickBarItemName = quickBarState.selectedItemName,
                QuickBarSlots = quickBarState.slots,
                Companions = companions
            };
        }

        public Godot.Collections.Dictionary<string, Variant> GetAiInputDictionary()
        {
            return CaptureGameState().ToAiInputDictionary();
        }

        public string GetAiInputJson(bool pretty = true)
        {
            return CaptureGameState().ToAiInputJson(pretty);
        }

        public string GetAiPromptText()
        {
            return CaptureGameState().ToAiPromptText();
        }

        private SamplePlayer? ResolvePlayer()
        {
            if (_cachedPlayer != null && IsInstanceValid(_cachedPlayer) && _cachedPlayer.IsInsideTree())
            {
                return _cachedPlayer;
            }

            if (!PlayerPath.IsEmpty)
            {
                _cachedPlayer = GetNodeOrNull<SamplePlayer>(PlayerPath);
                if (_cachedPlayer != null)
                {
                    return _cachedPlayer;
                }

                _cachedPlayer = GetNodeOrNull<SamplePlayer>($"../{PlayerPath}");
                if (_cachedPlayer != null)
                {
                    return _cachedPlayer;
                }
            }

            _cachedPlayer = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
            return _cachedPlayer;
        }

        private List<CompanionState> ResolveCompanions(SamplePlayer player)
        {
            var result = new List<CompanionState>();
            var seenNodes = new HashSet<ulong>();

            if (CompanionPaths.Count > 0)
            {
                foreach (var path in CompanionPaths)
                {
                    if (path == null || path.IsEmpty) continue;
                    var node = GetNodeOrNull<Node>(path) ?? GetNodeOrNull<Node>($"../{path}");
                    if (TryBuildCompanionState(node, player, out CompanionState? state) && state != null)
                    {
                        ulong id = node!.GetInstanceId();
                        if (!seenNodes.Add(id)) continue;
                        result.Add(state);
                    }
                }

                return result;
            }

            var fallbackGroups = new[] { "companions", "allies", "ally", "companion" };
            foreach (string group in fallbackGroups)
            {
                foreach (Node node in GetTree().GetNodesInGroup(group))
                {
                    if (!TryBuildCompanionState(node, player, out CompanionState? state) || state == null)
                    {
                        continue;
                    }

                    ulong id = node.GetInstanceId();
                    if (!seenNodes.Add(id)) continue;
                    result.Add(state);
                }

                if (result.Count > 0)
                {
                    break;
                }
            }

            return result;
        }

        private static bool TryBuildCompanionState(Node? node, SamplePlayer player, out CompanionState? state)
        {
            state = null;
            if (node == null)
            {
                return false;
            }

            if (node is GameActor actor)
            {
                if (actor == player || actor.IsDead || actor.IsDeathSequenceActive)
                {
                    return false;
                }

                state = new CompanionState
                {
                    Name = actor.Name,
                    CurrentHp = actor.CurrentHealth,
                    MaxHp = actor.MaxHealth
                };
                return true;
            }

            if (node is ICompanionStateSource source)
            {
                if (!source.IsCompanionAvailable)
                {
                    return false;
                }

                state = new CompanionState
                {
                    Name = string.IsNullOrWhiteSpace(source.CompanionName) ? node.Name : source.CompanionName,
                    CurrentHp = Mathf.Max(0, source.CurrentHp),
                    MaxHp = Mathf.Max(1, source.MaxHp)
                };
                return true;
            }

            return false;
        }

        private (int count, float nearestDistance, float averageDistance) ResolveEnemyMetrics(SamplePlayer player)
        {
            if (string.IsNullOrWhiteSpace(EnemyGroupName))
            {
                return (0, -1f, -1f);
            }

            int count = 0;
            float distanceSum = 0f;
            float nearest = float.MaxValue;

            foreach (Node node in GetTree().GetNodesInGroup(EnemyGroupName))
            {
                if (node is not GameActor actor) continue;
                if (actor.IsDead || actor.IsDeathSequenceActive) continue;

                count++;
                float distance = player.GlobalPosition.DistanceTo(actor.GlobalPosition);
                distanceSum += distance;
                if (distance < nearest)
                {
                    nearest = distance;
                }
            }

            if (count == 0)
            {
                return (0, -1f, -1f);
            }

            return (count, nearest, distanceSum / count);
        }

        private static (int totalItemCount, int occupiedSlots) ResolveBackpackMetrics(SamplePlayer player)
        {
            var backpack = player.InventoryComponent?.Backpack;
            if (backpack == null)
            {
                return (0, 0);
            }

            int totalItemCount = 0;
            int occupiedSlots = 0;
            foreach (var stack in backpack.Slots)
            {
                if (stack == null || stack.IsEmpty) continue;
                if (stack.Item.ItemId == "empty_item") continue;

                occupiedSlots++;
                totalItemCount += stack.Quantity;
            }

            return (totalItemCount, occupiedSlots);
        }

        private static (int slotCount, int occupiedSlots, int selectedSlotIndex, string selectedItemId, string selectedItemName, List<QuickBarSlotState> slots) ResolveQuickBarState(SamplePlayer player)
        {
            var quickBar = player.InventoryComponent?.QuickBar;
            int selectedSlotIndex = player.InventoryComponent?.SelectedQuickBarSlot ?? -1;
            var slots = new List<QuickBarSlotState>();

            if (quickBar == null)
            {
                return (0, 0, selectedSlotIndex, string.Empty, string.Empty, slots);
            }

            int occupiedSlots = 0;
            string selectedItemId = string.Empty;
            string selectedItemName = string.Empty;

            for (int index = 0; index < quickBar.Slots.Count; index++)
            {
                var stack = quickBar.GetStack(index);
                bool hasItem = stack != null && !stack.IsEmpty && stack.Item.ItemId != "empty_item";
                if (hasItem)
                {
                    occupiedSlots++;
                }

                if (index == selectedSlotIndex && hasItem)
                {
                    selectedItemId = stack!.Item.ItemId;
                    selectedItemName = stack.Item.DisplayName;
                }

                slots.Add(new QuickBarSlotState
                {
                    SlotIndex = index,
                    IsSelected = index == selectedSlotIndex,
                    IsOccupied = hasItem,
                    ItemId = hasItem ? stack!.Item.ItemId : string.Empty,
                    ItemName = hasItem ? stack!.Item.DisplayName : string.Empty,
                    Quantity = hasItem ? stack!.Quantity : 0
                });
            }

            return (quickBar.Slots.Count, occupiedSlots, selectedSlotIndex, selectedItemId, selectedItemName, slots);
        }

        private bool ResolvePlayerUnderAttack(SamplePlayer player)
        {
            if (TreatHitStateAsUnderAttack)
            {
                string stateName = player.StateMachine?.CurrentState?.Name ?? string.Empty;
                if (string.Equals(stateName, "Hit", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return player.GetSecondsSinceLastDamageTaken() <= UnderAttackWindowSeconds;
        }
    }
}
