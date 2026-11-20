using Godot;
using Kuros.Managers;
using Kuros.Systems;
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
    [Export] public PackedScene RunHistoryScene { get; set; } = null!;

    [Export] public bool IsInGameComputer { get; set; }
    internal bool IsHomeEntry { get; set; }
    internal HomeDesktopPage InitialPage { get; set; }

    private Control _desktop = null!;
    private Control _appWindows = null!;
    private Control _actionMenu = null!;
    private Button _startButton = null!;
    private Button _start = null!;
    private Button _cancelAction = null!;
    private Button _settingsButton = null!;
    private Button _compendiumButton = null!;
    private Button _runHistoryButton = null!;
    private Button _closeProfile = null!;
    private Label _profileLabel = null!;
    private Label _profileError = null!;
    private Control? _activeApp;
    private Button? _appShortcut;
    private SaveSlotSelection? _saveSlots;
    private SettingsMenu? _settings;
    private CompendiumWindow? _compendium;
    private RunHistoryApp? _runHistory;
    private Button? _runHistoryCloseButton;
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
        const string actions = "Content/ActionBand/Actions/";
        _actionMenu = GetNode<Control>("Desktop/ActionMenu");
        _startButton = GetNode<Button>("Desktop/Taskbar/StartButton");
        _startButton.Pressed += ToggleActionMenu;
        _start = _actionMenu.GetNode<Button>(actions + "StartGame");
        _cancelAction = _actionMenu.GetNode<Button>("Content/Cancel");
        _cancelAction.Pressed += () => CloseActionMenu();
        _compendiumButton = GetNode<Button>(apps + "Compendium");
        _compendiumButton.Pressed += OpenCompendium;
        _runHistoryButton = GetNode<Button>(apps + "RunHistory");
        _runHistoryButton.Pressed += OpenRunHistory;
        _settingsButton = GetNode<Button>(apps + "Settings");
        _settingsButton.Pressed += () => OpenSettings(_settingsButton);
        var returnButton = _actionMenu.GetNode<Button>(actions + "ReturnToTitle");
        var quitButton = _actionMenu.GetNode<Button>(actions + "Quit");
        _start.Pressed += () =>
        {
            CloseActionMenu();
            if (IsInGameComputer) CloseComputer();
            else EnterHome();
        };

        if (IsInGameComputer && !IsHomeEntry)
        {
            returnButton.Hide();
            quitButton.Hide();
            _start.Icon = returnButton.Icon;
            _start.TooltipText = "关闭电脑并返回当前游戏";
            _start.AccessibilityName = "返回游戏";
        }
        else
        {
            if (IsHomeEntry && InitialPage == HomeDesktopPage.RunHistory)
            {
                _start.TooltipText = string.Empty;
                _start.AccessibilityName = "返回住宅区";
            }
            returnButton.Pressed += () =>
            {
                CloseActionMenu();
                ReturnToTitle();
            };
            var quit = GetNode<ConfirmationDialog>("QuitConfirmation");
            quitButton.Pressed += () =>
            {
                CloseActionMenu();
                quit.PopupCentered();
            };
            quit.Canceled += _startButton.GrabFocus;
            quit.Confirmed += () => GetTree().Quit();
            quit.WindowInput += input =>
            {
                if (!IsEscapePressed(input)) return;
                quit.SetInputAsHandled();
                quit.Hide();
                _startButton.GrabFocus();
            };
        }

        RefreshDesktop();
        if (_start.Disabled)
            OpenProfiles();
        else if (IsInGameComputer && InitialPage == HomeDesktopPage.RunHistory)
            OpenRunHistory();
        else
        {
            _startButton.GrabFocus();
            // 从结算或暂停菜单返回桌面时可能已有当前档案。
            if (!IsInGameComputer) Callable.From(EnterHome).CallDeferred();
        }
    }

    public override void _Input(InputEvent input)
    {
        if (_enteringGame || !IsVisibleInTree()) return;

        if (_actionMenu.IsVisibleInTree() && (IsEscapePressed(input) || IsOutsideActionMenuClick(input)))
        {
            GetViewport().SetInputAsHandled();
            CloseActionMenu();
            return;
        }

        // 设置、图鉴和档案选择各自处理 Esc，包括取消设置中的改键捕获。
        if (!IsEscapePressed(input)) return;
        if (_activeApp != null)
        {
            if (_activeApp == _runHistory)
            {
                GetViewport().SetInputAsHandled();
                CloseApp();
            }
            return;
        }

        GetViewport().SetInputAsHandled();
        if (IsInGameComputer) CloseComputer();
        else ReturnToTitle();
    }

    internal static bool IsEscapePressed(InputEvent input) =>
        input is InputEventKey { Pressed: true, Echo: false } key
        && (key.Keycode == Key.Escape || key.PhysicalKeycode == Key.Escape);

    private bool IsOutsideActionMenuClick(InputEvent input)
    {
        if (input is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            return false;

        var position = GetGlobalMousePosition();
        return !_actionMenu.GetGlobalRect().HasPoint(position) && !_startButton.GetGlobalRect().HasPoint(position);
    }

    private void ToggleActionMenu()
    {
        if (_actionMenu.Visible)
        {
            CloseActionMenu();
            return;
        }

        _actionMenu.Show();
        _startButton.SetPressedNoSignal(true);
        (_start.Disabled ? _cancelAction : _start).GrabFocus();
    }

    private void CloseActionMenu(bool restoreFocus = true)
    {
        _actionMenu.Hide();
        _startButton.SetPressedNoSignal(false);
        if (restoreFocus) _startButton.GrabFocus();
    }

    private void RefreshDesktop()
    {
        var profile = SaveManager.Instance?.CurrentGameData;
        _start.Disabled = !IsInGameComputer && profile == null;
        _compendiumButton.Disabled = profile == null;
        _profileLabel.Visible = profile != null;
        if (profile != null) _profileLabel.Text = $"当前档案  {profile.SlotIndex + 1}";
    }

    internal void FocusComputerCloseButton()
    {
        if (_activeApp != null && _activeApp == _runHistory)
            _runHistoryCloseButton?.GrabFocus();
        else
            _startButton.GrabFocus();
    }

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
        {
            RunStateReset.AfterSceneChange();
            SaveManager.Instance?.ClearCurrentGameData();
        }
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

    private void OpenRunHistory()
    {
        if (_runHistory == null)
        {
            _runHistory = RunHistoryScene.Instantiate<RunHistoryApp>();
            _runHistoryCloseButton = _runHistory.GetNode<Button>("Panel/Content/Header/CloseButton");
            _runHistoryCloseButton.Pressed += CloseApp;
            AddApp(_runHistory);
        }
        OpenApp(_runHistory, _runHistoryButton);
        _runHistory.Refresh();
        _runHistoryCloseButton?.GrabFocus();
    }

    private void AddApp(Control app)
    {
        app.Hide();
        _appWindows.AddChild(app);
    }

    private void OpenApp(Control app, Button? shortcut = null)
    {
        CloseActionMenu(restoreFocus: false);
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
