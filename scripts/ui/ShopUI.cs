using Godot;
using System.Collections.Generic;

namespace Kuros.UI
{
    /// <summary>
    /// 购买界面UI - 商店界面
    /// </summary>
    public partial class ShopUI : Control
    {
        [ExportCategory("UI References")]
        [Export] public Control ShopPanel { get; private set; } = null!;
        [Export] public Label ShopTitleLabel { get; private set; } = null!;
        [Export] public ScrollContainer ItemScrollContainer { get; private set; } = null!;
        [Export] public GridContainer ShopItemGrid { get; private set; } = null!;
        [Export] public Label PlayerGoldLabel { get; private set; } = null!;
        [Export] public Button CloseButton { get; private set; } = null!;

        [ExportCategory("Settings")]
        [Export] public int ItemsPerRow = 3;

        // 信号
        [Signal] public delegate void ItemPurchasedEventHandler(int itemId);
        [Signal] public delegate void ShopClosedEventHandler();

        private List<ShopItemCard> _itemCards = new List<ShopItemCard>();

        public override void _Ready()
        {
            if (ShopPanel == null)
            {
                ShopPanel = GetNodeOrNull<Control>("ShopPanel");
            }

            if (ShopTitleLabel == null)
            {
                ShopTitleLabel = GetNodeOrNull<Label>("ShopPanel/TitleLabel");
            }

            if (ItemScrollContainer == null)
            {
                ItemScrollContainer = GetNodeOrNull<ScrollContainer>("ShopPanel/ItemScrollContainer");
            }

            if (ShopItemGrid == null)
            {
                ShopItemGrid = GetNodeOrNull<GridContainer>("ShopPanel/ItemScrollContainer/ShopItemGrid");
            }

            if (PlayerGoldLabel == null)
            {
                PlayerGoldLabel = GetNodeOrNull<Label>("ShopPanel/PlayerGoldLabel");
            }

            if (CloseButton == null)
            {
                CloseButton = GetNodeOrNull<Button>("ShopPanel/CloseButton");
            }

            if (ShopItemGrid != null)
            {
                ShopItemGrid.Columns = ItemsPerRow;
            }

            if (CloseButton != null)
            {
                CloseButton.Pressed += OnClosePressed;
            }

            // 初始状态：隐藏
            SetShopVisible(false);
        }

        /// <summary>
        /// 显示商店
        /// </summary>
        public void ShowShop(string shopName, int playerGold)
        {
            SetShopVisible(true);

            if (ShopTitleLabel != null)
            {
                ShopTitleLabel.Text = shopName;
            }

            UpdatePlayerGold(playerGold);
        }

        /// <summary>
        /// 更新玩家金币显示
        /// </summary>
        public void UpdatePlayerGold(int gold)
        {
            if (PlayerGoldLabel != null)
            {
                PlayerGoldLabel.Text = $"金币: {gold}";
            }
        }

        /// <summary>
        /// 添加商店物品
        /// </summary>
        public void AddShopItem(int itemId, string itemName, Texture2D? icon, int price, string? description = null)
        {
            if (ShopItemGrid == null) return;

            var itemCard = new ShopItemCard();
            itemCard.Initialize(itemId, itemName, icon, price, description);
            itemCard.ItemPurchased += OnItemPurchased;
            ShopItemGrid.AddChild(itemCard);
            _itemCards.Add(itemCard);
        }

        /// <summary>
        /// 清空商店物品
        /// </summary>
        public void ClearShopItems()
        {
            foreach (var card in _itemCards)
            {
                if (card != null && IsInstanceValid(card))
                {
                    card.QueueFree();
                }
            }
            _itemCards.Clear();
        }

        /// <summary>
        /// 设置商店可见性
        /// </summary>
        public void SetShopVisible(bool visible)
        {
            Visible = visible;
            if (ShopPanel != null)
            {
                ShopPanel.Visible = visible;
            }
        }

        private void OnItemPurchased(int itemId)
        {
            EmitSignal(SignalName.ItemPurchased, itemId);
        }

        private void OnClosePressed()
        {
            SetShopVisible(false);
            EmitSignal(SignalName.ShopClosed);
        }
    }

    /// <summary>
    /// 商店物品卡片
    /// </summary>
    public partial class ShopItemCard : Control
    {
        [Signal] public delegate void ItemPurchasedEventHandler(int itemId);

        private int _itemId;
        private int _price;
        private Button _cardButton = null!;
        private TextureRect _itemIcon = null!;
        private Label _itemNameLabel = null!;
        private Label _priceLabel = null!;
        private Label _descriptionLabel = null!;
        private Button _buyButton = null!;

        public void Initialize(int itemId, string itemName, Texture2D? icon, int price, string? description = null)
        {
            _itemId = itemId;
            _price = price;
            SetupUI();
            UpdateDisplay(itemName, icon, price, description);
        }

        private void SetupUI()
        {
            CustomMinimumSize = new Vector2(200, 250);

            _cardButton = new Button();
            _cardButton.Name = "CardButton";
            _cardButton.Flat = true;
            AddChild(_cardButton);
            _cardButton.AnchorLeft = 0.0f;
            _cardButton.AnchorTop = 0.0f;
            _cardButton.AnchorRight = 1.0f;
            _cardButton.AnchorBottom = 1.0f;

            var vbox = new VBoxContainer();
            _cardButton.AddChild(vbox);
            vbox.AnchorLeft = 0.0f;
            vbox.AnchorTop = 0.0f;
            vbox.AnchorRight = 1.0f;
            vbox.AnchorBottom = 1.0f;

            _itemIcon = new TextureRect();
            _itemIcon.Name = "ItemIcon";
            _itemIcon.ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional;
            _itemIcon.CustomMinimumSize = new Vector2(0, 100);
            vbox.AddChild(_itemIcon);

            _itemNameLabel = new Label();
            _itemNameLabel.Name = "ItemName";
            _itemNameLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(_itemNameLabel);

            _descriptionLabel = new Label();
            _descriptionLabel.Name = "Description";
            _descriptionLabel.HorizontalAlignment = HorizontalAlignment.Center;
            _descriptionLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            vbox.AddChild(_descriptionLabel);

            _priceLabel = new Label();
            _priceLabel.Name = "Price";
            _priceLabel.HorizontalAlignment = HorizontalAlignment.Center;
            vbox.AddChild(_priceLabel);

            _buyButton = new Button();
            _buyButton.Name = "BuyButton";
            _buyButton.Text = "购买";
            vbox.AddChild(_buyButton);
            _buyButton.Pressed += () => EmitSignal(SignalName.ItemPurchased, _itemId);
        }

        private void UpdateDisplay(string itemName, Texture2D? icon, int price, string? description)
        {
            if (_itemNameLabel != null)
            {
                _itemNameLabel.Text = itemName;
            }

            if (_itemIcon != null)
            {
                _itemIcon.Texture = icon;
                _itemIcon.Visible = icon != null;
            }

            if (_priceLabel != null)
            {
                _priceLabel.Text = $"价格: {price}";
            }

            if (_descriptionLabel != null)
            {
                _descriptionLabel.Text = description ?? "";
                _descriptionLabel.Visible = !string.IsNullOrEmpty(description);
            }
        }
    }
}

