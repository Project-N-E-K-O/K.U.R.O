using Godot;
using System.Collections.Generic;

namespace Kuros.UI
{
    /// <summary>
    /// 图鉴系统窗口 - 当前包含怪物与武器两个分类，均使用占位数据。
    /// </summary>
    public partial class CompendiumWindow : Control
    {
        private const int GridColumns = 4;
        private const string PlaceholderText = "我是测试文本";

        [Signal] public delegate void CompendiumClosedEventHandler();

        [ExportCategory("UI References")]
        [Export] public Button CloseButton { get; private set; } = null!;

        // 怪物图鉴引用
        [Export] public GridContainer MonsterGrid { get; private set; } = null!;
        [Export] public Label MonsterName { get; private set; } = null!;
        [Export] public TextureRect MonsterImage { get; private set; } = null!;
        [Export] public RichTextLabel MonsterDescription { get; private set; } = null!;
        [Export] public Label MonsterHpValue { get; private set; } = null!;
        [Export] public Label MonsterDefenseValue { get; private set; } = null!;
        [Export] public Label MonsterSpeedValue { get; private set; } = null!;
        [Export] public Label MonsterAttackValue { get; private set; } = null!;
        [Export] public Label MonsterRangeValue { get; private set; } = null!;
        [Export] public RichTextLabel MonsterSkillDescription { get; private set; } = null!;

        // NPC图鉴引用
        [Export] public GridContainer NpcGrid { get; private set; } = null!;
        [Export] public Label NpcName { get; private set; } = null!;
        [Export] public TextureRect NpcImage { get; private set; } = null!;
        [Export] public Label NpcHeartIcon { get; private set; } = null!;
        [Export] public RichTextLabel NpcDescriptionText { get; private set; } = null!;
        [Export] public Label NpcAffectionValue { get; private set; } = null!;
        [Export] public RichTextLabel NpcQuestDescription { get; private set; } = null!;

        // 武器图鉴引用
        [Export] public GridContainer WeaponGrid { get; private set; } = null!;
        [Export] public Label WeaponName { get; private set; } = null!;
        [Export] public TextureRect WeaponImage { get; private set; } = null!;
        [Export] public RichTextLabel WeaponDescriptionText { get; private set; } = null!;
        [Export] public Label WeaponAttackValue { get; private set; } = null!;
        [Export] public Label WeaponElementValue { get; private set; } = null!;
        [Export] public Label WeaponWeightValue { get; private set; } = null!;
        [Export] public Label WeaponRarityValue { get; private set; } = null!;
        [Export] public Label WeaponSpecialValue { get; private set; } = null!;
        [Export] public RichTextLabel WeaponEffectDescription { get; private set; } = null!;

        private readonly List<MonsterEntry> _monsterEntries = new();
        private readonly Dictionary<MonsterEntry, Button> _monsterButtons = new();
        private MonsterEntry? _currentMonsterSelection;

        private readonly List<NpcEntry> _npcEntries = new();
        private readonly Dictionary<NpcEntry, Button> _npcButtons = new();
        private NpcEntry? _currentNpcSelection;

        private readonly List<WeaponEntry> _weaponEntries = new();
        private readonly Dictionary<WeaponEntry, Button> _weaponButtons = new();
        private WeaponEntry? _currentWeaponSelection;

        public override void _Ready()
        {
            CacheNodeReferences();

            BuildMonsterEntries();
            PopulateMonsterGrid();

            BuildNpcEntries();
            PopulateNpcGrid();

            BuildWeaponEntries();
            PopulateWeaponGrid();

            if (_monsterEntries.Count > 0)
                SelectMonster(_monsterEntries[0]);
            else
                ClearMonsterPanel();

            if (_npcEntries.Count > 0)
                SelectNpc(_npcEntries[0]);
            else
                ClearNpcPanel();

            if (_weaponEntries.Count > 0)
                SelectWeapon(_weaponEntries[0]);
            else
                ClearWeaponPanel();

            HideWindow();
        }

        private void CacheNodeReferences()
        {
            CloseButton ??= GetNodeOrNull<Button>("MainPanel/RootMargin/RootVBox/Header/CloseButton");

            MonsterGrid ??= GetNodeOrNull<GridContainer>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterLeftPanel/MonsterLeftVBox/MonsterScroll/MonsterGrid");
            MonsterName ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/MonsterName");
            MonsterImage ??= GetNodeOrNull<TextureRect>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/MonsterImage");
            MonsterDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/DescriptionSection/DescriptionScroll/DescriptionText");
            MonsterHpValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/HpValue");
            MonsterDefenseValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/DefenseValue");
            MonsterSpeedValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/SpeedValue");
            MonsterAttackValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/AttackValue");
            MonsterRangeValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/StatsSection/StatsGrid/RangeValue");
            MonsterSkillDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/MonsterTab/MonsterBody/MonsterRightPanel/MonsterRightVBox/SkillSection/SkillScroll/SkillDescription");

            NpcGrid ??= GetNodeOrNull<GridContainer>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCLeftPanel/NPCLeftVBox/NPCScroll/NPCGrid");
            NpcName ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCName");
            NpcImage ??= GetNodeOrNull<TextureRect>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCImageWrapper/NPCImage");
            NpcHeartIcon ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCImageWrapper/NPCHearthIcon");
            NpcDescriptionText ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCDescriptionSection/NPCDescriptionScroll/NPCDescriptionText");
            NpcAffectionValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCAffectionSection/NPCAffectionValue");
            NpcQuestDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/NPCTab/NPCBody/NPCRightPanel/NPCRightVBox/NPCQuestSection/NPCQuestScroll/NPCQuestDescription");

            WeaponGrid ??= GetNodeOrNull<GridContainer>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponLeftPanel/WeaponLeftVBox/WeaponScroll/WeaponGrid");
            WeaponName ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponName");
            WeaponImage ??= GetNodeOrNull<TextureRect>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponImage");
            WeaponDescriptionText ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponDescriptionSection/WeaponDescriptionScroll/WeaponDescriptionText");
            WeaponAttackValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponAttackValue");
            WeaponElementValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponElementValue");
            WeaponWeightValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponWeightValue");
            WeaponRarityValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponRarityValue");
            WeaponSpecialValue ??= GetNodeOrNull<Label>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponStatsSection/WeaponStatsGrid/WeaponSpecialValue");
            WeaponEffectDescription ??= GetNodeOrNull<RichTextLabel>("MainPanel/RootMargin/RootVBox/Tabs/WeaponTab/WeaponBody/WeaponRightPanel/WeaponRightVBox/WeaponEffectSection/WeaponEffectScroll/WeaponEffectDescription");

            if (CloseButton != null)
            {
                CloseButton.Pressed += HideWindow;
            }
        }

        public void ShowWindow()
        {
            Visible = true;
            SetProcessInput(true);
        }

        public void HideWindow()
        {
            if (!Visible)
            {
                SetProcessInput(false);
                return;
            }

            Visible = false;
            SetProcessInput(false);
            EmitSignal(SignalName.CompendiumClosed);
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!Visible)
            {
                return;
            }

            if (@event.IsActionPressed("ui_cancel"))
            {
                HideWindow();
                GetViewport().SetInputAsHandled();
            }
        }

        private void BuildMonsterEntries()
        {
            _monsterEntries.Clear();
            for (int i = 0; i < 16; i++)
            {
                _monsterEntries.Add(new MonsterEntry(
                    $"monster_placeholder_{i}",
                    PlaceholderText,
                    null,
                    PlaceholderText,
                    new MonsterStats(PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText),
                    PlaceholderText));
            }
        }

        private void PopulateMonsterGrid()
        {
            if (MonsterGrid == null)
            {
                GD.PrintErr("MonsterGrid 缺失，无法构建怪物列表。");
                return;
            }

            foreach (Node child in MonsterGrid.GetChildren())
            {
                child.QueueFree();
            }

            MonsterGrid.Columns = GridColumns;
            _monsterButtons.Clear();

            foreach (var entry in _monsterEntries)
            {
                var button = CreateMonsterButton(entry);
                MonsterGrid.AddChild(button);
                _monsterButtons[entry] = button;
            }
        }

        private Button CreateMonsterButton(MonsterEntry entry)
        {
            var button = new Button
            {
                Text = entry.Name,
                CustomMinimumSize = new Vector2(160, 140),
                ToggleMode = true,
                TooltipText = $"{entry.Name}\n点击查看详情"
            };

            button.AddThemeFontSizeOverride("font_size", 18);
            button.Pressed += () => SelectMonster(entry);
            return button;
        }

        private void SelectMonster(MonsterEntry entry)
        {
            _currentMonsterSelection = entry;

            foreach (var pair in _monsterButtons)
            {
                pair.Value.ButtonPressed = pair.Key == entry;
            }

            UpdateMonsterPanel(entry);
        }

        private void UpdateMonsterPanel(MonsterEntry entry)
        {
            if (MonsterName != null)
                MonsterName.Text = entry.Name;

            if (MonsterImage != null)
                MonsterImage.Texture = entry.Texture;

            if (MonsterDescription != null)
                MonsterDescription.Text = entry.Description;

            if (MonsterHpValue != null)
                MonsterHpValue.Text = entry.Stats.Hp;

            if (MonsterDefenseValue != null)
                MonsterDefenseValue.Text = entry.Stats.Defense;

            if (MonsterSpeedValue != null)
                MonsterSpeedValue.Text = entry.Stats.Speed;

            if (MonsterAttackValue != null)
                MonsterAttackValue.Text = entry.Stats.Attack;

            if (MonsterRangeValue != null)
                MonsterRangeValue.Text = entry.Stats.AttackRange;

            if (MonsterSkillDescription != null)
                MonsterSkillDescription.Text = entry.SkillDescription;
        }

        private void ClearMonsterPanel()
        {
            if (MonsterName != null) MonsterName.Text = PlaceholderText;
            if (MonsterImage != null) MonsterImage.Texture = null;
            if (MonsterDescription != null) MonsterDescription.Text = PlaceholderText;
            if (MonsterHpValue != null) MonsterHpValue.Text = PlaceholderText;
            if (MonsterDefenseValue != null) MonsterDefenseValue.Text = PlaceholderText;
            if (MonsterSpeedValue != null) MonsterSpeedValue.Text = PlaceholderText;
            if (MonsterAttackValue != null) MonsterAttackValue.Text = PlaceholderText;
            if (MonsterRangeValue != null) MonsterRangeValue.Text = PlaceholderText;
            if (MonsterSkillDescription != null) MonsterSkillDescription.Text = PlaceholderText;
        }

        private void BuildNpcEntries()
        {
            _npcEntries.Clear();
            for (int i = 0; i < 16; i++)
            {
                _npcEntries.Add(new NpcEntry(
                    $"npc_placeholder_{i}",
                    PlaceholderText,
                    null,
                    PlaceholderText,
                    PlaceholderText,
                    PlaceholderText));
            }
        }

        private void BuildWeaponEntries()
        {
            _weaponEntries.Clear();
            for (int i = 0; i < 16; i++)
            {
                _weaponEntries.Add(new WeaponEntry(
                    $"weapon_placeholder_{i}",
                    PlaceholderText,
                    null,
                    PlaceholderText,
                    new WeaponStats(PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText, PlaceholderText),
                    PlaceholderText));
            }
        }

        private void PopulateNpcGrid()
        {
            if (NpcGrid == null)
            {
                GD.PrintErr("NPCGrid 缺失，无法构建 NPC 列表。");
                return;
            }

            foreach (Node child in NpcGrid.GetChildren())
            {
                child.QueueFree();
            }

            NpcGrid.Columns = GridColumns;
            _npcButtons.Clear();

            foreach (var entry in _npcEntries)
            {
                var button = CreateNpcButton(entry);
                NpcGrid.AddChild(button);
                _npcButtons[entry] = button;
            }
        }

        private void PopulateWeaponGrid()
        {
            if (WeaponGrid == null)
            {
                GD.PrintErr("WeaponGrid 缺失，无法构建武器列表。");
                return;
            }

            foreach (Node child in WeaponGrid.GetChildren())
            {
                child.QueueFree();
            }

            WeaponGrid.Columns = GridColumns;
            _weaponButtons.Clear();

            foreach (var entry in _weaponEntries)
            {
                var button = CreateWeaponButton(entry);
                WeaponGrid.AddChild(button);
                _weaponButtons[entry] = button;
            }
        }

        private Button CreateNpcButton(NpcEntry entry)
        {
            var button = new Button
            {
                Text = entry.Name,
                CustomMinimumSize = new Vector2(160, 140),
                ToggleMode = true,
                TooltipText = $"{entry.Name}\n点击查看详情"
            };

            button.AddThemeFontSizeOverride("font_size", 18);
            button.Pressed += () => SelectNpc(entry);
            return button;
        }

        private Button CreateWeaponButton(WeaponEntry entry)
        {
            var button = new Button
            {
                Text = entry.Name,
                CustomMinimumSize = new Vector2(160, 140),
                ToggleMode = true,
                TooltipText = $"{entry.Name}\n点击查看详情"
            };

            button.AddThemeFontSizeOverride("font_size", 18);
            button.Pressed += () => SelectWeapon(entry);
            return button;
        }

        private void SelectNpc(NpcEntry entry)
        {
            _currentNpcSelection = entry;

            foreach (var pair in _npcButtons)
            {
                pair.Value.ButtonPressed = pair.Key == entry;
            }

            UpdateNpcPanel(entry);
        }

        private void SelectWeapon(WeaponEntry entry)
        {
            _currentWeaponSelection = entry;

            foreach (var pair in _weaponButtons)
            {
                pair.Value.ButtonPressed = pair.Key == entry;
            }

            UpdateWeaponPanel(entry);
        }

        private void UpdateNpcPanel(NpcEntry entry)
        {
            if (NpcName != null)
                NpcName.Text = entry.Name;

            if (NpcImage != null)
                NpcImage.Texture = entry.Texture;

            if (NpcDescriptionText != null)
                NpcDescriptionText.Text = entry.Description;

            if (NpcAffectionValue != null)
                NpcAffectionValue.Text = entry.AffectionLevel;

            if (NpcQuestDescription != null)
                NpcQuestDescription.Text = entry.QuestInfo;

            if (NpcHeartIcon != null)
                NpcHeartIcon.Text = entry.HeartSymbol;
        }

        private void UpdateWeaponPanel(WeaponEntry entry)
        {
            if (WeaponName != null)
                WeaponName.Text = entry.Name;

            if (WeaponImage != null)
                WeaponImage.Texture = entry.Texture;

            if (WeaponDescriptionText != null)
                WeaponDescriptionText.Text = entry.Description;

            if (WeaponAttackValue != null)
                WeaponAttackValue.Text = entry.Stats.Attack;

            if (WeaponElementValue != null)
                WeaponElementValue.Text = entry.Stats.Element;

            if (WeaponWeightValue != null)
                WeaponWeightValue.Text = entry.Stats.Weight;

            if (WeaponRarityValue != null)
                WeaponRarityValue.Text = entry.Stats.Rarity;

            if (WeaponSpecialValue != null)
                WeaponSpecialValue.Text = entry.Stats.SpecialEffect;

            if (WeaponEffectDescription != null)
                WeaponEffectDescription.Text = entry.SkillDescription;
        }

        private void ClearNpcPanel()
        {
            if (NpcName != null) NpcName.Text = PlaceholderText;
            if (NpcImage != null) NpcImage.Texture = null;
            if (NpcHeartIcon != null) NpcHeartIcon.Text = "♥";
            if (NpcDescriptionText != null) NpcDescriptionText.Text = PlaceholderText;
            if (NpcAffectionValue != null) NpcAffectionValue.Text = PlaceholderText;
            if (NpcQuestDescription != null) NpcQuestDescription.Text = PlaceholderText;
        }

        private void ClearWeaponPanel()
        {
            if (WeaponName != null) WeaponName.Text = PlaceholderText;
            if (WeaponImage != null) WeaponImage.Texture = null;
            if (WeaponDescriptionText != null) WeaponDescriptionText.Text = PlaceholderText;
            if (WeaponAttackValue != null) WeaponAttackValue.Text = PlaceholderText;
            if (WeaponElementValue != null) WeaponElementValue.Text = PlaceholderText;
            if (WeaponWeightValue != null) WeaponWeightValue.Text = PlaceholderText;
            if (WeaponRarityValue != null) WeaponRarityValue.Text = PlaceholderText;
            if (WeaponSpecialValue != null) WeaponSpecialValue.Text = PlaceholderText;
            if (WeaponEffectDescription != null) WeaponEffectDescription.Text = PlaceholderText;
        }

        private sealed class MonsterEntry
        {
            public string Id { get; }
            public string Name { get; }
            public Texture2D? Texture { get; }
            public string Description { get; }
            public MonsterStats Stats { get; }
            public string SkillDescription { get; }

            public MonsterEntry(string id, string name, Texture2D? texture, string description, MonsterStats stats, string skillDescription)
            {
                Id = id;
                Name = name;
                Texture = texture;
                Description = description;
                Stats = stats;
                SkillDescription = skillDescription;
            }
        }

        private readonly struct MonsterStats
        {
            public string Hp { get; }
            public string Defense { get; }
            public string Speed { get; }
            public string Attack { get; }
            public string AttackRange { get; }

            public MonsterStats(string hp, string defense, string speed, string attack, string attackRange)
            {
                Hp = hp;
                Defense = defense;
                Speed = speed;
                Attack = attack;
                AttackRange = attackRange;
            }
        }

        private sealed class WeaponEntry
        {
            public string Id { get; }
            public string Name { get; }
            public Texture2D? Texture { get; }
            public string Description { get; }
            public WeaponStats Stats { get; }
            public string SkillDescription { get; }

            public WeaponEntry(string id, string name, Texture2D? texture, string description, WeaponStats stats, string skillDescription)
            {
                Id = id;
                Name = name;
                Texture = texture;
                Description = description;
                Stats = stats;
                SkillDescription = skillDescription;
            }
        }

        private readonly struct WeaponStats
        {
            public string Attack { get; }
            public string Element { get; }
            public string Weight { get; }
            public string Rarity { get; }
            public string SpecialEffect { get; }

            public WeaponStats(string attack, string element, string weight, string rarity, string specialEffect)
            {
                Attack = attack;
                Element = element;
                Weight = weight;
                Rarity = rarity;
                SpecialEffect = specialEffect;
            }
        }

        private sealed class NpcEntry
        {
            public string Id { get; }
            public string Name { get; }
            public Texture2D? Texture { get; }
            public string Description { get; }
            public string AffectionLevel { get; }
            public string QuestInfo { get; }
            public string HeartSymbol { get; }

            public NpcEntry(string id, string name, Texture2D? texture, string description, string affectionLevel, string questInfo, string heartSymbol = "♥")
            {
                Id = id;
                Name = name;
                Texture = texture;
                Description = description;
                AffectionLevel = affectionLevel;
                QuestInfo = questInfo;
                HeartSymbol = heartSymbol;
            }
        }
    }
}

