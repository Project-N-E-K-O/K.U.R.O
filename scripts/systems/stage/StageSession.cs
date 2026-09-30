using System;
using Godot;
using Godot.Collections;
using System.Collections.Generic;
using Kuros.Managers;
using Kuros.Utils;

namespace Kuros.Systems.Stage
{
    /// <summary>
    /// 关卡会话：持有全部注册的 StageConfig，跟踪当前关卡与所在层（Floor）。
    /// 电梯选关选项 = 下一层（Floor+1）的所有分支 + 已解锁彩蛋关；出舱时应用所选配置（world 内换关）。
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
        private int _pendingIndex = -1;

        /// <summary>已激活的彩蛋关（条件触发后出现在电梯面板）。</summary>
        private readonly HashSet<StageConfig> _activatedEggs = new();
        /// <summary>已进入过的彩蛋关（一次性：消费后不再出现，防重复触发）。</summary>
        private readonly HashSet<StageConfig> _consumedEggs = new();

        /// <summary>进入彩蛋前的所在层（彩蛋是两层间的隐藏停靠站——离开彩蛋后继续入口层的行程）。</summary>
        private StageConfig? _eggEntryStage;

        /// <summary>换关流程进行中（房间池后台预加载等待）——期间新请求排队，完成后自动应用最新。</summary>
        private bool _transitionInProgress;
        private StageConfig? _queuedConfig;

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

            // 首关（配合 StageGeneratorManager.GenerateOnReady=false）——房间池预加载后生成
            var first = StartStage ?? (AllConfigs.Count > 0 ? AllConfigs[0] : null);
            if (first != null)
            {
                _currentConfig = first;
                GameLogger.Info(nameof(StageSession), $"首关：{first.StageId}（Floor {first.Floor}）");
                ApplyStageAsync(first);
            }
        }

        public override void _ExitTree()
        {
            if (Current == this)
                Current = null;

            if (_generator != null)
                _generator.StageGenerated -= OnStageGenerated;
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!DebugKeysEnabled || _generator == null || AllConfigs.Count == 0) return;
            if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;

            if (key.Keycode == Key.F1 || key.Keycode == Key.F2)
                CycleDebugConfig();
            if (key.Keycode == Key.F3)
                ActivateAllEasterEggsDebug();
        }

        /// <summary>调试：激活全部已注册彩蛋（替代机关/剧情激活入口，验证彩蛋流程用）。</summary>
        private void ActivateAllEasterEggsDebug()
        {
            int count = 0;
            foreach (var config in AllConfigs)
            {
                if (config?.EasterEgg == true && _activatedEggs.Add(config))
                    count++;
            }
            GameLogger.Info(nameof(StageSession), $"调试激活 {count} 个彩蛋关");
        }

        // ── 电梯会话接口（ElevatorController 调用）─────────────────

        /// <summary>当前电梯可选关卡数（下一层分支 + 已解锁彩蛋）。</summary>
        public int GetOptionCount() => GetOptions().Count;

        /// <summary>拼装选关提示："1=餐厅a区  2=餐厅b区"。</summary>
        public string DescribeOptions()
        {
            var options = GetOptions();
            if (options.Count == 0) return "前方没有可前往的楼层";
            var parts = new List<string>();
            for (int i = 0; i < options.Count; i++)
            {
                var config = options[i];
                string label = !string.IsNullOrEmpty(config?.DisplayName)
                    ? config.DisplayName
                    : config?.StageId ?? "?";
                parts.Add($"{i + 1}={label}");
            }
            return string.Join("  ", parts);
        }

        /// <summary>玩家在电梯里选定目标关卡（出舱时才真正生效）。</summary>
        public void SelectStage(int index)
        {
            var options = GetOptions();
            if (index < 0 || index >= options.Count) return;
            _pendingIndex = index;
            GameLogger.Info(nameof(StageSession), $"电梯选定：{options[index]?.StageId ?? "(null)"}");
        }

        /// <summary>当前电梯选定目标（_pendingIndex）的房间池是否已预加载就绪（门可开）。</summary>
        public bool IsPendingReady { get; private set; } = true;

        /// <summary>立即后台预加载电梯选中的目标 config 房间池（ElevatorController.BeginLoading 时调用）。
        /// 无有效选择/无生成器时直接视为就绪（不卡门）。完成后 IsPendingReady=true（含失败路径）。</summary>
        public void PreloadPending()
        {
            IsPendingReady = false;
            if (_generator == null) { IsPendingReady = true; return; }

            var options = GetOptions();
            if (_pendingIndex < 0 || _pendingIndex >= options.Count)
            {
                IsPendingReady = true;
                return;
            }
            _ = PreloadPendingAsync(options[_pendingIndex]);
        }

        private async System.Threading.Tasks.Task PreloadPendingAsync(StageConfig config)
        {
            try { await PreloadStageRoomsAsync(config); }
            catch (Exception ex)
            {
                GameLogger.Warn(nameof(StageSession), $"预加载异常：{ex.Message}");
            }
            if (GodotObject.IsInstanceValid(this))
                IsPendingReady = true; // 含 Failed 路径（PreloadStageRoomsAsync 已把 Failed 当完成）——不卡门
        }

        /// <summary>出舱动作：应用选中的关卡配置（world 内换关，房间池预加载后生成）。</summary>
        public void CommitPending()
        {
            var options = GetOptions();
            if (_generator == null || _pendingIndex < 0 || _pendingIndex >= options.Count)
                return;

            var config = options[_pendingIndex];
            _pendingIndex = -1;
            ApplyStageAsync(config);
        }

        /// <summary>直接跳转指定关卡（隐藏入口/机关/剧情切关）：不经电梯骑行，房间池预加载后生成。
        /// landingPosition = 玩家落点（世界坐标，密道出口用）；null = 新关起点。</summary>
        public void GoToStage(StageConfig config, Vector2? landingPosition = null)
        {
            if (config == null || _generator == null) return;
            ApplyStageAsync(config, landingPosition);
        }

        /// <summary>激活彩蛋关（隐藏机关/剧情事件等条件达成时调用）——此后出现在电梯面板尾部。
        /// 进入该彩蛋关后自动消费（一次性，不再出现）。</summary>
        public void ActivateEasterEgg(StageConfig config)
        {
            if (config?.EasterEgg != true) return;
            if (_activatedEggs.Add(config))
                GameLogger.Info(nameof(StageSession), $"彩蛋已激活：{config.StageId}");
        }

        // ── 调试/内部 ─────────────────────────────────────────────

        private void CycleDebugConfig()
        {
            if (_generator == null || AllConfigs.Count == 0) return;
            _debugIndex = (_debugIndex + 1) % AllConfigs.Count;
            ApplyStageAsync(AllConfigs[_debugIndex]);
        }

        // ── 换关流程（方案 B：房间池后台预加载 → 完成才 Regenerate）──

        /// <summary>异步换关入口：先后台预加载 config 的房间池，完成后才清空/重生成。
        /// 切换进行中时新请求排队（_queuedConfig），完成后自动应用最新——等待期旧关完整保留。</summary>
        private void ApplyStageAsync(StageConfig config, Vector2? landingPosition = null)
        {
            if (config == null || _generator == null) return;

            if (_transitionInProgress)
            {
                _queuedConfig = config;
                GameLogger.Info(nameof(StageSession), $"换关进行中，排队：{config.StageId}");
                return;
            }

            _transitionInProgress = true;
            GameLogger.Info(nameof(StageSession), $"预加载房间池：{config.StageId}…");
            _ = RunTransitionAsync(config, landingPosition);
        }

        private async System.Threading.Tasks.Task RunTransitionAsync(StageConfig config, Vector2? landingPosition)
        {
            try
            {
                await PreloadStageRoomsAsync(config);
            }
            catch (Exception ex)
            {
                GameLogger.Warn(nameof(StageSession), $"房间池预加载异常：{ex.Message}");
            }

            // 节点可能已被释放（场景卸载/壳销毁）——后续访问前检查
            if (!GodotObject.IsInstanceValid(this) || _generator == null)
                return;

            if (_queuedConfig != null)
            {
                var queued = _queuedConfig;
                _queuedConfig = null;
                _transitionInProgress = false;
                ApplyStageAsync(queued);
                return;
            }

            _transitionInProgress = false;
            GameLogger.Info(nameof(StageSession), $"房间就绪，生成 {config.StageId}");
            ApplyStage(config, landingPosition);
        }

        /// <summary>后台加载 config 全部房间池（五池去重），每帧轮询等待完成——主线程不阻塞。
        /// 参考 EnemySpawnManager.LoadSpawnEffectScenesAsync 的异步等待范式。</summary>
        private async System.Threading.Tasks.Task PreloadStageRoomsAsync(StageConfig config)
        {
            var paths = new List<string>();
            CollectPoolPaths(paths, config.BeginPool);
            CollectPoolPaths(paths, config.EndPool);
            CollectPoolPaths(paths, config.EasyMiddlePool);
            CollectPoolPaths(paths, config.NormalMiddlePool);
            CollectPoolPaths(paths, config.HardMiddlePool);
            if (paths.Count == 0) return;

            foreach (var path in paths)
            {
                // 已缓存/正在加载的资源重复请求引擎幂等处理（缓存命中立即 Loaded）
                ResourceLoader.LoadThreadedRequest(path);
            }

            while (true)
            {
                if (!GodotObject.IsInstanceValid(this)) return;

                bool allDone = true;
                foreach (var path in paths)
                {
                    var status = ResourceLoader.LoadThreadedGetStatus(path);
                    if (status == ResourceLoader.ThreadLoadStatus.InProgress)
                    {
                        allDone = false;
                        break;
                    }
                    if (status == ResourceLoader.ThreadLoadStatus.Failed)
                        GameLogger.Warn(nameof(StageSession), $"房间加载失败：{path}");
                }
                if (allDone) break;

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        private static void CollectPoolPaths(List<string> paths, Array<PackedScene> pool)
        {
            foreach (var scene in pool)
            {
                if (scene == null) continue;
                string path = scene.ResourcePath;
                if (!string.IsNullOrEmpty(path) && !paths.Contains(path))
                    paths.Add(path);
            }
        }

        /// <summary>应用关卡配置：记录当前关并重新生成（玩家传送到新关起点或指定落点）。
        /// 进入彩蛋关即消费（_consumedEggs，防重复）并记录入口层（_eggEntryStage，供彩蛋层电梯推导行程）。</summary>
        private void ApplyStage(StageConfig config, Vector2? landingPosition = null)
        {
            if (config.EasterEgg)
            {
                _eggEntryStage = _currentConfig;
                _consumedEggs.Add(config);
            }
            else
            {
                _eggEntryStage = null;
            }
            _currentConfig = config;
            GameLogger.Info(nameof(StageSession), $"前往：{config.StageId}（Floor {config.Floor}）");

            // 换关清场（Regenerate 重排前）:整场白名单清理 World 直属残留——覆盖所有直连生成
            // 的残留物(place 家具/掉落/OnThrowDestroy 效果物 HealItemA/破坏产物/敌人死亡家具体),
            // 玩家资产不回收,语义 = 关卡重置即清场。房间由 Regenerate 自行释放。
            _generator!.ClearWorldRemnants();
            // 兜底:组追踪清理(覆盖挂在非 World 容器的动态道具)
            Kuros.Items.World.WorldItemSpawner.ClearStageWorldItems(this);

            _generator!.Regenerate(config, relocateActors: true, landingPosition: landingPosition);
        }

        /// <summary>
        /// 可选关卡：
        ///   推导基准 = 当前层；若当前在彩蛋层则用**入口层**（彩蛋是两层间的隐藏停靠站——离开后继续入口层的行程）；
        ///   普通分支 = 上行目标层（UpFloorTarget，0 时默认 Floor+1）+ 下行目标层（DownFloorTarget，0=无）；
        ///   彩蛋分支只出现在非彩蛋层的电梯（彩蛋层不再显示彩蛋）；
        ///   排除当前关自身。
        /// </summary>
        private List<StageConfig> GetOptions()
        {
            var list = new List<StageConfig>();
            var current = _currentConfig;
            if (current == null) return list;

            var baseConfig = current.EasterEgg && _eggEntryStage != null ? _eggEntryStage : current;
            int upFloor = baseConfig.UpFloorTarget != 0 ? baseConfig.UpFloorTarget : baseConfig.Floor + 1;
            int downFloor = baseConfig.DownFloorTarget;

            foreach (var config in AllConfigs)
            {
                if (config == null || config == current) continue;

                if (!config.EasterEgg)
                {
                    if (config.Floor == upFloor || (downFloor != 0 && config.Floor == downFloor))
                        list.Add(config);
                }
                else if (!current.EasterEgg && IsEggVisible(config))
                {
                    list.Add(config);
                }
            }
            return list;
        }

        /// <summary>
        /// 彩蛋可见条件（运行时三态）：
        ///   ① 已消费（进入过）→ 永不可见（防重复触发）
        ///   ② ActivateEasterEgg 显式激活 → 可见
        ///   ③ RequiredStoryFlag 非空且存档旗标满足 → 可见
        ///   未激活且无旗标 → 不可见（彩蛋默认关闭，不再"始终可选"）
        /// </summary>
        private bool IsEggVisible(StageConfig config)
        {
            if (!config.EasterEgg || _consumedEggs.Contains(config)) return false;
            if (_activatedEggs.Contains(config)) return true;
            if (string.IsNullOrEmpty(config.RequiredStoryFlag)) return false;
            var flags = SaveManager.Instance?.CurrentGameData?.StoryFlags;
            return flags != null && flags.Contains(config.RequiredStoryFlag);
        }

        private void OnStageGenerated()
        {
            // 玩家被传送到新关起点，相机直接跟随（避免平滑滑过整关）
            var camera = GetNodeOrNull<CameraFollow>(CameraPath);
            if (camera != null && GodotObject.IsInstanceValid(camera))
                camera.SnapToTarget();
        }
    }
}
