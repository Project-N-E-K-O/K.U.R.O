using Godot;
using Kuros.Managers;

namespace Kuros.UI
{
    public enum SaveLoadMode { Save, Load }

    /// <summary>
    /// 存档槽位选择界面。
    ///
    /// 3 个槽位卡片，每张卡片显示：
    ///   有存档 → 存档名 + 游戏时间 + 通关次数 + 循环次数 + 保存时间 + 删除按钮
    ///   空槽位 → "存档槽位 N（空）" + "点击开始新游戏"
    ///
    /// 点击按钮 → 发射 SlotSelected 信号，由菜单决定如何使用该档案。
    /// 删除按钮 → ConfirmationDialog 二次确认 → SaveManager.DeleteSave()
    ///
    /// 按钮在 .tscn 中预置为 Button 子节点，C# 通过 GetNodeOrNull 查找。
    /// Signal 连接使用 Godot 原生 Connect（非 C# +=），导出构建更可靠。
    /// </summary>
    public partial class SaveSlotSelection : Control
    {
        [Export] public Button SlotButton0 { get; private set; } = null!;
        [Export] public Label SlotName0 { get; private set; } = null!;
        [Export] public Label SlotDetail0 { get; private set; } = null!;
        [Export] public Button SlotDelete0 { get; private set; } = null!;

        [Export] public Button SlotButton1 { get; private set; } = null!;
        [Export] public Label SlotName1 { get; private set; } = null!;
        [Export] public Label SlotDetail1 { get; private set; } = null!;
        [Export] public Button SlotDelete1 { get; private set; } = null!;

        [Export] public Button SlotButton2 { get; private set; } = null!;
        [Export] public Label SlotName2 { get; private set; } = null!;
        [Export] public Label SlotDetail2 { get; private set; } = null!;
        [Export] public Button SlotDelete2 { get; private set; } = null!;

        [Signal] public delegate void SlotSelectedEventHandler(int slotIndex);

        private Button[] _slotButtons = null!;
        private Label[] _slotNames = null!;
        private Label[] _slotDetails = null!;
        private Button[] _slotDeletes = null!;
        public bool SelectProfileOnly { get; set; }

        public override void _Ready()
        {
            ProcessMode = ProcessModeEnum.Always;
            ResolveExports();

            _slotButtons = new[] { SlotButton0, SlotButton1, SlotButton2 };
            _slotNames = new[] { SlotName0, SlotName1, SlotName2 };
            _slotDetails = new[] { SlotDetail0, SlotDetail1, SlotDetail2 };
            _slotDeletes = new[] { SlotDelete0, SlotDelete1, SlotDelete2 };

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var slotBtn = _slotButtons[i];
                if (slotBtn == null)
                {
                    GD.PrintErr($"SaveSlotSelection: SlotButton{idx} 为 null");
                    continue;
                }
                slotBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
                {
                    GD.Print($"SaveSlotSelection: 按钮 {idx} 被点击，即将发射 SlotSelected 信号");
                    EmitSignal(SignalName.SlotSelected, idx);
                }));

                var delBtn = _slotDeletes[i];
                if (delBtn != null)
                {
                    delBtn.Connect(Button.SignalName.Pressed, Callable.From(() =>
                    {
                        GD.Print($"SaveSlotSelection: 删除按钮 {idx} 被点击");
                        OnDeleteRequested(idx);
                    }));
                }
            }

            RefreshSlots();

            if (_slotButtons[0] != null)
                _slotButtons[0].GrabFocus();

            GD.Print($"SaveSlotSelection._Ready 完成，按钮数量: {_slotButtons.Length}");
        }

        private void ResolveExports()
        {
            SlotButton0 ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SlotGrid/SlotButton0");
            SlotName0 ??= GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SlotGrid/SlotButton0/VBox/SlotName0");
            SlotDetail0 ??= GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SlotGrid/SlotButton0/VBox/SlotDetail0");
            SlotDelete0 ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SlotGrid/SlotButton0/DeleteButton0");

            SlotButton1 ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SlotGrid/SlotButton1");
            SlotName1 ??= GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SlotGrid/SlotButton1/VBox/SlotName1");
            SlotDetail1 ??= GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SlotGrid/SlotButton1/VBox/SlotDetail1");
            SlotDelete1 ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SlotGrid/SlotButton1/DeleteButton1");

            SlotButton2 ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SlotGrid/SlotButton2");
            SlotName2 ??= GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SlotGrid/SlotButton2/VBox/SlotName2");
            SlotDetail2 ??= GetNodeOrNull<Label>("MenuPanel/VBoxContainer/SlotGrid/SlotButton2/VBox/SlotDetail2");
            SlotDelete2 ??= GetNodeOrNull<Button>("MenuPanel/VBoxContainer/SlotGrid/SlotButton2/DeleteButton2");
        }

        public void RefreshSlots()
        {
            for (int i = 0; i < 3; i++)
            {
                var data = GetSlotData(i);
                PopulateSlot(i, data);
            }
        }

        private SaveSlotData GetSlotData(int slotIndex)
        {
            if (SaveManager.Instance == null)
                return new SaveSlotData { SlotIndex = slotIndex, HasSave = false };

            var d = SaveManager.Instance.GetSaveSlotData(slotIndex);
            return new SaveSlotData
            {
                SlotIndex = d.SlotIndex,
                HasSave = d.HasSave,
                SaveTime = d.SaveTime,
                PlayTimeSeconds = ParsePlayTime(d.PlayTime),
                ClearCount = d.ClearCount,
                CycleCount = d.CycleCount,
            };
        }

        private static int ParsePlayTime(string formatted)
        {
            if (string.IsNullOrEmpty(formatted)) return 0;
            var parts = formatted.Split(':');
            if (parts.Length != 3) return 0;
            int.TryParse(parts[0], out int h);
            int.TryParse(parts[1], out int m);
            int.TryParse(parts[2], out int s);
            return h * 3600 + m * 60 + s;
        }

        private void PopulateSlot(int i, SaveSlotData data)
        {
            if (data.HasSave)
            {
                _slotNames[i].Text = $"存档 {i + 1}";
                _slotDetails[i].Text =
                    $"游戏时间  {FormatPlayTime(data.PlayTimeSeconds)}\n" +
                    $"通关次数  {data.ClearCount}\n" +
                    $"循环次数  {data.CycleCount}\n" +
                    $"保存时间  {data.SaveTime}";
                _slotDeletes[i].Visible = true;
            }
            else
            {
                _slotNames[i].Text = $"存档槽位 {i + 1}（空）";
                _slotDetails[i].Text = SelectProfileOnly ? "创建并选择档案" : "点击开始新游戏";
                _slotDeletes[i].Visible = false;
            }
        }

        private void OnDeleteRequested(int slotIndex)
        {
            var dialog = new ConfirmationDialog();
            dialog.Title = "确认删除";
            dialog.DialogText = $"确定要删除存档槽位 {slotIndex + 1} 吗？此操作不可撤销。";
            dialog.GetOkButton()?.Set("text", "删除");
            dialog.Confirmed += () =>
            {
                SaveManager.Instance?.DeleteSave(slotIndex);
                RefreshSlots();
                dialog.QueueFree();
            };
            dialog.Canceled += dialog.QueueFree;
            AddChild(dialog);
            dialog.PopupCentered();
        }

        private static string FormatPlayTime(int seconds)
        {
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            return $"{h:D2}:{m:D2}:{s:D2}";
        }

        // ── v1 遗留 stub（BattleSceneManager 仍引用）──
        [Signal] public delegate void ModeSwitchRequestedEventHandler(int newMode);
        [Signal] public delegate void BackRequestedEventHandler();
        public SaveLoadMode Mode { get; set; } = SaveLoadMode.Load;
        public bool AllowSave { get; set; } = false;
        public bool FromBattleMenu { get; set; } = false;
        public void SetMode(SaveLoadMode _) { }
        public void SetAllowSave(bool _) { }
        public void SetSource(bool _) { }

        public override void _Input(InputEvent @event)
        {
            if (!IsVisibleInTree()) return;
            bool escape = @event is InputEventKey { Pressed: true, Echo: false } key
                && (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape);
            // 档案选择中 Enter 确认焦点槽位，Esc 返回。项目里的 ui_cancel 仍绑定 Enter。
            if (!escape && (SelectProfileOnly || !@event.IsActionPressed("ui_cancel"))) return;

            GetViewport().SetInputAsHandled();
            if (SelectProfileOnly) EmitSignal(SignalName.BackRequested);
        }
    }

    public class SaveSlotData
    {
        public int SlotIndex;
        public bool HasSave;
        public string SaveTime = "";
        public int PlayTimeSeconds;
        public int ClearCount;
        public int CycleCount;
    }
}
