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
    /// 有敌人时自动创建空气墙边界并锁定相机；
    /// 无敌人时自动移除空气墙并解除相机锁定。
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
        [Export(PropertyHint.Range, "1,50,1")]
        public float BoundaryThickness { get; set; } = 2f;

        /// <summary>检测敌人的碰撞掩码。</summary>
        [Export(PropertyHint.Layers2DPhysics)]
        public uint EnemyDetectionMask { get; set; } = 0; // Layer 2 - 敌人

        /// <summary>检查间隔（秒），多久检查一次敌人。</summary>
        [Export(PropertyHint.Range, "0.1,2,0.1")]
        public float CheckInterval { get; set; } = 0.3f;

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
        private CameraZoneManager? _cameraZoneManager;
        private float _checkTimer = 0f;
        private bool _isBattleActive = false;
        private List<GameActor> _trackedEnemies = new();
        private readonly List<GameActor> _detectedScratch = new(); // 复用缓冲区，避免每0.3s分配新列表
        private string? _originalCameraZoneName;
        /// <summary>
        /// 外部持锁标志。当为 true 时，即使检测到无敌人也不会撤销空气墙/相机锁定。
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

            // 解锁时若当前无敌人则立即停用
            if (!locked && _isBattleActive && _trackedEnemies.Count == 0)
                DeactivateBattle();
        }

        public override void _Ready()
        {
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
            _trackedEnemies.RemoveAll(enemy => !IsInstanceValid(enemy) || enemy.IsDead);

            // 通过物理查询找到范围内的所有敌人（复用暂存列表，避免每次 new List）
            var overlappingBodies = GetOverlappingBodies();
            _detectedScratch.Clear();

            foreach (var body in overlappingBodies)
            {
                if (body is GameActor actor && !_detectedScratch.Contains(actor))
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

            // 状态转移：无敌人 -> 有敌人
            if (hasEnemies && !_isBattleActive)
            {
                ActivateBattle();
            }
            // 状态转移：有敌人 -> 无敌人（锁定期间禁止停用）
            else if (!hasEnemies && _isBattleActive && !_forceLocked)
            {
                DeactivateBattle();
            }
        }

        /// <summary>
        /// 激活战斗：创建空气墙并锁定相机。
        /// </summary>
        private void ActivateBattle()
        {
            _isBattleActive = true;

            GameLogger.Info(nameof(BattleArena), $"战斗激活：检测到 {_trackedEnemies.Count} 个敌人，创建空气墙");

            // 找到相机管理器
            var cameraZoneManager = GetTree().Root.GetNodeOrNull<CameraZoneManager>("BattleScene/CameraZoneManager");
            if (cameraZoneManager == null)
            {
                cameraZoneManager = GetTree().Root.GetNodeOrNull<CameraZoneManager>("/root/BattleScene/CameraZoneManager");
            }

            if (cameraZoneManager != null)
            {
                _cameraZoneManager = cameraZoneManager;
                _originalCameraZoneName = cameraZoneManager.CurrentZoneName;

                // 创建临时相机区域
                var arenaRect = new Rect2(GlobalPosition - ArenaSize / 2f, ArenaSize);
                cameraZoneManager.CreateAndSwitchTemporaryCameraZone(arenaRect, $"arena_{GetInstanceId()}");

                GameLogger.Info(nameof(BattleArena), "相机已切换到战斗区域");
            }
            else
            {
                GameLogger.Warn(nameof(BattleArena), "未找到 CameraZoneManager，相机不会被锁定");
            }

            // 创建空气墙
            CreateBoundaryWalls();

            EmitSignal(SignalName.BattleStarted);
        }

        /// <summary>
        /// 停用战斗：移除空气墙并解除相机锁定。
        /// 优先恢复到玩家当前所在的 CameraZoneArea，其次才是战斗前的 Zone。
        /// </summary>
        private void DeactivateBattle()
        {
            _isBattleActive = false;

            GameLogger.Info(nameof(BattleArena), "战斗完成：所有敌人已击杀，移除空气墙");

            // 移除空气墙
            RemoveBoundaryWalls();

            // 恢复相机
            if (_cameraZoneManager != null)
            {
                // 优先检查玩家是否处于某个 CameraZoneArea 内
                var targetZoneName = FindPlayerCurrentCameraZone();
                if (targetZoneName == null)
                {
                    targetZoneName = _originalCameraZoneName;
                }

                if (!string.IsNullOrEmpty(targetZoneName))
                {
                    _cameraZoneManager.SwitchToZone(targetZoneName);
                    GameLogger.Info(nameof(BattleArena), $"相机已恢复到区域：{targetZoneName}");
                }
                else
                {
                    _cameraZoneManager.RemoveTemporaryCameraZone($"arena_{GetInstanceId()}");
                    GameLogger.Info(nameof(BattleArena), "相机区域已移除");
                }

                _cameraZoneManager = null;
            }

            _trackedEnemies.Clear();
            EmitSignal(SignalName.BattleEnded);
        }

        /// <summary>
        /// 查找玩家当前所在的 CameraZoneArea 对应的 Zone 名称。
        /// 基于玩家位置在 CameraZoneArea 碰撞形状范围内的判断（不依赖物理查询）。
        /// 若玩家处于多个 CameraZoneArea 的范围内，返回最后找到的那个。
        /// </summary>
        private string? FindPlayerCurrentCameraZone()
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (player == null) return null;

            var playerPos = player.GlobalPosition;
            string? currentZone = null;

            // 遍历场景树中所有 CameraZoneArea，检查玩家位置是否在其碰撞形状范围内
            var allCameraZones = GetTree().GetNodesInGroup("camera_zone");

            foreach (var zone in allCameraZones)
            {
                if (zone is not CameraZoneArea cameraZone || !IsInstanceValid(cameraZone))
                    continue;

                // 获取 CameraZoneArea 的碰撞形状
                var collisionShape = cameraZone.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                if (collisionShape?.Shape is not RectangleShape2D rectShape)
                    continue;

                // 计算世界范围
                var worldCenter = cameraZone.GlobalPosition + collisionShape.Position;
                var halfSize = rectShape.Size / 2f;
                var aabbRect = new Rect2(worldCenter - halfSize, rectShape.Size);

                // 检查玩家位置是否在范围内
                if (aabbRect.HasPoint(playerPos))
                {
                    currentZone = cameraZone.ZoneName;
                    GameLogger.Debug(nameof(BattleArena), $"检测到玩家位置在 CameraZone 内：{currentZone} 位置:{playerPos}");
                }
            }

            return currentZone;
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
            RemoveBoundaryWalls();
            DeactivateBattle();
            _trackedEnemies.Clear();
        }
    }
}
