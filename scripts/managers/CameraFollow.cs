using Godot;
using System;

namespace Kuros.Managers
{
    /// <summary>
    /// 摄像机跟随脚本，实现跟随玩家并在到达地图边缘时停止跟随
    /// </summary>
    public partial class CameraFollow : Camera2D
    {
        #region Constants
        private const float DEFAULT_FOLLOW_SPEED = 5.0f;
        private const float MIN_FOLLOW_SPEED = 0.1f;
        private const float MAX_FOLLOW_SPEED = 50.0f;
        private const float VIEWPORT_UPDATE_THRESHOLD = 1.0f; // 视口大小变化阈值
        #endregion

        #region Exported Properties
        [ExportCategory("跟随设置")]
        [Export] public Node2D? Target { get; set; } // 跟随目标（玩家）
        
        [ExportCategory("地图边界")]
        [Export] public Vector2 MapMin { get; set; } = Vector2.Zero; // 地图最小边界
        [Export] public Vector2 MapMax { get; set; } = Vector2.Zero; // 地图最大边界
        
        [ExportCategory("跟随选项")]
        [Export] public bool FollowEnabled { get; set; } = true; // 是否启用跟随
        
        [Export(PropertyHint.Range, "0.1,50,0.1")]
        public float FollowSpeed { get; set; } = DEFAULT_FOLLOW_SPEED; // 跟随速度（平滑度）
        
        [Export] public new Vector2 Offset { get; set; } = Vector2.Zero; // 摄像机偏移量
        #endregion

        #region Private Fields
        private Viewport? _viewport;
        private Vector2 _cachedViewportSize = Vector2.Zero;
        private bool _boundsValidated = false;
        #endregion

        #region Lifecycle
        public override void _Ready()
        {
            base._Ready();
            
            InitializeViewport();
            InitializeTarget();
            InitializeMapBounds();
            
            // 初始化时立即设置摄像机位置
            if (Target != null)
            {
                SnapToTarget();
            }
        }

        public override void _Process(double delta)
        {
            if (!FollowEnabled || Target == null || _viewport == null)
                return;

            // 检查视口大小是否变化（处理窗口大小调整）
            UpdateViewportSizeIfNeeded();

            // 计算目标位置（玩家位置 + 偏移量）
            Vector2 targetPosition = Target.GlobalPosition + Offset;
            
            // 获取视口大小的一半，用于计算摄像机边界
            Vector2 halfViewport = _cachedViewportSize / 2.0f;
            
            // 计算摄像机应该到达的位置，考虑地图边界
            Vector2 desiredPosition = CalculateClampedPosition(targetPosition, halfViewport);
            
            // 平滑移动到目标位置
            GlobalPosition = GlobalPosition.Lerp(desiredPosition, (float)delta * FollowSpeed);
        }
        #endregion

        #region Initialization
        /// <summary>
        /// 初始化视口
        /// </summary>
        private void InitializeViewport()
        {
            _viewport = GetViewport();
            if (_viewport != null)
            {
                _cachedViewportSize = _viewport.GetVisibleRect().Size;
            }
            else
            {
                GD.PrintErr("CameraFollow: 无法获取视口！");
            }
        }

        /// <summary>
        /// 初始化跟随目标
        /// </summary>
        private void InitializeTarget()
        {
            // 如果已经在编辑器中设置，直接返回
            if (Target != null)
                return;

            // 尝试从场景中查找Player节点
            var playerFromGroup = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (playerFromGroup != null)
            {
                Target = playerFromGroup;
                GD.Print("CameraFollow: 从组 'player' 中找到跟随目标");
                return;
            }

            // 尝试通过路径查找
            var playerNode = GetNodeOrNull<Node2D>("../Player");
            if (playerNode != null)
            {
                Target = playerNode;
                GD.Print("CameraFollow: 通过路径 '../Player' 找到跟随目标");
                return;
            }

            // 如果仍然没有目标，输出警告
            GD.PrintErr("CameraFollow: 未找到跟随目标！请在编辑器中设置Target属性，或将目标节点添加到 'player' 组。");
        }

        /// <summary>
        /// 初始化地图边界
        /// </summary>
        private void InitializeMapBounds()
        {
            // 如果地图边界未设置，尝试从Background节点获取
            if (!IsMapBoundsSet())
            {
                AutoDetectMapBounds();
            }

            // 验证边界有效性
            ValidateMapBounds();
        }

        /// <summary>
        /// 检查地图边界是否已设置
        /// </summary>
        private bool IsMapBoundsSet()
        {
            return MapMin != Vector2.Zero || MapMax != Vector2.Zero;
        }

        /// <summary>
        /// 验证地图边界的有效性
        /// </summary>
        private void ValidateMapBounds()
        {
            if (!IsMapBoundsSet())
            {
                _boundsValidated = false;
                return;
            }

            // 检查边界是否有效（Min应该小于Max）
            if (MapMin.X >= MapMax.X || MapMin.Y >= MapMax.Y)
            {
                GD.PrintErr($"CameraFollow: 地图边界无效！MapMin ({MapMin}) 应该小于 MapMax ({MapMax})");
                _boundsValidated = false;
                return;
            }

            _boundsValidated = true;
        }
        #endregion

        #region Map Bounds Detection
        /// <summary>
        /// 自动检测地图边界（从Background节点）
        /// </summary>
        private void AutoDetectMapBounds()
        {
            var background = FindBackgroundNode();
            
            if (background != null)
            {
                var rect = background.GetRect();
                MapMin = new Vector2(rect.Position.X, rect.Position.Y);
                MapMax = new Vector2(rect.Position.X + rect.Size.X, rect.Position.Y + rect.Size.Y);
                
                GD.Print($"CameraFollow: 自动检测到地图边界 - Min: {MapMin}, Max: {MapMax}");
                ValidateMapBounds();
            }
            else
            {
                GD.Print("CameraFollow: 未找到Background节点，无法自动检测地图边界。请在编辑器中手动设置MapMin和MapMax。");
            }
        }

        /// <summary>
        /// 查找Background节点
        /// </summary>
        private ColorRect? FindBackgroundNode()
        {
            // 首先尝试从组中查找
            var background = GetTree().GetFirstNodeInGroup("map_background") as ColorRect;
            if (background != null)
                return background;

            // 尝试通过路径查找
            background = GetNodeOrNull<ColorRect>("../Background");
            if (background != null)
                return background;

            // 尝试查找场景根节点下的Background
            var sceneRoot = GetTree().CurrentScene;
            if (sceneRoot != null)
            {
                background = sceneRoot.GetNodeOrNull<ColorRect>("Background");
                if (background != null)
                    return background;
            }

            return null;
        }
        #endregion

        #region Camera Movement
        /// <summary>
        /// 计算限制在地图边界内的摄像机位置
        /// </summary>
        /// <param name="targetPos">目标位置</param>
        /// <param name="halfViewport">视口大小的一半</param>
        /// <returns>限制后的摄像机位置</returns>
        private Vector2 CalculateClampedPosition(Vector2 targetPos, Vector2 halfViewport)
        {
            // 如果地图边界未验证，直接返回目标位置
            if (!_boundsValidated)
            {
                return targetPos;
            }

            // 计算有效的边界范围（考虑视口大小）
            float minX = MapMin.X + halfViewport.X;
            float maxX = MapMax.X - halfViewport.X;
            float minY = MapMin.Y + halfViewport.Y;
            float maxY = MapMax.Y - halfViewport.Y;

            // 如果地图太小（小于视口），将摄像机固定在地图中心
            if (minX > maxX)
            {
                minX = maxX = (MapMin.X + MapMax.X) / 2.0f;
            }
            if (minY > maxY)
            {
                minY = maxY = (MapMin.Y + MapMax.Y) / 2.0f;
            }

            // 限制摄像机位置
            float clampedX = Mathf.Clamp(targetPos.X, minX, maxX);
            float clampedY = Mathf.Clamp(targetPos.Y, minY, maxY);

            return new Vector2(clampedX, clampedY);
        }

        /// <summary>
        /// 立即跳转到目标位置（无平滑）
        /// </summary>
        public void SnapToTarget()
        {
            if (Target == null || _viewport == null)
                return;

            UpdateViewportSizeIfNeeded();
            
            Vector2 halfViewport = _cachedViewportSize / 2.0f;
            Vector2 targetPosition = Target.GlobalPosition + Offset;
            
            GlobalPosition = CalculateClampedPosition(targetPosition, halfViewport);
        }
        #endregion

        #region Viewport Management
        /// <summary>
        /// 检查并更新视口大小（如果变化超过阈值）
        /// </summary>
        private void UpdateViewportSizeIfNeeded()
        {
            if (_viewport == null)
                return;

            Vector2 currentSize = _viewport.GetVisibleRect().Size;
            
            // 如果视口大小变化超过阈值，更新缓存
            if ((currentSize - _cachedViewportSize).Length() > VIEWPORT_UPDATE_THRESHOLD)
            {
                _cachedViewportSize = currentSize;
            }
        }
        #endregion

        #region Public API
        /// <summary>
        /// 设置地图边界
        /// </summary>
        /// <param name="min">最小边界</param>
        /// <param name="max">最大边界</param>
        public void SetMapBounds(Vector2 min, Vector2 max)
        {
            MapMin = min;
            MapMax = max;
            ValidateMapBounds();
        }

        /// <summary>
        /// 设置跟随目标
        /// </summary>
        /// <param name="target">目标节点</param>
        public void SetTarget(Node2D? target)
        {
            Target = target;
            if (target != null)
            {
                SnapToTarget();
            }
        }

        /// <summary>
        /// 检查是否到达地图边界
        /// </summary>
        /// <returns>如果摄像机到达边界返回true</returns>
        public bool IsAtBoundary()
        {
            if (!_boundsValidated || _viewport == null)
                return false;

            Vector2 halfViewport = _cachedViewportSize / 2.0f;
            Vector2 currentPos = GlobalPosition;

            float minX = MapMin.X + halfViewport.X;
            float maxX = MapMax.X - halfViewport.X;
            float minY = MapMin.Y + halfViewport.Y;
            float maxY = MapMax.Y - halfViewport.Y;

            return Mathf.IsEqualApprox(currentPos.X, minX) || 
                   Mathf.IsEqualApprox(currentPos.X, maxX) ||
                   Mathf.IsEqualApprox(currentPos.Y, minY) || 
                   Mathf.IsEqualApprox(currentPos.Y, maxY);
        }
        #endregion
    }
}
