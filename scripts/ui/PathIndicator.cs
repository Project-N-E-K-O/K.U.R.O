using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 路径指示器 - 显示路径方向指示
    /// </summary>
    public partial class PathIndicator : Control
    {
        [ExportCategory("UI References")]
        [Export] public Label PathLabel { get; private set; } = null!;
        [Export] public TextureRect ArrowIcon { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public bool ShowArrow = true;
        [Export] public string DefaultPathText = "前进方向";

        public override void _Ready()
        {
            if (PathLabel == null)
            {
                PathLabel = GetNodeOrNull<Label>("PathLabel");
            }

            if (ArrowIcon == null)
            {
                ArrowIcon = GetNodeOrNull<TextureRect>("ArrowIcon");
            }

            if (ArrowIcon != null)
            {
                ArrowIcon.Visible = ShowArrow;
            }
        }

        /// <summary>
        /// 设置路径方向
        /// </summary>
        public void SetPathDirection(Vector2 direction, string? pathName = null)
        {
            if (PathLabel != null)
            {
                PathLabel.Text = pathName ?? DefaultPathText;
            }

            if (ArrowIcon != null && ShowArrow)
            {
                // 计算箭头旋转角度
                float angle = direction.Angle();
                ArrowIcon.Rotation = angle;
                ArrowIcon.Visible = true;
            }
        }

        /// <summary>
        /// 显示路径指示
        /// </summary>
        public void ShowPath(string pathName)
        {
            Visible = true;
            if (PathLabel != null)
            {
                PathLabel.Text = pathName;
            }
        }

        /// <summary>
        /// 隐藏路径指示
        /// </summary>
        public void HidePath()
        {
            Visible = false;
        }
    }
}

