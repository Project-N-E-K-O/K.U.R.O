using Godot;

namespace Kuros.Utils
{
    /// <summary>
    /// 通用玩家靠近高亮脚本。
    /// 挂在任意节点上，当玩家进入指定 Area2D 时将目标 Sprite2D 的 outline_color
    /// shader 参数切换为高亮色，离开后恢复默认色。
    ///
    /// 使用方式：
    ///   1. 将此脚本挂到场景内任意节点。
    ///   2. 在 Inspector 中设置 DetectionAreaPath（检测玩家的 Area2D）。
    ///   3. 设置 OutlineSpritePath（带 sprite_outline shader 的 Sprite2D）。
    ///   4. 按需调整 DefaultOutlineColor / HighlightOutlineColor。
    /// </summary>
    [GlobalClass]
    public partial class ProximityOutlineHighlight : Node
    {
        [ExportCategory("References")]
        /// <summary>检测玩家进出的 Area2D 路径（collision_mask 需包含玩家层）。</summary>
        [Export] public NodePath DetectionAreaPath { get; set; } = new NodePath("../InteractArea");

        /// <summary>带 sprite_outline ShaderMaterial 的 Sprite2D 路径。</summary>
        [Export] public NodePath OutlineSpritePath { get; set; } = new NodePath("../Environments/Outline");

        [ExportCategory("Colors")]
        /// <summary>默认描边颜色（通常 alpha=0 即透明）。</summary>
        [Export] public Color DefaultOutlineColor   { get; set; } = new Color(0.02f, 0.02f, 0.02f, 0f);

        /// <summary>玩家靠近后的描边颜色。</summary>
        [Export] public Color HighlightOutlineColor { get; set; } = new Color(1f, 1f, 1f, 1f);

        // ── 内部 ──────────────────────────────────────────────────

        private Area2D?         _area;
        private ShaderMaterial? _material;

        public override void _Ready()
        {
            _area = GetNodeOrNull<Area2D>(DetectionAreaPath);
            if (_area == null)
            {
                GD.PushWarning($"[ProximityOutlineHighlight] 未找到 DetectionArea，路径：{DetectionAreaPath}");
                return;
            }

            var sprite = GetNodeOrNull<Sprite2D>(OutlineSpritePath);
            if (sprite?.Material is ShaderMaterial sm)
            {
                // Duplicate 确保每个实例拥有独立材质，互不干扰
                _material = sm.Duplicate() as ShaderMaterial;
                if (_material != null)
                {
                    _material.ResourceLocalToScene = true;
                    sprite.Material = _material;
                    _material.SetShaderParameter("outline_color", DefaultOutlineColor);
                }
            }
            else
            {
                GD.PushWarning($"[ProximityOutlineHighlight] Sprite2D 未找到或无 ShaderMaterial，路径：{OutlineSpritePath}");
            }

            _area.BodyEntered += OnBodyEntered;
            _area.BodyExited  += OnBodyExited;
        }

        public override void _ExitTree()
        {
            if (_area == null) return;
            _area.BodyEntered -= OnBodyEntered;
            _area.BodyExited  -= OnBodyExited;
        }

        // ── 事件 ──────────────────────────────────────────────────

        private void OnBodyEntered(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            _material?.SetShaderParameter("outline_color", HighlightOutlineColor);
        }

        private void OnBodyExited(Node2D body)
        {
            if (!body.IsInGroup("player")) return;
            _material?.SetShaderParameter("outline_color", DefaultOutlineColor);
        }
    }
}
