using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Kuros.Core;
using Kuros.Managers;
using Kuros.UI;

namespace Kuros.Environments
{
    /// <summary>
    /// 敌人生成控制台交互器。
    /// 挂载到场景中作为独立的敌人生成系统，支持玩家靠近时显示提示，按E打开生成窗口，
    /// 选择敌人并确认后直接生成敌人（不依赖EnemySpawnManager）。
    /// </summary>
    public partial class EnemySpawnConsole : Node2D
    {
        /// <summary>敌人出场的触发模式。</summary>
        public enum BackEffectSpawnGateMode
        {
            Delay,
            BackEffectFrame,
            BackEffectFinished
        }

        /// <summary>敌人生成完成的信号。</summary>
        [Signal] public delegate void SpawnStartedEventHandler();
        /// <summary>单个敌人生成完成的信号。</summary>
        [Signal] public delegate void EnemySpawnedEventHandler(Node enemy, int index);
        /// <summary>所有敌人生成完成的信号。</summary>
        [Signal] public delegate void SpawnCompletedEventHandler();

        [ExportCategory("References")]
        /// <summary>敌人生成的目标父节点。（如果为空则使用当前节点的父节点）</summary>
        [Export] public NodePath SpawnParentPath { get; set; } = new NodePath();

        [ExportCategory("Enemy Configuration")]
        /// <summary>可生成的敌人场景列表。</summary>
        [Export] public PackedScene[]? EnemyScenes { get; set; }

        [ExportCategory("Spawn Area")]
        /// <summary>生成区域路径。（如果为空则尝试自动查找）</summary>
        [Export] public NodePath SpawnAreaPath { get; set; } = new NodePath("SpawnArea");
        
        /// <summary>交互检测区域路径。（如果为空则尝试自动查找）</summary>
        [Export] public NodePath InteractAreaPath { get; set; } = new NodePath("InteractArea");
        
        /// <summary>提示标签路径。（如果为空则尝试自动查找）</summary>
        [Export] public NodePath HintLabelPath { get; set; } = new NodePath("HintLabel");

        /// <summary>敌人限制区域路径。（玩家离开此区域时，区域内的敌人将被清除）</summary>
        [Export] public NodePath TestAreaPath { get; set; } = new NodePath("SpawnArea");

        [ExportCategory("UI")]
        /// <summary>生成窗口的场景路径。</summary>
        [Export] public string SpawnConsoleWindowPath { get; set; } 
            = "res://scenes/ui/windows/EnemySpawnConsoleWindow.tscn";

        [ExportCategory("Effects")]
        /// <summary>背景出场特效场景路径（仅在激活时加载）。</summary>
        [Export] public string BackEffectScenePath { get; set; }
            = "res://scenes/actors/etc/enemy_spaw_back.tscn";

        /// <summary>前景出场特效场景路径（仅在激活时加载）。</summary>
        [Export] public string FrontEffectScenePath { get; set; }
            = "res://scenes/actors/etc/enemy_spawn_front.tscn";

        // 按需加载的运行时特效场景（在 ProcessSpawnRequestsAsync 期间加载，结束后释放）
        private PackedScene? _runtimeBackEffectScene;
        private PackedScene? _runtimeFrontEffectScene;

        /// <summary>背景特效的位置偏移。</summary>
        [Export] public Vector2 SpawnBackEffectOffset { get; set; } = Vector2.Zero;

        /// <summary>前景特效的位置偏移。</summary>
        [Export] public Vector2 SpawnFrontEffectOffset { get; set; } = Vector2.Zero;

        /// <summary>敌人生成的位置偏移（相对于计算出的生成点）。</summary>
        [Export] public Vector2 EnemySpawnOffset { get; set; } = Vector2.Zero;

        /// <summary>等待敌人出场的延迟时间（秒）。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float EnemyAppearDelay { get; set; } = 0.2f;

        /// <summary>敌人出场的触发模式。</summary>
        [Export] public BackEffectSpawnGateMode EnemyAppearGateMode { get; set; } = BackEffectSpawnGateMode.Delay;

        /// <summary>在BackEffectFrame模式下，等待背景特效到达的帧数。</summary>
        [Export(PropertyHint.Range, "0,300,1")] public int BackEffectAppearFrame { get; set; } = 8;

        /// <summary>在BackEffectFrame/BackEffectFinished模式下的超时时间（秒）。</summary>
        [Export(PropertyHint.Range, "0,10,0.05")] public float BackEffectGateTimeout { get; set; } = 3f;

        /// <summary>当特效门控失败时，是否回退到延迟模式。</summary>
        [Export] public bool FallbackToDelayWhenGateUnavailable { get; set; } = true;

        /// <summary>是否在敌人生成后自动降低前景特效（使敌人显示在特效前面）。</summary>
        [Export] public bool AutoLowerFrontEffectAfterEnemySpawn { get; set; } = false;

        /// <summary>降低前景特效的延迟时间（秒）。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float FrontEffectLowerDelay { get; set; } = 0f;

        /// <summary>前景特效生成后的Z轴偏移。</summary>
        [Export(PropertyHint.Range, "-1000,1000,1")] public int FrontEffectPostSpawnZOffset { get; set; } = -1;
        
        /// <summary>生成效果是否启用调试日志。</summary>
        [Export] public bool LogSpawnEffects { get; set; } = true;

        [ExportCategory("Spawn Behavior")]
        /// <summary>是否同时生成所有敌人（true 时并行生成，false 时顺序生成）。</summary>
        [Export] public bool SimultaneousSpawn { get; set; } = false;

        [ExportCategory("Test Area Boundary")]
        /// <summary>是否为 TestArea 创建空气墙边界（阻挡敌人离开）。</summary>
        [Export] public bool CreateTestAreaBoundary { get; set; } = true;

        /// <summary>空气墙的厚度。</summary>
        [Export(PropertyHint.Range, "1,50,1")] public float BoundaryThickness { get; set; } = 2f;

        /// <summary>空气墙的碰撞层（应该包含敌人能碰撞的层）。</summary>
        [Export(PropertyHint.Layers2DPhysics)] public uint BoundaryCollisionLayer { get; set; } = 0;

        /// <summary>空气墙的碰撞掩码（应该包含敌人层 Layer 2）。</summary>
        [Export(PropertyHint.Layers2DPhysics)] public uint BoundaryCollisionMask { get; set; } = 0;

        private Area2D? _interactArea;
        private Label? _hintLabel;
        private Area2D? _spawnArea;
        private Area2D? _testArea;
        private Node2D? _testAreaBoundary;  // 空气墙边界
        private EnemySpawnConsoleWindow? _spawnWindow;
        private bool _playerInRange;
        private EnemySpawnConfig? _currentSpawnConfig;

        public override void _Ready()
        {
            base._Ready();

            _interactArea = GetNodeOrNull<Area2D>(InteractAreaPath);
            _hintLabel = GetNodeOrNull<Label>(HintLabelPath);
            _spawnArea = GetNodeOrNull<Area2D>(SpawnAreaPath);
            _testArea = GetNodeOrNull<Area2D>(TestAreaPath);

            if (_interactArea != null)
            {
                _interactArea.BodyEntered += OnBodyEntered;
                _interactArea.BodyExited += OnBodyExited;
            }
            else
            {
                GD.PushWarning($"[{nameof(EnemySpawnConsole)}] InteractArea not found at {InteractAreaPath}");
            }

            if (_spawnArea == null)
            {
                GD.PushWarning($"[{nameof(EnemySpawnConsole)}] SpawnArea not found at {SpawnAreaPath}");
            }

            if (_testArea != null)
            {
                // 为 TestArea 创建空气墙边界（阻挡敌人离开）
                if (CreateTestAreaBoundary)
                {
                    CreateTestAreaBoundaryWalls();
                }
            }
            else
            {
                GD.PushWarning($"[{nameof(EnemySpawnConsole)}] TestArea not found at {TestAreaPath}");
            }

            UpdateHintLabel();
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            if (_interactArea != null)
            {
                _interactArea.BodyEntered -= OnBodyEntered;
                _interactArea.BodyExited -= OnBodyExited;
            }
            
            // 销毁空气墙
            RemoveTestAreaBoundaryWalls();
        }

        public override void _Process(double delta)
        {
            if (_playerInRange && Input.IsActionJustPressed("interact"))
            {
                OpenSpawnWindow();
            }
        }

        // ── 区域检测 ──────────────────────────────────────────────

        private void OnBodyEntered(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            _playerInRange = true;
            UpdateHintLabel();
        }

        private void OnBodyExited(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            _playerInRange = false;
            UpdateHintLabel();
        }

        private void UpdateHintLabel()
        {
            if (_hintLabel == null) return;
            _hintLabel.Visible = _playerInRange;
            if (_playerInRange)
                _hintLabel.Text = "[E] 打开生成器";
        }

        /// <summary>
        /// 为 TestArea 创建空气墙边界（由4个 StaticBody2D + CollisionShape2D 组成）。
        /// 阻挡敌人离开测试区域。
        /// 使用与 BattleArenaBoundary 完全相同的坐标计算方式。
        /// </summary>
        private void CreateTestAreaBoundaryWalls()
        {
            if (_testArea == null || !GodotObject.IsInstanceValid(_testArea))
                return;

            if (_testAreaBoundary != null && GodotObject.IsInstanceValid(_testAreaBoundary))
                return; // 已经存在

            // 获取 TestArea 的 CollisionShape2D
            var collisionShape = _testArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (collisionShape == null || collisionShape.Shape is not RectangleShape2D rectShape)
            {
                GD.PushWarning("[EnemySpawnConsole] TestArea must have a CollisionShape2D with RectangleShape2D");
                return;
            }

            // 使用与 BattleArenaBoundary 完全相同的坐标计算方式
            Vector2 arenaSize = rectShape.Size;
            Vector2 testAreaGlobalCenter = _testArea.GlobalPosition;
            Vector2 arenaPosition = testAreaGlobalCenter - arenaSize / 2f;  // Rect2 的 Position
            
            Vector2 arenaEnd = arenaPosition + arenaSize;
            Vector2 arenaCenter = arenaPosition + arenaSize / 2f;

            // 创建边界容器（作为 EnemySpawnConsole 的子节点）
            _testAreaBoundary = new Node2D
            {
                Name = "TestAreaBoundary",
                GlobalPosition = Vector2.Zero
            };

            AddChild(_testAreaBoundary);

            // 按 BattleArenaBoundary 的完全相同方式创建4面墙体
            
            // 上墙（Y-）
            CreateTestAreaWall(
                "TopWall",
                new Vector2(arenaCenter.X, arenaPosition.Y - BoundaryThickness / 2f),
                new Vector2(arenaSize.X, BoundaryThickness)
            );

            // 下墙（Y+）
            CreateTestAreaWall(
                "BottomWall",
                new Vector2(arenaCenter.X, arenaEnd.Y + BoundaryThickness / 2f),
                new Vector2(arenaSize.X, BoundaryThickness)
            );

            // 左墙（X-）
            CreateTestAreaWall(
                "LeftWall",
                new Vector2(arenaPosition.X - BoundaryThickness / 2f, arenaCenter.Y),
                new Vector2(BoundaryThickness, arenaSize.Y)
            );

            // 右墙（X+）
            CreateTestAreaWall(
                "RightWall",
                new Vector2(arenaEnd.X + BoundaryThickness / 2f, arenaCenter.Y),
                new Vector2(BoundaryThickness, arenaSize.Y)
            );

            if (LogSpawnEffects)
                GD.Print($"[EnemySpawnConsole] TestArea boundary walls created: arenaPosition={arenaPosition}, arenaSize={arenaSize}, arenaCenter={arenaCenter}");
        }

        /// <summary>
        /// 创建单个测试区域墙体。
        /// 使用与 BattleArenaBoundary.CreateWall() 完全相同的方式。
        /// </summary>
        private void CreateTestAreaWall(string wallName, Vector2 wallCenter, Vector2 wallSize)
        {
            var body = new StaticBody2D
            {
                Name = wallName,
                CollisionLayer = BoundaryCollisionLayer,
                CollisionMask = BoundaryCollisionMask
            };

            var shape = new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = wallSize }
            };

            body.AddChild(shape);
            _testAreaBoundary?.AddChild(body);

            // 设置全局位置（关键步骤，确保墙体位置正确）
            body.GlobalPosition = wallCenter;

            if (LogSpawnEffects)
                GD.Print($"[EnemySpawnConsole] Wall '{wallName}' created at GlobalPosition={wallCenter} with size {wallSize}");
        }

        /// <summary>
        /// 移除 TestArea 的空气墙边界。
        /// </summary>
        private void RemoveTestAreaBoundaryWalls()
        {
            if (_testAreaBoundary != null && GodotObject.IsInstanceValid(_testAreaBoundary))
            {
                _testAreaBoundary.QueueFree();
                _testAreaBoundary = null;

                if (LogSpawnEffects)
                    GD.Print("[EnemySpawnConsole] TestArea boundary walls removed");
            }
        }

        // ── 窗口打开 ──────────────────────────────────────────────

        private void OpenSpawnWindow()
        {
            var scenes = GetConfiguredEnemyScenes();
            if (scenes.Count == 0)
            {
                GD.PushWarning("[EnemySpawnConsole] No enemy scenes configured in EnemyScenes!");
                return;
            }

            EnsureSpawnWindow();
            if (_spawnWindow != null)
            {
                _spawnWindow.ShowWindow(scenes);
            }
        }

        private void EnsureSpawnWindow()
        {
            if (_spawnWindow != null && GodotObject.IsInstanceValid(_spawnWindow))
                return;

            if (UIManager.Instance == null)
            {
                GD.PushError("[EnemySpawnConsole] UIManager not initialized!");
                return;
            }

            _spawnWindow = UIManager.Instance.LoadUI<EnemySpawnConsoleWindow>(
                SpawnConsoleWindowPath, UILayer.GameUI, "EnemySpawnConsoleWindow");

            if (_spawnWindow == null)
            {
                GD.PushError("[EnemySpawnConsole] Failed to load EnemySpawnConsoleWindow!");
                return;
            }

            // 传递 TestArea 给窗口，以便窗口可以消灭敌人
            _spawnWindow.SetTestArea(_testArea);
            
            _spawnWindow.SpawnConfirmed += OnSpawnConfirmed;
        }

        // ── 生成处理 ──────────────────────────────────────────────

        private void OnSpawnConfirmed(EnemySpawnConfig config)
        {
            var scenes = GetConfiguredEnemyScenes();
            if (scenes.Count == 0) return;

            _currentSpawnConfig = config;
            _ = ProcessSpawnRequestsAsync(config.Requests, scenes);
        }

        private async System.Threading.Tasks.Task ProcessSpawnRequestsAsync(
            Dictionary<int, int> requests, List<PackedScene> scenes)
        {
            var totalSpawns = requests.Values.Sum();
            if (totalSpawns == 0) return;

            EmitSignal(SignalName.SpawnStarted);

            await LoadSpawnEffectScenesAsync();

            // 构建敌人场景队列
            List<PackedScene> spawnQueue = new();
            foreach (var kvp in requests)
            {
                int sceneIndex = kvp.Key;
                int count = kvp.Value;

                if (sceneIndex < 0 || sceneIndex >= scenes.Count) continue;

                for (int i = 0; i < count; i++)
                {
                    spawnQueue.Add(scenes[sceneIndex]);
                }
            }

            if (SimultaneousSpawn)
            {
                // 同时启动所有敌人的生成流程，互不等待
                var tasks = new System.Threading.Tasks.Task[spawnQueue.Count];
                for (int i = 0; i < spawnQueue.Count; i++)
                {
                    tasks[i] = SpawnSingleEnemyAsync(spawnQueue[i], i, spawnQueue.Count);
                }
                await System.Threading.Tasks.Task.WhenAll(tasks);
            }
            else
            {
                // 顺序生成敌人
                for (int i = 0; i < spawnQueue.Count; i++)
                {
                    await SpawnSingleEnemyAsync(spawnQueue[i], i, spawnQueue.Count);
                }
            }

            EmitSignal(SignalName.SpawnCompleted);
            ReleaseSpawnEffectScenes();
        }

        private async System.Threading.Tasks.Task LoadSpawnEffectScenesAsync()
        {
            bool needBack  = !string.IsNullOrEmpty(BackEffectScenePath);
            bool needFront = !string.IsNullOrEmpty(FrontEffectScenePath);

            if (needBack)
                ResourceLoader.LoadThreadedRequest(BackEffectScenePath);
            if (needFront)
                ResourceLoader.LoadThreadedRequest(FrontEffectScenePath);

            if (needBack)
            {
                while (ResourceLoader.LoadThreadedGetStatus(BackEffectScenePath) == ResourceLoader.ThreadLoadStatus.InProgress)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _runtimeBackEffectScene = ResourceLoader.LoadThreadedGet(BackEffectScenePath) as PackedScene;
            }

            if (needFront)
            {
                while (ResourceLoader.LoadThreadedGetStatus(FrontEffectScenePath) == ResourceLoader.ThreadLoadStatus.InProgress)
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                _runtimeFrontEffectScene = ResourceLoader.LoadThreadedGet(FrontEffectScenePath) as PackedScene;
            }
        }

        private void ReleaseSpawnEffectScenes()
        {
            _runtimeBackEffectScene  = null;
            _runtimeFrontEffectScene = null;
        }

        private async System.Threading.Tasks.Task SpawnSingleEnemyAsync(
            PackedScene enemyScene, int spawnIndex, int totalCount)
        {
            // 基准生成位置
            var spawnAnchorPosition = ResolveSpawnPosition(spawnIndex, totalCount);
            
            // 特效位置 = 基准位置 + 特效偏移
            var backEffectPos = spawnAnchorPosition + SpawnBackEffectOffset;
            var frontEffectPos = spawnAnchorPosition + SpawnFrontEffectOffset;
            
            // 播放特效并获取特效实例
            var effectRefs = PlaySpawnEffects(backEffectPos, frontEffectPos);

            // 等待敌人出场（根据模式）
            await WaitForEnemyAppearGateAsync(effectRefs.BackAnimatedSprite);

            // 敌人生成位置 = 基准位置 + 敌人偏移
            var enemySpawnPos = spawnAnchorPosition + EnemySpawnOffset;
            
            var enemy = SpawnEnemyDirect(enemyScene, enemySpawnPos, spawnIndex);
            if (enemy != null)
            {
                // 应用生成配置
                if (_currentSpawnConfig != null)
                {
                    if (_currentSpawnConfig.DisableAI)
                    {
                        DisableEnemyAI(enemy);
                    }
                    if (_currentSpawnConfig.DisableLoot)
                    {
                        DisableEnemyLoot(enemy);
                    }
                }

                EmitSignal(SignalName.EnemySpawned, enemy, spawnIndex);
                
                if (AutoLowerFrontEffectAfterEnemySpawn && effectRefs.FrontEffectInstance != null)
                {
                    LowerFrontEffectAfterEnemySpawn(effectRefs.FrontEffectInstance, enemy);
                }
                
                if (LogSpawnEffects)
                {
                    GD.Print($"[EnemySpawnConsole] Enemy spawned {spawnIndex + 1}/{totalCount} at {enemySpawnPos} (anchor={spawnAnchorPosition}, offset={EnemySpawnOffset})");
                }
            }
        }

        /// <summary>
        /// 根据 SpawnArea 的 CollisionShape2D 尺寸计算均匀分布的生成位置。
        /// 将生成位置在矩形区域内均匀分布，避免所有敌人重叠。
        /// </summary>
        private Vector2 ResolveSpawnPosition(int spawnIndex, int totalCount)
        {
            if (_spawnArea == null)
                return GlobalPosition;

            // 从 SpawnArea 的 CollisionShape2D 获取实际范围中心
            Vector2 spawnAreaCenter = GetSpawnAreaCenter();
            Vector2 extents = GetSpawnAreaExtents();
            
            // 只生成一个时放在中心
            if (totalCount == 1)
                return spawnAreaCenter;
            
            // 计算网格布局：按列数均匀分布
            int cols = Mathf.CeilToInt(Mathf.Sqrt(totalCount));
            int rows = Mathf.CeilToInt((float)totalCount / cols);
            
            int row = spawnIndex / cols;
            int col = spawnIndex % cols;
            
            // 计算步长（区域分成 cols+1 和 rows+1 份，在中间部分生成）
            float stepX = (extents.X * 2f) / (cols + 1);
            float stepY = (extents.Y * 2f) / (rows + 1);
            
            // 计算相对偏移（-extents 到 +extents 范围内）
            float offsetX = -extents.X + stepX * (col + 1);
            float offsetY = -extents.Y + stepY * (row + 1);
            
            // 基于 SpawnArea 中心计算生成点
            var result = spawnAreaCenter + new Vector2(offsetX, offsetY);
            
            if (LogSpawnEffects)
            {
                GD.Print($"[EnemySpawnConsole] Resolved spawn pos for index {spawnIndex}/{totalCount}: center={spawnAreaCenter}, grid [{row},{col}] offset ({offsetX:F1}, {offsetY:F1}) → {result}");
            }
            
            return result;
        }

        /// <summary>
        /// 获取 SpawnArea 的实际中心位置（考虑 CollisionShape2D 的偏移）。
        /// </summary>
        private Vector2 GetSpawnAreaCenter()
        {
            if (_spawnArea == null)
                return GlobalPosition;

            var collisionShape = _spawnArea.GetNode<CollisionShape2D>("CollisionShape2D");
            if (collisionShape == null)
                return _spawnArea.GlobalPosition;

            // SpawnArea 全局位置 + CollisionShape2D 的本地位置偏移 = 实际中心
            return _spawnArea.GlobalPosition + collisionShape.Position;
        }

        /// <summary>
        /// 从 SpawnArea 的 CollisionShape2D 获取生成范围的 Extents。
        /// </summary>
        private Vector2 GetSpawnAreaExtents()
        {
            if (_spawnArea == null)
                return Vector2.Zero;

            var collisionShape = _spawnArea.GetNode<CollisionShape2D>("CollisionShape2D");
            if (collisionShape == null || collisionShape.Shape is not RectangleShape2D rect)
                return Vector2.Zero;

            // RectangleShape2D 的 Size 是完整大小，Extents 是半径（Size / 2）
            return rect.Size / 2f;
        }

        private SpawnEffectRefs PlaySpawnEffects(Vector2 backEffectPos, Vector2 frontEffectPos)
        {
            SpawnEffectRefs effectRefs = new();

            if (_runtimeBackEffectScene == null && _runtimeFrontEffectScene == null)
                return effectRefs;

            Node? parent = GetParent() ?? GetTree().Root;
            if (parent == null) return effectRefs;

            if (_runtimeBackEffectScene != null)
            {
                var backInstance = _runtimeBackEffectScene.Instantiate<Node2D>();
                if (backInstance != null)
                {
                    parent.AddChild(backInstance);
                    backInstance.GlobalPosition = backEffectPos;
                    var backAnimSprite = ConfigureAndPlayEffect(backInstance);

                    effectRefs.BackEffectInstance = backInstance;
                    effectRefs.BackAnimatedSprite = backAnimSprite;

                    if (LogSpawnEffects)
                    {
                        GD.Print($"[EnemySpawnConsole] Back effect spawned at {backEffectPos}");
                    }
                }
            }

            if (_runtimeFrontEffectScene != null)
            {
                var frontInstance = _runtimeFrontEffectScene.Instantiate<Node2D>();
                if (frontInstance != null)
                {
                    parent.AddChild(frontInstance);
                    frontInstance.GlobalPosition = frontEffectPos;

                    // 应用Z偏移
                    if (FrontEffectPostSpawnZOffset != 0 && frontInstance is CanvasItem canvasItem)
                    {
                        canvasItem.ZIndex += FrontEffectPostSpawnZOffset;
                    }

                    var frontAnimSprite = ConfigureAndPlayEffect(frontInstance);
                    effectRefs.FrontEffectInstance = frontInstance;
                    effectRefs.FrontAnimatedSprite = frontAnimSprite;

                    if (LogSpawnEffects)
                    {
                        GD.Print($"[EnemySpawnConsole] Front effect spawned at {frontEffectPos}, z offset={FrontEffectPostSpawnZOffset}");
                    }
                }
            }

            return effectRefs;
        }

        /// <summary>
        /// 配置特效：重置动画状态，禁用循环播放，并在动画完成后自动销毁。
        /// </summary>
        private AnimatedSprite2D? ConfigureAndPlayEffect(Node2D effectInstance)
        {
            // 查找 AnimatedSprite2D 组件
            AnimatedSprite2D? animatedSprite = effectInstance as AnimatedSprite2D;
            if (animatedSprite == null)
            {
                // 如果不是 AnimatedSprite2D，在子节点中查找
                foreach (Node child in effectInstance.FindChildren("*", "AnimatedSprite2D", true, false))
                {
                    if (child is AnimatedSprite2D foundSprite)
                    {
                        animatedSprite = foundSprite;
                        break;
                    }
                }
            }

            if (animatedSprite != null && animatedSprite.SpriteFrames != null)
            {
                var animationName = animatedSprite.Animation;
                if (!string.IsNullOrEmpty(animationName.ToString()))
                {
                    // 重置动画状态到初始状态
                    animatedSprite.Visible = true;
                    animatedSprite.Frame = 0;
                    animatedSprite.FrameProgress = 0f;
                    animatedSprite.SpeedScale = 1f;
                    
                    // 禁用循环
                    animatedSprite.SpriteFrames.SetAnimationLoop(animationName, false);
                    
                    // 开始播放
                    animatedSprite.Play(animationName);

                    // 动画完成后销毁
                    animatedSprite.AnimationFinished += () =>
                    {
                        if (GodotObject.IsInstanceValid(effectInstance))
                        {
                            effectInstance.QueueFree();
                        }
                    };

                    if (LogSpawnEffects)
                    {
                        GD.Print($"[EnemySpawnConsole] Effect configured: anim={animationName}, loop=false, frames={animatedSprite.SpriteFrames.GetFrameCount(animationName)}, speedScale={animatedSprite.SpeedScale}");
                    }
                    return animatedSprite;
                }
                
                GD.PushWarning($"[EnemySpawnConsole] AnimatedSprite2D found but animation is invalid. anim={animatedSprite.Animation}");
            }
            else if (animatedSprite == null)
            {
                GD.PushWarning($"[EnemySpawnConsole] Effect scene does not contain AnimatedSprite2D");
            }

            effectInstance.Visible = true;

            // 如果没有找到有效的 AnimatedSprite2D 或动画，设置超时自动销毁
            var timer = GetTree().CreateTimer(Mathf.Max(EnemyAppearDelay, 0.5f));
            timer.Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(effectInstance))
                {
                    effectInstance.QueueFree();
                }
            };
            
            return animatedSprite;
        }

        /// <summary>
        /// 等待敌人出场，根据配置的模式选择等待方式。
        /// </summary>
        private async System.Threading.Tasks.Task WaitForEnemyAppearGateAsync(AnimatedSprite2D? backAnimatedSprite)
        {
            switch (EnemyAppearGateMode)
            {
                case BackEffectSpawnGateMode.BackEffectFrame:
                    if (await WaitForBackEffectFrameAsync(backAnimatedSprite))
                        return;
                    break;
                    
                case BackEffectSpawnGateMode.BackEffectFinished:
                    if (await WaitForBackEffectFinishedAsync(backAnimatedSprite))
                        return;
                    break;
            }

            // Fallback或Delay模式
            if (EnemyAppearGateMode == BackEffectSpawnGateMode.Delay || FallbackToDelayWhenGateUnavailable)
            {
                await System.Threading.Tasks.Task.Delay((int)(EnemyAppearDelay * 1000));
            }
        }

        /// <summary>
        /// 等待背景特效到达指定帧数。
        /// </summary>
        private async System.Threading.Tasks.Task<bool> WaitForBackEffectFrameAsync(AnimatedSprite2D? backAnimatedSprite)
        {
            if (!GodotObject.IsInstanceValid(backAnimatedSprite) || backAnimatedSprite == null)
                return false;

            int targetFrame = Mathf.Max(0, BackEffectAppearFrame);
            var animationName = backAnimatedSprite.Animation;
            
            if (backAnimatedSprite.SpriteFrames != null && !string.IsNullOrEmpty(animationName.ToString()))
            {
                int frameCount = backAnimatedSprite.SpriteFrames.GetFrameCount(animationName);
                if (frameCount > 0)
                {
                    targetFrame = Mathf.Clamp(targetFrame, 0, frameCount - 1);
                }
            }

            double timeout = Mathf.Max(0f, BackEffectGateTimeout);
            double start = Time.GetTicksMsec() / 1000.0;

            while (GodotObject.IsInstanceValid(backAnimatedSprite))
            {
                if (backAnimatedSprite.Frame >= targetFrame)
                {
                    if (LogSpawnEffects)
                        GD.Print($"[EnemySpawnConsole] Back effect frame gate reached: {backAnimatedSprite.Frame}/{targetFrame}");
                    return true;
                }

                if (timeout > 0 && (Time.GetTicksMsec() / 1000.0 - start) >= timeout)
                {
                    if (LogSpawnEffects)
                        GD.PushWarning($"[EnemySpawnConsole] Back effect frame gate timeout after {BackEffectGateTimeout}s");
                    return false;
                }

                await System.Threading.Tasks.Task.Delay(16); // ~60fps
            }

            return false;
        }

        /// <summary>
        /// 等待背景特效播放完成。
        /// </summary>
        private async System.Threading.Tasks.Task<bool> WaitForBackEffectFinishedAsync(AnimatedSprite2D? backAnimatedSprite)
        {
            if (!GodotObject.IsInstanceValid(backAnimatedSprite) || backAnimatedSprite == null)
                return false;

            if (!backAnimatedSprite.IsPlaying())
                return true;

            double timeout = Mathf.Max(0f, BackEffectGateTimeout);
            double start = Time.GetTicksMsec() / 1000.0;

            while (GodotObject.IsInstanceValid(backAnimatedSprite))
            {
                if (!backAnimatedSprite.IsPlaying())
                {
                    if (LogSpawnEffects)
                        GD.Print($"[EnemySpawnConsole] Back effect finished gate reached");
                    return true;
                }

                if (timeout > 0 && (Time.GetTicksMsec() / 1000.0 - start) >= timeout)
                {
                    if (LogSpawnEffects)
                        GD.PushWarning($"[EnemySpawnConsole] Back effect finished gate timeout after {BackEffectGateTimeout}s");
                    return false;
                }

                await System.Threading.Tasks.Task.Delay(16); // ~60fps
            }

            return false;
        }

        /// <summary>
        /// 敌人生成后，降低前景特效使其显示在敌人后面。
        /// </summary>
        private async void LowerFrontEffectAfterEnemySpawn(Node2D frontEffectNode, Node enemy)
        {
            if (frontEffectNode == null || !GodotObject.IsInstanceValid(frontEffectNode))
                return;

            if (enemy is not Node2D enemyNode || !GodotObject.IsInstanceValid(enemyNode))
                return;

            // 等待降低延迟
            if (FrontEffectLowerDelay > 0f)
            {
                await System.Threading.Tasks.Task.Delay((int)(FrontEffectLowerDelay * 1000));
            }

            if (!GodotObject.IsInstanceValid(frontEffectNode) || !GodotObject.IsInstanceValid(enemyNode))
                return;

            // 将前景特效的Z设置为敌人Z - 1（在敌人后面）
            if (frontEffectNode is CanvasItem frontCanvas && enemyNode is CanvasItem enemyCanvas)
            {
                frontCanvas.ZIndex = enemyCanvas.ZIndex - 1;
                
                if (LogSpawnEffects)
                {
                    GD.Print($"[EnemySpawnConsole] Front effect lowered: {frontCanvas.Name} z={frontCanvas.ZIndex} (enemy z={enemyCanvas.ZIndex})");
                }
            }
        }

        /// <summary>
        /// 特效引用容器。
        /// </summary>
        private sealed class SpawnEffectRefs
        {
            public Node2D? BackEffectInstance;
            public AnimatedSprite2D? BackAnimatedSprite;
            public Node2D? FrontEffectInstance;
            public AnimatedSprite2D? FrontAnimatedSprite;
        }

        /// <summary>
        /// 获取配置的敌人场景列表。
        /// </summary>
        public List<PackedScene> GetConfiguredEnemyScenes()
        {
            List<PackedScene> scenes = new();
            if (EnemyScenes == null) return scenes;
            
            foreach (var scene in EnemyScenes)
            {
                if (scene != null)
                {
                    scenes.Add(scene);
                }
            }
            return scenes;
        }

        /// <summary>
        /// 直接生成敌人（不依赖EnemySpawnManager）。
        /// </summary>
        public Node? SpawnEnemyDirect(PackedScene enemyScene, Vector2 spawnPosition, int spawnIndex)
        {
            if (enemyScene == null)
            {
                GD.PushWarning($"[{nameof(EnemySpawnConsole)}] EnemyScene 未设置，无法生成敌人。");
                return null;
            }

            var instance = enemyScene.Instantiate();
            if (instance == null)
            {
                GD.PushWarning($"[{nameof(EnemySpawnConsole)}] 敌人场景实例化失败。scene={enemyScene.ResourcePath}");
                return null;
            }

            // 在添加到场景树之前禁用 EnemySpawn 状态
            // 这样当 _Ready() 被调用时，EnemySpawn 已经被禁用了
            DisableEnemySpawnState(instance);

            var parent = ResolveSpawnParent();
            parent.AddChild(instance);

            if (instance is Node2D node2D)
            {
                node2D.GlobalPosition = spawnPosition;
                node2D.Visible = true;
                StabilizeSpawnedEnemyVisualAsync(node2D);
            }

            EnsureSpawnedEnemyVisible(instance);

            if (instance is GameActor actor)
            {
                actor.FlipFacing(false);
            }

            if (instance is Node node)
            {
                node.Name = $"{node.Name}_{spawnIndex + 1}";
            }

            return instance;
        }

        /// <summary>
        /// 获取敌人应该添加到的父节点。
        /// </summary>
        private Node ResolveSpawnParent()
        {
            if (!SpawnParentPath.IsEmpty)
            {
                var customParent = GetNodeOrNull<Node>(SpawnParentPath);
                if (customParent != null)
                {
                    return customParent;
                }
            }

            return GetParent() ?? this;
        }

        /// <summary>
        /// 确保敌人及其子节点可见且不透明。
        /// </summary>
        private void EnsureSpawnedEnemyVisible(Node enemyRoot)
        {
            EnsureCanvasItemVisible(enemyRoot as CanvasItem);

            Node? spineNode = enemyRoot.GetNodeOrNull("SpineSprite");
            if (spineNode is CanvasItem spineCanvas)
            {
                EnsureCanvasItemVisible(spineCanvas);
            }

            Node? spriteNode = enemyRoot.GetNodeOrNull("Sprite2D");
            if (spriteNode is CanvasItem spriteCanvas)
            {
                EnsureCanvasItemVisible(spriteCanvas);
            }

            if (LogSpawnEffects)
            {
                string spineInfo = DescribeCanvasItem(spineNode as CanvasItem);
                string spriteInfo = DescribeCanvasItem(spriteNode as CanvasItem);
                GD.Print($"[{nameof(EnemySpawnConsole)}] Enemy visual restore: root={DescribeCanvasItem(enemyRoot as CanvasItem)}, spine={spineInfo}, sprite={spriteInfo}");
            }
        }

        /// <summary>
        /// 确保单个CanvasItem可见且透明度为1。
        /// </summary>
        private static void EnsureCanvasItemVisible(CanvasItem? item)
        {
            if (item == null || !GodotObject.IsInstanceValid(item))
            {
                return;
            }

            item.Visible = true;
            Color modulate = item.Modulate;
            if (modulate.A < 1f)
            {
                modulate.A = 1f;
                item.Modulate = modulate;
            }

            Color selfModulate = item.SelfModulate;
            if (selfModulate.A < 1f)
            {
                selfModulate.A = 1f;
                item.SelfModulate = selfModulate;
            }
        }

        /// <summary>
        /// 获取CanvasItem的描述信息（用于日志）。
        /// </summary>
        private static string DescribeCanvasItem(CanvasItem? item)
        {
            if (item == null || !GodotObject.IsInstanceValid(item))
            {
                return "null";
            }

            return $"{item.Name}(visible={item.Visible}, modA={item.Modulate.A:0.##}, selfA={item.SelfModulate.A:0.##}, z={item.ZIndex})";
        }

        /// <summary>
        /// 异步稳定敌人的视觉效果（多帧等待确保正确渲染）。
        /// </summary>
        private async void StabilizeSpawnedEnemyVisualAsync(Node2D enemyNode2D)
        {
            if (!GodotObject.IsInstanceValid(enemyNode2D))
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                if (!GodotObject.IsInstanceValid(enemyNode2D))
                {
                    return;
                }

                EnsureSpawnedEnemyVisible(enemyNode2D);
            }

            if (LogSpawnEffects)
            {
                GD.Print($"[{nameof(EnemySpawnConsole)}] Enemy visual stabilization complete: {DescribeCanvasItem(enemyNode2D)}");
            }
        }

        /// <summary>
        /// 禁用敌人 EnemySpawn 状态（所有测试敌人都不能召唤其他敌人）。
        /// </summary>
        private void DisableEnemySpawnState(Node enemy)
        {
            if (enemy == null || !GodotObject.IsInstanceValid(enemy))
                return;

            var stateMachine = enemy.GetNodeOrNull<Node>("StateMachine");
            if (stateMachine == null)
                return;

            var enemySpawnState = stateMachine.GetNodeOrNull<Node>("EnemySpawn");
            if (enemySpawnState != null)
            {
                stateMachine.RemoveChild(enemySpawnState);
                enemySpawnState.QueueFree();
            }
        }

        /// <summary>
        /// 禁用敌人AI（暂停所有AI行为）。
        /// </summary>
        private void DisableEnemyAI(Node enemy)
        {
            if (enemy == null || !GodotObject.IsInstanceValid(enemy))
                return;

            // 策略：禁用 ControllerDetectionArea、AttackDetectionArea 和 AttackArea 的碰撞检测
            // 直接将collision_mask设置为0，让它们无法检测任何东西（包括玩家）
            bool aiDisabled = false;

            // 禁用 ControllerDetectionArea（用于AttackController的玩家检测）
            var controllerDetectionArea = enemy.GetNodeOrNull<Area2D>("Sprite2D/ControllerDetectionArea");
            if (controllerDetectionArea != null)
            {
                controllerDetectionArea.CollisionMask = 0;
                if (LogSpawnEffects)
                    GD.Print($"[EnemySpawnConsole] Disabled ControllerDetectionArea collision_mask for enemy: {enemy.Name}");
                aiDisabled = true;
            }

            // 禁用 AttackDetectionArea（用于某些攻击的玩家检测）
            var attackDetectionArea = enemy.GetNodeOrNull<Area2D>("Sprite2D/AttackDetectionArea");
            if (attackDetectionArea != null)
            {
                attackDetectionArea.CollisionMask = 0;
                if (LogSpawnEffects)
                    GD.Print($"[EnemySpawnConsole] Disabled AttackDetectionArea collision_mask for enemy: {enemy.Name}");
                aiDisabled = true;
            }

            // 禁用 AttackArea（用于发起攻击的伤害判定区域）
            var attackArea = enemy.GetNodeOrNull<Area2D>("Sprite2D/AttackArea");
            if (attackArea != null)
            {
                attackArea.CollisionMask = 0;
                if (LogSpawnEffects)
                    GD.Print($"[EnemySpawnConsole] Disabled AttackArea collision_mask for enemy: {enemy.Name}");
                aiDisabled = true;
            }

            if (aiDisabled)
            {
                if (LogSpawnEffects)
                    GD.Print($"[EnemySpawnConsole] AI disabled for enemy: {enemy.Name}");
            }
            else
            {
                GD.PushWarning($"[EnemySpawnConsole] Could not find detection areas for enemy: {enemy.Name}");
            }
        }

        /// <summary>
        /// 禁用敌人掉落物品（清空LootDropTable）。
        /// </summary>
        private void DisableEnemyLoot(Node enemy)
        {
            if (enemy == null || !GodotObject.IsInstanceValid(enemy))
                return;

            // 尝试获取 GameActor 并清空其 LootTable
            if (enemy is Core.GameActor gameActor)
            {
                gameActor.LootTable = null;
                if (LogSpawnEffects)
                    GD.Print($"[EnemySpawnConsole] Disabled loot for enemy: {enemy.Name}");
                return;
            }

            GD.PushWarning($"[EnemySpawnConsole] Could not disable loot for enemy: {enemy.Name}");
        }
    }
}
