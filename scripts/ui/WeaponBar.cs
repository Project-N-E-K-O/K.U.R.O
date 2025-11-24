using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 武器栏UI - 显示当前装备的武器
    /// </summary>
    public partial class WeaponBar : Control
    {
        [ExportCategory("UI References")]
        [Export] public HBoxContainer WeaponContainer { get; private set; } = null!;
        [Export] public TextureRect CurrentWeaponIcon { get; private set; } = null!;
        [Export] public Label WeaponNameLabel { get; private set; } = null!;

        // 信号
        [Signal] public delegate void WeaponSelectedEventHandler(int weaponIndex);

        private int _currentWeaponIndex = 0;

        public override void _Ready()
        {
            if (WeaponContainer == null)
            {
                WeaponContainer = GetNodeOrNull<HBoxContainer>("WeaponContainer");
            }

            if (CurrentWeaponIcon == null)
            {
                CurrentWeaponIcon = GetNodeOrNull<TextureRect>("CurrentWeaponIcon");
            }

            if (WeaponNameLabel == null)
            {
                WeaponNameLabel = GetNodeOrNull<Label>("WeaponNameLabel");
            }
        }

        /// <summary>
        /// 设置当前武器
        /// </summary>
        public void SetCurrentWeapon(Texture2D? icon, string weaponName, int weaponIndex)
        {
            _currentWeaponIndex = weaponIndex;

            if (CurrentWeaponIcon != null)
            {
                CurrentWeaponIcon.Texture = icon;
                CurrentWeaponIcon.Visible = icon != null;
            }

            if (WeaponNameLabel != null)
            {
                WeaponNameLabel.Text = weaponName;
            }
        }

        /// <summary>
        /// 切换武器
        /// </summary>
        public void SwitchWeapon(int weaponIndex)
        {
            EmitSignal(SignalName.WeaponSelected, weaponIndex);
        }
    }
}

