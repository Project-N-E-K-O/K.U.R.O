using Godot;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Kuros.FX
{
    public partial class RandomLitterPartDisplay : Node2D
    {
        /// <summary>
        /// 要显示的LitterPart子节点（可多选）
        /// </summary>
        [Export]
        public Node2D[] SelectedLitterParts = Array.Empty<Node2D>();

        /// <summary>
        /// 要显示的项目数量（从选择的节点中随机选择）
        /// </summary>
        [Export]
        public int DisplayCount = 1;

        /// <summary>
        /// 是否显示主体 LargeSmoke（默认显示）。关掉 = 只让随机小部件出现；
        /// 隐藏时同步关闭其发射（与 LitterPart 同款成本考虑——不可见的粒子系统不该白跑模拟）。
        /// </summary>
        [Export]
        public bool ShowLargeSmoke = true;

        /// <summary>
        /// 参与随机显示的候选节点（来自 SelectedLitterParts，不依赖节点命名）
        /// </summary>
        private List<Node2D> _allLitterParts = new();

        /// <summary>
        /// LargeSmoke节点引用（始终显示）
        /// </summary>
        private Node2D? _largeSmoke;

        public override void _Ready()
        {
            CollectSelectedParts();
            FindLargeSmoke();
            ApplyRandomDisplay();
        }

        /// <summary>
        /// 查找LargeSmoke节点（可选：ShowLargeSmoke 关闭时场景里没有它也算合法，故用 OrNull）
        /// </summary>
        private void FindLargeSmoke()
        {
            _largeSmoke = GetNodeOrNull<Node2D>("LargeSmoke");
        }

        /// <summary>
        /// 候选节点直接取编辑器里选中的 SelectedLitterParts——不按节点名扫描，
        /// 场景里叫什么（LitterPartA / GlassA / …）都能用。
        /// </summary>
        private void CollectSelectedParts()
        {
            _allLitterParts.Clear();

            foreach (var part in SelectedLitterParts)
            {
                if (part != null && GodotObject.IsInstanceValid(part) && !_allLitterParts.Contains(part))
                {
                    _allLitterParts.Add(part);
                }
            }
        }

        /// <summary>
        /// 应用随机显示逻辑
        /// </summary>
        private void ApplyRandomDisplay()
        {
            // 隐藏所有LitterPart。隐藏的 GPUParticles2D 依然会模拟（emitting 已启动），
            // 必须同时关闭发射——否则 9 个粒子系统为 2 个可见项全额付出模拟/渲染成本
            foreach (var part in _allLitterParts)
            {
                part.Visible = false;
                if (part is GpuParticles2D gp)
                    gp.Emitting = false;
            }

            // 主体 LargeSmoke：按 ShowLargeSmoke 显示/隐藏（隐藏时同步关发射——与上面的 LitterPart 同款成本考虑）
            if (_largeSmoke != null)
            {
                _largeSmoke.Visible = ShowLargeSmoke;
                if (_largeSmoke is GpuParticles2D smokeGp)
                {
                    smokeGp.Emitting = ShowLargeSmoke;
                    if (ShowLargeSmoke) smokeGp.Restart();
                }
            }

            // 检查选择的节点是否有效
            if (SelectedLitterParts.Length == 0)
            {
                GD.PrintErr("RandomLitterPartDisplay: 没有选择任何LitterPart节点");
                return;
            }

            // 过滤出有效的选择节点
            var validSelected = SelectedLitterParts
                .Where(node => node != null && _allLitterParts.Contains(node))
                .ToList();

            if (validSelected.Count == 0)
            {
                GD.PrintErr("RandomLitterPartDisplay: 所有选择的节点都无效");
                return;
            }

            // 确保DisplayCount不超过选择的节点数量
            int displayCountActual = Mathf.Min(DisplayCount, validSelected.Count);

            // 从选择的节点中随机选择displayCountActual项进行显示
            List<Node2D> toDisplay = new();
            List<int> indices = new();
            for (int i = 0; i < validSelected.Count; i++)
            {
                indices.Add(i);
            }

            // Fisher-Yates洗牌算法
            Random random = new Random();
            for (int i = indices.Count - 1; i > 0; i--)
            {
                int randomIndex = random.Next(i + 1);
                // 交换
                (indices[i], indices[randomIndex]) = (indices[randomIndex], indices[i]);
            }

            // 取前displayCountActual个
            for (int i = 0; i < displayCountActual; i++)
            {
                toDisplay.Add(validSelected[indices[i]]);
            }

            // 显示随机选择的节点（粒子系统重新启动发射——_Ready 时可能已被隐藏关闭）
            foreach (var node in toDisplay)
            {
                node.Visible = true;
                if (node is GpuParticles2D gp)
                {
                    gp.Emitting = true;
                    gp.Restart();
                }
            }
        }

        /// <summary>
        /// 公共方法：重新随机显示
        /// </summary>
        public void ReRandomizeDisplay()
        {
            ApplyRandomDisplay();
        }
    }
}
