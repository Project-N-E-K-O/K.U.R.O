using Godot;
using System.Collections.Generic;
using Kuros.Utils;

namespace Kuros.Managers
{
    /// <summary>
    /// 相机区域管理器 - 动态区域注册模式。
    ///
    /// 工作流程：
    ///   1. StageGeneratorManager 生成关卡后调用 SetGlobalBounds() 设置全局初始相机范围。
    ///   2. 每个房间场景内的 CameraZoneArea 节点在玩家进入时调用 RegisterZoneAndSwitch()，
    ///      相机限制切换到该房间的边界。
    ///   3. 房间内的 BattleArena 检测到敌人时调用 CreateAndSwitchTemporaryCameraZone()，
    ///      相机锁定到战斗区域。
    ///   4. 战斗结束后调用 RemoveTemporaryCameraZone()，相机恢复到当前房间区域。
    /// </summary>
    public partial class CameraZoneManager : Node
    {
        /// <summary>相机区域定义。</summary>
        public class CameraZone
        {
            public string Name { get; set; } = "Zone";
            public int LimitLeft { get; set; } = 0;
            public int LimitTop { get; set; } = 0;
            public int LimitRight { get; set; } = 0;
            public int LimitBottom { get; set; } = 0;
            public float ZoomLevel { get; set; } = 0.43f;
        }

        [Export] public Camera2D? TargetCamera { get; set; }
        [Export] public Node2D? Player { get; set; }

        private CameraZone? _currentZone;
        private readonly Dictionary<string, CameraZone> _registeredZones = new();
        private readonly Dictionary<string, CameraZone> _temporaryCameraZones = new();
        private string? _temporaryCameraZoneNameBeforeSwitch;
        // 玩家当前身处的区域有序列表（进入时追加，退出时移除）
        private readonly List<string> _activeZoneStack = new();
        // 同名区域引用计数：处理同一场景中存在多个同名 CameraZoneArea 的情况
        private readonly Dictionary<string, int> _zoneRefCounts = new();
        // 过场动画期间锁定，防止玩家被 Disabled 导致区域误退出
        private bool _zoneLocked = false;

        /// <summary>当前激活的相机区域名称。</summary>
        public string? CurrentZoneName => _currentZone?.Name;

        /// <summary>锁定区域切换，过场动画接管摄像机时调用。</summary>
        public void LockZone()
        {
            _zoneLocked = true;
            GameLogger.Debug(nameof(CameraZoneManager), $"区域锁定（当前: {_currentZone?.Name ?? "无"})，过场期间忽略区域进出");
        }

        /// <summary>解锁区域切换，过场动画结束时调用。</summary>
        public void UnlockZone()
        {
            _zoneLocked = false;
            GameLogger.Debug(nameof(CameraZoneManager), "区域解锁");
        }

        public override void _Ready()
        {
            if (TargetCamera == null)
            {
                GameLogger.Error(nameof(CameraZoneManager), "未设置目标相机！");
                return;
            }

            if (Player == null)
            {
                Player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (Player == null)
                    GameLogger.Warn(nameof(CameraZoneManager), "未找到玩家节点，将在首次区域进入时自动查找。");
            }

            GameLogger.Info(nameof(CameraZoneManager), "相机区域管理器已初始化（动态区域注册模式）");
        }

        // ─── 区域注册 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 注册一个命名相机区域（不切换）。
        /// </summary>
        public void RegisterZone(string name, Rect2 bounds, float zoomLevel = 0.43f)
        {
            _registeredZones[name] = BoundsToZone(name, bounds, zoomLevel);
            GameLogger.Debug(nameof(CameraZoneManager), $"注册区域: {name}  X[{(int)bounds.Position.X}, {(int)(bounds.Position.X + bounds.Size.X)}]");
        }

        /// <summary>
        /// 玩家进入某区域时调用：将该区域推入活跃栈并切换相机。
        /// </summary>
        public void EnterZone(string name, Rect2 bounds, float zoomLevel = 0.43f)
        {
            RegisterZone(name, bounds, zoomLevel);

            int count = _zoneRefCounts.GetValueOrDefault(name, 0);
            _zoneRefCounts[name] = count + 1;

            if (count == 0)
                _activeZoneStack.Add(name);

            if (_zoneLocked)
            {
                GameLogger.Debug(nameof(CameraZoneManager), $"区域已锁定，忽略进入: {name}");
                return;
            }
            // 只有当栈中仅有本区域时（无其他重叠区域）才立刻切换，否则留在当前区域
            if (_activeZoneStack.Count == 1)
                SwitchToZone(name);
            GameLogger.Debug(nameof(CameraZoneManager), $"进入区域: {name} (ref:{count + 1})，栈: [{string.Join(", ", _activeZoneStack)}]");
        }

        /// <summary>
        /// 玩家离开某区域时调用：从活跃栈移除，并切换到栈顶的上一个区域。
        /// </summary>
        public void ExitZone(string name)
        {
            int count = _zoneRefCounts.GetValueOrDefault(name, 0);
            if (count <= 0) return;

            _zoneRefCounts[name] = count - 1;

            if (count > 1)
            {
                GameLogger.Debug(nameof(CameraZoneManager), $"离开区域: {name} (ref:{count - 1})，仍有同名区域未退出");
                return;
            }

            _activeZoneStack.Remove(name);
            GameLogger.Debug(nameof(CameraZoneManager), $"离开区域: {name}，栈: [{string.Join(", ", _activeZoneStack)}]");

            // 区域锁定期间不切换相机，仅维护栈
            if (_zoneLocked)
            {
                GameLogger.Debug(nameof(CameraZoneManager), $"区域已锁定，忽略离开: {name}");
                return;
            }

            // 当前相机正是该区域才需要切换
            if (_currentZone?.Name == name)
            {
                if (_activeZoneStack.Count > 0)
                    SwitchToZone(_activeZoneStack[^1]); // 切回栈顶（最近进入的区域）
                else
                    SwitchToZone("Stage_Global");       // 已无房间区域，回到全局
            }
        }

        /// <summary>
        /// 注销一个区域（房间离开场景树时调用）。
        /// </summary>
        public void UnregisterZone(string name)
        {
            _registeredZones.Remove(name);
            _activeZoneStack.Remove(name);
            _zoneRefCounts.Remove(name);
        }

        /// <summary>
        /// 注册并立即切换（向后兼容，内部调用 EnterZone）。
        /// </summary>
        public void RegisterZoneAndSwitch(string name, Rect2 bounds, float zoomLevel = 0.43f) => EnterZone(name, bounds, zoomLevel);

        // ─── 区域切换 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 切换到已注册的区域（含临时区域）。
        /// </summary>
        public void SwitchToZone(string zoneName)
        {
            if (_currentZone?.Name == zoneName) return;

            CameraZone? zone = null;
            if (!_registeredZones.TryGetValue(zoneName, out zone))
                _temporaryCameraZones.TryGetValue(zoneName, out zone);

            if (zone == null)
            {
                GameLogger.Warn(nameof(CameraZoneManager), $"区域 '{zoneName}' 未注册，切换失败。");
                return;
            }

            _currentZone = zone;
            ApplyZoneToCamera(zone);
            GameLogger.Info(nameof(CameraZoneManager),
                $"✓ 切换到区域: {zoneName}  L:{zone.LimitLeft} R:{zone.LimitRight}  T:{zone.LimitTop} B:{zone.LimitBottom}");
        }

        // ─── 临时区域（战斗锁定）──────────────────────────────────────────────

        /// <summary>
        /// 创建临时战斗相机区域并立即切换。同时记录当前区域以便战斗结束后恢复。
        /// 由 BattleArena 在激活战斗时调用。
        /// </summary>
        public void CreateAndSwitchTemporaryCameraZone(Rect2 arenaRect, string zoneName, float zoomLevel = 0.43f)
        {
            _temporaryCameraZones[zoneName] = BoundsToZone(zoneName, arenaRect, zoomLevel);
            _temporaryCameraZoneNameBeforeSwitch = _currentZone?.Name;
            SwitchToZone(zoneName);
            GameLogger.Info(nameof(CameraZoneManager), $"✓ 创建临时战斗区域: {zoneName}，战斗结束后恢复至: {_temporaryCameraZoneNameBeforeSwitch ?? "无"}");
        }

        /// <summary>
        /// 移除临时战斗区域并恢复至战斗前的区域。
        /// 由 BattleArena 在战斗结束时调用。
        /// </summary>
        public void RemoveTemporaryCameraZone(string zoneName)
        {
            if (!_temporaryCameraZones.Remove(zoneName)) return;

            if (_currentZone?.Name == zoneName)
            {
                if (!string.IsNullOrEmpty(_temporaryCameraZoneNameBeforeSwitch))
                    SwitchToZone(_temporaryCameraZoneNameBeforeSwitch);

                _temporaryCameraZoneNameBeforeSwitch = null;
            }

            GameLogger.Info(nameof(CameraZoneManager), $"✓ 移除临时战斗区域: {zoneName}");
        }

        // ─── 全局边界（由 StageGeneratorManager 调用）──────────────────────────

        /// <summary>
        /// 设置关卡全局相机边界，关卡生成完毕后立即生效。
        /// 此区域作为默认基础区域，CameraZoneArea 进入房间后会覆盖为房间边界。
        /// </summary>
        public void SetGlobalBounds(int limitLeft, int limitTop, int limitRight, int limitBottom, float zoomLevel = 0.43f)
        {
            if (TargetCamera == null) return;
            var bounds = new Rect2(limitLeft, limitTop, limitRight - limitLeft, limitBottom - limitTop);
            RegisterZone("Stage_Global", bounds, zoomLevel);
            SwitchToZone("Stage_Global");
            GameLogger.Info(nameof(CameraZoneManager),
                $"全局相机边界已设置：X[{limitLeft}, {limitRight}]  Y[{limitTop}, {limitBottom}]");
        }

        // ─── 内部工具 ──────────────────────────────────────────────────────────

        private static CameraZone BoundsToZone(string name, Rect2 bounds, float zoomLevel = 0.43f) => new CameraZone
        {
            Name = name,
            LimitLeft   = (int)bounds.Position.X,
            LimitTop    = (int)bounds.Position.Y,
            LimitRight  = (int)(bounds.Position.X + bounds.Size.X),
            LimitBottom = (int)(bounds.Position.Y + bounds.Size.Y),
            ZoomLevel   = zoomLevel,
        };

        private void ApplyZoneToCamera(CameraZone zone)
        {
            if (TargetCamera == null) return;
            TargetCamera.LimitLeft   = zone.LimitLeft;
            TargetCamera.LimitTop    = zone.LimitTop;
            TargetCamera.LimitRight  = zone.LimitRight;
            TargetCamera.LimitBottom = zone.LimitBottom;
            SetZoom(zone.ZoomLevel);
        }

        // ─── 缩放 ──────────────────────────────────────────────────────────────

        private Tween? _zoomTween;

        /// <summary>
        /// 平滑过渡相机 Zoom。<br/>
        /// <paramref name="zoom"/> 为目标缩放值（X/Y 相同）。<br/>
        /// <paramref name="duration"/> 为过渡时长（秒），0 表示立即生效。
        /// </summary>
        public void SetZoom(float zoom, float duration = 0.25f)
        {
            if (TargetCamera == null) return;

            _zoomTween?.Kill();

            var target = new Vector2(zoom, zoom);
            if (duration <= 0f)
            {
                TargetCamera.Zoom = target;
                return;
            }

            _zoomTween = TargetCamera.CreateTween();
            _zoomTween.TweenProperty(TargetCamera, "zoom", target, duration)
                      .SetTrans(Tween.TransitionType.Sine)
                      .SetEase(Tween.EaseType.InOut);
        }

        // ─── 调试 ──────────────────────────────────────────────────────────────

        public string GetDebugInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== CameraZoneManager ===");
            sb.AppendLine($"当前区域: {(_currentZone?.Name ?? "无")}");
            sb.AppendLine($"已注册区域 ({_registeredZones.Count}):");
            foreach (var kv in _registeredZones)
                sb.AppendLine($"  {kv.Key}: L={kv.Value.LimitLeft} R={kv.Value.LimitRight}");
            if (_temporaryCameraZones.Count > 0)
            {
                sb.AppendLine($"临时区域 ({_temporaryCameraZones.Count}):");
                foreach (var kv in _temporaryCameraZones)
                    sb.AppendLine($"  {kv.Key}: L={kv.Value.LimitLeft} R={kv.Value.LimitRight}");
            }
            return sb.ToString();
        }
    }
}
