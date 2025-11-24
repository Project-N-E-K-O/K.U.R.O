using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 血条组件 - 可复用的血条UI
    /// </summary>
    public partial class HealthBar : Control
    {
        [ExportCategory("UI References")]
        [Export] public TextureProgressBar HealthProgressBar { get; private set; } = null!;
        [Export] public Label HealthLabel { get; private set; } = null!;
        [Export] public Label NameLabel { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public string CharacterName { get; set; } = "角色";
        [Export] public bool ShowName = true;
        [Export] public bool ShowHealthText = true;

        private int _currentHealth = 100;
        private int _maxHealth = 100;

        public override void _Ready()
        {
            if (HealthProgressBar == null)
            {
                HealthProgressBar = GetNodeOrNull<TextureProgressBar>("HealthProgressBar");
            }

            if (HealthLabel == null)
            {
                HealthLabel = GetNodeOrNull<Label>("HealthLabel");
            }

            if (NameLabel == null)
            {
                NameLabel = GetNodeOrNull<Label>("NameLabel");
            }

            UpdateDisplay();
        }

        /// <summary>
        /// 设置角色名称
        /// </summary>
        public void SetCharacterName(string name)
        {
            CharacterName = name;
            if (NameLabel != null && ShowName)
            {
                NameLabel.Text = name;
                NameLabel.Visible = true;
            }
            else if (NameLabel != null)
            {
                NameLabel.Visible = false;
            }
        }

        /// <summary>
        /// 更新生命值
        /// </summary>
        public void UpdateHealth(int current, int max)
        {
            _currentHealth = Mathf.Clamp(current, 0, max);
            _maxHealth = max;
            UpdateDisplay();
        }

        private void UpdateDisplay()
        {
            // 更新进度条
            if (HealthProgressBar != null)
            {
                HealthProgressBar.MaxValue = _maxHealth;
                HealthProgressBar.Value = _currentHealth;
            }

            // 更新文本
            if (HealthLabel != null)
            {
                if (ShowHealthText)
                {
                    HealthLabel.Text = $"{_currentHealth}/{_maxHealth}";
                    HealthLabel.Visible = true;
                }
                else
                {
                    HealthLabel.Visible = false;
                }
            }
        }

        /// <summary>
        /// 设置血条颜色（通过进度条的tint）
        /// </summary>
        public void SetHealthColor(Color color)
        {
            if (HealthProgressBar != null)
            {
                HealthProgressBar.TintProgress = color;
            }
        }
    }
}

