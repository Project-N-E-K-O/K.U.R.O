# UI系统使用指南

## 概述

本项目实现了一个**可拆卸的UI系统**，允许您轻松地加载、显示和卸载UI界面，而无需直接修改战斗场景文件。

## 架构设计

### 核心组件

1. **UIManager** (`scripts/managers/UIManager.cs`)
   - 单例管理器，负责所有UI的加载和卸载
   - 已在 `project.godot` 中配置为 autoload
   - 提供两个UI层：HUD层（游戏内UI）和Menu层（菜单UI）

2. **BattleHUD** (`scripts/ui/BattleHUD.cs` + `scenes/ui/hud/BattleHUD.tscn`)
   - 战斗HUD界面，显示玩家状态、生命值、分数等
   - 通过信号系统与玩家解耦

3. **BattleMenu** (`scripts/ui/BattleMenu.cs` + `scenes/ui/menus/BattleMenu.tscn`)
   - 战斗菜单（暂停菜单）
   - 可通过ESC键打开/关闭
   - 支持暂停游戏功能

4. **BattleSceneManager** (`scripts/scenes/BattleSceneManager.cs`)
   - 战斗场景管理器
   - 自动加载和连接UI
   - 已在 `ExampleBattle.tscn` 中配置

## 使用方法

### 方式一：自动加载（推荐）

在战斗场景中添加 `BattleSceneManager` 节点（已在 `ExampleBattle.tscn` 中完成）：

```gdscript
# 在场景中添加 BattleSceneManager 节点
# 它会自动加载 HUD 和菜单
```

### 方式二：手动加载

在代码中手动加载UI：

```csharp
using Kuros.Managers;
using Kuros.UI;

// 加载战斗HUD
var hud = UIManager.Instance.LoadBattleHUD();

// 连接玩家
if (player is SamplePlayer samplePlayer)
{
    hud.SetPlayer(samplePlayer);
    hud.ConnectToPlayer(samplePlayer);
}

// 加载战斗菜单
var menu = UIManager.Instance.LoadBattleMenu();
```

### 卸载UI

```csharp
// 卸载特定UI
UIManager.Instance.UnloadBattleHUD();
UIManager.Instance.UnloadBattleMenu();

// 或使用通用方法
UIManager.Instance.UnloadUI("BattleHUD");
```

## UI可拆卸性实现

### 1. 独立场景文件
- 每个UI都是独立的 `.tscn` 场景文件
- 可以在编辑器中单独编辑和预览
- 不依赖战斗场景的结构

### 2. 信号系统解耦
- UI通过信号接收数据更新
- 游戏逻辑不需要直接引用UI节点
- 示例：`SamplePlayer` 发出 `StatsChanged` 信号，`BattleHUD` 监听并更新显示

### 3. 管理器模式
- `UIManager` 统一管理所有UI
- 支持动态加载和卸载
- 可以轻松切换不同的UI版本

## 菜单栏放置建议

### 当前实现
- **BattleMenu** 使用全屏半透明背景
- 菜单面板居中显示
- 通过ESC键打开/关闭
- 打开时自动暂停游戏

### 自定义菜单位置

编辑 `scenes/ui/menus/BattleMenu.tscn`：

1. 修改 `MenuPanel` 的锚点和偏移量
2. 调整 `VBoxContainer` 的位置
3. 可以添加更多按钮或选项

## 扩展UI系统

### 添加新的UI组件

1. 创建新的UI脚本（继承自 `Control`）
2. 创建对应的场景文件
3. 在 `UIManager` 中添加加载方法：

```csharp
public YourNewUI LoadYourNewUI()
{
    return LoadUI<YourNewUI>("res://scenes/ui/your_ui.tscn", UILayer.HUD, "YourNewUI");
}
```

### 修改现有UI

1. 在编辑器中打开对应的 `.tscn` 文件
2. 修改节点结构、样式等
3. 脚本会自动适配（如果节点路径不变）

## 注意事项

1. **UIManager 必须在 project.godot 中配置为 autoload**
   - 已在配置文件中添加
   - 如果遇到 `UIManager.Instance` 为 null，请检查 autoload 配置

2. **旧UI节点已隐藏但保留**
   - `ExampleBattle.tscn` 中的旧UI节点已设置为 `visible = false`
   - 可以安全删除，或保留作为备用

3. **信号连接**
   - UI会自动连接玩家的信号
   - 场景退出时会自动断开连接

4. **菜单暂停功能**
   - 默认打开菜单时会暂停游戏
   - 可在 `BattleMenu.cs` 中修改 `PauseGameWhenOpen` 属性

## 文件结构

```
K.U.R.O/
├── scripts/
│   ├── managers/
│   │   └── UIManager.cs          # UI管理器（单例）
│   ├── ui/
│   │   ├── BattleHUD.cs          # 战斗HUD脚本
│   │   └── BattleMenu.cs         # 战斗菜单脚本
│   └── scenes/
│       └── BattleSceneManager.cs # 战斗场景管理器
└── scenes/
    ├── ui/
    │   ├── hud/
    │   │   └── BattleHUD.tscn    # 战斗HUD场景
    │   └── menus/
    │       └── BattleMenu.tscn    # 战斗菜单场景
    └── ExampleBattle.tscn         # 战斗场景（已集成新系统）
```

## 总结

这个UI系统提供了：
- ✅ **可拆卸性**：UI是独立的场景，可以轻松替换
- ✅ **解耦设计**：通过信号系统实现松耦合
- ✅ **易于扩展**：可以轻松添加新的UI组件
- ✅ **统一管理**：通过UIManager统一管理所有UI
- ✅ **灵活配置**：支持自动加载或手动加载

现在您可以在不修改战斗场景的情况下，轻松地修改、替换或扩展UI界面！

