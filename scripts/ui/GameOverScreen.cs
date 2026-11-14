using Godot;
using Kuros.Managers;

namespace Kuros.UI
{
    /// <summary>
    /// 死亡界面：玩家死亡时全屏弹出。GameOver 标题与两个按钮（重试/返回初始界面）
    /// 均为可替换贴图素材——贴图为空时用 StyleBoxFlat 占位（保证开箱可见可点）。
    /// </summary>
    [GlobalClass]
    public partial class GameOverScreen : Control
    {
        private const string TitleScreenPath = "res://scenes/ui/menus/TitleScreen.tscn";
        private const string HomeScenePath = "res://scenes/Stage_home.tscn";

        [ExportGroup("Title")]
        /// <summary>GameOver 标题贴图（可替换）。为空时隐藏 TextureRect，显示场景里的兜底 Label。</summary>
        [Export] public Texture2D? TitleTexture { get; set; }
        [Export] public NodePath? TitlePath { get; set; }
        /// <summary>兜底文字 Label 路径（有贴图时隐藏）。</summary>
        [Export] public NodePath? TitleLabelPath { get; set; } = new("TitleLabel");

        [ExportGroup("Retry Button")]
        [Export] public Texture2D? RetryNormal { get; set; }
        [Export] public Texture2D? RetryHover { get; set; }
        [Export] public Texture2D? RetryPressed { get; set; }
        [Export] public NodePath? RetryButtonPath { get; set; }

        [ExportGroup("Menu Button")]
        [Export] public Texture2D? MenuNormal { get; set; }
        [Export] public Texture2D? MenuHover { get; set; }
        [Export] public Texture2D? MenuPressed { get; set; }
        [Export] public NodePath? MenuButtonPath { get; set; }

        public override void _Ready()
        {
            ApplyTitle();

            var retry = ApplyButtonTexture(RetryButtonPath, RetryNormal, RetryHover, RetryPressed);
            var menu = ApplyButtonTexture(MenuButtonPath, MenuNormal, MenuHover, MenuPressed);
            if (retry != null) retry.Pressed += OnRetryPressed;
            if (menu != null) menu.Pressed += OnMenuPressed;
        }

        private void ApplyTitle()
        {
            var title = GetNodeOrNull<TextureRect>(TitlePath);
            var label = GetNodeOrNull<Label>(TitleLabelPath);

            // 贴图与兜底文字互斥：有贴图显示贴图、隐藏文字；无贴图反之
            if (TitleTexture != null)
            {
                if (title != null)
                {
                    title.Texture = TitleTexture;
                    title.Visible = true;
                }
                if (label != null) label.Visible = false;
            }
            else
            {
                if (title != null) title.Visible = false;
                if (label != null) label.Visible = true;
            }
        }

        /// <summary>应用按钮贴图（三态，缺省时回退 normal）；贴图为空时用 StyleBoxFlat 占位保证可见可点。</summary>
        private TextureButton? ApplyButtonTexture(NodePath? path, Texture2D? normal, Texture2D? hover, Texture2D? pressed)
        {
            if (path == null || path.IsEmpty) return null;
            var btn = GetNodeOrNull<TextureButton>(path);
            if (btn == null) return null;

            btn.TextureNormal = normal;
            btn.TextureHover = hover ?? normal;
            btn.TexturePressed = pressed ?? normal;

            if (normal == null)
            {
                var baseStyle = new StyleBoxFlat
                {
                    BgColor = new Color(0.16f, 0.16f, 0.22f, 0.95f),
                    CornerRadiusTopLeft = 8,
                    CornerRadiusTopRight = 8,
                    CornerRadiusBottomLeft = 8,
                    CornerRadiusBottomRight = 8,
                };
                var hoverStyle = (StyleBoxFlat)baseStyle.Duplicate();
                hoverStyle.BgColor = new Color(0.25f, 0.25f, 0.32f, 0.95f);
                var pressedStyle = (StyleBoxFlat)baseStyle.Duplicate();
                pressedStyle.BgColor = new Color(0.1f, 0.1f, 0.14f, 0.95f);

                btn.AddThemeStyleboxOverride("normal", baseStyle);
                btn.AddThemeStyleboxOverride("hover", hoverStyle);
                btn.AddThemeStyleboxOverride("pressed", pressedStyle);
            }
            return btn;
        }

        private void OnRetryPressed()
        {
            // UIManager 常驻根节点，切换场景不会销毁它——先卸载死亡界面再切换
            UIManager.Instance?.UnloadGameOverScreen();
            // 清空本局构筑/分数/核心选择与武器电量，从 Stage 1 从头开始
            BuildSelectionManager.Instance?.ResetForNewRun();
            WeaponBatteryManager.Instance?.ResetAll();
            FloatingDamageTextManager.Instance?.ClearAll(); // 清理 Root 级残留飘字
            GetTree().ChangeSceneToFile(HomeScenePath); // 重试：从第一关重新开始
        }

        private void OnMenuPressed()
        {
            UIManager.Instance?.UnloadGameOverScreen();
            // 返回标题同样清空本局构筑与武器电量，避免旧数据带入新游戏
            BuildSelectionManager.Instance?.ResetForNewRun();
            WeaponBatteryManager.Instance?.ResetAll();
            FloatingDamageTextManager.Instance?.ClearAll(); // 清理 Root 级残留飘字
            GetTree().ChangeSceneToFile(TitleScreenPath); // 返回初始界面（标题界面）
        }
    }
}
