using Godot;
using System;   
using System.Collections.Generic;
using Kuros.Utils;

namespace Kuros.Managers
{
    /// <summary>
    /// 相机区域管理器 - 使用 Area2D 检测玩家进入/离开区域，动态切换相机限制
    /// </summary>
    public partial class CameraZoneManager : Node
    {
        #region Camera Zone Class
        /// <summary>
        /// 相机区域定义
        /// </summary>
        [System.Serializable]
        public class CameraZone
        {
            [Export] public string Name { get; set; } = "Zone";
            
            [ExportCategory("相机限制")]
            [Export] public int LimitLeft { get; set; } = 0;
            [Export] public int LimitTop { get; set; } = 0;
            [Export] public int LimitRight { get; set; } = 0;
            [Export] public int LimitBottom { get; set; } = 0;
        }
        #endregion

        #region Exported Properties
        [Export] public Camera2D? TargetCamera { get; set; }
        [Export] public Node2D? Player { get; set; }
        
        [ExportCategory("相机区域配置")]
        [Export] public int Zone1_LimitLeft { get; set; } = -9300;
        [Export] public int Zone1_LimitTop { get; set; } = -1500;
        [Export] public int Zone1_LimitRight { get; set; } = -9300 + 5650;
        [Export] public int Zone1_LimitBottom { get; set; } = 1500;

        [Export] public int Zone2_LimitLeft { get; set; } = -3650;
        [Export] public int Zone2_LimitTop { get; set; } = -1500;
        [Export] public int Zone2_LimitRight { get; set; } = 5000;
        [Export] public int Zone2_LimitBottom { get; set; } = 1500;

        [ExportCategory("Area2D 节点路径")]
        [Export] public NodePath? Zone1AreaPath { get; set; }
        [Export] public NodePath? Zone2AreaPath { get; set; }
        #endregion

        #region Private Fields
        private int _currentZoneIndex = -1;
        private CameraZone? _currentZone;
        private CameraZone[] CameraZones = new CameraZone[0];
        private Area2D? _zone1Area;
        private Area2D? _zone2Area;
        #endregion

        #region Lifecycle
        public override void _Ready()
        {
            // 初始化相机区域数据
            CameraZones = new CameraZone[]
            {
                new CameraZone 
                { 
                    Name = "Zone_1_左侧房间",
                    LimitLeft = Zone1_LimitLeft,
                    LimitTop = Zone1_LimitTop,
                    LimitRight = Zone1_LimitRight,
                    LimitBottom = Zone1_LimitBottom
                },
                new CameraZone 
                { 
                    Name = "Zone_2_右侧房间",
                    LimitLeft = Zone2_LimitLeft,
                    LimitTop = Zone2_LimitTop,
                    LimitRight = Zone2_LimitRight,
                    LimitBottom = Zone2_LimitBottom
                }
            };

            if (TargetCamera == null)
            {
                GameLogger.Error(nameof(CameraZoneManager), "未设置目标相机！");
                return;
            }

            if (Player == null)
            {
                // 尝试自动查找玩家
                Player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (Player == null)
                {
                    GameLogger.Error(nameof(CameraZoneManager), "未找到玩家节点！");
                    return;
                }
            }

            // 查找 Area2D 节点
            InitializeAreas();

            // 初始化相机到第一个区域
            if (CameraZones.Length > 0)
            {
                SwitchToZone(0);
            }

            GameLogger.Info(nameof(CameraZoneManager), $"相机区域管理器已初始化，共有 {CameraZones.Length} 个区域");
        }

        public override void _ExitTree()
        {
            UnsubscribeAreaSignals();
            base._ExitTree();
        }
        #endregion

        #region Initialization
        /// <summary>
        /// 初始化 Area2D 节点
        /// </summary>
        private void InitializeAreas()
        {
            // 尝试从指定路径获取 Area2D
            if (Zone1AreaPath != null)
            {
                _zone1Area = GetNode<Area2D>(Zone1AreaPath);
            }

            if (Zone2AreaPath != null)
            {
                _zone2Area = GetNode<Area2D>(Zone2AreaPath);
            }

            // 如果没有找到，尝试自动查找
            if (_zone1Area == null)
            {
                _zone1Area = GetNodeOrNull<Area2D>("CameraZones/Zone1_Area2D");
            }

            if (_zone2Area == null)
            {
                _zone2Area = GetNodeOrNull<Area2D>("CameraZones/Zone2_Area2D");
            }

            // 订阅 Area2D 信号
            SubscribeAreaSignals();
        }

        /// <summary>
        /// 订阅 Area2D 进入/离开信号
        /// </summary>
        private void SubscribeAreaSignals()
        {
            if (_zone1Area != null)
            {
                _zone1Area.AreaEntered -= OnZone1AreaEntered;
                _zone1Area.AreaEntered += OnZone1AreaEntered;
                GameLogger.Info(nameof(CameraZoneManager), "已订阅 Zone1 Area2D 信号");
            }
            else
            {
                GameLogger.Warn(nameof(CameraZoneManager), "未找到 Zone1 Area2D 节点");
            }

            if (_zone2Area != null)
            {
                _zone2Area.AreaEntered -= OnZone2AreaEntered;
                _zone2Area.AreaEntered += OnZone2AreaEntered;
                GameLogger.Info(nameof(CameraZoneManager), "已订阅 Zone2 Area2D 信号");
            }
            else
            {
                GameLogger.Warn(nameof(CameraZoneManager), "未找到 Zone2 Area2D 节点");
            }
        }

        /// <summary>
        /// 取消订阅 Area2D 信号
        /// </summary>
        private void UnsubscribeAreaSignals()
        {
            if (_zone1Area != null)
            {
                _zone1Area.AreaEntered -= OnZone1AreaEntered;
            }

            if (_zone2Area != null)
            {
                _zone2Area.AreaEntered -= OnZone2AreaEntered;
            }
        }
        #endregion

        #region Area Signals
        /// <summary>
        /// 当玩家进入 Zone1 时
        /// </summary>
        private void OnZone1AreaEntered(Area2D area)
        {
            if (IsPlayerHitArea(area))
            {
                SwitchToZone(0);
            }
        }

        /// <summary>
        /// 当玩家进入 Zone2 时
        /// </summary>
        private void OnZone2AreaEntered(Area2D area)
        {
            if (IsPlayerHitArea(area))
            {
                SwitchToZone(1);
            }
        }

        /// <summary>
        /// 判断是否是玩家的 HitArea（玩家的碰撞检测区域）
        /// </summary>
        private bool IsPlayerHitArea(Area2D area)
        {
            if (Player == null || area == null)
                return false;

            // 检查是否是玩家的 HitArea 节点
            // HitArea 是 MainCharacter 下的 Area2D，用于伤害检测和区域检测
            var hitArea = Player.GetNodeOrNull<Area2D>("HitArea");
            if (hitArea != null && area == hitArea)
            {
                return true;
            }

            // 备选方案：检查是否是玩家本身或其子节点
            if (area == Player || Player.IsAncestorOf(area))
            {
                return true;
            }

            return false;
        }
        #endregion

        #region Zone Management
        /// <summary>
        /// 切换到指定的相机区域
        /// </summary>
        /// <param name="zoneIndex">区域索引</param>
        public void SwitchToZone(int zoneIndex)
        {
            if (zoneIndex < 0 || zoneIndex >= CameraZones.Length || TargetCamera == null)
                return;

            // 避免重复切换
            if (zoneIndex == _currentZoneIndex)
                return;

            CameraZone zone = CameraZones[zoneIndex];

            // 更新相机限制
            TargetCamera.LimitLeft = zone.LimitLeft;
            TargetCamera.LimitTop = zone.LimitTop;
            TargetCamera.LimitRight = zone.LimitRight;
            TargetCamera.LimitBottom = zone.LimitBottom;

            _currentZoneIndex = zoneIndex;
            _currentZone = zone;

            GameLogger.Info(nameof(CameraZoneManager), $"✓ 切换到相机区域: {zone.Name} " +
                $"(Left:{zone.LimitLeft}, Top:{zone.LimitTop}, Right:{zone.LimitRight}, Bottom:{zone.LimitBottom})");
        }

        /// <summary>
        /// 获取当前相机区域
        /// </summary>
        public CameraZone? GetCurrentZone()
        {
            return _currentZone;
        }

        /// <summary>
        /// 获取指定名称的区域
        /// </summary>
        public CameraZone? GetZoneByName(string name)
        {
            foreach (var zone in CameraZones)
            {
                if (zone.Name == name)
                    return zone;
            }
            return null;
        }
        #endregion

        #region Debug Helper
        /// <summary>
        /// 获取所有区域信息用于调试
        /// </summary>
        public string GetDebugInfo()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== 相机区域管理器调试信息 ===");
            sb.AppendLine($"当前激活区域: {(_currentZone?.Name ?? "无")}");
            sb.AppendLine($"总区域数: {CameraZones.Length}");
            sb.AppendLine($"Zone1 Area2D: {(_zone1Area != null ? "已连接" : "未找到")}");
            sb.AppendLine($"Zone2 Area2D: {(_zone2Area != null ? "已连接" : "未找到")}");
            
            for (int i = 0; i < CameraZones.Length; i++)
            {
                var zone = CameraZones[i];
                sb.AppendLine($"\n区域 {i}: {zone.Name}");
                sb.AppendLine($"  相机限制: L:{zone.LimitLeft} T:{zone.LimitTop} R:{zone.LimitRight} B:{zone.LimitBottom}");
            }
            
            return sb.ToString();
        }
        #endregion
    }
}
