using Godot;
using System.Collections.Generic;

namespace Kuros.Core
{
    /// <summary>
    /// 导航烘焙协调器：统一家具类（RigidBodyWorldItemEntity/DestructibleObject）的导航网格重烘焙请求。
    /// 防抖窗口（DebounceSeconds）内合并所有请求——窗口结束且无新请求时统一烘焙一次，
    /// 消除地图加载/批量破坏时同帧或连续帧的重复全场景遍历烘焙。
    /// </summary>
    public static class NavigationRebakeCoordinator
    {
        /// <summary>防抖窗口（秒）：窗口内新请求会推迟烘焙，直到停顿满窗口才执行一次。</summary>
        public const double DebounceSeconds = 0.5;
        /// <summary>手工制作/随平台移动的独立导航面加入此组，不参与家具触发的全场重烘焙。</summary>
        public const string ExcludedRegionGroup = "navigation_rebake_excluded";

        private static readonly HashSet<SceneTree> PendingRebakeScenes = new();
        private static double _lastRequestMsec;
        private static bool _timerScheduled;

        /// <summary>节点子树中是否存在属于导航源几何组的节点（导航源移除/恢复时需重烘焙）。</summary>
        public static bool HasNavigationSourceGeometry(Node node)
        {
            foreach (Node child in node.GetChildren())
            {
                if (child.IsInGroup("navigation_polygon_source_geometry_group")) return true;
                if (HasNavigationSourceGeometry(child)) return true;
            }
            return false;
        }

        /// <summary>请求一次场景导航重烘焙（自动防抖合并）。</summary>
        public static void RequestRebake(Node node)
        {
            var tree = node.GetTree();
            if (tree == null) return;

            PendingRebakeScenes.Add(tree);
            _lastRequestMsec = Time.GetTicksMsec();

            if (_timerScheduled) return;
            _timerScheduled = true;
            ScheduleFlush(tree);
        }

        private static void ScheduleFlush(SceneTree tree)
        {
            // 定时器到期后检查窗口内是否有新请求——有则滑动续期，无则统一烘焙
            tree.CreateTimer(DebounceSeconds).Timeout += () => OnDebounceElapsed(tree);
        }

        private static void OnDebounceElapsed(SceneTree tree)
        {
            _timerScheduled = false;

            if (Time.GetTicksMsec() - _lastRequestMsec < DebounceSeconds * 1000.0)
            {
                if (PendingRebakeScenes.Count > 0)
                {
                    _timerScheduled = true;
                    ScheduleFlush(tree);
                }
                return;
            }

            foreach (var sceneTree in PendingRebakeScenes)
            {
                var scene = sceneTree.CurrentScene;
                if (scene != null) RebakeAllNavigationRegions(scene);
            }
            PendingRebakeScenes.Clear();
        }

        /// <summary>递归重烘焙普通区域；保留显式排除的独立导航面。</summary>
        private static void RebakeAllNavigationRegions(Node node)
        {
            if (!GodotObject.IsInstanceValid(node)) return;
            if (node is NavigationRegion2D navRegion)
            {
                if (!navRegion.IsInGroup(ExcludedRegionGroup)) navRegion.BakeNavigationPolygon();
                return;
            }
            foreach (Node child in node.GetChildren())
                RebakeAllNavigationRegions(child);
        }
    }
}
