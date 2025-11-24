# 主菜单系统使用指南

## 概述

已为主界面创建了完整的UI系统，包括主菜单、模式选择、设置和存档选择等功能。

## 组件列表

### 1. 主菜单 (MainMenu)
- **文件位置**: 
  - 脚本: `scripts/ui/MainMenu.cs`
  - 场景: `scenes/ui/menus/MainMenu.tscn`
- **功能**: 
  - 开始游戏
  - 模式选择
  - 读取存档
  - 设置
  - 退出游戏

### 2. 模式选择菜单 (ModeSelectionMenu)
- **文件位置**: 
  - 脚本: `scripts/ui/ModeSelectionMenu.cs`
  - 场景: `scenes/ui/menus/ModeSelectionMenu.tscn`
- **功能**: 
  - 剧情模式
  - 街机模式
  - 无尽模式
  - 返回主菜单

### 3. 设置菜单 (SettingsMenu)
- **文件位置**: 
  - 脚本: `scripts/ui/SettingsMenu.cs`
  - 场景: `scenes/ui/menus/SettingsMenu.tscn`
- **功能**: 
  - 音量设置（主音量、音乐、音效）
  - 视频设置（分辨率、全屏）
  - 语言选择
  - 返回主菜单

### 4. 存档选择菜单 (SaveSlotSelection)
- **文件位置**: 
  - 脚本: `scripts/ui/SaveSlotSelection.cs`
  - 场景: `scenes/ui/menus/SaveSlotSelection.tscn`
- **功能**: 
  - 软排样式（卡片式布局）
  - 显示存档信息（名称、时间、游戏时长等）
  - 支持空存档显示
  - 返回主菜单

### 5. 主菜单管理器 (MainMenuManager)
- **文件位置**: 
  - 脚本: `scripts/scenes/MainMenuManager.cs`
  - 场景: `scenes/MainMenu.tscn`
- **功能**: 
  - 管理所有菜单的显示和切换
  - 处理菜单间的导航
  - 处理场景切换

## 使用方法

### 设置主菜单为启动场景

1. 打开 `project.godot`
2. 在 `[application]` 部分，修改 `run/main_scene`:
```
run/main_scene="res://scenes/MainMenu.tscn"
```

### 在代码中使用

```csharp
using Kuros.Managers;
using Kuros.UI;

// 加载主菜单
var mainMenu = UIManager.Instance.LoadMainMenu();

// 加载模式选择菜单
var modeMenu = UIManager.Instance.LoadModeSelectionMenu();

// 加载设置菜单
var settingsMenu = UIManager.Instance.LoadSettingsMenu();

// 加载存档选择菜单
var saveMenu = UIManager.Instance.LoadSaveSlotSelection();
```

## 存档系统说明

### 存档槽位卡片 (SaveSlotCard)

存档选择界面使用软排样式（卡片式布局），每个存档槽位显示为一张卡片。

**卡片显示内容**:
- 存档缩略图（如果有）
- 存档名称
- 保存时间
- 游戏时长
- 空存档提示

**自定义存档卡片**:

1. 创建自定义的 `SaveSlotCard` 场景
2. 在 `SaveSlotSelection` 的 Inspector 中设置 `SaveSlotCardScene` 属性
3. 场景的根节点必须是 `SaveSlotCard` 类型

### 存档数据结构

```csharp
public class SaveSlotData
{
    public int SlotIndex { get; set; }           // 槽位索引
    public bool HasSave { get; set; }            // 是否有存档
    public string SaveName { get; set; }         // 存档名称
    public string SaveTime { get; set; }         // 保存时间
    public string PlayTime { get; set; }         // 游戏时长
    public int Level { get; set; }               // 等级
    public Texture2D? Thumbnail { get; set; }    // 缩略图
}
```

### 实现存档加载

在 `SaveSlotSelection.cs` 的 `GetSaveSlotData` 方法中实现实际的存档加载逻辑：

```csharp
private SaveSlotData GetSaveSlotData(int slotIndex)
{
    // 检查存档文件是否存在
    string savePath = $"user://save_{slotIndex}.save";
    bool hasSave = FileAccess.FileExists(savePath);
    
    if (hasSave)
    {
        // 加载存档数据
        // 这里应该从文件读取实际的存档信息
        return new SaveSlotData
        {
            SlotIndex = slotIndex,
            HasSave = true,
            SaveName = "存档名称",
            SaveTime = "2024-01-01 12:00:00",
            PlayTime = "10:30:45",
            Level = 5,
            Thumbnail = null // 加载缩略图
        };
    }
    
    return new SaveSlotData
    {
        SlotIndex = slotIndex,
        HasSave = false
    };
}
```

## 菜单导航流程

```
主菜单
├── 开始游戏 → 直接进入战斗场景
├── 模式选择 → 模式选择菜单
│   ├── 剧情模式 → 进入战斗场景
│   ├── 街机模式 → 进入战斗场景
│   ├── 无尽模式 → 进入战斗场景
│   └── 返回 → 主菜单
├── 读取存档 → 存档选择菜单
│   ├── 选择存档 → 加载存档并进入战斗场景
│   └── 返回 → 主菜单
├── 设置 → 设置菜单
│   └── 返回 → 主菜单
└── 退出游戏 → 退出程序
```

## 自定义和扩展

### 添加新的菜单选项

1. 在 `MainMenu.tscn` 中添加新按钮
2. 在 `MainMenu.cs` 中添加对应的信号和处理方法
3. 在 `MainMenuManager.cs` 中处理新选项的逻辑

### 修改菜单样式

所有菜单场景文件都可以在编辑器中直接编辑：
- 修改颜色、字体、布局等
- 添加背景图片
- 调整按钮样式

### 添加新的游戏模式

1. 在 `ModeSelectionMenu.tscn` 中添加新按钮
2. 在 `ModeSelectionMenu.cs` 中添加对应的处理
3. 在 `MainMenuManager.cs` 的 `OnModeSelected` 方法中处理新模式

## 文件结构

```
K.U.R.O/
├── scripts/
│   ├── ui/
│   │   ├── MainMenu.cs                    # 主菜单脚本
│   │   ├── ModeSelectionMenu.cs           # 模式选择脚本
│   │   ├── SettingsMenu.cs                # 设置菜单脚本
│   │   └── SaveSlotSelection.cs           # 存档选择脚本
│   ├── scenes/
│   │   └── MainMenuManager.cs             # 主菜单管理器
│   └── managers/
│       └── UIManager.cs                   # UI管理器（已更新）
└── scenes/
    ├── MainMenu.tscn                      # 主菜单场景（入口）
    └── ui/
        └── menus/
            ├── MainMenu.tscn              # 主菜单UI场景
            ├── ModeSelectionMenu.tscn     # 模式选择UI场景
            ├── SettingsMenu.tscn          # 设置UI场景
            └── SaveSlotSelection.tscn     # 存档选择UI场景
```

## 注意事项

1. **UID文件**: 首次在编辑器中打开场景时，Godot会自动生成 `.cs.uid` 文件
2. **场景路径**: 确保所有场景路径在 `UIManager.cs` 中正确配置
3. **存档系统**: 当前存档系统是框架，需要实现实际的存档/读档逻辑
4. **设置保存**: 设置菜单的更改需要保存到配置文件，当前只是示例实现

## 下一步

1. 在Godot编辑器中打开 `scenes/MainMenu.tscn`
2. 检查所有UI场景是否正确加载
3. 自定义菜单样式和布局
4. 实现实际的存档/读档系统
5. 实现设置保存功能

祝您开发顺利！🎮

