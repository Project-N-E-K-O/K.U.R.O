# UI系统故障排除指南

## 问题：tscn文件无法调用/加载

如果您遇到UI场景无法加载的问题，请按照以下步骤检查：

### 1. 检查UIManager是否已正确配置为autoload

**检查方法：**
- 打开 `project.godot` 文件
- 查找 `[autoload]` 部分
- 确认有以下配置：
```
[autoload]

UIManager="*res://scripts/managers/UIManager.cs"
```

**如果缺失：**
- 在Godot编辑器中：Project → Project Settings → Autoload
- 添加 `UIManager`，路径为 `res://scripts/managers/UIManager.cs`
- 确保勾选了 "Enable" 选项

### 2. 检查场景文件路径是否正确

**检查文件是否存在：**
- `scenes/ui/hud/BattleHUD.tscn`
- `scenes/ui/menus/BattleMenu.tscn`

**检查UIManager中的路径：**
打开 `scripts/managers/UIManager.cs`，确认路径：
```csharp
private const string BATTLE_HUD_PATH = "res://scenes/ui/hud/BattleHUD.tscn";
private const string BATTLE_MENU_PATH = "res://scenes/ui/menus/BattleMenu.tscn";
```

### 3. 检查脚本文件是否存在

**确认以下文件存在：**
- `scripts/ui/BattleHUD.cs`
- `scripts/ui/BattleMenu.cs`
- `scripts/managers/UIManager.cs`
- `scripts/scenes/BattleSceneManager.cs`

### 4. 检查场景文件中的脚本引用

**检查场景文件格式：**

打开 `scenes/ui/hud/BattleHUD.tscn`，应该看到：
```
[ext_resource type="Script" uid="uid://3vrm8fkuw0da" path="res://scripts/ui/BattleHUD.cs" id="1_battle_hud"]
```

打开 `scenes/ui/menus/BattleMenu.tscn`，应该看到：
```
[ext_resource type="Script" uid="uid://cmrjwunvir0ik" path="res://scripts/ui/BattleMenu.cs" id="1_battle_menu"]
```

**如果UID不正确：**
- 在Godot编辑器中打开场景文件
- 选择根节点
- 在Inspector中重新分配脚本
- 保存场景

### 5. 检查编译错误

**在Godot编辑器中：**
1. 打开 Output 面板（底部）
2. 查看是否有C#编译错误
3. 如果有错误，修复后再试

**常见编译错误：**
- 缺少命名空间引用：确保 `UIManager.cs` 中有 `using Kuros.UI;`
- 类型不匹配：确保场景根节点类型与脚本中的类型匹配

### 6. 检查控制台输出

**运行游戏时查看控制台：**

如果看到以下错误信息：
- `UIManager: 无法加载UI场景` - 检查文件路径
- `UIManager: UI场景实例化失败` - 检查场景文件格式
- `BattleSceneManager: UIManager未初始化` - 检查autoload配置

### 7. 手动测试加载

**在代码中手动测试：**

创建一个测试脚本，在 `_Ready()` 中添加：
```csharp
public override void _Ready()
{
    // 测试UIManager是否可用
    if (UIManager.Instance == null)
    {
        GD.PrintErr("UIManager未初始化！");
        return;
    }
    
    GD.Print("UIManager已初始化");
    
    // 测试加载HUD
    var hud = UIManager.Instance.LoadBattleHUD();
    if (hud == null)
    {
        GD.PrintErr("无法加载BattleHUD！");
    }
    else
    {
        GD.Print("BattleHUD加载成功！");
    }
}
```

### 8. 检查场景文件格式

**确保场景文件格式正确：**

场景文件应该以以下格式开头：
```
[gd_scene load_steps=2 format=3 uid="uid://..."]
```

**检查场景根节点：**
- `BattleHUD.tscn` 的根节点应该是 `Control` 类型
- `BattleMenu.tscn` 的根节点应该是 `Control` 类型

### 9. 重新生成场景文件（如果以上都不行）

**在Godot编辑器中：**
1. 删除现有的 `.tscn` 文件
2. 创建新场景
3. 添加根节点（Control）
4. 附加脚本
5. 添加子节点
6. 保存场景

### 10. 检查项目设置

**确保项目设置正确：**
1. Project → Project Settings → General → Application → Run
2. 确认 Main Scene 设置为 `res://scenes/ExampleBattle.tscn`

## 常见错误和解决方案

### 错误：`UIManager.Instance is null`

**原因：** UIManager未正确配置为autoload

**解决：** 按照步骤1重新配置autoload

### 错误：`无法加载UI场景`

**原因：** 文件路径错误或文件不存在

**解决：** 
- 检查文件是否存在
- 检查路径是否正确（注意大小写）
- 在Godot编辑器中右键场景文件 → "Copy Path" 获取正确路径

### 错误：`UI场景实例化失败`

**原因：** 场景文件格式错误或根节点类型不匹配

**解决：**
- 在编辑器中打开场景文件检查
- 确认根节点类型与脚本中的类型匹配
- 重新保存场景文件

### 错误：编译错误

**原因：** 代码中有语法错误或缺少引用

**解决：**
- 检查所有脚本文件
- 确保所有命名空间引用正确
- 重新编译项目

## 调试技巧

### 启用详细日志

在 `UIManager.cs` 的 `LoadUI` 方法中添加更多日志：
```csharp
GD.Print($"尝试加载UI: {uiPath}");
GD.Print($"文件是否存在: {ResourceLoader.Exists(uiPath)}");
```

### 检查场景树

在运行时检查场景树：
```csharp
// 在控制台输入
print_tree()
// 查看UIManager及其子节点
```

### 使用断点调试

在Visual Studio或Rider中设置断点，检查：
- UIManager.Instance 是否为null
- LoadUI方法是否被调用
- 场景加载是否成功

## 如果问题仍然存在

1. 检查Godot版本是否为4.5.1
2. 检查C#项目是否正确编译
3. 尝试重新导入项目
4. 检查是否有其他脚本或插件冲突

## 联系支持

如果以上方法都无法解决问题，请提供：
- 完整的错误信息
- 控制台输出
- 相关文件的内容
- Godot版本信息

