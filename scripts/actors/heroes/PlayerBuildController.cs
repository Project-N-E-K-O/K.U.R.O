using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kuros.Builds;
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

        /// <summary>
        /// 获取当前活跃的所有构筑类型（只读）
        /// </summary>
        public IReadOnlyCollection<string> ActiveBuildClasses => _activeBuildClasses;

        /// <summary>
        /// 获取各构筑类型的点数（只读）
        /// </summary>
        public IReadOnlyDictionary<string, int> BuildCountByClass => _buildCountByClass;

        /// <summary>
        /// 获取各构筑类型的等级（只读）
        /// </summary>
        public IReadOnlyDictionary<string, int> BuildLevelByClass => _buildLevelByClass;

        private SamplePlayer? _player;
        private InventoryContainer? _currentQuickBar;
        private HashSet<string> _activeBuildClasses = new();
        private HashSet<string> _lastLoggedBuildClasses = new();
        private Dictionary<string, int> _buildCountByClass = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _buildLevelByClass = new(StringComparer.OrdinalIgnoreCase);
        // 投掷中的武器列表：武器被投出后暂存于此，确保构筑效果持续生效直到归还或销毁
        private readonly List<ItemDefinition> _inFlightThrowItems = new();

        /// <summary>
        /// 注册一件投掷中的武器，使构筑效果在武器飞行/冷却期间保持生效。
        /// 在武器投出时调用。
        /// </summary>
        public void RegisterThrowInFlight(ItemDefinition item)
        {
            if (item == null) return;
            _inFlightThrowItems.Add(item);
            // GD.Print($"[{Name}][InFlight] RegisterThrowInFlight: item={item.ItemId}, BuildClass={item.BuildClass}, IsThrowable={item.IsThrowable}, 飞行列表数量={_inFlightThrowItems.Count}");
            RefreshBuildState();
        }

        /// <summary>
        /// 注销一件投掷中的武器（归还背包或飞行中被销毁时调用）。
        /// 归还背包时：物品已回到 QuickBar，不会造成点数减少。
        /// 飞行命中销毁时：物品永久消失，构筑效果正常降低。
        /// </summary>
        public void UnregisterThrowInFlight(ItemDefinition item)
        {
            if (item == null) return;
            // 只移除一次（支持同类型多件武器同时飞行的情况）
            int idx = _inFlightThrowItems.IndexOf(item);
            bool removed = idx >= 0;
            if (removed)
            {
                _inFlightThrowItems.RemoveAt(idx);
            }
            // GD.Print($"[{Name}][InFlight] UnregisterThrowInFlight: item={item.ItemId}, 找到={removed}, 飞行列表剩余={_inFlightThrowItems.Count}");
            RefreshBuildState();
        }

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
            var oldBuildClasses = new HashSet<string>(_lastLoggedBuildClasses);
            var oldBuildCountByClass = new Dictionary<string, int>(_buildCountByClass, StringComparer.OrdinalIgnoreCase);

            // 重新计算各构筑类型的点数
            _buildCountByClass.Clear();
            _activeBuildClasses = ResolveActiveBuildClasses();
            
            CountOwnedBuildWeaponsByClass();
            
            // 为每种构筑类型计算应有的等级
            _buildLevelByClass.Clear();
            foreach (var buildClass in _activeBuildClasses)
            {
                _buildLevelByClass[buildClass] = ResolveBuildLevelForClass(buildClass);
            }

            // 计算总点数和全局等级（用于兼容旧的API）
            int newCount = 0;
            int newLevel = 0;
            foreach (var count in _buildCountByClass.Values)
            {
                newCount += count;
            }
            foreach (var level in _buildLevelByClass.Values)
            {
                newLevel = Math.Max(newLevel, level);
            }

            bool countChanged = newCount != oldCount;
            bool levelChanged = newLevel != oldLevel;
            bool buildClassChanged = !AreHashSetEqual(_activeBuildClasses, oldBuildClasses);
            
            string logBuildClasses = _activeBuildClasses.Count == 0 ? "<auto>" : string.Join(", ", _activeBuildClasses);
            string oldLogBuildClasses = oldBuildClasses.Count == 0 ? "<auto>" : string.Join(", ", oldBuildClasses);

            if (levelChanged)
            {
                CurrentBuildLevel = newLevel;
            }

            if (countChanged)
            {
                CurrentBuildCount = newCount;
                BuildCountChanged?.Invoke(CurrentBuildCount);
                
                // 详细打印各构筑类型的点数
                string classCountDetails = string.Join(", ", _buildCountByClass.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                GD.Print($"[{Name}] 构筑 [{logBuildClasses}] 点数变化: {oldCount} -> {CurrentBuildCount} (详情: {classCountDetails}), GlobalLevel={CurrentBuildLevel}");
            }

            if (levelChanged)
            {
                BuildLevelChanged?.Invoke(CurrentBuildLevel);
                string classLevelDetails = string.Join(", ", _buildLevelByClass.Select(kvp => $"{kvp.Key}:{kvp.Value}"));
                GD.Print($"[{Name}] 构筑 [{logBuildClasses}] 等级变化: {oldLevel} -> {CurrentBuildLevel} (详情: {classLevelDetails}), TotalPoints={CurrentBuildCount}");
            }

            if (buildClassChanged)
            {
                GD.Print($"[{Name}] 生效构筑类别变化: [{oldLogBuildClasses}] -> [{logBuildClasses}]");
            }

            _lastLoggedBuildClasses = new HashSet<string>(_activeBuildClasses);
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

            _activeBuildClasses = ResolveActiveBuildClasses();

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

        /// <summary>
        /// 按构筑类型分别统计点数。
        /// 同时将飞行中（已投出但尚未归还/销毁）的武器计入，使构筑效果在投掷生命周期内保持生效。
        /// </summary>
        private void CountOwnedBuildWeaponsByClass()
        {
            if (Inventory == null)
            {
                return;
            }

            CollectPointsFromContainerByClass(Inventory.Backpack);
            CollectPointsFromContainerByClass(Inventory.QuickBar);

            foreach (var slot in Inventory.SpecialSlots.Values)
            {
                var item = slot?.Stack?.Item;
                if (item != null)
                {
                    AddItemPointsByClass(item);
                }
            }

            // 将飞行中的投掷武器也计入构筑点数
            foreach (var item in _inFlightThrowItems)
            {
                if (item != null)
                {
                    // GD.Print($"[{Name}][InFlight] 飞行武器计入: item={item.ItemId}, BuildClass={item.BuildClass}, IsTracked={IsTrackedBuildItem(item)}, ActiveClasses=[{string.Join(", ", _activeBuildClasses)}]");
                    AddItemPointsByClass(item);
                }
            }
        }

        private void CollectPointsFromContainerByClass(InventoryContainer? container)
        {
            if (container == null)
            {
                return;
            }

            foreach (var stack in container.Slots)
            {
                var item = stack?.Item;
                if (stack == null || stack.IsEmpty || item == null)
                {
                    continue;
                }

                AddItemPointsByClass(item);
            }
        }

        private void AddItemPointsByClass(ItemDefinition item)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.ItemId) || item.ItemId == "empty_item")
            {
                return;
            }

            if (!IsTrackedBuildItem(item))
            {
                return;
            }

            int levelCount = Math.Max(0, item.LevelCount);
            if (levelCount <= 0)
            {
                return;
            }

            // 按构筑类型分别统计
            if (!string.IsNullOrWhiteSpace(item.BuildClass))
            {
                string buildClass = item.BuildClass.Trim();
                if (!_buildCountByClass.ContainsKey(buildClass))
                {
                    _buildCountByClass[buildClass] = 0;
                }
                _buildCountByClass[buildClass] += levelCount;
            }
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

            // 检查物品的构筑类型是否在活跃的构筑类型中
            if (!string.IsNullOrWhiteSpace(item.BuildClass))
            {
                foreach (var activeBuildClass in _activeBuildClasses)
                {
                    if (string.Equals(item.BuildClass, activeBuildClass, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
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

        /// <summary>
        /// 为指定的构筑类型计算应有的等级。
        /// </summary>
        private int ResolveBuildLevelForClass(string buildClass)
        {
            if (!_buildCountByClass.ContainsKey(buildClass))
            {
                return 0;
            }

            int classPoints = _buildCountByClass[buildClass];
            int resolvedLevel = 0;

            foreach (var entry in GetSortedEntries())
            {
                if (entry == null) continue;
                
                // 只检查匹配该构筑类型的条目
                if (string.IsNullOrWhiteSpace(entry.BuildClass))
                {
                    continue; // 跳过通用条目
                }
                
                if (!string.Equals(entry.BuildClass, buildClass, StringComparison.OrdinalIgnoreCase))
                {
                    continue; // 不匹配该构筑类型
                }
                
                if (classPoints < entry.RequiredPoints) continue;
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

                bool shouldExist = ShouldActivateEffect(entry);
                EnsureEffect(shouldExist, entry.EffectId, () => CreateEffectFromEntry(entry));
            }
        }

        /// <summary>
        /// 判断一个效果条目是否应该被激活。
        /// </summary>
        private bool ShouldActivateEffect(BuildLevelEffectEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            // 如果条目没有指定构筑类型，则不激活（通用条目需要别的逻辑）
            if (string.IsNullOrWhiteSpace(entry.BuildClass))
            {
                return false;
            }

            // 检查该构筑类型是否活跃
            string buildClass = entry.BuildClass.Trim();
            if (!_buildLevelByClass.ContainsKey(buildClass))
            {
                return false;
            }

            // 检查该构筑类型的等级是否满足条件
            int classLevel = _buildLevelByClass[buildClass];
            if (classLevel < entry.Level)
            {
                return false;
            }

            // 检查该构筑类型的点数是否满足条件
            if (!_buildCountByClass.ContainsKey(buildClass))
            {
                return false;
            }

            int classPoints = _buildCountByClass[buildClass];
            return classPoints >= entry.RequiredPoints;
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

            // 如果条目没有指定构筑类型，则总是匹配
            if (string.IsNullOrWhiteSpace(entry.BuildClass))
            {
                return true;
            }

            // 检查条目的构筑类型是否在活跃的构筑类型中
            foreach (var activeBuildClass in _activeBuildClasses)
            {
                if (string.Equals(entry.BuildClass, activeBuildClass, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private HashSet<string> ResolveActiveBuildClasses()
        {
            var buildClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 如果设置了全局构筑类型，则只用该类型
            if (!string.IsNullOrWhiteSpace(BuildClass))
            {
                buildClasses.Add(BuildClass.Trim());
                return buildClasses;
            }

            if (Inventory == null)
            {
                return buildClasses;
            }

            // 收集所有已装备武器的构筑类型
            var activeWeapon = Inventory.GetActiveCombatWeaponDefinition();
            if (!string.IsNullOrWhiteSpace(activeWeapon?.BuildClass))
            {
                buildClasses.Add(activeWeapon.BuildClass.Trim());
            }

            CollectBuildClassesFromSpecialSlots(buildClasses);
            CollectBuildClassesFromContainer(Inventory.QuickBar, buildClasses);
            CollectBuildClassesFromContainer(Inventory.Backpack, buildClasses);

            // 将飞行中的投掷武器的构筑类型也纳入活跃集合
            // 否则物品从快捷栏提取后 _activeBuildClasses 变空，IsTrackedBuildItem 会对飞行武器返回 false
            foreach (var inFlightItem in _inFlightThrowItems)
            {
                if (!string.IsNullOrWhiteSpace(inFlightItem?.BuildClass))
                    buildClasses.Add(inFlightItem.BuildClass.Trim());
            }

            // GD.Print($"[{Name}][InFlight] ResolveActiveBuildClasses 结果: [{string.Join(", ", buildClasses)}], 飞行列表={_inFlightThrowItems.Count}件 [{string.Join(", ", _inFlightThrowItems.Select(i => i?.ItemId ?? \"null\"))}]");
            return buildClasses;
        }

        private void CollectBuildClassesFromSpecialSlots(HashSet<string> buildClasses)
        {
            if (Inventory == null)
            {
                return;
            }

            foreach (var slot in Inventory.SpecialSlots.Values)
            {
                var buildClass = slot?.Stack?.Item?.BuildClass;
                if (!string.IsNullOrWhiteSpace(buildClass))
                {
                    buildClasses.Add(buildClass.Trim());
                }
            }
        }

        private static void CollectBuildClassesFromContainer(InventoryContainer? container, HashSet<string> buildClasses)
        {
            if (container == null)
            {
                return;
            }

            foreach (var stack in container.Slots)
            {
                if (stack == null || stack.IsEmpty || stack.Item == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(stack.Item.BuildClass))
                {
                    buildClasses.Add(stack.Item.BuildClass.Trim());
                }
            }
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

        //添加新的构筑效果时，需要在这里添加对应的逻辑
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
                nameof(BuildGuardLevel1Effect) => new BuildGuardLevel1Effect(),
                nameof(BuildGuardLevel2Effect) => new BuildGuardLevel2Effect(),
                nameof(BuildGuardLevel3Effect) => new BuildGuardLevel3Effect(),
                _ => null
            };
        }

        private static bool AreHashSetEqual(HashSet<string> a, HashSet<string> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (var item in a)
            {
                if (!b.Contains(item))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
