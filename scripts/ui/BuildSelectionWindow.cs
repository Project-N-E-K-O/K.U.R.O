using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Managers;
using Kuros.Systems;

namespace Kuros.UI
{
    public partial class BuildSelectionWindow : Control
    {
        /// <summary>键盘焦点域：卡片选择区 / 操作栏按钮区（上下切换，左右在域内移动）。</summary>
        private enum FocusDomain { Cards, Buttons }

        [Export] public PackedScene? CardTemplate { get; set; }
        [Export] public Control? CardContainer { get; set; }
        [Export] public Button? ConfirmButton { get; set; }
        [Export] public Button? RefreshButton { get; set; }
        [Export] public Button? SkipButton { get; set; }

        /// <summary>卡牌固定宽度（像素）：不随并排数量变化——视口/文本换行宽度恒定；数量多时自动重叠。</summary>
        [Export(PropertyHint.Range, "200,800,10")]
        public float CardWidth = 400f;

        /// <summary>数量多时的最大重叠比例（0~0.9）：后卡最多覆盖前卡右侧 此比例 的宽度（保证组宽不超容器；0.85 = 大量卡牌时接近堆叠）。</summary>
        [Export(PropertyHint.Range, "0,0.9,0.05")]
        public float MaxOverlapRatio = 0.85f;

        private List<BuildCard> _cards = new();
        private List<BuildEffectDefinition> _options = new();
        private Action<BuildEffectDefinition>? _onConfirmed;
        private IReadOnlyDictionary<string, int>? _stacksByEffectId;
        private Action? _onSkipped;
        private int _skipReward;
        private Func<ICollection<string>, List<BuildEffectDefinition>?>? _rerollProvider; // 传入当前显示卡 EffectId，返回新一批（null/空 = 不可刷新）
        private Func<BuildEffectDefinition, BuildPickCardContext?>? _pickContextProvider; // 候选卡族上下文(反向取代标注),每次填充卡面实时查询
        private int _rerollBaseCost;
        private float _rerollCostGrowth;
        private int _freeRerollCount; // 本窗口可用的免费刷新次数（含局外养成加成）
        private int _rerollCount; // 本窗口已刷新次数
        private Texture2D? _refreshIcon; // 刷新按钮的付费金币 icon（免费时隐藏）
        private int _selectedIndex;
        private bool _isOpen;
        private int _pauseCountOnOpen; // 窗口打开时的暂停计数：其他系统（如拾取弹窗）可能已持有暂停，不能以全局计数>1判断覆盖层
        private bool _cardsEntering; // 卡牌进入动画期间：禁用鼠标点击（MouseFilter Ignore）与键盘选择/确认
        private FocusDomain _focusDomain = FocusDomain.Cards;
        private int _buttonIndex; // 按钮区当前选中索引
        private readonly List<Button> _actionButtons = new(); // 可见操作栏按钮（顺序：刷新、确认、跳过）

        public override void _Ready()
        {
            ResolveExports();
            Visible = false;
            ProcessMode = ProcessModeEnum.Always;
        }

        private void ResolveExports()
        {
            CardContainer ??= GetNodeOrNull<Control>("Cards");
            ConfirmButton ??= GetNodeOrNull<Button>("ActionBar/ConfirmButton");
            if (ConfirmButton != null)
                ConfirmButton.Pressed += () => ConfirmSelection(_selectedIndex);
            RefreshButton ??= GetNodeOrNull<Button>("ActionBar/RefreshButton");
            if (RefreshButton != null)
                RefreshButton.Pressed += OnRefreshClicked;
            SkipButton ??= GetNodeOrNull<Button>("ActionBar/SkipButton");
            if (SkipButton != null)
                SkipButton.Pressed += OnSkipClicked;
        }

        public void ShowWindow(List<BuildEffectDefinition> options, Action<BuildEffectDefinition> onConfirmed,
            IReadOnlyDictionary<string, int>? stacksByEffectId = null,
            int skipReward = 0, Action? onSkipped = null,
            Func<ICollection<string>, List<BuildEffectDefinition>?>? rerollProvider = null,
            Func<BuildEffectDefinition, BuildPickCardContext?>? pickContextProvider = null,
            int rerollBaseCost = 10, float rerollCostGrowth = 1.5f, int freeRerollCount = 1)
        {
            if (_isOpen) return;

            _options = options;
            _onConfirmed = onConfirmed;
            _stacksByEffectId = stacksByEffectId;
            _onSkipped = onSkipped;
            _skipReward = skipReward;
            _rerollProvider = rerollProvider;
            _pickContextProvider = pickContextProvider;
            _rerollBaseCost = rerollBaseCost;
            _rerollCostGrowth = rerollCostGrowth;
            _freeRerollCount = Math.Max(0, freeRerollCount);
            _rerollCount = 0;
            _selectedIndex = -1;

            // 弃选按钮：仅当调用方提供了 onSkipped 才显示（核心选择窗口隐藏）
            if (SkipButton != null)
            {
                SkipButton.Visible = onSkipped != null;
                // 文字顺序 "+N 跳过"：配合 icon（金币图）在最左 → 显示为 [金币] +N 跳过
                SkipButton.Text = $"+{skipReward} 跳过";
            }

            // 刷新按钮：仅当调用方提供了 rerollProvider（核心选择无刷新）
            if (RefreshButton != null)
                RefreshButton.Visible = rerollProvider != null;

            BuildActionButtonList();

            Visible = true;
            ProcessMode = ProcessModeEnum.Always;
            SetProcessInput(true);
            _isOpen = true;

            PauseManager.Instance.PushPause();
            // 记录叠加后的计数：战斗中的拾取弹窗可能已持有暂停（计数 2+）——此时键盘应可用；
            // 只有窗口打开后又有新暂停（如暂停菜单盖上来）才锁键盘
            _pauseCountOnOpen = PauseManager.Instance.PauseCount;

            CallDeferred(nameof(DeferredPopulate));
        }

        private void DeferredPopulate()
        {
            PopulateOptions();
            UpdateHighlights();
        }

        public void CloseWindow(bool immediate = false)
        {
            if (!_isOpen) return;

            Visible = false;
            SetProcessInput(false);
            _isOpen = false;

            if (immediate)
            {
                if (PauseManager.Instance.IsPaused)
                    PauseManager.Instance.PopPause();
            }
            else
            {
                GetTree().CreateTimer(0.15f).Timeout += () =>
                {
                    if (PauseManager.Instance.IsPaused)
                        PauseManager.Instance.PopPause();
                };
            }

            var parent = GetParent();
            if (parent is CanvasLayer canvasLayer)
                canvasLayer.QueueFree();
            QueueFree();
        }

        public override void _Input(InputEvent @event)
        {
            if (!_isOpen) return;
            // 只在"打开后又叠加了新暂停"时锁键盘——窗口打开前已有的暂停（拾取弹窗等）不影响本窗口
            if (PauseManager.Instance.PauseCount > _pauseCountOnOpen) return;

            // 鼠标点击（卡牌/按钮/空白）：焦点域重置回卡片区——键盘状态与鼠标操作保持一致
            if (@event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
            {
                if (_focusDomain != FocusDomain.Cards)
                    SwitchToCardsDomain();
                return; // 不标记 handled：让点击继续传给卡牌/按钮
            }

            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                // 数字键只选中不确认：1-5 是战斗快捷栏键，残留输入只会高亮不会误选卡；
                // 再按一次已选中的数字 = 取消选中
                int numKey = (int)(keyEvent.Keycode - Key.Key1);
                if (numKey >= 0 && numKey < _options.Count)
                {
                    _selectedIndex = _selectedIndex == numKey ? -1 : numKey;
                    UpdateHighlights();
                    SwitchToCardsDomain();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            // 上下：切换焦点域（卡片区 ↓ 按钮区，按钮区 ↑ 卡片区）
            if (@event.IsActionPressed("ui_down") && _focusDomain == FocusDomain.Cards && _actionButtons.Count > 0)
            {
                SwitchToButtonsDomain();
                GetViewport().SetInputAsHandled();
                return;
            }
            if (@event.IsActionPressed("ui_up") && _focusDomain == FocusDomain.Buttons)
            {
                SwitchToCardsDomain();
                GetViewport().SetInputAsHandled();
                return;
            }

            // 左右：在域内移动（卡片选中 / 按钮焦点）
            if (_focusDomain == FocusDomain.Cards)
            {
                if (@event.IsActionPressed("ui_left"))
                {
                    _selectedIndex = (_selectedIndex - 1 + _options.Count) % _options.Count;
                    UpdateHighlights();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (@event.IsActionPressed("ui_right"))
                {
                    _selectedIndex = (_selectedIndex + 1) % _options.Count;
                    UpdateHighlights();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }
            else if (_actionButtons.Count > 0)
            {
                if (@event.IsActionPressed("ui_left"))
                {
                    _buttonIndex = (_buttonIndex - 1 + _actionButtons.Count) % _actionButtons.Count;
                    _actionButtons[_buttonIndex].GrabFocus();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (@event.IsActionPressed("ui_right"))
                {
                    _buttonIndex = (_buttonIndex + 1) % _actionButtons.Count;
                    _actionButtons[_buttonIndex].GrabFocus();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            if (@event.IsActionPressed("ui_accept"))
            {
                if (_focusDomain == FocusDomain.Cards)
                    ConfirmSelection(_selectedIndex);
                else if (_actionButtons.Count > 0)
                    _actionButtons[_buttonIndex].EmitSignal(BaseButton.SignalName.Pressed);
                GetViewport().SetInputAsHandled();
                return;
            }

            // Esc 弃选（仅效果选择窗口；核心选择无弃选）——任意焦点域都直接触发跳过
            if (@event.IsActionPressed("ui_cancel") && _onSkipped != null)
            {
                OnSkipClicked();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        private void BuildActionButtonList()
        {
            _actionButtons.Clear();
            if (RefreshButton != null && RefreshButton.Visible) _actionButtons.Add(RefreshButton);
            if (ConfirmButton != null && ConfirmButton.Visible) _actionButtons.Add(ConfirmButton);
            if (SkipButton != null && SkipButton.Visible) _actionButtons.Add(SkipButton);
        }

        private void SwitchToButtonsDomain()
        {
            _focusDomain = FocusDomain.Buttons;
            _buttonIndex = 0;
            if (_actionButtons.Count > 0)
                _actionButtons[0].GrabFocus();
        }

        private void SwitchToCardsDomain()
        {
            _focusDomain = FocusDomain.Cards;
            foreach (var button in _actionButtons)
                button.ReleaseFocus();
        }

        private async void PopulateOptions()
        {
            foreach (var card in _cards)
                card.QueueFree();
            _cards.Clear();

            if (CardTemplate == null || CardContainer == null) return;

            int count = _options.Count;
            float totalWidth = Mathf.Max(CardContainer.Size.X, 1f);
            float totalHeight = Mathf.Max(CardContainer.Size.Y, 1f);

            // 固定卡宽（不随数量变化——视口/文本换行宽度恒定；限制在容器宽 80% 内防超屏）
            float cardWidth = Mathf.Min(CardWidth, totalWidth * 0.8f);
            // 卡高受容器高度限制（保持设计比例 500:340），高度受限时同步收窄卡宽
            float designRatio = 340f / 260f;
            float cardHeight = Mathf.Min(cardWidth * designRatio, totalHeight * 0.9f);
            if (cardHeight < cardWidth * designRatio)
                cardWidth = cardHeight / designRatio;
            float cardY = (totalHeight - cardHeight) / 2f;

            // 动态步长：容器够宽则并排（step = cardWidth，数量少不堆叠）；
            // 数量多容器不够时自动重叠（step 减小），下限 = cardWidth × (1 - MaxOverlapRatio) 防叠死
            float step = cardWidth;
            if (count > 1)
            {
                float fitStep = (totalWidth - cardWidth) / (count - 1);
                step = Mathf.Clamp(fitStep, cardWidth * (1f - Mathf.Clamp(MaxOverlapRatio, 0f, 0.9f)), cardWidth);
            }
            float groupWidth = step * (count - 1) + cardWidth;
            float startX = (totalWidth - groupWidth) / 2f;

            // 阶段1：入树 + 定尺寸 + 同步 SubViewport + 字号缩放（此时不填文本）
            for (int i = 0; i < count; i++)
            {
                var effect = _options[i];
                var card = CardTemplate.Instantiate<BuildCard>();
                card.CardIndex = i;
                card.Selected += OnCardSelected;

                float x = startX + i * step;
                CardContainer.AddChild(card);
                card.Position = new Vector2(x, cardY);
                card.Size = new Vector2(cardWidth, cardHeight);
                card.SyncViewportSize();
                card.ApplyCardScale();
                card.ApplyRarityVisuals(effect.Rarity);
                _cards.Add(card);
            }

            // 等待一帧：SubViewport 内部的控件需要一次布局（sort）才能把锚点解析成
            // 最终尺寸。若在此之前设文本，DescLabel 会按过期尺寸测量换行——
            // 导出版帧节奏快时就会出现"只显示第一行、行尾半字被裁"。
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

            // 阶段2：布局稳定后填文本，保证按最终宽度约束测量换行
            for (int i = 0; i < count; i++)
            {
                var effect = _options[i];
                var card = _cards[i];
                if (!IsInstanceValid(card) || !card.IsInsideTree())
                    continue;

                card.NameLabel!.Text = effect.DisplayName;
                int currentStacks = 0;
                _stacksByEffectId?.TryGetValue(effect.EffectId, out currentStacks);

                // 反向候选：按取代规则展示生效层(高亮生效档),并附极简取代说明——
                // 描述区固定高度且无法自动缩字,附加文案必须尽量短(单行内)
                var pickContext = _pickContextProvider?.Invoke(effect);
                string description = effect.BuildDescriptionWithValues(
                    pickContext != null ? Mathf.Max(0, pickContext.ResultStacks - 1) : currentStacks);
                if (pickContext != null)
                {
                    description += $"\n[color=#E3B341]取代 {pickContext.ResultStacks}级{string.Join("、", pickContext.SupersedeTargets)}[/color]";
                }
                card.DescLabel!.Text = description;
                card.BuildClassLabel!.Text = !string.IsNullOrWhiteSpace(effect.BuildClass)
                    ? $"[{GetBuildClassName(effect.BuildClass)}]"
                    : "";
                card.KeyLabel!.Text = $"[{i + 1}]";
                if (card.Icon != null)
                {
                    card.Icon.Texture = effect.Icon;
                    card.Icon.Visible = effect.Icon != null;
                }
            }

            // 卡牌进入动画（错峰从屏幕下方飞入）：动画期间不可点击/键盘锁定——防连续攻击残留输入误触
            _cardsEntering = true;
            float stagger = 0.06f;
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].PlayEnterAnimation(i * stagger);

            float unlockAfter = (_cards.Count - 1) * stagger + 0.4f;
            GetTree().CreateTimer(unlockAfter).Timeout += () =>
            {
                _cardsEntering = false;
                UpdateHighlights(); // 解锁后重新评估确认按钮可用性
            };
        }

        private void OnCardSelected(int index)
        {
            // 再点已选中的卡 = 取消选中
            _selectedIndex = _selectedIndex == index ? -1 : index;
            UpdateHighlights();
        }

        private void UpdateHighlights()
        {
            for (int i = 0; i < _cards.Count; i++)
                _cards[i].IsSelected = i == _selectedIndex;

            // 确认按钮：无选中或卡牌进入动画期间禁用
            if (ConfirmButton != null)
                ConfirmButton.Disabled = _selectedIndex < 0 || _cardsEntering;
            // 跳过按钮：进入动画期间禁用（无选中依赖）
            if (SkipButton != null)
                SkipButton.Disabled = _cardsEntering;
            UpdateRefreshButton();
        }

        private void UpdateRefreshButton()
        {
            if (RefreshButton == null) return;

            // 在任何修改之前缓存初始 icon（tscn 配置的金币图）——免费分支会置 null，付费时据此恢复
            _refreshIcon ??= RefreshButton.Icon;

            int cost = GetNextRerollCost();
            if (cost > 0)
            {
                // 付费：显示金币 icon（[金币] +N 刷新）
                RefreshButton.Icon = _refreshIcon;
                RefreshButton.Text = $"-{cost} 刷新";
            }
            else
            {
                // 免费：不显示金币 icon（花钱语义，免费时隐藏）
                RefreshButton.Icon = null;
                RefreshButton.Text = "刷新";
            }

            bool affordable = true;
            if (cost > 0)
            {
                var player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
                affordable = player != null && player.GetGold() >= cost;
            }
            RefreshButton.Disabled = !affordable || _cardsEntering;
        }

        private void OnSkipClicked()
        {
            var callback = _onSkipped;
            _onSkipped = null;
            CloseWindow();
            callback?.Invoke();
        }

        /// <summary>本次刷新的费用：前 N 次免费（N = 免费次数，含局外养成加成），之后 cost = base × growth^付费次数。</summary>
        private int GetNextRerollCost()
        {
            if (_rerollCount < _freeRerollCount) return 0;
            int paidCount = _rerollCount - _freeRerollCount;
            return Math.Max(1, Mathf.RoundToInt(_rerollBaseCost * Mathf.Pow(_rerollCostGrowth, paidCount)));
        }

        private void OnRefreshClicked()
        {
            if (_rerollProvider == null) return;
            if (_cardsEntering) return; // 动画期间不可刷新（键盘 EmitSignal 绕过 Disabled 的兜底）

            // 先取新一批卡（可能因排除当前卡后无候选而失败——失败不扣费）
            var newOptions = _rerollProvider(_options.Select(o => o.EffectId).ToList());
            if (newOptions == null || newOptions.Count == 0) return;

            int cost = GetNextRerollCost();
            if (cost > 0)
            {
                var player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
                if (player == null || !player.TrySpendGold(cost)) return;
            }

            _rerollCount++;
            _options = newOptions;
            _selectedIndex = -1;
            PopulateOptions(); // 重填 + 进入动画 + 输入锁（_cardsEntering 复用）
        }

        private static readonly Dictionary<string, string> BuildClassDisplayNames = new()
        {
            { BuildClassConstants.Machine, "机械协议" },
            { BuildClassConstants.Waiter, "宴会协议" },
            { BuildClassConstants.Throw, "投掷协议" },
            { BuildClassConstants.Generic, "通用" },
        };

        private static string GetBuildClassName(string buildClass)
        {
            if (BuildClassDisplayNames.TryGetValue(buildClass, out var name))
                return name;
            return buildClass;
        }

        private void ConfirmSelection(int index)
        {
            if (index < 0 || index >= _options.Count) return;

            var chosen = _options[index];
            var callback = _onConfirmed;
            CloseWindow();
            callback?.Invoke(chosen);
        }
    }
}
