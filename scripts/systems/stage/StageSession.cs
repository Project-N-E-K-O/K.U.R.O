using System.Threading.Tasks;
using System;
using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Kuros.Managers;
using Kuros.Actors.Heroes;
using Kuros.Scenes;
using Kuros.Systems.Cutscene;
using System.Linq;
using Kuros.Utils;

namespace Kuros.Systems.Stage
{
    /// <summary>
    /// 关卡会话：持有全部注册的 StageConfig，跟踪当前关卡与所在层（Floor）。
    /// 电梯选关选项 = 下一层的所有分支 + 已解锁隐藏楼层；出舱时应用所选配置（world 内换关）。
    /// </summary>
    [GlobalClass]
    public partial class StageSession : Node
    {
        [ExportCategory("Refs")]
        [Export] public NodePath GeneratorPath { get; set; } = new NodePath("../StageGeneratorManager");
        [Export] public NodePath CameraPath { get; set; } = new NodePath("../World/Camera2D");

        [ExportCategory("Stage Registry")]
        /// <summary>全部注册关卡（层级推导与调试遍历的数据源）。</summary>
        [Export] public Array<StageConfig> AllConfigs { get; set; } = new();
        /// <summary>起始关卡（null = AllConfigs[0]）。</summary>
        [Export] public StageConfig? StartStage { get; set; }

        /// <summary>楼层难度系数（敌人血量/伤害随层数增长）。留空 = 一律 ×1。</summary>
        [Export] public StageDifficultyConfig? Difficulty { get; set; }

        /// <summary>F1/F2 调试键在全部关卡间遍历（数字键已归电梯选关）。</summary>
        [Export] public bool DebugKeysEnabled { get; set; } = true;

        private StageGeneratorManager? _generator;
        private StageConfig? _currentConfig;
        private int _debugIndex = 0;
        private StagePlan? _pendingPlan;
        private Task<IReadOnlyList<PackedScene>?>? _pendingLoad;
        private readonly System.Collections.Generic.Dictionary<string, Task<PackedScene?>> _roomLoads = new();
        public bool PendingReady => _pendingLoad?.IsCompletedSuccessfully == true && _pendingLoad.Result != null;
        public bool PendingFailed => _pendingLoad?.IsCompleted == true && !PendingReady;
        private readonly System.Collections.Generic.Dictionary<StageConfig, StagePlan?> _candidatePlans = new();
        private List<StagePlan> _displayedPlans = new();
        private readonly HashSet<StageConfig> _unlockedHiddenFloors = new();
        private StageConfig? _hiddenEntryStage;
        private bool _transitionInProgress;

        /// <summary>当前会话（同场景只有一个）。敌人生成时据此拿楼层系数；未就绪 = null。</summary>
        public static StageSession? Current { get; private set; }

        /// <summary>当前关卡配置（未就绪时为 null）。</summary>
        public StageConfig? CurrentConfig => _currentConfig;

        /// <summary>当前层数（未就绪时为 0 = 基准层）。</summary>
        public int CurrentFloor => _currentConfig?.Floor ?? 0;

        /// <summary>本层生效的难度配置：关卡自带覆盖（彩蛋关/Boss 层）优先，否则用会话级默认。
        /// 两边都没有 = 一律 ×1。</summary>
        private StageDifficultyConfig? ResolvedDifficulty => _currentConfig?.DifficultyOverride ?? Difficulty;

        /// <summary>本层敌人血量系数（Floor 0 严格 ×1；地下层走另一套、更强）。</summary>
        public float EnemyHealthMultiplier => ResolvedDifficulty?.HealthMultiplier(CurrentFloor) ?? 1f;

        /// <summary>本层敌人伤害系数（Floor 0 严格 ×1；地下层走另一套、更强）。</summary>
        public float EnemyDamageMultiplier => ResolvedDifficulty?.DamageMultiplier(CurrentFloor) ?? 1f;

        public override void _EnterTree()
        {
            Current = this;

            // 兜底：比本节点更早 _Ready 的敌人（关卡里预摆的）也要读得到层数——
            // 先按 StartStage/第一关填上，_Ready 的正常流程随后覆盖。
            _currentConfig ??= StartStage ?? (AllConfigs.Count > 0 ? AllConfigs[0] : null);
        }

        public override void _Ready()
        {
            AddToGroup("stage_session");

            _generator = GetNodeOrNull<StageGeneratorManager>(GeneratorPath);
            if (_generator == null)
            {
                GameLogger.Warn(nameof(StageSession), $"未找到 StageGeneratorManager（路径：{GeneratorPath}）。");
                return;
            }
            _generator.StageGenerated += OnStageGenerated;
            _generator.StageGenerationFailed += OnStageGenerationFailed;

            // 酒店首层由会话规划，加载路线中的房间后生成。
            var first = StartStage ?? (AllConfigs.Count > 0 ? AllConfigs[0] : null);
            if (first != null)
            {
                _currentConfig = first;
                _debugIndex = AllConfigs.IndexOf(first);
                GameLogger.Info(nameof(StageSession), $"首关：{first.StageId}（Floor {first.Floor}）");
                ApplyStageAsync(first);
            }
        }

        public override void _ExitTree()
        {
            if (Current == this)
                Current = null;

            if (_generator != null)
            {
                _generator.StageGenerated -= OnStageGenerated;
                _generator.StageGenerationFailed -= OnStageGenerationFailed;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!DebugKeysEnabled || _generator == null || AllConfigs.Count == 0) return;
            if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

            if (key.Keycode == Key.F1 || key.Keycode == Key.F2)
                CycleDebugConfig();
            if (key.Keycode == Key.F3)
                ActivateAllHiddenFloorsDebug();
        }

        private void ActivateAllHiddenFloorsDebug()
        {
            foreach (var config in AllConfigs)
                if (config?.IsHiddenFloor == true)
                    UnlockHiddenFloor(config);
        }

        public int PrepareElevatorOptions()
        {
            _pendingPlan = null;
            _pendingLoad = null;
            _displayedPlans = GetOptions().Select(GetCandidatePlan).OfType<StagePlan>().ToList();
            return _displayedPlans.Count;
        }

        private StagePlan? GetCandidatePlan(StageConfig config)
        {
            if (!_candidatePlans.TryGetValue(config, out var plan))
                _candidatePlans[config] = plan = _generator?.CreatePlan(config);
            return plan;
        }

        public IReadOnlyList<StagePlan> DisplayedPlans => _displayedPlans;

        public string DescribeOptions() => _displayedPlans.Count == 0
            ? "前方没有可前往的楼层"
            : string.Join("\n", _displayedPlans.Select((plan, index) =>
                $"{index + 1}. {plan.Config.DepartureLabel}　{plan.DescribeRoute()}"));

        public bool SelectStage(int index)
        {
            if (_transitionInProgress || index < 0 || index >= _displayedPlans.Count) return false;
            var plan = _displayedPlans[index];
            if (!GetOptions().Contains(plan.Config)) return false;
            _pendingPlan = plan;
            _pendingLoad = LoadPlanAsync(plan);
            return true;
        }

        public void CommitPending()
        {
            if (_pendingPlan is { } plan && PendingReady) QueueTransition(plan, _pendingLoad!.Result!);
        }

        public void GoToStage(StageConfig config, Vector2? landingPosition = null)
            => ApplyStageAsync(config, landingPosition);

        public void UnlockHiddenFloor(StageConfig config)
        {
            if (config.IsHiddenFloor) _unlockedHiddenFloors.Add(config);
        }

        public StagePlan? ChooseHiddenFloor(IEnumerable<StageConfig> destinations)
        {
            if (_transitionInProgress || _currentConfig?.IsHiddenFloor != false) return null;
            var plans = destinations.Where(config => config?.IsHiddenFloor == true)
                .Distinct().Select(GetCandidatePlan).OfType<StagePlan>().ToArray();
            return plans.Length == 0 ? null : plans[GD.RandRange(0, plans.Length - 1)];
        }

        public async Task<IReadOnlyList<PackedScene>?> LoadPlanAsync(StagePlan plan)
        {
            try
            {
                var rooms = await Task.WhenAll(plan.RoomPaths.Select(GetRoomLoad));
                return rooms.All(room => room?.CanInstantiate() == true)
                    ? rooms.Select(room => room!).ToArray() : null;
            }
            catch (Exception ex)
            {
                GD.PushWarning($"[StageSession] {plan.Config.StageId}: {ex.Message}");
                return null;
            }
        }

        private Task<PackedScene?> GetRoomLoad(string path)
        {
            if (!_roomLoads.TryGetValue(path, out var load))
                _roomLoads[path] = load = LoadRoomAsync(path);
            return load;
        }

        private async Task<PackedScene?> LoadRoomAsync(string path)
        {
            try
            {
                // 首次等待后才进入 finally，确保并行请求能先登记。
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return null;
                if (ResourceLoader.LoadThreadedRequest(path, "PackedScene") != Error.Ok) return null;
                while (true)
                {
                    switch (ResourceLoader.LoadThreadedGetStatus(path))
                    {
                        case ResourceLoader.ThreadLoadStatus.Loaded:
                            return ResourceLoader.LoadThreadedGet(path) as PackedScene;
                        case ResourceLoader.ThreadLoadStatus.InProgress:
                            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return null;
                            break;
                        default:
                            GD.PushWarning($"[StageSession] 房间加载失败：{path}");
                            return null;
                    }
                }
            }
            finally { _roomLoads.Remove(path); }
        }

        private void CycleDebugConfig()
        {
            if (_generator == null || AllConfigs.Count == 0 || _transitionInProgress) return;
            _debugIndex = (_debugIndex + 1) % AllConfigs.Count;
            ApplyStageAsync(AllConfigs[_debugIndex]);
        }

        private async void ApplyStageAsync(StageConfig config, Vector2? landingPosition = null)
        {
            if (_transitionInProgress || GetCandidatePlan(config) is not { } plan) return;
            var source = _currentConfig;
            _transitionInProgress = true;
            var rooms = await LoadPlanAsync(plan);
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
            _transitionInProgress = false;
            if (rooms != null && source == _currentConfig)
                QueueTransition(plan, rooms, landingPosition);
        }

        public bool QueueTransition(StagePlan plan, IReadOnlyList<PackedScene> rooms,
            Vector2? landingPosition = null, Func<bool>? stillValid = null)
        {
            if (_transitionInProgress || _generator == null
                || _generator.GetNodeOrNull<Node2D>(_generator.WorldNodePath) == null
                || rooms.Count != plan.RoomPaths.Count || !rooms.All(room => room.CanInstantiate()))
                return false;
            _transitionInProgress = true;
            Callable.From(() => ApplyStage(plan, rooms, landingPosition, stillValid)).CallDeferred();
            return true;
        }

        private void ApplyStage(StagePlan plan, IReadOnlyList<PackedScene> rooms,
            Vector2? landingPosition, Func<bool>? stillValid)
        {
            if (stillValid?.Invoke() == false)
            {
                _transitionInProgress = false;
                return;
            }
            var cutscene = GetParent()?.GetNodeOrNull<CutsceneManager>("CutsceneManager");
            if (cutscene?.IsPlaying == true)
            {
                _ = WaitForCutsceneAsync(cutscene, plan, rooms, landingPosition, stillValid);
                return;
            }

            var config = plan.Config;
            if (config.IsHiddenFloor)
                _hiddenEntryStage ??= _currentConfig;
            else
                _hiddenEntryStage = null;
            _currentConfig = config;
            _pendingPlan = null;
            _pendingLoad = null;
            _candidatePlans.Clear();
            _displayedPlans.Clear();

            foreach (var root in GetTree().GetNodesInGroup(CutsceneManager.SpawnedRootsGroup))
                if (GodotObject.IsInstanceValid(root)) root.QueueFree();

            _generator!.ClearWorldRemnants();
            Kuros.Items.World.WorldItemSpawner.ClearStageWorldItems(this);
            _generator.Regenerate(plan, rooms, landingPosition);
        }

        private async System.Threading.Tasks.Task WaitForCutsceneAsync(
            CutsceneManager cutscene, StagePlan plan, IReadOnlyList<PackedScene> rooms,
            Vector2? landingPosition, Func<bool>? stillValid)
        {
            while (GodotObject.IsInstanceValid(cutscene) && cutscene.IsPlaying)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(this) || !IsInsideTree()) return;
            }
            Callable.From(() => ApplyStage(plan, rooms, landingPosition, stillValid)).CallDeferred();
        }

        private List<StageConfig> GetOptions()
        {
            var list = new List<StageConfig>();
            var current = _currentConfig;
            if (current == null) return list;

            var baseConfig = current.IsHiddenFloor && _hiddenEntryStage != null ? _hiddenEntryStage : current;
            int upFloor = baseConfig.UpFloorTarget != 0 ? baseConfig.UpFloorTarget : baseConfig.Floor + 1;
            int downFloor = baseConfig.DownFloorTarget;

            foreach (var config in AllConfigs)
            {
                if (config == null || config == current) continue;

                if (!config.IsHiddenFloor)
                {
                    if (config.Floor == upFloor || (downFloor != 0 && config.Floor == downFloor))
                        list.Add(config);
                }
                else if (!current.IsHiddenFloor && IsHiddenFloorAccessible(config))
                {
                    list.Add(config);
                }
            }
            return list;
        }

        private bool IsHiddenFloorAccessible(StageConfig config)
        {
            if (_unlockedHiddenFloors.Contains(config)) return true;
            if (!string.IsNullOrWhiteSpace(config.RequiredStoryFlag)
                && SaveManager.Instance?.CurrentGameData?.StoryFlags?.Contains(config.RequiredStoryFlag) == true)
                return true;

            string key = config.RequiredRoomKeyItemId;
            if (string.IsNullOrWhiteSpace(key)) return false;
            var inventory = (GetTree().GetFirstNodeInGroup("player") as SamplePlayer)?.InventoryComponent;
            if (inventory == null) return false;
            return inventory.Backpack?.Slots.Any(stack => stack?.Item.ItemId == key) == true
                || inventory.QuickBar?.Slots.Any(stack => stack?.Item.ItemId == key) == true
                || inventory.SpecialSlots.Values.Any(slot => slot.Stack?.Item.ItemId == key);
        }

        private void OnStageGenerationFailed() => _transitionInProgress = false;

        private void OnStageGenerated()
        {
            _transitionInProgress = false;
            if (_currentConfig is { } config && GetParent() is BattleSceneManager { AutoShowLevelName: true } manager)
                manager.ShowLevelNamePopup(config.ArrivalName);

            // 玩家被传送到新关起点，相机直接跟随（避免平滑滑过整关）
            var camera = GetNodeOrNull<CameraFollow>(CameraPath);
            if (camera != null && GodotObject.IsInstanceValid(camera))
                camera.SnapToTarget();
        }
    }
}
