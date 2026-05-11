using Godot;
using System;
using System.Collections.Generic;

namespace Kuros.UI
{
    /// <summary>
    /// 敌人生成配置。包含敌人数量和特殊选项。
    /// </summary>
    public class EnemySpawnConfig
    {
        public Dictionary<int, int> Requests { get; set; } = new();
        public bool DisableAI { get; set; }
        public bool DisableLoot { get; set; }
    }

    /// <summary>
    /// 敌人生成控制台弹窗。
    /// 显示可生成的敌人列表（名称 + 数量选择器），点击"确认生成"后通知控制台执行生成。
    /// </summary>
    public partial class EnemySpawnConsoleWindow : Control
    {
        [ExportCategory("UI References")]
        [Export] public Button? CloseButton { get; private set; }
        [Export] public Button? CancelButton { get; private set; }
        [Export] public Button? ConfirmButton { get; private set; }
        [Export] public VBoxContainer? EnemyListContainer { get; private set; }
        [Export] public CheckBox? DisableAICheckBox { get; private set; }
        [Export] public Button? KillAllEnemiesButton { get; private set; }

        [ExportCategory("Spawn Settings")]
        /// <summary>最大敌人生成总数（所有敌人的数量和不能超过此值，0表示无限制）。</summary>
        [Export(PropertyHint.Range, "0,1000,1")] public int MaxEnemyCount { get; set; } = 0;

        /// <summary>
        /// 玩家点击"确认生成"后触发。
        /// 发送敌人数量请求和生成配置（AI/掉落选项）。
        /// </summary>
        public event Action<EnemySpawnConfig>? SpawnConfirmed;

        private bool _isOpen = false;

        /// <summary>
        /// 获取窗口是否开启
        /// </summary>
        public bool IsOpen => _isOpen;

        private readonly List<SpinBox> _spinBoxes = new();
        private List<PackedScene> _enemyScenes = new();
        private Area2D? _testArea;  // TestArea 的引用

        public override void _Ready()
        {
            base._Ready();
            ProcessMode = ProcessModeEnum.Always;
            CacheNodes();
            ConnectButtons();
            Visible = false;
            SetProcessInput(false);
            SetProcessUnhandledInput(false);
            _isOpen = false;
        }

        public override void _Input(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: true))
            {
                return;
            }
        }

        public override void _GuiInput(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: true, useSetInputAsHandled: false))
            {
                return;
            }
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            // 检查窗口是否打开
            if (!Visible || !_isOpen) return;

            if (TryHandleCloseInput(@event, useAcceptEvent: false, useSetInputAsHandled: true))
            {
                return;
            }
        }

        /// <summary>
        /// 尝试处理关闭窗口的输入（ESC键或物品栏键）
        /// </summary>
        /// <param name="event">输入事件</param>
        /// <param name="useAcceptEvent">是否调用AcceptEvent</param>
        /// <param name="useSetInputAsHandled">是否调用SetInputAsHandled</param>
        /// <returns>如果输入被处理返回true，否则返回false</returns>
        private bool TryHandleCloseInput(InputEvent @event, bool useAcceptEvent, bool useSetInputAsHandled)
        {
            var itemPopup = Kuros.Managers.UIManager.Instance?.GetUI<ItemObtainedPopup>("ItemObtainedPopup");
            if (itemPopup != null && itemPopup.Visible)
            {
                return false;
            }

            bool isEscKey = @event.IsActionPressed("ui_cancel") ||
                (@event is InputEventKey keyEvent && keyEvent.Pressed && keyEvent.Keycode == Key.Escape);

            if (isEscKey)
            {
                CloseWindow();
                if (useSetInputAsHandled) GetViewport().SetInputAsHandled();
                if (useAcceptEvent) AcceptEvent();
                GD.Print($"[EnemySpawnConsoleWindow] ESC 键按下，窗口关闭");
                return true;
            }
            return false;
        }

        // ── 公共接口 ──────────────────────────────────────────────

        /// <summary>
        /// 以给定的敌人场景列表填充列表并显示窗口。
        /// </summary>
        public void ShowWindow(List<PackedScene> scenes)
        {
            if (_isOpen) return;

            _enemyScenes = scenes;
            PopulateEnemyList();
            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            SetProcessUnhandledInput(true);
            _isOpen = true;
            
            // 请求暂停游戏（参考 SkillDetailWindow）
            if (Kuros.Managers.PauseManager.Instance != null)
            {
                Kuros.Managers.PauseManager.Instance.PushPause();
            }
            
            GD.Print($"[EnemySpawnConsoleWindow] 窗口打开，游戏已暂停");
        }

        /// <summary>
        /// 关闭窗口（不触发生成）。
        /// </summary>
        public void CloseWindow()
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            SetProcessUnhandledInput(false);
            _isOpen = false;
            
            // 取消暂停请求（参考 SkillDetailWindow）
            if (Kuros.Managers.PauseManager.Instance != null)
            {
                Kuros.Managers.PauseManager.Instance.PopPause();
            }
            
            GD.Print($"[EnemySpawnConsoleWindow] 窗口关闭，游戏已恢复");
        }

        /// <summary>
        /// 设置 TestArea 的引用（供 EnemySpawnConsole 调用）。
        /// </summary>
        public void SetTestArea(Area2D? testArea)
        {
            _testArea = testArea;
        }

        // ── 初始化 ────────────────────────────────────────────────

        private void CacheNodes()
        {
            CloseButton   ??= GetNodeOrNull<Button>("MainPanel/Header/CloseButton");
            CancelButton  ??= GetNodeOrNull<Button>("MainPanel/Footer/CancelButton");
            ConfirmButton ??= GetNodeOrNull<Button>("MainPanel/Footer/ConfirmButton");
            EnemyListContainer ??= GetNodeOrNull<VBoxContainer>(
                "MainPanel/Body/EnemyListScroll/EnemyListContainer");
            DisableAICheckBox ??= GetNodeOrNull<CheckBox>("MainPanel/Footer/DisableAICheckBox");
            KillAllEnemiesButton ??= GetNodeOrNull<Button>("MainPanel/Footer/KillAllEnemiesButton");
        }

        private void ConnectButtons()
        {
            ConnectButton(CloseButton,   nameof(CloseWindow));
            ConnectButton(CancelButton,  nameof(CloseWindow));
            ConnectButton(ConfirmButton, nameof(OnConfirmPressed));
            ConnectButton(KillAllEnemiesButton, nameof(OnKillAllEnemiesPressed));
        }

        private void ConnectButton(Button? button, string methodName)
        {
            if (button == null) return;
            var callable = new Callable(this, methodName);
            if (!button.IsConnected(Button.SignalName.Pressed, callable))
                button.Connect(Button.SignalName.Pressed, callable);
        }

        // ── 列表填充 ──────────────────────────────────────────────

        private void PopulateEnemyList()
        {
            if (EnemyListContainer == null) return;

            // 清空旧行
            foreach (Node child in EnemyListContainer.GetChildren())
                child.QueueFree();
            _spinBoxes.Clear();

            if (_enemyScenes.Count == 0)
            {
                var emptyLabel = new Label();
                emptyLabel.Text = "未配置可生成的敌人，请在 Inspector 中设置 EnemyScenes。";
                emptyLabel.AddThemeFontSizeOverride("font_size", 16);
                emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
                EnemyListContainer.AddChild(emptyLabel);
                return;
            }

            for (int i = 0; i < _enemyScenes.Count; i++)
            {
                var scene = _enemyScenes[i];
                if (scene == null) continue;

                var row = BuildEnemyRow(scene, i);
                EnemyListContainer.AddChild(row);
            }
        }

        private HBoxContainer BuildEnemyRow(PackedScene scene, int index)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);

            // 序号
            var indexLabel = new Label();
            indexLabel.Text = $"{index + 1}.";
            indexLabel.CustomMinimumSize = new Vector2(28, 0);
            indexLabel.VerticalAlignment = VerticalAlignment.Center;
            indexLabel.AddThemeFontSizeOverride("font_size", 16);

            // 敌人名称
            var nameLabel = new Label();
            nameLabel.Text = GetEnemyDisplayName(scene);
            nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            nameLabel.VerticalAlignment = VerticalAlignment.Center;
            nameLabel.AddThemeFontSizeOverride("font_size", 18);

            // "数量:" 标签
            var countLabel = new Label();
            countLabel.Text = "数量:";
            countLabel.VerticalAlignment = VerticalAlignment.Center;
            countLabel.AddThemeFontSizeOverride("font_size", 16);

            // 数量选择器
            var spinBox = new SpinBox();
            spinBox.MinValue = 0;
            spinBox.MaxValue = 99;
            spinBox.Value = 0;
            spinBox.Step = 1;
            spinBox.CustomArrowStep = 1;
            spinBox.AllowGreater = false;
            spinBox.AllowLesser = false;
            spinBox.CustomMinimumSize = new Vector2(110, 0);

            // 当SpinBox值改变时，检查总数是否超过最大值
            spinBox.ValueChanged += (value) => OnSpinBoxValueChanged(spinBox);

            row.AddChild(indexLabel);
            row.AddChild(nameLabel);
            row.AddChild(countLabel);
            row.AddChild(spinBox);

            _spinBoxes.Add(spinBox);
            return row;
        }

        // ── 敌人数量限制 ────────────────────────────────────────

        /// <summary>
        /// 当任意SpinBox的值改变时，检查总数是否超过MaxEnemyCount。
        /// 如果超过，则将当前SpinBox的值限制为允许的最大值。
        /// </summary>
        private void OnSpinBoxValueChanged(SpinBox changedSpinBox)
        {
            if (MaxEnemyCount <= 0)
                return; // 无限制

            int totalCount = 0;
            for (int i = 0; i < _spinBoxes.Count; i++)
            {
                totalCount += (int)_spinBoxes[i].Value;
            }

            if (totalCount > MaxEnemyCount)
            {
                // 超过限制，回退当前SpinBox的值
                int excess = totalCount - MaxEnemyCount;
                int currentValue = (int)changedSpinBox.Value;
                changedSpinBox.Value = Mathf.Max(0, currentValue - excess);
            }
        }

        // ── 确认生成 ──────────────────────────────────────────────

        private void OnConfirmPressed()
        {
            var requests = new Dictionary<int, int>();
            int totalCount = 0;
            for (int i = 0; i < _spinBoxes.Count; i++)
            {
                int count = (int)_spinBoxes[i].Value;
                if (count > 0)
                {
                    requests[i] = count;
                    totalCount += count;
                }
            }

            // 验证总数是否超过最大值
            if (MaxEnemyCount > 0 && totalCount > MaxEnemyCount)
            {
                GD.PushWarning($"[EnemySpawnConsoleWindow] Total enemy count ({totalCount}) exceeds maximum ({MaxEnemyCount}). Spawn cancelled.");
                return;
            }

            if (requests.Count > 0)
            {
                var config = new EnemySpawnConfig
                {
                    Requests = requests,
                    DisableAI = DisableAICheckBox?.ButtonPressed ?? false,
                    DisableLoot = true  // 固定为 true，禁用掉落物品
                };
                SpawnConfirmed?.Invoke(config);
            }

            CloseWindow();
        }

        /// <summary>
        /// 消灭 TestArea 内的所有敌人。
        /// </summary>
        private void OnKillAllEnemiesPressed()
        {
            if (_testArea == null || !GodotObject.IsInstanceValid(_testArea))
            {
                GD.PushWarning("[EnemySpawnConsoleWindow] TestArea not available, cannot kill enemies");
                return;
            }

            var enemies = GetTree().GetNodesInGroup("enemies");
            int killCount = 0;
            foreach (var enemyNode in enemies)
            {
                if (!GodotObject.IsInstanceValid(enemyNode)) continue;

                if (enemyNode is Kuros.Core.GameActor gameActor)
                {
                    gameActor.TakeDamage(9999, gameActor.GlobalPosition, null);
                    killCount++;
                }
                else if (enemyNode is Node2D node2D)
                {
                    node2D.QueueFree();
                    killCount++;
                }
            }

            GD.Print($"[EnemySpawnConsoleWindow] Killed {killCount} enemies");
        }

        // ── 工具方法 ──────────────────────────────────────────────

        private static string GetEnemyDisplayName(PackedScene scene)
        {
            var path = scene.ResourcePath;
            var lastSlash = path.LastIndexOf('/');
            var fileName = lastSlash >= 0 ? path[(lastSlash + 1)..] : path;
            var dot = fileName.LastIndexOf('.');
            return dot >= 0 ? fileName[..dot] : fileName;
        }
    }
}
