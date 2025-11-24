# 战斗UI系统完整指南

## 概述

已为战斗场景创建了完整的UI系统，包括所有必需的组件和交互逻辑。

## 战斗UI组件列表

### 1. 路径指示器 (PathIndicator)
**脚本**: `scripts/ui/PathIndicator.cs`

**功能**:
- 显示路径方向指示
- 显示路径名称
- 箭头指示方向

**使用方法**:
```csharp
pathIndicator.SetPathDirection(new Vector2(1, 0), "前进方向");
pathIndicator.ShowPath("目标地点");
pathIndicator.HidePath();
```

### 2. 血条组件 (HealthBar)
**脚本**: `scripts/ui/HealthBar.cs`

**功能**:
- 显示角色生命值
- 显示角色名称
- 可自定义颜色

**使用方法**:
```csharp
healthBar.SetCharacterName("玩家1");
healthBar.UpdateHealth(80, 100);
healthBar.SetHealthColor(new Color(1, 0, 0, 1)); // 红色
```

### 3. 敌人出场提示 (EnemySpawnIndicator)
**脚本**: `scripts/ui/EnemySpawnIndicator.cs`

**功能**:
- 显示敌人出现提示
- 自动隐藏
- 支持动画

**使用方法**:
```csharp
enemySpawnIndicator.ShowEnemySpawn("敌人名称", "敌人出现！");
// 会自动在2秒后隐藏，或手动调用 HideEnemySpawn()
```

### 4. 物品栏 (InventoryUI)
**脚本**: `scripts/ui/InventoryUI.cs`

**功能**:
- 显示物品列表（网格布局）
- 支持物品图标和数量
- 可切换显示/隐藏

**使用方法**:
```csharp
inventoryUI.ToggleInventory(); // 切换显示/隐藏
inventoryUI.SetInventoryVisible(true); // 显示
// 通过信号接收物品选择事件
inventoryUI.ItemSelected += OnItemSelected;
```

### 5. 武器栏 (WeaponBar)
**脚本**: `scripts/ui/WeaponBar.cs`

**功能**:
- 显示当前装备的武器
- 显示武器名称和图标

**使用方法**:
```csharp
weaponBar.SetCurrentWeapon(weaponIcon, "武器名称", 0);
weaponBar.SwitchWeapon(1); // 切换武器
```

### 6. 对话UI (DialogueUI)
**脚本**: `scripts/ui/DialogueUI.cs`

**功能**:
- 显示对话内容
- 显示说话者名称
- 支持最多5个选项
- 退出和谈话按钮

**使用方法**:
```csharp
// 显示对话
dialogueUI.ShowDialogue("NPC名称", "对话内容", new[] { "选项1", "选项2", "选项3" });

// 监听选项选择
dialogueUI.OptionSelected += OnOptionSelected;

// 监听退出
dialogueUI.ExitRequested += OnDialogueExit;
```

### 7. 购买界面 (ShopUI)
**脚本**: `scripts/ui/ShopUI.cs`

**功能**:
- 显示商店物品
- 显示玩家金币
- 购买功能

**使用方法**:
```csharp
// 显示商店
shopUI.ShowShop("商店名称", playerGold);

// 添加商品
shopUI.AddShopItem(itemId, "物品名称", itemIcon, 100, "物品描述");

// 更新金币
shopUI.UpdatePlayerGold(newGold);

// 监听购买事件
shopUI.ItemPurchased += OnItemPurchased;
```

## 更新的BattleHUD

`BattleHUD` 现在包含所有新UI组件的引用：

```csharp
public PathIndicator PathIndicator { get; private set; }
public HealthBar P1HealthBar { get; private set; }
public HealthBar P2HealthBar { get; private set; }
public EnemySpawnIndicator EnemySpawnIndicator { get; private set; }
public InventoryUI InventoryUI { get; private set; }
public WeaponBar WeaponBar { get; private set; }
public DialogueUI DialogueUI { get; private set; }
public ShopUI ShopUI { get; private set; }
```

## 场景文件设置

### BattleHUD.tscn 结构建议

```
BattleHUD (Control)
├── PlayerStats (Label)
├── HealthBar (TextureProgressBar)
├── ScoreLabel (Label)
├── Instructions (Label)
├── PathIndicator (Control) - 路径指示器
│   ├── PathLabel (Label)
│   └── ArrowIcon (TextureRect)
├── P1HealthBar (Control) - P1血条
│   ├── NameLabel (Label)
│   ├── HealthProgressBar (TextureProgressBar)
│   └── HealthLabel (Label)
├── P2HealthBar (Control) - P2血条
│   ├── NameLabel (Label)
│   ├── HealthProgressBar (TextureProgressBar)
│   └── HealthLabel (Label)
├── EnemySpawnIndicator (Control) - 敌人出场提示
│   ├── EnemyNameLabel (Label)
│   ├── SpawnTextLabel (Label)
│   └── AnimationPlayer (AnimationPlayer)
├── InventoryUI (Control) - 物品栏
│   ├── ToggleButton (Button)
│   └── InventoryPanel (Control)
│       ├── TitleLabel (Label)
│       └── ItemGrid (GridContainer)
├── WeaponBar (Control) - 武器栏
│   ├── WeaponContainer (HBoxContainer)
│   ├── CurrentWeaponIcon (TextureRect)
│   └── WeaponNameLabel (Label)
├── DialogueUI (Control) - 对话UI
│   └── DialoguePanel (Control)
│       ├── SpeakerName (Label)
│       ├── DialogueText (Label)
│       ├── OptionsContainer (VBoxContainer)
│       │   ├── Option1 (Button)
│       │   ├── Option2 (Button)
│       │   ├── Option3 (Button)
│       │   ├── Option4 (Button)
│       │   └── Option5 (Button)
│       ├── TalkButton (Button)
│       └── ExitButton (Button)
└── ShopUI (Control) - 购买界面
    └── ShopPanel (Control)
        ├── TitleLabel (Label)
        ├── PlayerGoldLabel (Label)
        ├── ItemScrollContainer (ScrollContainer)
        │   └── ShopItemGrid (GridContainer)
        └── CloseButton (Button)
```

## 在BattleSceneManager中使用

```csharp
// 获取UI组件
var hud = UIManager.Instance.LoadBattleHUD();

// 使用路径指示器
hud.PathIndicator?.ShowPath("目标地点");

// 更新P1血条
hud.P1HealthBar?.UpdateHealth(80, 100);

// 显示敌人出场
hud.EnemySpawnIndicator?.ShowEnemySpawn("敌人名称");

// 切换物品栏
hud.InventoryUI?.ToggleInventory();

// 显示对话
hud.DialogueUI?.ShowDialogue("NPC", "你好！", new[] { "选项1", "选项2" });

// 显示商店
hud.ShopUI?.ShowShop("商店", 1000);
```

## 交互逻辑

### 物品栏
- 通过按钮切换显示/隐藏
- 点击物品槽位触发 `ItemSelected` 信号

### 对话UI
- **退出按钮**: 关闭对话UI
- **谈话按钮**: 触发 `TalkRequested` 信号
- **选项按钮**: 触发 `OptionSelected` 信号，传递选项索引

### 商店UI
- **购买按钮**: 触发 `ItemPurchased` 信号，传递物品ID
- **关闭按钮**: 关闭商店UI

## 下一步

1. 在Godot编辑器中打开 `scenes/ui/hud/BattleHUD.tscn`
2. 添加所有新UI组件作为子节点
3. 在Inspector中为BattleHUD脚本分配所有UI组件引用
4. 创建对话UI和商店UI的场景文件
5. 在代码中连接信号处理交互逻辑

## 文件清单

**已创建的脚本**:
- `scripts/ui/PathIndicator.cs`
- `scripts/ui/HealthBar.cs`
- `scripts/ui/EnemySpawnIndicator.cs`
- `scripts/ui/InventoryUI.cs`
- `scripts/ui/WeaponBar.cs`
- `scripts/ui/DialogueUI.cs`
- `scripts/ui/ShopUI.cs`

**需要创建的场景文件**:
- `scenes/ui/windows/DialogueUI.tscn`
- `scenes/ui/windows/ShopUI.tscn`

**已更新的文件**:
- `scripts/ui/BattleHUD.cs` - 添加了新UI组件引用
- `scripts/managers/UIManager.cs` - 添加了对话和商店UI的加载方法

所有代码已通过编译检查，可以直接使用！

