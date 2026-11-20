using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kuros.Items;
using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.UI;

public partial class RunHistoryApp : Control
{
    private const int PageSize = 20;
    private static readonly Color ClearedColor = new(0.65f, 0.87f, 0.63f);
    private static readonly Color NeutralColor = new(0.76f, 0.80f, 0.86f);

    private VBoxContainer _entries = null!;
    private HBoxContainer _pages = null!;
    private Label _pageLabel = null!;
    private BuildCardList _cards = null!;
    private ScrollContainer _stats = null!;
    private Label _status = null!;
    private Label _elapsed = null!;
    private Label _best = null!;
    private Label _gold = null!;
    private HFlowContainer _items = null!;
    private IReadOnlyList<RunRecord> _records = Array.Empty<RunRecord>();
    private int _page;
    private int? _bestSeconds;

    public override void _Ready()
    {
        const string columns = "Panel/Content/Columns/";
        var history = GetNode<VBoxContainer>(columns + "History/Body");
        _entries = history.GetNode<VBoxContainer>("Scroll/Entries");
        _pages = history.GetNode<HBoxContainer>("Pages");
        _pageLabel = _pages.GetNode<Label>("PageLabel");
        _pages.GetNode<Button>("Previous").Pressed += () => SetPage(_page - 1);
        _pages.GetNode<Button>("Next").Pressed += () => SetPage(_page + 1);
        _cards = GetNode<BuildCardList>(columns + "Build/Body/BuildCardList");
        _stats = GetNode<ScrollContainer>(columns + "Stats/Scroll");
        var stats = _stats.GetNode<VBoxContainer>("Sections");
        _status = stats.GetNode<Label>("Result");
        _elapsed = stats.GetNode<Label>("Elapsed/Value");
        _best = stats.GetNode<Label>("Best/Value");
        _gold = stats.GetNode<Label>("Gold/Value");
        _items = stats.GetNode<HFlowContainer>("Items");
    }

    internal void Refresh()
    {
        _records = SaveManager.Instance?.CurrentGameData?.ReadRunHistory().Reverse().ToArray() ?? Array.Empty<RunRecord>();
        _bestSeconds = _records.Where(record => record.Outcome == RunOutcome.Cleared).Select(record => (int?)record.Seconds).Min();
        SetPage(0);
    }

    private void SetPage(int page)
    {
        int pages = Math.Max(1, (_records.Count + PageSize - 1) / PageSize);
        _page = Math.Clamp(page, 0, pages - 1);
        _pages.Visible = pages > 1;
        _pages.GetNode<Button>("Previous").Disabled = _page == 0;
        _pages.GetNode<Button>("Next").Disabled = _page == pages - 1;
        _pageLabel.Text = $"{_page + 1} / {pages}";
        ClearChildren(_entries);
        var group = new ButtonGroup();
        int start = _page * PageSize;
        foreach (var record in _records.Skip(start).Take(PageSize))
        {
            var button = new Button
            {
                Text = $"{DateTimeOffset.FromUnixTimeSeconds(record.FinishedAt).ToLocalTime():MM-dd HH:mm}   {Status(record)}",
                ToggleMode = true, ButtonGroup = group, Alignment = HorizontalAlignment.Left,
                CustomMinimumSize = new Vector2(0, 56),
            };
            button.AddThemeColorOverride("font_color", record.Outcome == RunOutcome.Cleared ? ClearedColor : NeutralColor);
            button.Pressed += () => ShowRecord(record);
            _entries.AddChild(button);
        }
        if (_entries.GetChildCount() > 0)
            _entries.GetChild<Button>(0).SetPressedNoSignal(true);
        else
            _entries.AddChild(new Label { Text = "暂无结算记录" });
        ShowRecord(_records.ElementAtOrDefault(start));
    }

    private void ShowRecord(RunRecord? record)
    {
        _cards.Display(record?.Cards ?? Array.Empty<RunCard>(), "暂无卡牌记录");
        _status.Text = record == null ? "—" : Status(record);
        _status.AddThemeColorOverride("font_color", record?.Outcome == RunOutcome.Cleared ? ClearedColor : NeutralColor);
        _elapsed.Text = FormatDuration(record?.Seconds);
        _best.Text = FormatDuration(_bestSeconds);
        _gold.Text = record?.Gold.ToString() ?? "—";
        _stats.ScrollVertical = 0;
        ClearChildren(_items);
        if (record == null || record.Items.Count == 0)
        {
            _items.AddChild(new Label { Text = "暂无道具记录" });
            return;
        }

        foreach (var item in record.Items)
        {
            var icon = BuildCardList.LoadIcon(item.IconPath);
            if (icon == null && !string.IsNullOrEmpty(item.DefinitionPath) && ResourceLoader.Exists(item.DefinitionPath))
                icon = GD.Load<ItemDefinition>(item.DefinitionPath)?.Icon;
            var tile = new VBoxContainer { TooltipText = $"{item.Name} ×{item.Quantity}" };
            if (icon != null)
                tile.AddChild(new TextureRect { Texture = icon, CustomMinimumSize = new Vector2(56, 56),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    MouseFilter = MouseFilterEnum.Pass });
            tile.AddChild(new Label { Text = icon == null ? $"{item.Name} ×{item.Quantity}" : $"×{item.Quantity}",
                HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Pass });
            _items.AddChild(tile);
        }
    }

    private static void ClearChildren(Node parent)
    {
        foreach (var child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static string Status(RunRecord record) => record.Outcome == RunOutcome.Cleared ? "通关" : "未通关";

    private static string FormatDuration(int? seconds) => seconds is { } value ? $"{value / 60:00}:{value % 60:00}" : "—";
}
