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
        /// 所有LitterPart节点列表
        /// </summary>
        private List<Node2D> _allLitterParts = new();

        /// <summary>
        /// LargeSmoke节点引用（始终显示）
        /// </summary>
        private Node2D? _largeSmoke;

        public override void _Ready()
        {
            FindAllLitterParts();
            FindLargeSmoke();
            ApplyRandomDisplay();
        }

        /// <summary>
        /// 查找LargeSmoke节点
        /// </summary>
        private void FindLargeSmoke()
        {
            _largeSmoke = GetNode<Node2D>("LargeSmoke");
            if (_largeSmoke != null)
            {
                _largeSmoke.Visible = true;
            }
        }

        /// <summary>
        /// 查找所有LitterPart节点
        /// </summary>
        private void FindAllLitterParts()
        {
            _allLitterParts.Clear();
            
            foreach (var child in GetChildren())
            {
                if (child is Node2D node && child.Name.ToString().StartsWith("LitterPart"))
                {
                    _allLitterParts.Add(node);
                }
            }
        }

        /// <summary>
        /// 应用随机显示逻辑
        /// </summary>
        private void ApplyRandomDisplay()
        {
            // 隐藏所有LitterPart
            foreach (var part in _allLitterParts)
            {
                part.Visible = false;
            }

            // 确保LargeSmoke始终显示
            if (_largeSmoke != null)
            {
                _largeSmoke.Visible = true;
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

            // 显示随机选择的节点
            foreach (var node in toDisplay)
            {
                node.Visible = true;
            }

            var displayNames = string.Join(", ", toDisplay.Select(n => n.Name));
            GD.Print($"RandomLitterPartDisplay: 随机显示 {displayCountActual} 项 - {displayNames}");
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
