using Godot;
using System;
using System.Collections.Generic;
using Kuros.Managers;
using Kuros.Systems.Stage;

namespace Kuros.UI;

public partial class ElevatorSelectionPanel : CanvasLayer
{
    public event Action<int>? Confirmed;
    public event Action? Cancelled;

    private const string Art = "res://assets/ui/elevator/";
    private readonly List<Card> _cards = new();
    private Control _panel = null!;
    private ScrollContainer _scroll = null!;
    private VBoxContainer _rows = null!;
    private int _selected;
    private PauseManager? _pauseOwner;

    private sealed record Card(Button Button, TextureRect Background, TextureRect Arrow,
        TextureRect ThumbnailBacking, TextureRect Frame, Label Floor);

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        var screen = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(screen);
        screen.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _panel = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        screen.AddChild(_panel);
        _panel.AnchorLeft = _panel.AnchorRight = 1;
        _panel.AnchorTop = _panel.AnchorBottom = 0.5f;
        _panel.OffsetLeft = -679;
        _panel.OffsetRight = -90;
        _panel.OffsetTop = -319;
        _panel.OffsetBottom = 319;
        Picture(_panel, "底图.png", 0, 0, 589, 638);
        Picture(_panel, "选择楼层牌子.png", 19, 19, 537, 96);
        Text(_panel, "选择楼层", 134, 32, 355, 36, 29, Colors.White);
        Text(_panel, "请选择要前往的楼层", 134, 69, 355, 26, 19, new Color("#dedbd0"));

        var close = new TextureButton { TextureNormal = GD.Load<Texture2D>(Art + "关闭按钮.png"),
            Position = new Vector2(523, 16), FocusMode = Control.FocusModeEnum.None,
            TooltipText = "关闭" };
        close.Pressed += () => Cancelled?.Invoke();
        _panel.AddChild(close);

        _scroll = new ScrollContainer
        {
            Position = new Vector2(27, 124),
            Size = new Vector2(527, 491),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        _panel.AddChild(_scroll);
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_rows);
        Close();
    }

    public void Open(IReadOnlyList<StagePlan> plans)
    {
        foreach (Node child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        _cards.Clear();
        for (int i = 0; i < plans.Count; i++)
            AddCard(plans[i], i);
        _scroll.ScrollVertical = 0;
        _panel.Show();
        Select(0);
        if (_pauseOwner == null && PauseManager.Instance is { } pause)
        {
            _pauseOwner = pause;
            pause.PushPause();
        }
        SetProcessInput(true);
    }

    public void Close()
    {
        _panel.Hide();
        SetProcessInput(false);
        ReleasePause();
    }

    public override void _ExitTree() => ReleasePause();

    private void ReleasePause()
    {
        var pause = _pauseOwner;
        _pauseOwner = null;
        if (IsInstanceValid(pause)) pause!.PopPause();
    }

    public override void _Input(InputEvent input)
    {
        if (!_panel.IsVisibleInTree() || _cards.Count == 0) return;
        if (input.IsActionPressed("ui_cancel"))
        {
            GetViewport().SetInputAsHandled();
            Cancelled?.Invoke();
        }
        else if (input.IsActionPressed("ui_accept"))
        {
            GetViewport().SetInputAsHandled();
            Confirmed?.Invoke(_selected);
        }
        else if (input.IsActionPressed("ui_up", true) || input.IsActionPressed("ui_down", true))
        {
            GetViewport().SetInputAsHandled();
            Select(Mathf.Clamp(_selected + (input.IsActionPressed("ui_up", true) ? -1 : 1), 0, _cards.Count - 1));
        }
        else if (input is InputEventKey { Pressed: true, Echo: false } key)
        {
            int index = (int)(key.Keycode - Key.Key1);
            if (index < 0 || index >= Math.Min(9, _cards.Count)) return;
            GetViewport().SetInputAsHandled();
            Select(index);
        }
    }

    private void AddCard(StagePlan plan, int index)
    {
        var button = new Button { Flat = true, CustomMinimumSize = new Vector2(0, 120),
            FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        _rows.AddChild(button);
        var background = Picture(button, "未选中-底图.png", 17, 2, 493, 117);
        var arrow = Picture(button, "选中框箭头.png", 0, 44, 33, 32);
        string floorName = plan.Config.IsHiddenFloor ? "?" :
            (plan.Config.Floor < 0 ? $"B{-plan.Config.Floor}" : $"{plan.Config.Floor}F");
        var floor = Text(button, floorName, 35, 33, 84, 54, 30, Colors.White);
        floor.HorizontalAlignment = HorizontalAlignment.Center;
        Text(button, plan.Config.DepartureLabel, 130, 21, 208, 33, 23, new Color("#25252d"));

        var route = new RichTextLabel
        {
            Position = new Vector2(130, 56), Size = new Vector2(206, 52),
            ScrollActive = false, MouseFilter = Control.MouseFilterEnum.Ignore
        };
        route.AddThemeColorOverride("default_color", new Color("#44434a"));
        route.AddThemeFontSizeOverride("normal_font_size", 15);
        button.AddChild(route);
        bool first = true;
        foreach (var (label, icon) in plan.GetElevatorPreview())
        {
            if (!first) route.AddText(" → ");
            first = false;
            if (icon != null && icon.GetWidth() > 0 && icon.GetHeight() > 0)
            {
                float scale = Mathf.Min(26f / icon.GetWidth(), 22f / icon.GetHeight());
                route.AddImage(icon, Mathf.Max(1, Mathf.RoundToInt(icon.GetWidth() * scale)),
                    Mathf.Max(1, Mathf.RoundToInt(icon.GetHeight() * scale)), altText: label);
            }
            else route.AddText(label);
        }
        button.TooltipText = plan.DescribeRoute();

        var backing = Picture(button, "未选中图片底图.png", 339, 17, 156, 85);
        var preview = plan.Config.IsHiddenFloor ? null : plan.Config.ElevatorPreviewTexture;
        if (preview != null)
        {
            var image = Picture(button, preview, 342, 20, 150, 79);
            image.StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered;
        }
        else
        {
            var placeholder = Text(button, "?", 339, 25, 156, 67, 34, new Color("#585860"));
            placeholder.HorizontalAlignment = HorizontalAlignment.Center;
        }
        var frame = Picture(button, "未选中图片勾线.png", 339, 17, 156, 85);
        _cards.Add(new Card(button, background, arrow, backing, frame, floor));
        button.MouseEntered += () => Select(index, reveal: false);
        button.Pressed += () => Confirmed?.Invoke(index);
    }

    private void Select(int index, bool reveal = true)
    {
        if (index < 0 || index >= _cards.Count) return;
        _selected = index;
        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            bool active = i == index;
            card.Background.Texture = GD.Load<Texture2D>(Art + (active ? "选中的框_底图.png" : "未选中-底图.png"));
            card.ThumbnailBacking.Texture = GD.Load<Texture2D>(Art + (active ? "选中框的图片底图.png" : "未选中图片底图.png"));
            card.Frame.Texture = GD.Load<Texture2D>(Art + (active ? "选中框的图片勾线.png" : "未选中图片勾线.png"));
            card.Arrow.Visible = active;
            card.Floor.AddThemeColorOverride("font_color", active ? new Color("#ffe77c") : Colors.White);
        }
        if (reveal)
            _scroll.CallDeferred(ScrollContainer.MethodName.EnsureControlVisible, _cards[index].Button);
    }

    private static TextureRect Picture(Control parent, string file, int x, int y, int w, int h)
        => Picture(parent, GD.Load<Texture2D>(Art + file), x, y, w, h);

    private static TextureRect Picture(Control parent, Texture2D texture, int x, int y, int w, int h)
    {
        var view = new TextureRect { Texture = texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            Position = new Vector2(x, y), Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        parent.AddChild(view);
        return view;
    }

    private static Label Text(Control parent, string text, int x, int y, int w, int h, int size, Color color)
    {
        var label = new Label { Text = text, Position = new Vector2(x, y), Size = new Vector2(w, h),
            VerticalAlignment = VerticalAlignment.Center, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = Control.MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        parent.AddChild(label);
        return label;
    }
}
