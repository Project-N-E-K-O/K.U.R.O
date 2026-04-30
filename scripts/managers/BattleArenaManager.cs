using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Kuros.Core;
using Kuros.Utils;

namespace Kuros.Managers
{
    /// <summary>
    /// 战斗区域管理器：
    /// 在敌人生成完成后创建"空气墙"边界，限制玩家和敌人的移动范围，
    /// 同时将摄像头限制在战斗区域内。
    /// 当所有敌人被击杀时移除空气墙和相机限制。
    /// </summary>
    [GlobalClass]
    public partial class BattleArenaManager : Node
    {
        /// <summary>战斗区域的世界坐标矩形。</summary>
        public Rect2 ArenaRect { get; private set; }

        /// <summary>空气墙使用的碰撞层。</summary>
        [Export(PropertyHint.Layers2DPhysics)]
        public uint BoundaryCollisionLayer { get; set; } = 5u;

        /// <summary>空气墙的碰撞掩码（应包含玩家层0和敌人层2）。</summary>
        [Export(PropertyHint.Layers2DPhysics)]
        public uint BoundaryCollisionMask { get; set; } = 0b101u; // Layer 0 + Layer 2

        /// <summary>空气墙的厚度（向内缩进多少）。</summary>
        [Export(PropertyHint.Range, "1,50,1")]
        public float BoundaryThickness { get; set; } = 2f;

        /// <summary>战斗区域名称标识。</summary>
        public string ArenaId { get; set; } = "battle_arena";

        [Signal]
        public delegate void BattleArenaStartedEventHandler(string arenaId);

        [Signal]
        public delegate void BattleArenaCompletedEventHandler(string arenaId);

        private Rect2 _originalCameraZoneRect;
        private string? _originalCameraZoneName;
        private List<GameActor> _arenaEnemies = new();
        private BattleArenaBoundary? _boundaryWalls;
        private CameraZoneManager? _cameraZoneManager;
        private bool _battleCompleted = false;
        private bool _enemySpawningComplete = false;

        public void InitializeBattleArea(
            Rect2 arenaRect,
            CameraZoneManager cameraManager,
            string arenaId = "battle_arena")
        {
            ArenaRect = arenaRect;
            ArenaId = arenaId;
            _cameraZoneManager = cameraManager;

            if (cameraManager == null)
            {
                GameLogger.Warn(nameof(BattleArenaManager), "空气墙：CameraZoneManager not found, cannot initialize arena.");
                return;
            }

            // 创建空气墙
            CreateBoundaryWalls();

            // 切换相机区域
            SwitchCameraZone();

            // 开启定时检查
            SetProcess(true);

            EmitSignal(SignalName.BattleArenaStarted, ArenaId);
            GameLogger.Info(nameof(BattleArenaManager), $"空气墙：'{ArenaId}' initialized at rect {ArenaRect}");
        }

        /// <summary>
        /// 添加敌人到战斗区域监管列表。
        /// </summary>
        public void AddArenaEnemy(GameActor enemy)
        {
            if (enemy != null && !_arenaEnemies.Contains(enemy))
            {
                _arenaEnemies.Add(enemy);
            }
        }

        /// <summary>
        /// 标记敌人生成完成，此时可以开始检查敌人是否全部击杀。
        /// </summary>
        public void SetEnemySpawningComplete()
        {
            _enemySpawningComplete = true;
            GameLogger.Info(nameof(BattleArenaManager), $"空气墙：'{ArenaId}' enemy spawning complete, {_arenaEnemies.Count} enemies to defeat.");
        }

        /// <summary>
        /// 每帧检查敌人状态。
        /// </summary>
        public override void _Process(double delta)
        {
            if (_battleCompleted) return;

            CheckEnemyStatus();
        }

        /// <summary>
        /// 检查敌人状态，移除死亡或无效的敌人。
        /// 只有在敌人生成完成后，才会判断是否需要完成战斗。
        /// </summary>
        private void CheckEnemyStatus()
        {
            _arenaEnemies.RemoveAll(enemy => !IsInstanceValid(enemy) || enemy.IsDead);

            // 只有在敌人生成完成且敌人全部被击杀时，才完成战斗
            if (_enemySpawningComplete && _arenaEnemies.Count == 0)
            {
                CompleteBattle();
            }
        }

        /// <summary>
        /// 创建空气墙边界（四面体）。
        /// </summary>
        private void CreateBoundaryWalls()
        {
            _boundaryWalls = new BattleArenaBoundary
            {
                Name = $"{ArenaId}_Boundary",
                ArenaRect = ArenaRect,
                WallThickness = BoundaryThickness,
                CollisionLayer = BoundaryCollisionLayer,
                CollisionMask = BoundaryCollisionMask
            };

            AddChild(_boundaryWalls);
            GameLogger.Info(nameof(BattleArenaManager), $"空气墙：Boundary walls created for arena '{ArenaId}'");
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
                GameLogger.Info(nameof(BattleArenaManager), $"空气墙：Boundary walls removed for arena '{ArenaId}'");
            }
        }

        /// <summary>
        /// 切换相机区域到战斗区域。
        /// </summary>
        private void SwitchCameraZone()
        {
            if (_cameraZoneManager == null)
            {
                GameLogger.Warn(nameof(BattleArenaManager), "空气墙：CameraZoneManager not found, skipping camera zone switch.");
                return;
            }

            _originalCameraZoneName = _cameraZoneManager.CurrentZoneName;
            _cameraZoneManager.CreateAndSwitchTemporaryCameraZone(ArenaRect, ArenaId);
            GameLogger.Info(nameof(BattleArenaManager), $"空气墙：Camera switched to battle arena '{ArenaId}'");
        }

        /// <summary>
        /// 恢复原始相机区域。
        /// </summary>
        private void RestoreCameraZone()
        {
            if (_cameraZoneManager == null) return;

            if (!string.IsNullOrEmpty(_originalCameraZoneName))
            {
                _cameraZoneManager.SwitchToZone(_originalCameraZoneName);
                GameLogger.Info(nameof(BattleArenaManager), $"空气墙：Camera restored to zone '{_originalCameraZoneName}'");
            }
            else
            {
                // 如果没有原始区域，移除临时区域即可
                _cameraZoneManager.RemoveTemporaryCameraZone(ArenaId);
            }
        }

        /// <summary>
        /// 战斗完成：移除空气墙和相机限制。
        /// </summary>
        private void CompleteBattle()
        {
            if (_battleCompleted) return;

            _battleCompleted = true;
            SetProcess(false);

            GameLogger.Info(nameof(BattleArenaManager), $"空气墙：Battle arena '{ArenaId}' completed, all enemies defeated!");

            RemoveBoundaryWalls();
            RestoreCameraZone();

            EmitSignal(SignalName.BattleArenaCompleted, ArenaId);

            // 清理此管理器
            QueueFree();
        }

        /// <summary>
        /// 清理资源。
        /// </summary>
        public override void _ExitTree()
        {
            RemoveBoundaryWalls();
            RestoreCameraZone();
            _arenaEnemies.Clear();
        }
    }
}
