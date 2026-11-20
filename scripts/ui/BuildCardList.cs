using System.Collections.Generic;
using Godot;
using Kuros.Systems;

namespace Kuros.UI;

public partial class BuildCardList : ScrollContainer
{
    internal void Display(IEnumerable<RunCard> cards, string emptyText)
    {
        var list = GetNode<VBoxContainer>("Cards");
        foreach (var child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }

        foreach (var card in cards) list.AddChild(CreateCard(card));
        if (list.GetChildCount() == 0)
            list.AddChild(new Label { Text = emptyText, HorizontalAlignment = HorizontalAlignment.Center });
        ScrollVertical = 0;
    }

    private static Control CreateCard(RunCard card)
    {
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.16f, 0.19f, 0.25f), ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 14, ContentMarginBottom = 14,
            CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8, CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 16);
        panel.AddChild(row);
        row.AddChild(new TextureRect
        {
            Texture = LoadIcon(card.IconPath), CustomMinimumSize = new Vector2(84, 84),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        });
        var content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        content.AddThemeConstantOverride("separation", 6);
        row.AddChild(content);
        var title = new Label
        {
            Text = $"{card.Name}  ·  {card.Stacks}层" + (card.Active ? "" : "  ·  已替换"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        title.AddThemeFontSizeOverride("font_size", 23);
        title.AddThemeColorOverride("font_color", card.Rarity switch
        {
            BuildRarity.Common => new Color(0.51f, 0.76f, 1f),
            BuildRarity.Rare => new Color(0.79f, 0.63f, 1f),
            BuildRarity.Epic => new Color(1f, 0.85f, 0.3f),
            _ => Colors.White,
        });
        content.AddChild(title);
        content.AddChild(new RichTextLabel
        {
            Text = card.Description, BbcodeEnabled = true, FitContent = true, ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        if (!card.Active) panel.Modulate = new Color(0.75f, 0.75f, 0.75f);
        return panel;
    }

    internal static Texture2D? LoadIcon(string path) =>
        !string.IsNullOrEmpty(path) && ResourceLoader.Exists(path, "Texture2D") ? GD.Load<Texture2D>(path) : null;
}
