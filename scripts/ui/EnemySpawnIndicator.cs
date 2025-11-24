using Godot;

namespace Kuros.UI
{
    /// <summary>
    /// 敌人出场提示 - 显示敌人出现的信息
    /// </summary>
    public partial class EnemySpawnIndicator : Control
    {
        [ExportCategory("UI References")]
        [Export] public Label EnemyNameLabel { get; private set; } = null!;
        [Export] public Label SpawnTextLabel { get; private set; } = null!;
        [Export] public AnimationPlayer AnimationPlayer { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public float DisplayDuration = 2.0f;

        private Timer? _hideTimer;

        public override void _Ready()
        {
            if (EnemyNameLabel == null)
            {
                EnemyNameLabel = GetNodeOrNull<Label>("EnemyNameLabel");
            }

            if (SpawnTextLabel == null)
            {
                SpawnTextLabel = GetNodeOrNull<Label>("SpawnTextLabel");
            }

            if (AnimationPlayer == null)
            {
                AnimationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            }

            Visible = false;
        }

        /// <summary>
        /// 显示敌人出场提示
        /// </summary>
        public void ShowEnemySpawn(string enemyName, string? customText = null)
        {
            if (EnemyNameLabel != null)
            {
                EnemyNameLabel.Text = enemyName;
            }

            if (SpawnTextLabel != null)
            {
                SpawnTextLabel.Text = customText ?? "敌人出现！";
            }

            Visible = true;

            // 播放动画
            if (AnimationPlayer != null)
            {
                if (AnimationPlayer.HasAnimation("spawn"))
                {
                    AnimationPlayer.Play("spawn");
                }
            }

            // 设置自动隐藏
            if (_hideTimer != null)
            {
                _hideTimer.QueueFree();
            }

            _hideTimer = new Timer();
            _hideTimer.WaitTime = DisplayDuration;
            _hideTimer.OneShot = true;
            _hideTimer.Timeout += HideEnemySpawn;
            AddChild(_hideTimer);
            _hideTimer.Start();
        }

        /// <summary>
        /// 隐藏敌人出场提示
        /// </summary>
        public void HideEnemySpawn()
        {
            Visible = false;
            if (_hideTimer != null)
            {
                _hideTimer.QueueFree();
                _hideTimer = null;
            }
        }
    }
}

