# UI交互逻辑完整指南

## 概述

本文档详细说明了所有UI界面的交互逻辑和导航流程。

## 主菜单系统

### 主菜单 (MainMenu)
**场景**: `scenes/ui/menus/MainMenu.tscn`  
**脚本**: `scripts/ui/MainMenu.cs`

**按钮功能**:
- **开始游戏** → 直接进入战斗场景 (`ExampleBattle.tscn`)
- **模式选择** → 打开模式选择菜单
- **读取存档** → 打开存档选择菜单
- **设置** → 打开设置菜单
- **退出游戏** → 退出程序

**交互逻辑**:
```csharp
StartGameButton.Pressed → OnStartGamePressed() → 发出 StartGameRequested 信号
ModeSelectionButton.Pressed → OnModeSelectionPressed() → 发出 ModeSelectionRequested 信号
LoadGameButton.Pressed → OnLoadGamePressed() → 发出 LoadGameRequested 信号
SettingsButton.Pressed → OnSettingsPressed() → 发出 SettingsRequested 信号
QuitButton.Pressed → OnQuitPressed() → 发出 QuitRequested 信号
```

### 模式选择菜单 (ModeSelectionMenu)
**场景**: `scenes/ui/menus/ModeSelectionMenu.tscn`  
**脚本**: `scripts/ui/ModeSelectionMenu.cs`

**按钮功能**:
- **剧情模式** → 选择剧情模式并进入战斗场景
- **街机模式** → 选择街机模式并进入战斗场景
- **无尽模式** → 选择无尽模式并进入战斗场景
- **返回** → 返回主菜单

**交互逻辑**:
```csharp
StoryModeButton.Pressed → OnModeSelected("Story") → 发出 ModeSelected 信号
ArcadeModeButton.Pressed → OnModeSelected("Arcade") → 发出 ModeSelected 信号
EndlessModeButton.Pressed → OnModeSelected("Endless") → 发出 ModeSelected 信号
BackButton.Pressed → OnBackPressed() → 发出 BackRequested 信号
```

### 设置菜单 (SettingsMenu)
**场景**: `scenes/ui/menus/SettingsMenu.tscn`  
**脚本**: `scripts/ui/SettingsMenu.cs`

**功能**:
- **主音量滑块** → 调整主音量 (0-100)
- **音乐音量滑块** → 调整音乐音量 (0-100)
- **音效音量滑块** → 调整音效音量 (0-100)
- **分辨率选项** → 选择分辨率 (1280x720, 1920x1080, 2560x1440)
- **全屏复选框** → 切换全屏/窗口模式
- **语言选项** → 选择语言 (简体中文, English)
- **返回按钮** → 返回主菜单

**交互逻辑**:
```csharp
MasterVolumeSlider.ValueChanged → OnMasterVolumeChanged() → 设置主音量
MusicVolumeSlider.ValueChanged → OnMusicVolumeChanged() → 设置音乐音量
SFXVolumeSlider.ValueChanged → OnSFXVolumeChanged() → 设置音效音量
ResolutionOption.ItemSelected → OnResolutionSelected() → 改变分辨率
FullscreenCheckBox.Toggled → OnFullscreenToggled() → 切换全屏
LanguageOption.ItemSelected → OnLanguageSelected() → 改变语言
BackButton.Pressed → OnBackPressed() → 发出 BackRequested 信号
```

### 存档选择菜单 (SaveSlotSelection)
**场景**: `scenes/ui/menus/SaveSlotSelection.tscn`  
**脚本**: `scripts/ui/SaveSlotSelection.cs`

**功能**:
- **存档卡片** (软排样式，3列布局) → 显示存档信息或空存档
- **返回按钮** → 返回主菜单

**存档卡片显示内容**:
- 存档缩略图
- 存档名称
- 保存时间
- 游戏时长
- 空存档提示

**交互逻辑**:
```csharp
SaveSlotCard.Pressed → OnSlotCardSelected(slotIndex) → 发出 SlotSelected 信号
BackButton.Pressed → OnBackPressed() → 发出 BackRequested 信号
```

## 战斗UI系统

### 战斗HUD (BattleHUD)
**场景**: `scenes/ui/hud/BattleHUD.tscn`  
**脚本**: `scripts/ui/BattleHUD.cs`

**显示内容**:
- 玩家状态标签 (生命值/最大生命值, 分数)
- 生命值进度条 (TextureProgressBar)
- 分数标签
- 操作说明

**交互逻辑**:
- 通过信号系统自动更新 (`SamplePlayer.StatsChanged`)
- 连接到玩家后自动同步显示

### 战斗菜单 (BattleMenu)
**场景**: `scenes/ui/menus/BattleMenu.tscn`  
**脚本**: `scripts/ui/BattleMenu.cs`

**按钮功能**:
- **继续** → 关闭菜单，恢复游戏
- **设置** → 打开设置菜单 (待实现)
- **退出到主菜单** → 返回主菜单场景

**交互逻辑**:
```csharp
ESC键 → ToggleMenu() → 打开/关闭菜单
ResumeButton.Pressed → OnResumePressed() → 发出 ResumeRequested 信号 → 关闭菜单
SettingsButton.Pressed → OnSettingsPressed() → 发出 SettingsRequested 信号
QuitButton.Pressed → OnQuitPressed() → 发出 QuitRequested 信号 → 返回主菜单
```

**特殊功能**:
- 打开菜单时自动暂停游戏 (`PauseGameWhenOpen = true`)
- 关闭菜单时自动恢复游戏

## 场景管理器

### 主菜单管理器 (MainMenuManager)
**场景**: `scenes/MainMenu.tscn`  
**脚本**: `scripts/scenes/MainMenuManager.cs`

**功能**:
- 管理所有菜单的加载和显示
- 处理菜单间的导航
- 处理场景切换

**交互流程**:
```
主菜单
├── 开始游戏 → 切换场景到 ExampleBattle.tscn
├── 模式选择 → 显示模式选择菜单
│   ├── 选择模式 → 切换场景到 ExampleBattle.tscn
│   └── 返回 → 显示主菜单
├── 读取存档 → 显示存档选择菜单
│   ├── 选择存档 → 加载存档并切换场景到 ExampleBattle.tscn
│   └── 返回 → 显示主菜单
├── 设置 → 显示设置菜单
│   └── 返回 → 显示主菜单
└── 退出游戏 → 退出程序
```

### 战斗场景管理器 (BattleSceneManager)
**场景**: `scenes/ExampleBattle.tscn` (附加到场景)  
**脚本**: `scripts/scenes/BattleSceneManager.cs`

**功能**:
- 自动加载战斗HUD和战斗菜单
- 连接玩家和UI
- 处理场景退出时的清理

**交互流程**:
```
战斗场景启动
├── 自动加载 BattleHUD
├── 自动加载 BattleMenu
└── 连接玩家信号

战斗菜单
├── 继续 → 关闭菜单，恢复游戏
├── 设置 → 打开设置 (待实现)
└── 退出到主菜单 → 切换场景到 MainMenu.tscn
```

## UI管理器 (UIManager)

**脚本**: `scripts/managers/UIManager.cs`  
**类型**: 单例 (Autoload)

**功能**:
- 统一管理所有UI的加载和卸载
- 提供UI层管理 (HUD层, Menu层)
- 提供便捷的加载方法

**可用的UI加载方法**:
```csharp
UIManager.Instance.LoadMainMenu()
UIManager.Instance.LoadModeSelectionMenu()
UIManager.Instance.LoadSettingsMenu()
UIManager.Instance.LoadSaveSlotSelection()
UIManager.Instance.LoadBattleHUD()
UIManager.Instance.LoadBattleMenu()
```

## 完整的导航流程

```
启动游戏
  ↓
MainMenu.tscn (主菜单场景)
  ↓
MainMenu UI (主菜单界面)
  ├─→ 开始游戏 → ExampleBattle.tscn
  ├─→ 模式选择 → ModeSelectionMenu UI
  │     ├─→ 选择模式 → ExampleBattle.tscn
  │     └─→ 返回 → MainMenu UI
  ├─→ 读取存档 → SaveSlotSelection UI
  │     ├─→ 选择存档 → ExampleBattle.tscn
  │     └─→ 返回 → MainMenu UI
  ├─→ 设置 → SettingsMenu UI
  │     └─→ 返回 → MainMenu UI
  └─→ 退出游戏 → 退出程序

ExampleBattle.tscn (战斗场景)
  ↓
BattleHUD UI (战斗HUD)
BattleMenu UI (战斗菜单)
  ├─→ 继续 → 关闭菜单
  ├─→ 设置 → (待实现)
  └─→ 退出到主菜单 → MainMenu.tscn
```

## 信号连接图

### 主菜单系统
```
MainMenu
  ├─ StartGameRequested → MainMenuManager.OnStartGame()
  ├─ ModeSelectionRequested → MainMenuManager.OnModeSelectionRequested()
  ├─ LoadGameRequested → MainMenuManager.OnLoadGameRequested()
  ├─ SettingsRequested → MainMenuManager.OnSettingsRequested()
  └─ QuitRequested → MainMenuManager.OnQuit()

ModeSelectionMenu
  ├─ ModeSelected → MainMenuManager.OnModeSelected()
  └─ BackRequested → MainMenuManager.LoadMainMenu()

SettingsMenu
  └─ BackRequested → MainMenuManager.LoadMainMenu()

SaveSlotSelection
  ├─ SlotSelected → MainMenuManager.OnSaveSlotSelected()
  └─ BackRequested → MainMenuManager.LoadMainMenu()
```

### 战斗系统
```
SamplePlayer
  └─ StatsChanged → BattleHUD.OnPlayerStatsChanged()

BattleMenu
  ├─ ResumeRequested → BattleSceneManager.OnMenuResume()
  ├─ SettingsRequested → (待实现)
  └─ QuitRequested → BattleSceneManager.OnMenuQuit()
```

## 注意事项

1. **场景切换**: 所有场景切换都使用 `GetTree().ChangeSceneToFile()`
2. **UI显示**: UI通过 `Visible` 属性控制显示/隐藏
3. **信号连接**: 所有UI交互都通过信号系统实现解耦
4. **暂停处理**: 战斗菜单打开时会暂停游戏，关闭时恢复
5. **存档系统**: 当前存档系统是框架，需要实现实际的存档/读档逻辑

## 待实现功能

1. **战斗菜单中的设置**: 在战斗场景中打开设置菜单
2. **存档系统**: 实现实际的存档保存和加载
3. **设置保存**: 将设置保存到配置文件
4. **语言切换**: 实现多语言支持

## 调试提示

如果UI不显示或交互不工作，请检查：
1. UIManager是否正确配置为autoload
2. 场景文件路径是否正确
3. 信号连接是否正确
4. 节点路径是否正确
5. 控制台是否有错误信息

