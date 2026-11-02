using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Kuros.Core;
using Kuros.Utils;

namespace Kuros.Managers
{
    /// <summary>
    /// 独立的战斗区域管理器：
    /// 实时检测指定范围内是否有敌人。
    /// 默认模式：有敌人时自动创建空气墙边界，无敌人时自动移除。
    /// 手动触发模式（ManualTriggerMode）：激活只由触发器驱动——在 TriggerArea 导出槽指定场景中的一个
    /// Area2D，玩家进入即触发一次（激活时机锚定玩家位置，杜绝"墙生成时玩家在场外"）；
    /// 拆墙条件 = 持续满 TriggerDurationSeconds 且场上无敌人。
    /// </summary>
    [GlobalClass]
    public partial class BattleArena : Area2D
    {
        /// <summary>战斗区域的大小。</summary>
        [Export]
        public Vector2 ArenaSize { get; set; } = new Vector2(800, 600);

        /// <summary>空气墙使用的碰撞层。</summary>
        [Export(PropertyHint.Layers2DPhysics)]
        public uint BoundaryCollisionLayer { get; set; } = 0;

        /// <summary>空气墙的碰撞掩码（应包含玩家层0和敌人层2）。</summary>
        [Export(PropertyHint.Layers2DPhysics)]
        public uint BoundaryCollisionMask { get; set; } = 0; // Layer 0 + Layer 2

        /// <summary>空气墙的厚度。</summary>
        [Export(PropertyHint.Range, "1,200,1")]
        public float BoundaryThickness { get; set; } = 100f;

        /// <summary>检测敌人的碰撞掩码。</summary>
        [Export(PropertyHint.Layers2DPhysics)]
        public uint EnemyDetectionMask { get; set; } = 0; // Layer 2 - 敌人

        /// <summary>检查间隔（秒），多久检查一次敌人。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.1")]
        public float CheckInterval { get; set; } = 0.3f;

        /// <summary>手动触发模式：激活只由触发器驱动，敌人检测不再自动激活。</summary>
        [Export]
        public bool ManualTriggerMode { get; set; } = true;

        /// <summary>触发器区域（玩家进入时触发一次，全局仅一次）。配合 ManualTriggerMode 使用。</summary>
        [Export]
        public Area2D? TriggerArea { get; set; }

        /// <summary>触发后空气墙最短持续时长（秒）：到期后若场上无敌人则拆墙，有敌人则保留至清场。</summary>
        [Export(PropertyHint.Range, "0,300,1")]
        public float TriggerDurationSeconds { get; set; } = 3f;

        [ExportCategory("Debug")]
        [Export]
        public bool ShowDebugOverlay { get; set; } = true;

        [Export]
        public bool ShowDebugOverlayInGame { get; set; } = false;

        [Export]
        public Color DebugArenaColor { get; set; } = new Color(0.2f, 1f, 0.2f, 0.5f);

        [Export(PropertyHint.Range, "1,8,0.5")]
        public float DebugLineWidth { get; set; } = 2f;

        [Export(PropertyHint.Range, "2,16,0.5")]
        public float DebugPointRadius { get; set; } = 5f;

        [Signal]
        public delegate void BattleStartedEventHandler();

        [Signal]
        public delegate void BattleEndedEventHandler();

        private BattleArenaBoundary? _boundaryWalls;
        private float _checkTimer = 0f;
        private bool _isBattleActive = false;
        private List<GameActor> _trackedEnemies = new();
        private readonly List<GameActor> _detectedScratch = new(); // 复用缓冲区，避免每0.3s分配新列表
        private bool _triggerUsed = false;      // TriggerBattleOnce 仅一次标志（全局）
        private float _minRemaining = 0f;       // 触发模式最短持续时长剩余（到期且无敌人才拆墙）
        /// <summary>
        /// 外部持锁标志。当为 true 时，即使检测到无敌人也不会撤销空气墙。
        /// 由 WaveSpawnManager 在整个波次期间持锁，全部波次结束后释放。
        /// </summary>
        private bool _forceLocked = false;

        /// <summary>
        /// 设置强制锁定状态。
        /// locked=true：波次进行中，禁止自动 DeactivateBattle。
        /// locked=false：所有波次结束，恢复正常自动停用逻辑。
        /// </summary>
        public void SetForceLock(bool locked)
        {
            _forceLocked = locked;
            GameLogger.Debug(nameof(BattleArena), $"SetForceLock({locked})");

            // 解锁时若当前无敌人且已持续满最短时长则立即停用
            if (!locked && _isBattleActive && _trackedEnemies.Count == 0 && _minRemaining <= 0f)
                DeactivateBattle();
        }

        public override void _Ready()
        {
            if (TriggerArea != null)
            {
                TriggerArea.BodyEntered += OnTriggerBodyEntered;
            }

            // 配置 Area2D 的碰撞层
            CollisionLayer = 0;
            CollisionMask = EnemyDetectionMask;

            // 确保有碰撞形状
            var collisionShape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (collisionShape == null)
            {
                collisionShape = new CollisionShape2D
                {
                    Name = "CollisionShape2D",
                    Shape = new RectangleShape2D { Size = ArenaSize }
                };
                AddChild(collisionShape);
            }
            else
            {
                // 更新现有碰撞形状大小
                if (collisionShape.Shape is RectangleShape2D rectShape)
                {
                    rectShape.Size = ArenaSize;
                }
            }
        }

        public override void _Process(double delta)
        {
            if (Engine.IsEditorHint())
            {
                QueueRedraw();
                return;
            }

            _checkTimer -= (float)delta;
            if (_checkTimer <= 0f)
            {
                _checkTimer = CheckInterval;
                CheckEnemyStatus();
            }

            if (_minRemaining > 0f)
            {
                _minRemaining -= (float)delta;
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

            DrawDebugArenaShape();
        }

        /// <summary>
        /// 检查范围内敌人状态（使用物理查询）。
        /// </summary>
        private void CheckEnemyStatus()
        {
            // 更新现有敌人列表
            _trackedEnemies.RemoveAll(enemy => !IsInstanceValid(enemy) || enemy.IsDeadOrDying);

            // 通过物理查询找到范围内的所有敌人（复用暂存列表，避免每次 new List）
            var overlappingBodies = GetOverlappingBodies();
            _detectedScratch.Clear();

            foreach (var body in overlappingBodies)
            {
                // 尸体不算敌人：死亡对象即使碰撞体还在场（可破坏物留尸、碰撞不随死亡关闭）也不进跟踪表——
                // 否则每轮开头被 RemoveAll 剔除、又被检测成"新进入"：日志每 CheckInterval 刷一条，
                // 且 hasEnemies 恒真（空气墙拆不掉）。
                if (body is GameActor actor && !actor.IsDeadOrDying && !_detectedScratch.Contains(actor))
                {
                    _detectedScratch.Add(actor);

                    // 新进入的敌人
                    if (!_trackedEnemies.Contains(actor))
                    {
                        GameLogger.Debug(nameof(BattleArena), $"敌人进入检测范围：{actor.Name}");
                    }
                }
            }

            // 检查离开的敌人（直接遍历，无需 ToList 拷贝）
            foreach (var enemy in _trackedEnemies)
            {
                if (!_detectedScratch.Contains(enemy))
                {
                    GameLogger.Debug(nameof(BattleArena), $"敌人离开检测范围：{enemy.Name}");
                }
            }

            // 将检测结果同步回追踪列表（复用，不分配新对象）
            _trackedEnemies.Clear();
            _trackedEnemies.AddRange(_detectedScratch);

            bool hasEnemies = _trackedEnemies.Count > 0;

            // 状态转移：无敌人 -> 有敌人（手动触发模式下激活只由 TriggerBattleOnce 驱动）
            if (hasEnemies && !_isBattleActive && !ManualTriggerMode)
            {
                ActivateBattle();
            }
            // 状态转移：有敌人 -> 无敌人（锁定期间禁止停用；触发模式需先持续满最短时长）
            else if (!hasEnemies && _isBattleActive && !_forceLocked && _minRemaining <= 0f)
            {
                DeactivateBattle();
            }
        }

        /// <summary>
        /// 手动触发战斗（全局仅一次）：立即创建空气墙并持续最短 durationSeconds。
        /// 激活时机锚定玩家位置（玩家进入 TriggerArea 时调用），
        /// 玩家不可能处于墙外；拆墙条件 = 持续满时长且场上无敌人（有敌人则一直保留）。
        /// </summary>
        public void TriggerBattleOnce(float durationSeconds)
        {
            if (_triggerUsed) return;
            _triggerUsed = true;
            _minRemaining = Mathf.Max(0f, durationSeconds);
            ActivateBattle();
        }

        private void OnTriggerBodyEntered(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            TriggerBattleOnce(TriggerDurationSeconds);
        }

        /// <summary>
        /// 激活战斗：创建空气墙。
        /// </summary>
        private void ActivateBattle()
        {
            _isBattleActive = true;

            GameLogger.Info(nameof(BattleArena), $"战斗激活：检测到 {_trackedEnemies.Count} 个敌人，创建空气墙");

            // 创建空气墙
            CreateBoundaryWalls();

            EmitSignal(SignalName.BattleStarted);
        }

        /// <summary>
        /// 停用战斗：移除空气墙。
        /// </summary>
        private void DeactivateBattle()
        {
            _isBattleActive = false;

            GameLogger.Info(nameof(BattleArena), "战斗完成：所有敌人已击杀，移除空气墙");

            // 移除空气墙
            RemoveBoundaryWalls();

            _trackedEnemies.Clear();
            EmitSignal(SignalName.BattleEnded);
        }

        /// <summary>
        /// 创建空气墙边界。
        /// </summary>
        private void CreateBoundaryWalls()
        {
            if (_boundaryWalls != null && IsInstanceValid(_boundaryWalls))
            {
                return; // 已经存在
            }

            var arenaRect = new Rect2(GlobalPosition - ArenaSize / 2f, ArenaSize);

            _boundaryWalls = new BattleArenaBoundary
            {
                Name = $"BattleArenaBoundary_{GetInstanceId()}",
                ArenaRect = arenaRect,
                WallThickness = BoundaryThickness,
                CollisionLayer = BoundaryCollisionLayer,
                CollisionMask = BoundaryCollisionMask
            };

            GetParent()?.AddChild(_boundaryWalls);
            GameLogger.Info(nameof(BattleArena), "空气墙已创建");
        }

        /// <summary>
        /// 移除空气墙。
        /// </summary>
        private void RemoveBoundaryWalls()
        {
            if (_boundaryWalls != null && IsInstanceValid(_boundaryWalls))
            {
                _boundaryWalls.QueueFree();
                _boundaryWalls = null;
                GameLogger.Info(nameof(BattleArena), "空气墙已移除");
            }
        }


        private void DrawDebugArenaShape()
        {
            var halfSize = ArenaSize / 2f;
            var topLeft = new Vector2(-halfSize.X, -halfSize.Y);
            var topRight = new Vector2(halfSize.X, -halfSize.Y);
            var bottomRight = new Vector2(halfSize.X, halfSize.Y);
            var bottomLeft = new Vector2(-halfSize.X, halfSize.Y);

            var color = _isBattleActive 
                ? new Color(1f, 0.2f, 0.2f, 0.7f)  // 红色表示战斗激活
                : DebugArenaColor;                  // 绿色表示待命

            DrawLine(topLeft, topRight, color, DebugLineWidth);
            DrawLine(topRight, bottomRight, color, DebugLineWidth);
            DrawLine(bottomRight, bottomLeft, color, DebugLineWidth);
            DrawLine(bottomLeft, topLeft, color, DebugLineWidth);

            // 中心圆点
            DrawCircle(Vector2.Zero, DebugPointRadius, color);

            // 敌人计数标签（编辑器中显示）
            if (Engine.IsEditorHint())
            {
                DrawCircle(new Vector2(0, -halfSize.Y - 20), 3, color);
            }
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

        public override void _ExitTree()
        {
            if (TriggerArea != null)
            {
                TriggerArea.BodyEntered -= OnTriggerBodyEntered;
            }
            RemoveBoundaryWalls();
            DeactivateBattle();
            _trackedEnemies.Clear();
        }
    }
}
