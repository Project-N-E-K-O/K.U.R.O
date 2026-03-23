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
        [Export(PropertyHint.Range, "-1000,1000,1")] public int BackEffectZOffset { get; set; } = -1;
        [Export(PropertyHint.Range, "-1000,1000,1")] public int FrontEffectZOffset { get; set; } = 1;

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
                GD.Print($"[{Name}] StartSpawnSequence path={GetPath()}, count={SpawnCount}, interval={SpawnInterval}, enemyDelay={EnemyAppearDelay}, backOffset={SpawnBackEffectOffset}, frontOffset={SpawnFrontEffectOffset}");
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
                PlaySpawnEffects(spawnPosition);

                float waitSeconds = Mathf.Max(0f, EnemyAppearDelay);
                ulong waitStart = Time.GetTicksMsec();
                if (waitSeconds > 0f)
                {
                    var appearTimer = GetTree().CreateTimer(waitSeconds);
                    await ToSignal(appearTimer, SceneTreeTimer.SignalName.Timeout);
                }
                ulong waitedMs = Time.GetTicksMsec() - waitStart;

                if (LogSpawnEffectPositions)
                {
                    GD.Print($"[{Name}] Spawn index={i + 1}/{spawnTotal}, intendedDelay={waitSeconds:0.###}s, actualWait={waitedMs}ms");
                }

                var enemy = SpawnEnemy(spawnPosition, i);
                if (enemy != null)
                {
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
            }

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

        private void PlaySpawnEffects(Vector2 spawnPosition)
        {
            Vector2 backEffectPos = spawnPosition + SpawnBackEffectOffset;
            Vector2 frontEffectPos = spawnPosition + SpawnFrontEffectOffset;

            if (LogSpawnEffectPositions)
            {
                GD.Print($"[{Name}] SpawnFX base={spawnPosition}, backOffset={SpawnBackEffectOffset}, backPos={backEffectPos}, frontOffset={SpawnFrontEffectOffset}, frontPos={frontEffectPos}");
            }

            SpawnEffect(SpawnBackEffectScene, backEffectPos, BackEffectZOffset);
            SpawnEffect(SpawnFrontEffectScene, frontEffectPos, FrontEffectZOffset);
        }

        private void SpawnEffect(PackedScene? effectScene, Vector2 spawnPosition, int zOffset)
        {
            if (effectScene == null)
            {
                return;
            }

            var instance = effectScene.Instantiate();
            if (instance == null)
            {
                return;
            }

            var parent = ResolveSpawnParent();
            parent.AddChild(instance);

            if (instance is Node2D node2D)
            {
                node2D.GlobalPosition = spawnPosition;
                node2D.ZIndex += zOffset;
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
                    animatedSprite.SpriteFrames.SetAnimationLoop(animationName, false);
                    animatedSprite.Play(animationName);
                    animatedSprite.AnimationFinished += () =>
                    {
                        if (GodotObject.IsInstanceValid(instance))
                        {
                            instance.QueueFree();
                        }
                    };
                    return;
                }
            }

            var timer = GetTree().CreateTimer(Mathf.Max(EnemyAppearDelay, 0.5f));
            timer.Timeout += () =>
            {
                if (GodotObject.IsInstanceValid(instance))
                {
                    instance.QueueFree();
                }
            };
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
