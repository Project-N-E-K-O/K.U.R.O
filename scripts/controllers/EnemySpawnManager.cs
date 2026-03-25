using System;
using Godot;
using Godot.Collections;
using Kuros.Core;

namespace Kuros.Controllers
{
    /// <summary>
    /// 进入触发范围后批量生成敌人的管理器。
    /// 支持选择敌人场景、生成数量、触发范围，以及前后景出场动画。
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class EnemySpawnManager : Node2D
    {
        public enum BackEffectSpawnGateMode
        {
            Delay,
            BackEffectFrame,
            BackEffectFinished
        }

        [Signal] public delegate void SpawnStartedEventHandler();
        [Signal] public delegate void EnemySpawnedEventHandler(Node enemy, int index);
        [Signal] public delegate void SpawnCompletedEventHandler();

        [ExportCategory("Enemy")]
        [Export] public PackedScene EnemyScene { get; set; } = null!;
        [Export(PropertyHint.Range, "1,100,1")] public int SpawnCount { get; set; } = 1;
        [Export(PropertyHint.Range, "0,10,0.05")] public float SpawnInterval { get; set; } = 0.15f;
        [Export] public NodePath SpawnParentPath { get; set; } = new NodePath();
        [Export] public bool SpawnOnReady { get; set; } = false;
        [Export] public bool TriggerOnce { get; set; } = true;

        [ExportCategory("Trigger")]
        [Export] public Area2D? TriggerArea { get; set; }
        [Export] public bool AutoConfigureAssignedTriggerArea { get; set; } = false;
        [Export] public string TriggerGroupName { get; set; } = "player";
        [Export] public Vector2 TriggerOffset { get; set; } = Vector2.Zero;
        [Export] public Vector2 TriggerSize { get; set; } = new Vector2(320, 180);
        [Export] public uint TriggerCollisionLayer { get; set; } = 0;
        [Export] public uint TriggerCollisionMask { get; set; } = uint.MaxValue;

        [ExportCategory("Spawn Placement")]
        [Export] public bool UseExplicitSpawnOffsets { get; set; } = false;
        [Export] public Array<Vector2> SpawnOffsets { get; set; } = new();
        [Export] public Vector2 SpawnAreaExtents { get; set; } = new Vector2(96, 48);
        [Export] public bool AlignEnemyFacingToManager { get; set; } = true;
        [Export] public bool FaceRightOnSpawn { get; set; } = false;

        [ExportCategory("Spawn FX")]
        [Export] public PackedScene? SpawnBackEffectScene { get; set; } = GD.Load<PackedScene>("res://scenes/actors/etc/enemy_spaw_back.tscn");
        [Export] public PackedScene? SpawnFrontEffectScene { get; set; } = GD.Load<PackedScene>("res://scenes/actors/etc/enemy_spawn_front.tscn");
        [Export] public Vector2 SpawnBackEffectOffset { get; set; } = Vector2.Zero;
        [Export] public Vector2 SpawnFrontEffectOffset { get; set; } = Vector2.Zero;
        [Export(PropertyHint.Range, "0,5,0.05")] public float EnemyAppearDelay { get; set; } = 0.2f;
        [Export] public BackEffectSpawnGateMode EnemyAppearGateMode { get; set; } = BackEffectSpawnGateMode.Delay;
        [Export(PropertyHint.Range, "0,300,1")] public int BackEffectAppearFrame { get; set; } = 8;
        [Export(PropertyHint.Range, "0,10,0.05")] public float BackEffectGateTimeout { get; set; } = 3f;
        [Export] public bool FallbackToDelayWhenGateUnavailable { get; set; } = true;
        [Export(PropertyHint.Range, "-1000,1000,1")] public int BackEffectZOffset { get; set; } = -1;
        [Export(PropertyHint.Range, "-1000,1000,1")] public int FrontEffectZOffset { get; set; } = 1;
        [Export] public bool AutoLowerFrontEffectAfterEnemySpawn { get; set; } = true;
        [Export(PropertyHint.Range, "0,5,0.05")] public float FrontEffectLowerDelay { get; set; } = 0f;
        [Export(PropertyHint.Range, "-1000,1000,1")] public int FrontEffectPostSpawnZOffset { get; set; } = -1;

        [ExportCategory("Y-Sort")]
        [Export] public bool EnableYAxisAutoLayering { get; set; } = false;
        [Export(PropertyHint.Range, "0.1,20,0.1")] public float YAxisZScale { get; set; } = 1f;
        [Export(PropertyHint.Range, "-10000,10000,1")] public int YAxisZBase { get; set; } = 0;
        [Export] public bool ClampYAxisZRange { get; set; } = true;
        [Export(PropertyHint.Range, "-10000,10000,1")] public int YAxisZMin { get; set; } = -200;
        [Export(PropertyHint.Range, "-10000,10000,1")] public int YAxisZMax { get; set; } = 400;
        [Export(PropertyHint.Range, "-1000,1000,1")] public int EnemyZOffset { get; set; } = 0;

        [ExportCategory("Debug")]
        [Export] public bool ShowDebugOverlay { get; set; } = true;
        [Export] public bool ShowDebugOverlayInGame { get; set; } = true;
        [Export] public bool LogSpawnEffectPositions { get; set; } = true;
        [Export] public Color TriggerDebugColor { get; set; } = new Color(0.2f, 0.8f, 1f, 0.9f);
        [Export] public Color SpawnDebugColor { get; set; } = new Color(1f, 0.85f, 0.25f, 0.9f);
        [Export] public Color ExplicitPointDebugColor { get; set; } = new Color(1f, 0.45f, 0.2f, 1f);
        [Export] public Color BackEffectPointColor { get; set; } = new Color(0.4f, 0.9f, 1f, 1f);
        [Export] public Color FrontEffectPointColor { get; set; } = new Color(1f, 0.5f, 0.9f, 1f);
        [Export(PropertyHint.Range, "1,8,0.5")] public float DebugLineWidth { get; set; } = 2f;
        [Export(PropertyHint.Range, "2,16,0.5")] public float DebugPointRadius { get; set; } = 5f;

        private readonly RandomNumberGenerator _rng = new();
        private CollisionShape2D? _triggerShape;
        private bool _hasTriggered;
        private bool _isSpawning;
        private bool _triggerAreaAutoCreated;

        public override void _Ready()
        {
            _rng.Randomize();
            EnsureTriggerArea();
            UpdateTriggerAreaShape();

            if (Engine.IsEditorHint())
            {
                return;
            }

            if (TriggerArea != null)
            {
                TriggerArea.BodyEntered += OnTriggerBodyEntered;
            }

            if (SpawnOnReady)
            {
                StartSpawnSequence();
            }
        }

        public override void _ExitTree()
        {
            if (TriggerArea != null)
            {
                TriggerArea.BodyEntered -= OnTriggerBodyEntered;
            }

            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint())
            {
                EnsureTriggerArea();
                if (ShouldAutoConfigureTriggerArea())
                {
                    UpdateTriggerAreaShape();
                }
            }

            if (ShouldDrawDebugOverlay())
            {
                QueueRedraw();
            }
        }

        public override void _Draw()
        {
            if (!ShouldDrawDebugOverlay())
            {
                return;
            }

            DrawTriggerDebugShape();
            DrawSpawnDebugShape();
            DrawExplicitSpawnPoints();
            DrawEffectOffsetDebugPoints();
        }

        public void StartSpawnSequence()
        {
            if (_isSpawning)
            {
                return;
            }

            if (TriggerOnce && _hasTriggered)
            {
                return;
            }

            if (LogSpawnEffectPositions)
            {
                GD.Print($"[{Name}] StartSpawnSequence path={GetPath()}, count={SpawnCount}, interval={SpawnInterval}, enemyDelay={EnemyAppearDelay}, gateMode={EnemyAppearGateMode}, gateFrame={BackEffectAppearFrame}, gateTimeout={BackEffectGateTimeout}, backOffset={SpawnBackEffectOffset}, frontOffset={SpawnFrontEffectOffset}, ySort={EnableYAxisAutoLayering}, yScale={YAxisZScale:0.##}, yBase={YAxisZBase}");
            }

            _ = SpawnSequenceAsync();
        }

        public void ResetTrigger()
        {
            _hasTriggered = false;
        }

        private async System.Threading.Tasks.Task SpawnSequenceAsync()
        {
            if (_isSpawning)
            {
                return;
            }

            _isSpawning = true;
            _hasTriggered = true;
            EmitSignal(SignalName.SpawnStarted);

            int spawnTotal = Mathf.Max(1, SpawnCount);
            for (int i = 0; i < spawnTotal; i++)
            {
                Vector2 spawnPosition = ResolveSpawnPosition(i);
                SpawnEffectRefs effectRefs = PlaySpawnEffects(spawnPosition);

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

                ulong waitStart = Time.GetTicksMsec();
                await WaitForEnemyAppearGateAsync(effectRefs.BackEffectInstance);
                ulong waitedMs = Time.GetTicksMsec() - waitStart;

                if (LogSpawnEffectPositions)
                {
                    GD.Print($"[{Name}] Spawn index={i + 1}/{spawnTotal}, gateMode={EnemyAppearGateMode}, actualWait={waitedMs}ms");
                }

                var enemy = SpawnEnemy(spawnPosition, i);
                if (enemy != null)
                {
                    if (LogSpawnEffectPositions)
                    {
                        GD.Print($"[{Name}] Enemy spawned index={i + 1}/{spawnTotal}, pos={spawnPosition}, root={DescribeCanvasItem(enemy as CanvasItem)}");
                    }

                    if (AutoLowerFrontEffectAfterEnemySpawn)
                    {
                        LowerFrontEffectAfterEnemySpawn(effectRefs.FrontEffect, enemy);
                    }

                    EmitSignal(SignalName.EnemySpawned, enemy, i);
                }

                if (i < spawnTotal - 1 && SpawnInterval > 0f)
                {
                    var intervalTimer = GetTree().CreateTimer(SpawnInterval);
                    await ToSignal(intervalTimer, SceneTreeTimer.SignalName.Timeout);
                }
            }

            _isSpawning = false;
            EmitSignal(SignalName.SpawnCompleted);
        }

        private Node? SpawnEnemy(Vector2 spawnPosition, int spawnIndex)
        {
            if (EnemyScene == null)
            {
                GD.PushWarning($"{Name}: EnemyScene 未设置，无法生成敌人。");
                return null;
            }

            var instance = EnemyScene.Instantiate();
            if (instance == null)
            {
                GD.PushWarning($"{Name}: 敌人场景实例化失败。");
                return null;
            }

            var parent = ResolveSpawnParent();
            parent.AddChild(instance);

            if (instance is Node2D node2D)
            {
                node2D.GlobalPosition = spawnPosition;
                ApplyNodeZIndex(node2D, spawnPosition, EnemyZOffset);
                node2D.Visible = true;

                // Some enemy sub-controllers may toggle visibility/modulate in their own _Ready,
                // so we re-apply visibility and z over a few frames for multi-spawn stability.
                StabilizeSpawnedEnemyVisualAsync(node2D, spawnPosition);
            }

            EnsureSpawnedEnemyVisible(instance);

            if (instance is GameActor actor && AlignEnemyFacingToManager)
            {
                actor.FlipFacing(FaceRightOnSpawn);
            }

            if (instance is Node node)
            {
                node.Name = $"{node.Name}_{spawnIndex + 1}";
            }

            return instance;
        }

        private async void StabilizeSpawnedEnemyVisualAsync(Node2D enemyNode2D, Vector2 spawnPosition)
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

                ApplyNodeZIndex(enemyNode2D, spawnPosition, EnemyZOffset);
                EnsureSpawnedEnemyVisible(enemyNode2D);
            }

            if (LogSpawnEffectPositions)
            {
                GD.Print($"[{Name}] Enemy visual stabilization complete: {DescribeCanvasItem(enemyNode2D)}");
            }
        }

        private void EnsureSpawnedEnemyVisible(Node enemyRoot)
        {
            // Ensure common render nodes are visible and opaque after instantiation.
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

            if (LogSpawnEffectPositions)
            {
                string spineInfo = DescribeCanvasItem(spineNode as CanvasItem);
                string spriteInfo = DescribeCanvasItem(spriteNode as CanvasItem);
                GD.Print($"[{Name}] Enemy visual restore: root={DescribeCanvasItem(enemyRoot as CanvasItem)}, spine={spineInfo}, sprite={spriteInfo}");
            }
        }

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

        private static string DescribeCanvasItem(CanvasItem? item)
        {
            if (item == null || !GodotObject.IsInstanceValid(item))
            {
                return "null";
            }

            return $"{item.Name}(visible={item.Visible}, modA={item.Modulate.A:0.##}, selfA={item.SelfModulate.A:0.##}, z={item.ZIndex})";
        }

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

        private Vector2 ResolveSpawnPosition(int index)
        {
            if (UseExplicitSpawnOffsets && index < SpawnOffsets.Count)
            {
                return GlobalPosition + SpawnOffsets[index];
            }

            if (UseExplicitSpawnOffsets && SpawnOffsets.Count > 0)
            {
                return GlobalPosition + SpawnOffsets[index % SpawnOffsets.Count];
            }

            float x = _rng.RandfRange(-SpawnAreaExtents.X, SpawnAreaExtents.X);
            float y = _rng.RandfRange(-SpawnAreaExtents.Y, SpawnAreaExtents.Y);
            return GlobalPosition + new Vector2(x, y);
        }

        private void OnTriggerBodyEntered(Node2D body)
        {
            if (!string.IsNullOrWhiteSpace(TriggerGroupName) && !body.IsInGroup(TriggerGroupName))
            {
                GD.Print($"[{Name}] Trigger ignored: {body.Name} is not in group '{TriggerGroupName}'");
                return;
            }

            GD.Print($"[{Name}] Trigger entered by: {body.Name}");
            StartSpawnSequence();
        }

        private void EnsureTriggerArea()
        {
            if (TriggerArea == null || !GodotObject.IsInstanceValid(TriggerArea))
            {
                TriggerArea = GetNodeOrNull<Area2D>("TriggerArea");
                _triggerAreaAutoCreated = false;
            }

            if (TriggerArea == null || !GodotObject.IsInstanceValid(TriggerArea))
            {
                TriggerArea = new Area2D
                {
                    Name = "TriggerArea",
                    Monitoring = true,
                    Monitorable = false
                };
                AddChild(TriggerArea);
                _triggerAreaAutoCreated = true;
                if (Engine.IsEditorHint())
                {
                    TriggerArea.Owner = GetTree().EditedSceneRoot;
                }
            }

            bool shouldAutoConfigure = ShouldAutoConfigureTriggerArea();
            if (!shouldAutoConfigure)
            {
                TriggerArea.Monitoring = true;
                _triggerShape = TriggerArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                return;
            }

            TriggerArea.Position = TriggerOffset;
            TriggerArea.CollisionLayer = TriggerCollisionLayer;
            TriggerArea.CollisionMask = TriggerCollisionMask;
            TriggerArea.Monitoring = true;

            _triggerShape = TriggerArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (_triggerShape == null)
            {
                _triggerShape = new CollisionShape2D
                {
                    Name = "CollisionShape2D"
                };
                TriggerArea.AddChild(_triggerShape);
                if (Engine.IsEditorHint())
                {
                    _triggerShape.Owner = GetTree().EditedSceneRoot;
                }
            }
        }

        private void UpdateTriggerAreaShape()
        {
            if (!ShouldAutoConfigureTriggerArea())
            {
                return;
            }

            if (_triggerShape == null)
            {
                return;
            }

            if (_triggerShape.Shape is not RectangleShape2D rectangle)
            {
                rectangle = new RectangleShape2D();
                _triggerShape.Shape = rectangle;
            }

            rectangle.Size = new Vector2(Mathf.Max(1f, TriggerSize.X), Mathf.Max(1f, TriggerSize.Y));
            _triggerShape.Position = Vector2.Zero;
            _triggerShape.Disabled = false;
        }

        private SpawnEffectRefs PlaySpawnEffects(Vector2 spawnPosition)
        {
            SpawnEffectRefs effectRefs = new();
            Vector2 backEffectPos = spawnPosition + SpawnBackEffectOffset;
            Vector2 frontEffectPos = spawnPosition + SpawnFrontEffectOffset;

            if (LogSpawnEffectPositions)
            {
                GD.Print($"[{Name}] SpawnFX base={spawnPosition}, backOffset={SpawnBackEffectOffset}, backPos={backEffectPos}, frontOffset={SpawnFrontEffectOffset}, frontPos={frontEffectPos}");
            }

            var backEffectInstance = SpawnEffect(SpawnBackEffectScene, backEffectPos, BackEffectZOffset);
            var frontEffectInstance = SpawnEffect(SpawnFrontEffectScene, frontEffectPos, FrontEffectZOffset);

            effectRefs.BackEffect = backEffectInstance?.Root;
            effectRefs.BackAnimatedSprite = backEffectInstance?.AnimatedSprite;
            effectRefs.BackEffectInstance = backEffectInstance;
            effectRefs.FrontEffect = frontEffectInstance?.Root;
            return effectRefs;
        }

        private SpawnEffectInstance? SpawnEffect(PackedScene? effectScene, Vector2 spawnPosition, int zOffset)
        {
            if (effectScene == null)
            {
                return null;
            }

            var instance = effectScene.Instantiate();
            if (instance == null)
            {
                return null;
            }

            var effectInstance = new SpawnEffectInstance
            {
                Root = instance as Node2D
            };

            var parent = ResolveSpawnParent();
            parent.AddChild(instance);

            if (instance is Node2D node2D)
            {
                node2D.GlobalPosition = spawnPosition;
                ApplyNodeZIndex(node2D, spawnPosition, zOffset);
                node2D.Visible = true;
            }

            AnimatedSprite2D? animatedSprite = instance as AnimatedSprite2D;
            if (animatedSprite == null)
            {
                foreach (Node child in instance.FindChildren("*", "AnimatedSprite2D", true, false))
                {
                    if (child is AnimatedSprite2D foundSprite)
                    {
                        animatedSprite = foundSprite;
                        break;
                    }
                }
            }

            if (animatedSprite != null)
            {
                var animationName = animatedSprite.Animation;
                if (!string.IsNullOrEmpty(animationName.ToString()) && animatedSprite.SpriteFrames != null)
                {
                    animatedSprite.Visible = true;
                    animatedSprite.Frame = 0;
                    animatedSprite.FrameProgress = 0f;
                    animatedSprite.SpeedScale = 1f;
                    animatedSprite.SpriteFrames.SetAnimationLoop(animationName, false);
                    animatedSprite.Play(animationName);

                    if (LogSpawnEffectPositions)
                    {
                        GD.Print($"[{Name}] Spawn FX started: scene={effectScene.ResourcePath}, anim={animationName}, pos={spawnPosition}, z={animatedSprite.ZIndex}, frames={animatedSprite.SpriteFrames.GetFrameCount(animationName)}");
                    }

                    animatedSprite.AnimationFinished += () =>
                    {
                        effectInstance.Finished = true;

                        if (GodotObject.IsInstanceValid(instance))
                        {
                            instance.QueueFree();
                        }
                    };
                    return effectInstance;
                }

                GD.PushWarning($"{Name}: Spawn FX found AnimatedSprite2D but animation is invalid. scene={effectScene.ResourcePath}, anim={animationName}");
            }
            else
            {
                GD.PushWarning($"{Name}: Spawn FX scene does not contain AnimatedSprite2D. scene={effectScene.ResourcePath}");
            }

            effectInstance.AnimatedSprite = animatedSprite;

            var timer = GetTree().CreateTimer(Mathf.Max(EnemyAppearDelay, 0.5f));
            timer.Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(instance))
                {
                    instance.QueueFree();
                }
            };

            return effectInstance;
        }

        private async System.Threading.Tasks.Task WaitForEnemyAppearGateAsync(SpawnEffectInstance? backEffectInstance)
        {
            switch (EnemyAppearGateMode)
            {
                case BackEffectSpawnGateMode.BackEffectFrame:
                    if (await WaitForBackEffectFrameAsync(backEffectInstance))
                    {
                        return;
                    }
                    break;
                case BackEffectSpawnGateMode.BackEffectFinished:
                    if (await WaitForBackEffectFinishedAsync(backEffectInstance))
                    {
                        return;
                    }
                    break;
                default:
                    break;
            }

            if (EnemyAppearGateMode == BackEffectSpawnGateMode.Delay || FallbackToDelayWhenGateUnavailable)
            {
                await WaitSecondsAsync(EnemyAppearDelay);
            }
        }

        private async System.Threading.Tasks.Task<bool> WaitForBackEffectFrameAsync(SpawnEffectInstance? backEffectInstance)
        {
            AnimatedSprite2D? backAnimatedSprite = backEffectInstance?.AnimatedSprite;
            if (!GodotObject.IsInstanceValid(backAnimatedSprite) || backAnimatedSprite == null)
            {
                return false;
            }

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
                if (backEffectInstance?.Finished == true)
                {
                    return true;
                }

                if (backAnimatedSprite.Frame >= targetFrame)
                {
                    return true;
                }

                if (timeout > 0 && (Time.GetTicksMsec() / 1000.0 - start) >= timeout)
                {
                    GD.PushWarning($"{Name}: WaitForBackEffectFrame timeout, frame={backAnimatedSprite.Frame}, target={targetFrame}");
                    return false;
                }

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            return false;
        }

        private async System.Threading.Tasks.Task<bool> WaitForBackEffectFinishedAsync(SpawnEffectInstance? backEffectInstance)
        {
            AnimatedSprite2D? backAnimatedSprite = backEffectInstance?.AnimatedSprite;
            if (!GodotObject.IsInstanceValid(backAnimatedSprite) || backAnimatedSprite == null)
            {
                return false;
            }

            if (backEffectInstance?.Finished == true || !backAnimatedSprite.IsPlaying())
            {
                return true;
            }

            double timeout = Mathf.Max(0f, BackEffectGateTimeout);
            double start = Time.GetTicksMsec() / 1000.0;

            while (GodotObject.IsInstanceValid(backAnimatedSprite))
            {
                if (backEffectInstance?.Finished == true || !backAnimatedSprite.IsPlaying())
                {
                    return true;
                }

                if (timeout > 0 && (Time.GetTicksMsec() / 1000.0 - start) >= timeout)
                {
                    GD.PushWarning($"{Name}: WaitForBackEffectFinished timeout.");
                    return false;
                }

                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            return backEffectInstance?.Finished == true;
        }

        private async System.Threading.Tasks.Task WaitSecondsAsync(float seconds)
        {
            float waitSeconds = Mathf.Max(0f, seconds);
            if (waitSeconds <= 0f)
            {
                return;
            }

            var timer = GetTree().CreateTimer(waitSeconds);
            await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
        }

        private async void LowerFrontEffectAfterEnemySpawn(Node2D? frontEffectNode, Node enemy)
        {
            if (frontEffectNode == null || !GodotObject.IsInstanceValid(frontEffectNode))
            {
                return;
            }

            if (enemy is not Node2D enemyNode || !GodotObject.IsInstanceValid(enemyNode))
            {
                return;
            }

            float delay = Mathf.Max(0f, FrontEffectLowerDelay);
            if (delay > 0f)
            {
                var timer = GetTree().CreateTimer(delay);
                await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            }

            if (!GodotObject.IsInstanceValid(frontEffectNode) || !GodotObject.IsInstanceValid(enemyNode))
            {
                return;
            }

            frontEffectNode.ZAsRelative = false;
            frontEffectNode.ZIndex = enemyNode.ZIndex + FrontEffectPostSpawnZOffset;

            if (LogSpawnEffectPositions)
            {
                GD.Print($"[{Name}] Front FX lowered after spawn: enemyZ={enemyNode.ZIndex}, frontFXZ={frontEffectNode.ZIndex}, offset={FrontEffectPostSpawnZOffset}, delay={delay:0.###}s");
            }
        }

        private sealed class SpawnEffectRefs
        {
            public Node2D? BackEffect;
            public AnimatedSprite2D? BackAnimatedSprite;
            public SpawnEffectInstance? BackEffectInstance;
            public Node2D? FrontEffect;
        }

        private sealed class SpawnEffectInstance
        {
            public Node2D? Root;
            public AnimatedSprite2D? AnimatedSprite;
            public bool Finished;
        }

        private void ApplyNodeZIndex(Node2D node2D, Vector2 worldPosition, int extraOffset)
        {
            int originalZ = node2D.ZIndex;
            int resolvedZ = ResolveZIndex(worldPosition.Y, originalZ + extraOffset);
            node2D.ZAsRelative = false;
            node2D.ZIndex = resolvedZ;
        }

        private int ResolveZIndex(float worldY, int fallbackZ)
        {
            if (!EnableYAxisAutoLayering)
            {
                return fallbackZ;
            }

            float scale = Mathf.Max(0.1f, YAxisZScale);
            int z = YAxisZBase + Mathf.RoundToInt(worldY * scale);
            if (ClampYAxisZRange)
            {
                int min = Mathf.Min(YAxisZMin, YAxisZMax);
                int max = Mathf.Max(YAxisZMin, YAxisZMax);
                z = Mathf.Clamp(z, min, max);
            }

            return z;
        }

        private bool ShouldDrawDebugOverlay()
        {
            if (!ShowDebugOverlay)
            {
                return false;
            }

            if (Engine.IsEditorHint())
            {
                return true;
            }

            return ShowDebugOverlayInGame;
        }

        private void DrawTriggerDebugShape()
        {
            if (!ShouldAutoConfigureTriggerArea() && TryDrawAssignedTriggerAreaShape())
            {
                return;
            }

            var triggerRect = new Rect2(TriggerOffset - TriggerSize * 0.5f, TriggerSize);
            DrawRect(triggerRect, TriggerDebugColor, filled: false, width: DebugLineWidth);
        }

        private bool TryDrawAssignedTriggerAreaShape()
        {
            if (TriggerArea == null || !GodotObject.IsInstanceValid(TriggerArea))
            {
                return false;
            }

            CollisionShape2D? shapeNode = TriggerArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shapeNode == null)
            {
                foreach (Node child in TriggerArea.GetChildren())
                {
                    if (child is CollisionShape2D collisionShape)
                    {
                        shapeNode = collisionShape;
                        break;
                    }
                }
            }

            if (shapeNode?.Shape is not RectangleShape2D rectangle)
            {
                return false;
            }

            Vector2 half = rectangle.Size * 0.5f;
            var worldCorners = new[]
            {
                shapeNode.GlobalTransform * new Vector2(-half.X, -half.Y),
                shapeNode.GlobalTransform * new Vector2(half.X, -half.Y),
                shapeNode.GlobalTransform * new Vector2(half.X, half.Y),
                shapeNode.GlobalTransform * new Vector2(-half.X, half.Y),
                shapeNode.GlobalTransform * new Vector2(-half.X, -half.Y)
            };

            var localPoints = new Vector2[worldCorners.Length];
            for (int i = 0; i < worldCorners.Length; i++)
            {
                localPoints[i] = ToLocal(worldCorners[i]);
            }

            DrawPolyline(localPoints, TriggerDebugColor, DebugLineWidth, antialiased: true);
            return true;
        }

        private bool ShouldAutoConfigureTriggerArea()
        {
            return _triggerAreaAutoCreated || AutoConfigureAssignedTriggerArea;
        }

        private void DrawSpawnDebugShape()
        {
            if (UseExplicitSpawnOffsets)
            {
                return;
            }

            var size = SpawnAreaExtents * 2f;
            var spawnRect = new Rect2(-SpawnAreaExtents, size);
            DrawRect(spawnRect, SpawnDebugColor, filled: false, width: DebugLineWidth);
        }

        private void DrawExplicitSpawnPoints()
        {
            if (!UseExplicitSpawnOffsets)
            {
                return;
            }

            foreach (var offset in SpawnOffsets)
            {
                DrawCircle(offset, DebugPointRadius, ExplicitPointDebugColor);
            }
        }

        private void DrawEffectOffsetDebugPoints()
        {
            if (!UseExplicitSpawnOffsets)
            {
                DrawCircle(SpawnBackEffectOffset, DebugPointRadius, BackEffectPointColor);
                DrawCircle(SpawnFrontEffectOffset, DebugPointRadius, FrontEffectPointColor);
                return;
            }

            foreach (var baseOffset in SpawnOffsets)
            {
                DrawCircle(baseOffset + SpawnBackEffectOffset, DebugPointRadius, BackEffectPointColor);
                DrawCircle(baseOffset + SpawnFrontEffectOffset, DebugPointRadius, FrontEffectPointColor);
            }
        }
    }
}
