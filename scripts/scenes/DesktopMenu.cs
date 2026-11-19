using Godot;
using Kuros.Managers;
using Kuros.UI;
using Kuros.Utils;

namespace Kuros.Scenes;

public partial class DesktopMenu : Control
{
    public const string ScenePath = "res://scenes/ui/menus/DesktopMenu.tscn";

    [Signal] public delegate void ComputerClosedEventHandler();

    [Export] public PackedScene SaveSlotsScene { get; set; } = null!;
    [Export] public PackedScene SettingsScene { get; set; } = null!;
    [Export] public PackedScene CompendiumScene { get; set; } = null!;

    [Export] public bool IsInGameComputer { get; set; }
    internal bool IsHomeEntry { get; set; }

    private Control _desktop = null!;
    private Control _appWindows = null!;
    private Button _start = null!;
    private Button _settingsButton = null!;
    private Button _compendiumButton = null!;
    private Button _closeProfile = null!;
    private Label _profileLabel = null!;
    private Label _profileError = null!;
    private Control? _activeApp;
    private Button? _appShortcut;
    private SaveSlotSelection? _saveSlots;
    private SettingsMenu? _settings;
    private CompendiumWindow? _compendium;
    private bool _enteringGame;

    public override void _Ready()
    {
        if (!IsInGameComputer)
        {
            UIManager.Instance?.ClearAllUI();
            DialogicUtils.CleanupPersistentState(this);
        }

        _desktop = GetNode<Control>("Desktop");
        _appWindows = GetNode<Control>("AppWindows");
        _profileLabel = GetNode<Label>("Desktop/Profile");
        _profileError = GetNode<Label>("ProfileError");
        _closeProfile = GetNode<Button>("CloseProfile");
        _closeProfile.Pressed += CloseProfiles;

        const string apps = "Desktop/Apps/";
        const string taskbarActions = "Desktop/Taskbar/Actions/";
        _start = GetNode<Button>(taskbarActions + "StartGame");
        _compendiumButton = GetNode<Button>(apps + "Compendium");
        _compendiumButton.Pressed += OpenCompendium;
        _settingsButton = GetNode<Button>(apps + "Settings");
        _settingsButton.Pressed += () => OpenSettings(_settingsButton);
        var returnButton = GetNode<Button>(taskbarActions + "ReturnToTitle");
        var quitButton = GetNode<Button>(taskbarActions + "Quit");
        if (IsInGameComputer)
            _start.Pressed += CloseComputer;
        else
            _start.Pressed += EnterHome;

        if (IsInGameComputer && !IsHomeEntry)
        {
            returnButton.Hide();
            quitButton.Hide();
            _start.Text = "返回游戏";
            _start.Icon = returnButton.Icon;
            _start.IconAlignment = HorizontalAlignment.Left;
            _start.CustomMinimumSize = new Vector2(164f, _start.CustomMinimumSize.Y);
            _start.TooltipText = "关闭电脑并返回当前游戏";
            _start.AccessibilityName = "返回游戏";
        }
        else
        {
            returnButton.Pressed += ReturnToTitle;
            var quit = GetNode<ConfirmationDialog>("QuitConfirmation");
            quitButton.Pressed += () => quit.PopupCentered();
            quit.Confirmed += () => GetTree().Quit();
            quit.WindowInput += input =>
            {
                if (!IsEscapePressed(input)) return;
                quit.SetInputAsHandled();
                quit.Hide();
                quitButton.GrabFocus();
            };
        }

        RefreshDesktop();
        if (_start.Disabled)
            OpenProfiles();
        else
        {
            _start.GrabFocus();
            // 从结算或暂停菜单返回桌面时可能已有当前档案。
            if (!IsInGameComputer) Callable.From(EnterHome).CallDeferred();
        }
    }

    public override void _Input(InputEvent input)
    {
        // 设置、图鉴和档案选择各自处理 Esc，包括取消设置中的改键捕获。
        if (_enteringGame || _activeApp != null || !IsVisibleInTree() || !IsEscapePressed(input)) return;

        GetViewport().SetInputAsHandled();
        if (IsInGameComputer) CloseComputer();
        else ReturnToTitle();
    }

    internal static bool IsEscapePressed(InputEvent input) =>
        input is InputEventKey { Pressed: true, Echo: false } key
        && (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape);

    private void RefreshDesktop()
    {
        var profile = SaveManager.Instance?.CurrentGameData;
        _start.Disabled = !IsInGameComputer && profile == null;
        _compendiumButton.Disabled = profile == null;
        _profileLabel.Visible = profile != null;
        if (profile != null) _profileLabel.Text = $"当前档案  {profile.SlotIndex + 1}";
    }

    internal void FocusComputerCloseButton() => _start.GrabFocus();

    private void CloseComputer() => EmitSignal(SignalName.ComputerClosed);

    private void OpenProfiles()
    {
        if (_saveSlots == null)
        {
            _saveSlots = SaveSlotsScene.Instantiate<SaveSlotSelection>();
            _saveSlots.SelectProfileOnly = true;
            _saveSlots.SlotSelected += SelectProfile;
            _saveSlots.BackRequested += CloseProfiles;
            AddApp(_saveSlots);
        }
        OpenApp(_saveSlots);
        _closeProfile.Show();
        _saveSlots.RefreshSlots();
        _saveSlots.SlotButton0?.GrabFocus();
    }

    private void CloseProfiles()
    {
        // 未选择档案时，关闭选择器会回到标题页。
        if (SaveManager.Instance?.CurrentGameData == null)
        {
            ReturnToTitle();
            return;
        }
        CloseApp();
    }

    private void ReturnToTitle()
    {
        if ((IsInGameComputer && !IsHomeEntry) || _enteringGame) return;

        var error = GetTree().ChangeSceneToFile(TitleScreen.ScenePath);
        if (error == Error.Ok)
            SaveManager.Instance?.ClearCurrentGameData();
        else
            GD.PushError($"[DesktopMenu] 无法返回标题页：{error}");
    }

    private void SelectProfile(int slotIndex)
    {
        var saves = SaveManager.Instance;
        if (_enteringGame || saves == null) return;

        if (saves.HasSave(slotIndex))
        {
            var profile = saves.LoadGame(slotIndex);
            if (profile == null)
            {
                ShowProfileError("读取档案失败");
                return;
            }
            saves.SetCurrentGameData(profile);
        }
        else if (!saves.NewGame(slotIndex))
        {
            ShowProfileError("创建档案失败");
            return;
        }

        CloseApp();
        EnterHome();
    }

    private void ShowProfileError(string message)
    {
        _profileError.Text = message;
        _profileError.Show();
    }

    private void OpenSettings(Button shortcut)
    {
        if (_settings == null)
        {
            _settings = SettingsScene.Instantiate<SettingsMenu>();
            _settings.BackRequested += CloseApp;
            AddApp(_settings);
        }
        OpenApp(_settings, shortcut);
        _settings.GetNodeOrNull<Button>("MenuPanel/VBoxContainer/BackButton")?.GrabFocus();
    }

    private void OpenCompendium()
    {
        if (_compendium == null)
        {
            _compendium = CompendiumScene.Instantiate<CompendiumWindow>();
            AddApp(_compendium);
            _compendium.CompendiumClosed += CloseApp;
        }
        OpenApp(_compendium, _compendiumButton);
        _compendium.ShowWindow();
        _compendium.CloseButton?.GrabFocus();
    }

    private void AddApp(Control app)
    {
        app.Hide();
        _appWindows.AddChild(app);
    }

    private void OpenApp(Control app, Button? shortcut = null)
    {
        _desktop.Hide();
        _activeApp = app;
        _appShortcut = shortcut;
        app.Show();
    }

    private void CloseApp()
    {
        _activeApp?.Hide();
        _activeApp = null;
        _closeProfile.Hide();
        _profileError.Hide();
        _desktop.Show();
        RefreshDesktop();

        if (_appShortcut is { Disabled: false } shortcut)
            shortcut.GrabFocus();
        else
            _settingsButton.GrabFocus();
        _appShortcut = null;
    }

    private void EnterHome()
    {
        if (IsInGameComputer || _enteringGame || !IsInsideTree() || SaveManager.Instance?.CurrentGameData == null)
            return;

        _enteringGame = true;
        var error = StageHomeArrivalController.EnterFromDesktop(GetTree());
        if (error == Error.Ok) return;

        GD.PushError($"[DesktopMenu] 无法进入 Stage_home：{error}");
        _enteringGame = false;
        OpenProfiles();
        ShowProfileError("无法进入住宅区，请重试");
    }
}
