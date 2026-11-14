using System;
using System.Collections.Generic;
using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Heroes;
using Kuros.Builds.BuildCore;
using Kuros.Core.Effects;
using Kuros.Systems;
using Kuros.UI;

namespace Kuros.Managers
{
    /// <summary>
    /// 构筑选择管理器：监听玩家分数，达到阈值时弹出三选一构筑效果窗口。
    /// </summary>
    public partial class BuildSelectionManager : Node
    {
        public static BuildSelectionManager Instance { get; private set; } = null!;

        /// <summary>构筑效果变动时触发（选择新效果、恢复效果等）。</summary>
        public event Action? PickedEffectsChanged;

        [ExportGroup("Thresholds")]
        [Export] public ScoreThresholdCurve? ThresholdCurve { get; set; }

        [ExportGroup("Core Pool")]
        [Export] public Godot.Collections.Array<BuildCoreDefinition> CorePool { get; set; } = new();

        [ExportGroup("Effect Pool")]
        [Export] public Godot.Collections.Array<BuildEffectDefinition> EffectPool { get; set; } = new();

        [ExportGroup("Rarity")]
        /// <summary>稀有度权重倍率。Key: "Common"/"Rare"/"Epic"，默认 Common=3, Rare=1, Epic=0.3。</summary>
        [Export] public Godot.Collections.Dictionary<string, float> RarityMultiplier { get; set; } = new()
        {
            { "Common", 3.0f },
            { "Rare", 1.0f },
            { "Epic", 0.3f },
        };

        /// <summary>每次选择的卡牌数量。</summary>
        [Export(PropertyHint.Range, "2,10,1")]
        public int CardsPerSelection { get; set; } = 3;

        [ExportGroup("Economy")]
        /// <summary>弃选（跳过）本次升级时获得的金币——局内经济收入源之一（金币局内保存，退出/死亡清除）。</summary>
        [Export(PropertyHint.Range, "0,999,1")]
        public int SkipGoldReward { get; set; } = 15;

        /// <summary>弃选（跳过）核心选择时获得的金币——数值独立于效果跳过（跳过核心 = 放弃本局构筑，补偿更高）。</summary>
        [Export(PropertyHint.Range, "0,999,1")]
        public int CoreSkipGoldReward { get; set; } = 30;

        [ExportGroup("Reroll")]
        /// <summary>每个选择窗口的免费刷新次数（基础值；局外养成可用 <see cref="AddFreeRerollCount"/> 增加）。</summary>
        [Export(PropertyHint.Range, "0,10,1")]
        public int FreeRerollCount { get; set; } = 1;
        /// <summary>首次付费刷新的价格。</summary>
        [Export(PropertyHint.Range, "1,999,1")]
        public int RerollBaseCost { get; set; } = 10;
        /// <summary>刷新费用递增倍率：cost = base × growth^已付费次数（窗口内递增，每窗重置）。</summary>
        [Export(PropertyHint.Range, "1.0,3.0,0.05")]
        public float RerollCostGrowth { get; set; } = 1.5f;

        private int _bonusFreeRerolls; // 局外养成/其他系统增加的额外免费刷新次数

        /// <summary>局外养成调用：增加后续每个选择窗口的免费刷新次数。</summary>
        public void AddFreeRerollCount(int amount)
        {
            if (amount <= 0) return;
            _bonusFreeRerolls += amount;
        }

        /// <summary>当前每个选择窗口可用的免费刷新次数（基础值 + 养成加成）。</summary>
        public int GetFreeRerollCount() => FreeRerollCount + _bonusFreeRerolls;

        /// <summary>未选核心时使用的默认构筑类别。空 = 不选核心不触发三选一。</summary>
        [Export] public string DefaultBuildClass { get; set; } = "";

        [ExportGroup("Debug")]
        [Export] public bool DebugTrigger { get; set; }
        /// <summary>调试触发键（默认 B）：每次按下翻转 DebugTrigger（等价于 Inspector 勾选/取消）。
        /// 置 true 后由 _Process 消费（需已绑定玩家且当前无选择窗）→ 打开一次三选一窗口。</summary>
        [Export] public Key DebugTriggerKey { get; set; } = Key.B;

        private SamplePlayer? _boundPlayer;
        private string? _playerCoreClass;
        private string? _selectedCoreId;
        private bool _coreSelected;
        private int _lastKnownScore;
        private int _triggerCount;
        private bool _isSelectionActive;
        private BuildSelectionWindow? _activeWindow;
        private int _pendingScore;
        /// <summary>构筑选择主数据源（时间序,含被反向取代的废卡）。</summary>
        private readonly List<BuildPickRecord> _pickRecords = new();
        /// <summary>派生快照：active 记录按 EffectId 归并最大层数（外部兼容出口,废卡不进入）。</summary>
        private readonly Dictionary<string, int> _pickedEffectIds = new();
        private readonly System.Random _rng = new();

        public bool IsSelectionActive => _isSelectionActive;

        public override void _Ready()
        {
            if (Instance != null && Instance != this)
            {
                QueueFree();
                return;
            }
            Instance = this;
            TryBindPlayer();
        }

        public override void _ExitTree()
        {
            UnbindPlayer();
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer))
                TryBindPlayer();

            if (DebugTrigger && _boundPlayer != null && !_isSelectionActive)
            {
                DebugTrigger = false;
                TriggerSelection();
            }
        }

        /// <summary>调试键：翻转 DebugTrigger。窗口打开期间树暂停（INHERIT 输入不触发）——不会误触发连续弹窗。</summary>
        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
            if (key.Keycode != DebugTriggerKey) return;

            DebugTrigger = !DebugTrigger;
            GetViewport().SetInputAsHandled();
        }

        private void TryBindPlayer()
        {
            if (_isSelectionActive) return;

            var tree = GetTree();
            if (tree == null) return;

            var player = tree.GetFirstNodeInGroup("player") as SamplePlayer;
            if (player == null || !IsInstanceValid(player)) return;
            if (player == _boundPlayer) return;

            if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                _boundPlayer.StatsUpdated -= OnPlayerStatsUpdated;

            _boundPlayer = player;
            _boundPlayer.StatsUpdated += OnPlayerStatsUpdated;

            // 恢复跨场景分数和核心状态
            if (_pendingScore > 0 && player.Score < _pendingScore)
            {
                player.AddScore(_pendingScore - player.Score);
            }

            _lastKnownScore = _pendingScore > 0 ? _pendingScore : player.Score;
            _triggerCount = ThresholdCurve?.GetTriggerCount(_lastKnownScore) ?? 0;
        }

        private void UnbindPlayer()
        {
            if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                _boundPlayer.StatsUpdated -= OnPlayerStatsUpdated;
            _boundPlayer = null;
        }

        /// <summary>
        /// 重置本局 Build 进度（死亡重试/返回标题时调用）：清空构筑选择、核心、触发计数与分数，
        /// 让玩家从零开始新一轮（玩家重载后为初始属性，不再保留旧构筑）。
        /// </summary>
        public void ResetForNewRun()
        {
            if (_activeWindow != null && IsInstanceValid(_activeWindow))
                _activeWindow.CloseWindow(immediate: true);
            _activeWindow = null;
            UnbindPlayer();
            _triggerCount = 0;
            _coreSelected = false;
            _selectedCoreId = null;
            _playerCoreClass = null;
            _isSelectionActive = false;
            _lastKnownScore = 0;
            _pendingScore = 0;
            _pickRecords.Clear();
            RebuildPickedSnapshot();
            _boundPlayer = null;
        }

        private void OnPlayerStatsUpdated(int health, int maxHealth, int score)
        {
            if (score > _lastKnownScore)
                CheckAndTriggerSelection(score);
            _lastKnownScore = score;
            _pendingScore = score;
        }

        private void CheckAndTriggerSelection(int newScore)
        {
            if (_isSelectionActive) return;
            if (ThresholdCurve == null) return;

            // 必须选完核心（或配置了 DefaultBuildClass）才能进入效果三选一
            if (!_coreSelected && string.IsNullOrWhiteSpace(DefaultBuildClass)) return;
            if (EffectPool.Count == 0) return;

            int nextThreshold = ThresholdCurve.GetCumulativeScore(_triggerCount + 1);
            if (newScore >= nextThreshold)
            {
                _triggerCount++;
                // 武器槽解锁与 Build 等级完全解耦：仅由 Build 效果（解锁武器槽卡片）调用
                // PlayerInventoryComponent.UnlockWeaponSlot，升级本身不再自动加槽。
                TriggerSelection();
            }
        }

        private PackedScene? _coreWindowScene;

        public bool TryTriggerCoreSelection()
        {
            if (_coreSelected || _isSelectionActive) return false;
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer)) TryBindPlayer();
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer)) return false;
            if (CorePool.Count == 0) return false;

            // 将 CoreDefinition 包装为 BuildEffectDefinition 以复用现有窗口
            var options = new List<BuildEffectDefinition>();
            foreach (var core in CorePool)
            {
                if (core == null) continue;
                options.Add(new BuildEffectDefinition
                {
                    EffectId = core.CoreId,
                    DisplayName = core.DisplayName,
                    Description = core.Description,
                    BuildClass = core.BuildClass,
                    Icon = core.Icon,
                    Rarity = BuildRarity.Core,
                });
            }

            if (options.Count == 0) return false;
            _coreWindowScene ??= GD.Load<PackedScene>("res://scenes/ui/windows/BuildSelectionWindow.tscn");
            if (_coreWindowScene == null) return false;
            var window = _coreWindowScene.Instantiate<BuildSelectionWindow>();
            _isSelectionActive = true;
            _activeWindow = window;

            var canvasLayer = new CanvasLayer { Layer = 2 };
            GetTree().Root.AddChild(canvasLayer);
            canvasLayer.AddChild(window);

            window.ShowWindow(options, chosenEffect =>
            {
                _selectedCoreId = chosenEffect.EffectId;
                SetPlayerCoreClass(chosenEffect.BuildClass);
                _coreSelected = true;
                _isSelectionActive = false;

                // 实例化核心机制的 ActorEffect
                var chosenCore = FindCoreById(chosenEffect.EffectId);
                ActorEffect? createdCoreEffect = null;
                if (chosenCore?.CoreEffectScene != null && _boundPlayer?.EffectController != null)
                {
                    var coreEffect = chosenCore.CoreEffectScene.Instantiate<ActorEffect>();
                    coreEffect.EffectId = chosenCore.CoreId;
                    coreEffect.DisplayName = chosenCore.DisplayName;
                    coreEffect.Duration = 0f;
                    _boundPlayer.ApplyEffect(coreEffect);
                    createdCoreEffect = coreEffect;
                }

                // 通知 CoreHUD 切换显示，并注入核心效果引用（Machine 热量 / Throw 充能）
                var coreHUD = GetTree().Root.FindChild("CoreHUD", recursive: true, owned: false) as UI.CoreHUD;
                if (coreHUD != null)
                {
                    coreHUD.ShowFor(chosenEffect.BuildClass);
                    if (createdCoreEffect is MachineCoreEffect machineCore)
                        coreHUD.BindMachineCore(machineCore);
                    else if (createdCoreEffect is ThrowCoreEffect throwCore)
                        coreHUD.BindThrowCore(throwCore);
                }

                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                    CheckAndTriggerSelection(_boundPlayer.Score);
            }, _pickedEffectIds, CoreSkipGoldReward, () =>
            {
                // 跳过核心：放弃本局构筑选择，获得核心跳过金币（独立数值）；
                // _coreSelected 保持 false——若未配置 DefaultBuildClass，后续不再触发效果选择
                _boundPlayer?.AddGold(CoreSkipGoldReward);
                _isSelectionActive = false;

                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                    CheckAndTriggerSelection(_boundPlayer.Score);
            });
            return true;
        }

        private BuildCoreDefinition? FindCoreById(string coreId)
        {
            foreach (var core in CorePool)
            {
                if (core?.CoreId == coreId)
                    return core;
            }
            return null;
        }

        public void SetPlayerCoreClass(string buildClass)
        {
            _playerCoreClass = buildClass;
        }

        /// <summary>重置所有构筑状态（新游戏开始时调用）。</summary>
        public void ResetBuildState()
        {
            _coreSelected = false;
            _selectedCoreId = null;
            _playerCoreClass = null;
            _pickedEffectIds.Clear();
            _pendingScore = 0;
            _lastKnownScore = 0;
            _triggerCount = 0;
            _boundPlayer?.InventoryComponent?.ResetWeaponSlots(); // 武器槽位还原到初始值
        }

        private PackedScene? _windowScene;

        private void TriggerSelection()
        {
            if (_boundPlayer == null || !IsInstanceValid(_boundPlayer)) return;

            var options = PickRandomEffects(CardsPerSelection);
            if (options.Count == 0) return;

            _isSelectionActive = true;

            _windowScene ??= GD.Load<PackedScene>("res://scenes/ui/windows/BuildSelectionWindow.tscn");
            var window = _windowScene.Instantiate<BuildSelectionWindow>();
            _activeWindow = window;

            var canvasLayer = new CanvasLayer { Layer = 2 };
            GetTree().Root.AddChild(canvasLayer);
            canvasLayer.AddChild(window);

            window.ShowWindow(options, chosenEffect =>
            {
                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                {
                    ApplyEffectBonuses(chosenEffect);
                }
                _isSelectionActive = false;

                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                    CheckAndTriggerSelection(_boundPlayer.Score);
            }, _pickedEffectIds, SkipGoldReward, () =>
            {
                // 弃选：放弃本次升级，获得少量金币（收入源；不消耗额外升级次数）
                _boundPlayer?.AddGold(SkipGoldReward);
                _isSelectionActive = false;

                if (_boundPlayer != null && IsInstanceValid(_boundPlayer))
                    CheckAndTriggerSelection(_boundPlayer.Score);
            },
            excluded => PickRandomEffects(CardsPerSelection, excluded),
            GetPickContext,
            RerollBaseCost, RerollCostGrowth, GetFreeRerollCount());
        }

        private void ApplyEffectBonuses(BuildEffectDefinition effect)
        {
            if (_boundPlayer?.EffectController == null) return;

            string effectId = effect.EffectId;
            if (string.IsNullOrWhiteSpace(effectId)) return;

            // ── 反向取代判定：同族(共用 EffectScene)存在方向相反的 active 卡 → 整族作废 ──
            var familyRecords = ActiveFamilyRecords(effect);
            bool hasOpposite = false;
            int familyNet = 0;
            foreach (var r in familyRecords)
            {
                familyNet += r.Stacks;
                if (!hasOpposite && effect.Direction != 0)
                {
                    var d = FindEffectById(r.EffectId);
                    if (d?.Direction != 0 && d.Direction != effect.Direction)
                        hasOpposite = true;
                }
            }

            int recordStacks;
            if (hasOpposite)
            {
                // 整族作废：移除实例(→ OnRemoved → RemoveStatModifier)并把族内全部 active 记录标废
                foreach (var r in familyRecords)
                {
                    _boundPlayer.RemoveEffect(r.EffectId);
                    r.Active = false;
                }
                // 生效层数 = 旧族净层 + 1（反向补偿起步），钳制在档位表长度内
                int tierLen = effect.GetTierValues()?.Length ?? Mathf.Max(1, effect.MaxStacks);
                recordStacks = Mathf.Clamp(familyNet + 1, 1, Mathf.Max(1, tierLen));
            }
            else
            {
                // 同方向/无同族：层数 = 该卡 active 最大层 + 1（首次=1）
                recordStacks = ActiveStacksOf(effectId) + 1;
            }

            // ── 效果实例推进（沿用原语义：isNew 每 entry 实例化一次；否则每 entry Refresh 一次）──
            bool isNew = _boundPlayer.EffectController.GetEffect(effectId) == null;
            if (effect.EffectEntries.Count > 0)
            {
                foreach (var entry in effect.EffectEntries)
                {
                    if (entry?.Scene == null) continue;
                    if (isNew)
                    {
                        var instance = entry.InstantiateEffect();
                        if (instance != null)
                        {
                            instance.EffectId = effectId;
                            instance.DisplayName = effect.DisplayName;
                            instance.Duration = 0f;
                            _boundPlayer.ApplyEffect(instance);
                        }
                    }
                    else
                    {
                        var existing = _boundPlayer.EffectController.GetEffect(effectId);
                        existing?.Refresh(1);
                    }
                }
            }

            // 反向取代起手高于 1 层：实例从第 1 层起,补 Refresh 至生效层（单实例目标,显式次数防多 entry 漂移）
            if (hasOpposite && recordStacks > 1)
            {
                var existing = _boundPlayer.EffectController.GetEffect(effectId);
                for (int r = 1; r < recordStacks; r++)
                    existing?.Refresh(1);
            }

            // ── 记录入栈（同 id 续接合并到现有 active 记录；否则新增）──
            BuildPickRecord? activeRecord = null;
            foreach (var r in _pickRecords)
            {
                if (r.Active && r.EffectId == effectId)
                {
                    activeRecord = r;
                    break;
                }
            }
            if (activeRecord != null)
                activeRecord.Stacks = recordStacks;
            else
                _pickRecords.Add(new BuildPickRecord { EffectId = effectId, Stacks = recordStacks, Active = true });

            RebuildPickedSnapshot();
            PickedEffectsChanged?.Invoke();
        }

        private List<BuildEffectDefinition> PickRandomEffects(int count, ICollection<string>? excludeEffectIds = null)
        {
            var result = new List<BuildEffectDefinition>();

            // 找到当前核心的 AllowedEffectClasses
            var core = FindActiveCore();
            // 确定过滤条件
            var allowedClasses = core?.AllowedEffectClasses;
            HashSet<string> allowedSet;
            if (allowedClasses != null && allowedClasses.Count > 0)
                allowedSet = new HashSet<string>(allowedClasses);
            else if (!string.IsNullOrWhiteSpace(_playerCoreClass))
                allowedSet = new HashSet<string> { _playerCoreClass };
            else if (!string.IsNullOrWhiteSpace(DefaultBuildClass))
                allowedSet = new HashSet<string> { DefaultBuildClass };
            else
                return result;

            // 排除集：刷新时排除当前显示的卡（避免刷出同批），不足时放回
            var excluded = excludeEffectIds != null
                ? new HashSet<string>(excludeEffectIds)
                : new HashSet<string>();

            // 按 BuildClass 过滤 + MaxStacks 排除 + Rarity 加权（统一路径）
            var candidates = new List<(BuildEffectDefinition effect, float cumulativeWeight)>();
            float totalWeight = 0f;

            void AddCandidate(BuildEffectDefinition effect)
            {
                float mult = 1.0f;
                RarityMultiplier?.TryGetValue(effect.Rarity.ToString(), out mult);
                float w = effect.Weight * mult;
                if (w <= 0f) return;
                totalWeight += w;
                candidates.Add((effect, totalWeight));
            }

            bool IsEligible(BuildEffectDefinition effect)
            {
                if (effect == null) return false;
                if (string.IsNullOrWhiteSpace(effect.BuildClass)) return false;
                if (!allowedSet.Contains(effect.BuildClass)) return false;
                int stacks = ActiveStacksOf(effect.EffectId); // 废卡不计层:被取代后可重选
                if (effect.MaxStacks > 0 && stacks >= effect.MaxStacks) return false;
                return true;
            }

            foreach (var effect in EffectPool)
            {
                if (!IsEligible(effect)) continue;
                if (excluded.Contains(effect.EffectId)) continue;
                AddCandidate(effect);
            }

            // 排除当前显示卡后候选不足：放回被排除的卡（优先保证刷新仍出满 count 张）
            if (candidates.Count < count)
            {
                foreach (var effect in EffectPool)
                {
                    if (candidates.Count >= count) break;
                    if (!IsEligible(effect)) continue;
                    if (!excluded.Contains(effect.EffectId)) continue;
                    AddCandidate(effect);
                }
            }

            if (candidates.Count == 0) return result;
            int pickCount = Mathf.Min(count, candidates.Count);

            for (int p = 0; p < pickCount; p++)
            {
                float roll = (float)_rng.NextDouble() * totalWeight;
                int idx = 0;
                while (idx < candidates.Count && candidates[idx].cumulativeWeight < roll)
                    idx++;
                if (idx >= candidates.Count) idx = candidates.Count - 1;

                var picked = candidates[idx].effect;
                result.Add(picked);

                // 移除已选，重新计算权重
                float removedWeight = picked.Weight;
                float removedMult = 1f;
                RarityMultiplier?.TryGetValue(picked.Rarity.ToString(), out removedMult);
                totalWeight -= removedWeight * removedMult;
                candidates.RemoveAt(idx);
            }

            return result;
        }

        private BuildCoreDefinition? FindActiveCore()
        {
            if (string.IsNullOrWhiteSpace(_selectedCoreId)) return null;
            foreach (var core in CorePool)
            {
                if (core?.CoreId == _selectedCoreId)
                    return core;
            }
            return null;
        }

        /// <summary>清除构筑选择状态（返回主菜单/退出战斗时调用）——清空已选记录，重进存档后重新选择构筑，避免旧构筑效果跨主菜单残留。</summary>
        public void ClearBuildState()
        {
            ResetForNewRun();
            PickedEffectsChanged?.Invoke();
        }

        /// <summary>跨场景恢复核心效果和所有已选构筑效果。</summary>
        public void RestoreBuildState(SamplePlayer player)
        {
            if (player?.EffectController == null) return;

            // 恢复核心效果
            if (!string.IsNullOrWhiteSpace(_selectedCoreId))
            {
                ActorEffect? restoredCoreEffect = null;
                var core = FindCoreById(_selectedCoreId);
                if (core?.CoreEffectScene != null)
                {
                    var coreEffect = core.CoreEffectScene.Instantiate<ActorEffect>();
                    coreEffect.EffectId = core.CoreId;
                    coreEffect.DisplayName = core.DisplayName;
                    coreEffect.Duration = 0f;
                    player.ApplyEffect(coreEffect);
                    restoredCoreEffect = coreEffect;
                }

                // 恢复 CoreHUD，注入核心效果引用（Machine 热量 / Throw 充能——与核心选择回调一致）
                var coreHUD = GetTree().Root.FindChild("CoreHUD", recursive: true, owned: false) as UI.CoreHUD;
                if (coreHUD != null)
                {
                    coreHUD.ShowFor(_playerCoreClass ?? "");
                    if (restoredCoreEffect is MachineCoreEffect machineCore)
                        coreHUD.BindMachineCore(machineCore);
                    else if (restoredCoreEffect is ThrowCoreEffect throwCore)
                        coreHUD.BindThrowCore(throwCore);
                }
            }

            // 恢复已选的构筑效果（重新实例化，按记录的栈层数；废卡保留记录但不重建实例）
            foreach (var record in _pickRecords)
            {
                if (!record.Active) continue;
                string effectId = record.EffectId;
                int stacks = record.Stacks;

                var definition = FindEffectById(effectId);
                if (definition == null) continue;

                for (int s = 0; s < stacks; s++)
                {
                    if (definition.EffectEntries.Count > 0 && s == 0)
                    {
                        foreach (var entry in definition.EffectEntries)
                        {
                            if (entry?.Scene == null) continue;
                            var instance = entry.InstantiateEffect();
                            if (instance != null)
                            {
                                instance.EffectId = effectId;
                                instance.DisplayName = definition.DisplayName;
                                instance.Duration = 0f;
                                player.ApplyEffect(instance);
                            }
                        }
                        // 额外栈层：Refresh
                        for (int r = 1; r < stacks; r++)
                        {
                            var existing = player.EffectController.GetEffect(effectId);
                            existing?.Refresh(1);
                        }
                        break;
                    }
                    // 所有效果统一由 EffectEntries 驱动
                }
            }

            PickedEffectsChanged?.Invoke();
        }

        public BuildEffectDefinition? FindEffectById(string effectId)
        {
            foreach (var effect in EffectPool)
            {
                if (effect?.EffectId == effectId)
                    return effect;
            }
            return null;
        }

        public IReadOnlyDictionary<string, int> PickedEffectIds => _pickedEffectIds;

        /// <summary>构筑选择历史（含废卡,UI 层叠展示用）。</summary>
        public IReadOnlyList<BuildPickRecord> PickHistory => _pickRecords;

        /// <summary>指定效果的当前生效层数（active 记录最大 Stacks——层数唯一口径）。</summary>
        public int ActiveStacksOf(string effectId)
        {
            int max = 0;
            foreach (var r in _pickRecords)
            {
                if (r.Active && r.EffectId == effectId && r.Stacks > max)
                    max = r.Stacks;
            }
            return max;
        }

        /// <summary>同族(共用 EffectScene)且 active 的记录集合；无族返回空。族净层 = 各记录 Stacks 之和(族内恒同向)。</summary>
        private List<BuildPickRecord> ActiveFamilyRecords(BuildEffectDefinition effect)
        {
            var result = new List<BuildPickRecord>();
            if (effect?.FamilyKey == null) return result;
            foreach (var r in _pickRecords)
            {
                if (!r.Active) continue;
                var d = FindEffectById(r.EffectId);
                if (d?.FamilyKey != null && d.FamilyKey == effect.FamilyKey)
                    result.Add(r);
            }
            return result;
        }

        private void RebuildPickedSnapshot()
        {
            _pickedEffectIds.Clear();
            foreach (var r in _pickRecords)
            {
                if (!r.Active) continue;
                if (!_pickedEffectIds.TryGetValue(r.EffectId, out int cur) || r.Stacks > cur)
                    _pickedEffectIds[r.EffectId] = r.Stacks;
            }
        }

        /// <summary>候选卡的族上下文（选择窗展示用）：反向候选 → 取代目标与生效层；否则 null。</summary>
        public BuildPickCardContext? GetPickContext(BuildEffectDefinition effect)
        {
            if (effect?.FamilyKey == null || effect.Direction == 0) return null;

            var familyRecords = ActiveFamilyRecords(effect);
            if (familyRecords.Count == 0) return null;

            int familyNet = 0;
            var targetNames = new List<string>();
            bool hasOpposite = false;
            foreach (var r in familyRecords)
            {
                familyNet += r.Stacks;
                var d = FindEffectById(r.EffectId);
                if (d == null) continue;
                targetNames.Add(d.DisplayName); // 被取代的是整族(无论叠几层),卡面只报目标名
                if (d.Direction != 0 && d.Direction != effect.Direction)
                    hasOpposite = true;
            }
            if (!hasOpposite) return null;

            int tierLen = effect.GetTierValues()?.Length ?? Mathf.Max(1, effect.MaxStacks);
            return new BuildPickCardContext
            {
                SupersedeTargets = targetNames,
                ResultStacks = Mathf.Clamp(familyNet + 1, 1, Mathf.Max(1, tierLen)),
            };
        }
    }

    /// <summary>候选卡的选择窗上下文（反向取代时）。</summary>
    public sealed class BuildPickCardContext
    {
        public List<string> SupersedeTargets { get; set; } = new();
        public int ResultStacks { get; set; } = 1;
    }
}
