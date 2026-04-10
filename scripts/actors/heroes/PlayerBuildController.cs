using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Items;
using Kuros.Systems.Inventory;

namespace Kuros.Actors.Heroes
{
    /// <summary>
    /// 玩家构筑控制器：统计指定构筑点数，并按配置条目挂载层级效果。
    /// </summary>
    public partial class PlayerBuildController : Node
    {
        [Export] public PlayerInventoryComponent? Inventory { get; set; }
        [Export] public EffectController? TargetEffectController { get; set; }

        [ExportGroup("Build Config")]
        [Export] public string BuildClass { get; set; } = string.Empty;
        [Export] public string BuildTagFallback { get; set; } = string.Empty;
        [Export] public Godot.Collections.Array<string> TrackedItemIds { get; set; } = new();
        [ExportGroup("Build Level Entries")]
        [Export] public Godot.Collections.Array<BuildLevelEffectEntry> LevelEntries { get; set; } = new();

        public int CurrentBuildCount { get; private set; }
        public int CurrentBuildLevel { get; private set; }

        public event Action<int>? BuildCountChanged;
        public event Action<int>? BuildLevelChanged;

        private SamplePlayer? _player;
        private InventoryContainer? _currentQuickBar;
        private string _effectiveBuildClass = string.Empty;
        private string _lastLoggedBuildClass = string.Empty;

        public override void _Ready()
        {
            _player = GetParent() as SamplePlayer ?? GetOwner() as SamplePlayer;
            Inventory ??= _player?.InventoryComponent ?? _player?.GetNodeOrNull<PlayerInventoryComponent>("Inventory");
            TargetEffectController ??= _player?.EffectController ?? _player?.GetNodeOrNull<EffectController>("EffectController");

            if (Inventory == null)
            {
                GD.PushWarning($"{Name}: 未找到 PlayerInventoryComponent，无法统计构筑。");
                return;
            }

            if (TargetEffectController == null)
            {
                GD.PushWarning($"{Name}: 未找到 EffectController，无法应用构筑效果。");
                return;
            }

            SubscribeSignals();
            CallDeferred(nameof(RefreshBuildState));
        }

        public override void _ExitTree()
        {
            UnsubscribeSignals();
            base._ExitTree();
        }

        public void RefreshBuildState()
        {
            if (Inventory == null || TargetEffectController == null)
            {
                return;
            }

            int oldCount = CurrentBuildCount;
            int oldLevel = CurrentBuildLevel;
            string oldBuildClass = _lastLoggedBuildClass;

            int newCount = CountOwnedBuildWeapons();
            int newLevel = ResolveBuildLevel(newCount);
            bool countChanged = newCount != oldCount;
            bool levelChanged = newLevel != oldLevel;
            bool buildClassChanged = !string.Equals(_effectiveBuildClass, oldBuildClass, StringComparison.OrdinalIgnoreCase);
            string logBuildClass = string.IsNullOrWhiteSpace(_effectiveBuildClass) ? "<auto>" : _effectiveBuildClass;
            string oldLogBuildClass = string.IsNullOrWhiteSpace(oldBuildClass) ? "<auto>" : oldBuildClass;

            if (levelChanged)
            {
                CurrentBuildLevel = newLevel;
            }

            if (countChanged)
            {
                CurrentBuildCount = newCount;
                BuildCountChanged?.Invoke(CurrentBuildCount);
                GD.Print($"[{Name}] 构筑 {logBuildClass} 点数变化: {oldCount} -> {CurrentBuildCount}, Level={CurrentBuildLevel}");
            }

            if (levelChanged)
            {
                BuildLevelChanged?.Invoke(CurrentBuildLevel);
                GD.Print($"[{Name}] 构筑 {logBuildClass} 等级变化: {oldLevel} -> {CurrentBuildLevel}, Points={CurrentBuildCount}");
            }

            if (buildClassChanged)
            {
                GD.Print($"[{Name}] 生效构筑类别变化: {oldLogBuildClass} -> {logBuildClass}");
            }

            _lastLoggedBuildClass = _effectiveBuildClass;
            SyncBuildEffects();
        }

        private void SubscribeSignals()
        {
            if (Inventory == null)
            {
                return;
            }

            Inventory.WeaponEquipped += OnWeaponChanged;
            Inventory.WeaponUnequipped += OnWeaponUnequipped;
            Inventory.ActiveBackpackSlotChanged += OnBackpackSlotChanged;
            Inventory.QuickBarAssigned += OnQuickBarAssigned;
            Inventory.QuickBarSlotChanged += OnQuickBarSlotChanged;

            if (Inventory.Backpack != null)
            {
                Inventory.Backpack.InventoryChanged += OnInventoryChanged;
            }

            SubscribeQuickBarSignals();
        }

        private void UnsubscribeSignals()
        {
            if (Inventory != null)
            {
                Inventory.WeaponEquipped -= OnWeaponChanged;
                Inventory.WeaponUnequipped -= OnWeaponUnequipped;
                Inventory.ActiveBackpackSlotChanged -= OnBackpackSlotChanged;
                Inventory.QuickBarAssigned -= OnQuickBarAssigned;
                Inventory.QuickBarSlotChanged -= OnQuickBarSlotChanged;

                if (Inventory.Backpack != null)
                {
                    Inventory.Backpack.InventoryChanged -= OnInventoryChanged;
                }
            }

            UnsubscribeQuickBarSignals();
        }

        private void SubscribeQuickBarSignals()
        {
            if (Inventory?.QuickBar == null)
            {
                return;
            }

            if (_currentQuickBar == Inventory.QuickBar)
            {
                return;
            }

            UnsubscribeQuickBarSignals();
            _currentQuickBar = Inventory.QuickBar;
            _currentQuickBar.InventoryChanged += OnInventoryChanged;
            _currentQuickBar.SlotChanged += OnQuickBarSlotChangedDetailed;
        }

        private void UnsubscribeQuickBarSignals()
        {
            if (_currentQuickBar == null)
            {
                return;
            }

            _currentQuickBar.InventoryChanged -= OnInventoryChanged;
            _currentQuickBar.SlotChanged -= OnQuickBarSlotChangedDetailed;
            _currentQuickBar = null;
        }

        private void OnWeaponChanged(ItemDefinition _weapon)
        {
            RefreshBuildState();
        }

        private void OnWeaponUnequipped()
        {
            RefreshBuildState();
        }

        private void OnBackpackSlotChanged(int _slotIndex)
        {
            RefreshBuildState();
        }

        private void OnQuickBarAssigned()
        {
            SubscribeQuickBarSignals();
            RefreshBuildState();
        }

        private void OnQuickBarSlotChanged(int _slotIndex)
        {
            RefreshBuildState();
        }

        private void OnQuickBarSlotChangedDetailed(int _slotIndex, string _itemId, int _quantity)
        {
            RefreshBuildState();
        }

        private void OnInventoryChanged()
        {
            RefreshBuildState();
        }

        private int CountOwnedBuildWeapons()
        {
            if (Inventory == null)
            {
                return 0;
            }

            _effectiveBuildClass = ResolveEffectiveBuildClass();

            int totalPoints = 0;
            totalPoints += CollectPointsFromContainer(Inventory.Backpack);
            totalPoints += CollectPointsFromContainer(Inventory.QuickBar);

            foreach (var slot in Inventory.SpecialSlots.Values)
            {
                var item = slot?.Stack?.Item;
                if (item != null)
                {
                    totalPoints += ResolveItemBuildPoints(item);
                }
            }

            return totalPoints;
        }

        private int CollectPointsFromContainer(InventoryContainer? container)
        {
            if (container == null)
            {
                return 0;
            }

            int points = 0;

            foreach (var stack in container.Slots)
            {
                var item = stack?.Item;
                if (stack == null || stack.IsEmpty || item == null)
                {
                    continue;
                }

                points += ResolveItemBuildPoints(item);
            }

            return points;
        }

        private int ResolveItemBuildPoints(ItemDefinition item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || item.ItemId == "empty_item")
            {
                return 0;
            }

            if (!IsTrackedBuildItem(item))
            {
                return 0;
            }

            int levelCount = Math.Max(0, item.LevelCount);
            return levelCount;
        }

        private bool IsTrackedBuildItem(ItemDefinition item)
        {
            if (item == null)
            {
                return false;
            }

            foreach (var trackedItemId in TrackedItemIds)
            {
                if (string.Equals(item.ItemId, trackedItemId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(_effectiveBuildClass) &&
                string.Equals(item.BuildClass, _effectiveBuildClass, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(BuildTagFallback) && item.HasTag(BuildTagFallback);
        }

        private int ResolveBuildLevel(int buildCount)
        {
            int resolvedLevel = 0;
            foreach (var entry in GetSortedEntries())
            {
                if (entry == null) continue;
                if (!MatchesCurrentBuild(entry)) continue;
                if (buildCount < entry.RequiredPoints) continue;
                resolvedLevel = Math.Max(resolvedLevel, entry.Level);
            }

            return resolvedLevel;
        }

        private void SyncBuildEffects()
        {
            if (TargetEffectController == null)
            {
                return;
            }

            foreach (var entry in GetSortedEntries())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.EffectId))
                {
                    continue;
                }

                bool shouldExist = MatchesCurrentBuild(entry) && CurrentBuildCount >= entry.RequiredPoints;
                EnsureEffect(shouldExist, entry.EffectId, () => CreateEffectFromEntry(entry));
            }
        }

        private void EnsureEffect(bool shouldExist, string effectId, Func<ActorEffect?> factory)
        {
            if (TargetEffectController == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(effectId))
            {
                return;
            }

            var existing = TargetEffectController.GetEffect(effectId);
            if (shouldExist)
            {
                if (existing != null)
                {
                    return;
                }

                var effect = factory();
                if (effect != null)
                {
                    TargetEffectController.AddEffect(effect);
                    GD.Print($"[{Name}] 添加构筑效果: {effectId}");
                }

                return;
            }

            if (existing != null)
            {
                GD.Print($"[{Name}] 移除构筑效果: {effectId}");
                TargetEffectController.RemoveEffect(existing);
            }
        }

        private BuildLevelEffectEntry[] GetSortedEntries()
        {
            var list = new List<BuildLevelEffectEntry>();
            foreach (var entry in LevelEntries)
            {
                if (entry == null)
                {
                    continue;
                }

                list.Add(entry);
            }

            list.Sort((a, b) =>
            {
                int byPoints = a.RequiredPoints.CompareTo(b.RequiredPoints);
                if (byPoints != 0) return byPoints;
                return a.Level.CompareTo(b.Level);
            });

            return list.ToArray();
        }

        private bool MatchesCurrentBuild(BuildLevelEffectEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(entry.BuildClass))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_effectiveBuildClass))
            {
                return false;
            }

            return string.Equals(entry.BuildClass, _effectiveBuildClass, StringComparison.OrdinalIgnoreCase);
        }

        private string ResolveEffectiveBuildClass()
        {
            if (!string.IsNullOrWhiteSpace(BuildClass))
            {
                return BuildClass.Trim();
            }

            if (Inventory == null)
            {
                return string.Empty;
            }

            var activeWeapon = Inventory.GetActiveCombatWeaponDefinition();
            if (!string.IsNullOrWhiteSpace(activeWeapon?.BuildClass))
            {
                return activeWeapon.BuildClass.Trim();
            }

            string fromSpecial = ResolveBuildClassFromSpecialSlots();
            if (!string.IsNullOrWhiteSpace(fromSpecial))
            {
                return fromSpecial;
            }

            string fromQuickBar = ResolveBuildClassFromContainer(Inventory.QuickBar);
            if (!string.IsNullOrWhiteSpace(fromQuickBar))
            {
                return fromQuickBar;
            }

            return ResolveBuildClassFromContainer(Inventory.Backpack);
        }

        private string ResolveBuildClassFromSpecialSlots()
        {
            if (Inventory == null)
            {
                return string.Empty;
            }

            foreach (var slot in Inventory.SpecialSlots.Values)
            {
                var buildClass = slot?.Stack?.Item?.BuildClass;
                if (!string.IsNullOrWhiteSpace(buildClass))
                {
                    return buildClass.Trim();
                }
            }

            return string.Empty;
        }

        private static string ResolveBuildClassFromContainer(InventoryContainer? container)
        {
            if (container == null)
            {
                return string.Empty;
            }

            foreach (var stack in container.Slots)
            {
                if (stack == null || stack.IsEmpty || stack.Item == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(stack.Item.BuildClass))
                {
                    return stack.Item.BuildClass.Trim();
                }
            }

            return string.Empty;
        }

        private ActorEffect? CreateEffectFromEntry(BuildLevelEffectEntry entry)
        {
            ActorEffect? effect = entry.EffectScene?.Instantiate<ActorEffect>();
            if (effect == null)
            {
                effect = CreateFallbackEffectByScript(entry.EffectScript);
            }

            if (effect != null && !string.IsNullOrWhiteSpace(entry.EffectId))
            {
                effect.EffectId = entry.EffectId;
            }

            return effect;
        }

        private static ActorEffect? CreateFallbackEffectByScript(string effectScript)
        {
            if (string.IsNullOrWhiteSpace(effectScript))
            {
                return null;
            }

            return effectScript.Trim() switch
            {
                nameof(BuildMachineLevel1Effect) => new BuildMachineLevel1Effect(),
                nameof(BuildMachineLevel2Effect) => new BuildMachineLevel2Effect(),
                nameof(BuildMachineLevel3Effect) => new BuildMachineLevel3Effect(),
                _ => null
            };
        }
    }
}
