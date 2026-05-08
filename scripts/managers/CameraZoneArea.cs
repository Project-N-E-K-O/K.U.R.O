using Godot;
using Kuros.Utils;

namespace Kuros.Managers
{
    /// <summary>
    /// 挂载在房间场景内的 Area2D 上，负责向 CameraZoneManager 注册该房间的相机区域。
    ///
    /// 使用方法：
    ///   1. 在房间场景根节点下添加一个 Area2D 节点，挂上此脚本。
    ///   2. 设置 collision_mask = 4（检测玩家 HitArea 所在的 Layer 3）。
    ///   3. 添加 CollisionShape2D 子节点，形状覆盖整个房间宽度。
    ///   4. 配置 ZoneName（建议与房间名相同）、CameraLimitTop/Bottom。
    ///
    /// 工作流程：
    ///   玩家 HitArea 进入此区域 → 调用 CameraZoneManager.RegisterZoneAndSwitch()
    ///   → 相机限制切换到此房间边界。
    ///   节点离开场景树时 → 调用 UnregisterZone() 清理注册信息。
    /// </summary>
    [GlobalClass]
    public partial class CameraZoneArea : Area2D
    {
        /// <summary>区域名称，需在同一关卡内唯一。建议与房间场景名一致。</summary>
        [Export] public string ZoneName { get; set; } = "Room_Zone";

        /// <summary>相机上限（世界 Y 坐标）。</summary>
        [Export] public int CameraLimitTop { get; set; } = -1500;

        /// <summary>相机下限（世界 Y 坐标）。</summary>
        [Export] public int CameraLimitBottom { get; set; } = 1500;

        /// <summary>进入此区域时相机的目标 Zoom（X/Y 相同）。</summary>
        [Export(PropertyHint.Range, "0.1,2.0,0.01")] public float ZoomLevel { get; set; } = 0.43f;

        /// <summary>离开此区域时恢复的默认 Zoom。</summary>
        [Export(PropertyHint.Range, "0.1,2.0,0.01")] public float DefaultZoom { get; set; } = 0.43f;

        private CameraZoneManager? _cameraZoneManager;
        private bool _playerInside = false;

        public override void _Ready()
        {
            AreaEntered += OnAreaEntered;
            AreaExited  += OnAreaExited;
        }

        public override void _ExitTree()
        {
            AreaEntered -= OnAreaEntered;
            AreaExited  -= OnAreaExited;

            if (_cameraZoneManager != null && IsInstanceValid(_cameraZoneManager))
                _cameraZoneManager.UnregisterZone(ZoneName);
        }

        private void OnAreaEntered(Area2D area)
        {
            if (!IsPlayerArea(area)) return;
            if (_playerInside) return; // 防止重复触发

            _playerInside = true;
            var mgr = GetOrFindManager();
            if (mgr == null) return;

            var bounds = ComputeWorldBounds();
            mgr.EnterZone(ZoneName, bounds);
            mgr.SetZoom(ZoomLevel);
        }

        private void OnAreaExited(Area2D area)
        {
            if (!IsPlayerArea(area)) return;
            if (!_playerInside) return;

            // 延迟一帧验证：物理边界抖动时 AreaExited 可能紧跟 AreaEntered
            // 通过 GetOverlappingAreas() 确认玩家确实不在区域内才真正退出
            CallDeferred(MethodName.DeferredExitCheck);
        }

        private void DeferredExitCheck()
        {
            // 若玩家仍在重叠列表中，说明是抖动误触发，不处理退出
            foreach (var overlapping in GetOverlappingAreas())
            {
                if (IsPlayerArea(overlapping))
                    return; // 玩家还在里面，忽略此次退出
            }

            // 玩家真的离开了
            if (!_playerInside) return;
            _playerInside = false;
            var mgr = GetOrFindManager();
            if (mgr == null) return;
            mgr.ExitZone(ZoneName);
            mgr.SetZoom(DefaultZoom);
        }

        // ─── 内部工具 ──────────────────────────────────────────────────────

        private bool IsPlayerArea(Area2D area)
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (player == null) return false;

            // 检查是否是玩家的 HitArea
            var hitArea = player.GetNodeOrNull<Area2D>("HitArea");
            if (hitArea != null && area == hitArea) return true;

            // 备选：area 是玩家节点树的成员
            return player.IsAncestorOf(area);
        }

        private CameraZoneManager? GetOrFindManager()
        {
            if (_cameraZoneManager != null && IsInstanceValid(_cameraZoneManager))
                return _cameraZoneManager;

            // 从场景树根节点向下查找 CameraZoneManager
            _cameraZoneManager = GetTree().Root.GetNodeOrNull<CameraZoneManager>("BattleScene/CameraZoneManager");
            if (_cameraZoneManager == null)
                GameLogger.Warn(nameof(CameraZoneArea), $"[{ZoneName}] 未找到 CameraZoneManager，相机区域不会切换。");

            return _cameraZoneManager;
        }

        /// <summary>
        /// 根据 CollisionShape2D 的世界位置计算房间相机边界。
        /// X 边界由碰撞形状决定，Y 边界由 CameraLimitTop/Bottom 决定。
        /// </summary>
        private Rect2 ComputeWorldBounds()
        {
            var shape = GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            if (shape?.Shape is RectangleShape2D rect)
            {
                // shape.Position 是相对于 Area2D 的本地坐标
                var worldCenter = GlobalPosition + shape.Position;
                return new Rect2(
                    worldCenter.X - rect.Size.X / 2f,
                    CameraLimitTop,
                    rect.Size.X,
                    CameraLimitBottom - CameraLimitTop
                );
            }

            GameLogger.Warn(nameof(CameraZoneArea), $"[{ZoneName}] 未找到 RectangleShape2D，使用默认宽度 5000。");
            return new Rect2(GlobalPosition.X - 2500f, CameraLimitTop, 5000f, CameraLimitBottom - CameraLimitTop);
        }
    }
}
