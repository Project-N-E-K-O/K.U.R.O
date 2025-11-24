# 生命值条自定义素材设置指南

## 节点类型

已将生命值条从 `ProgressBar` 替换为 **`TextureProgressBar`**，这是使用自定义素材的最佳选择。

## TextureProgressBar 的优势

1. ✅ **支持自定义纹理**：可以设置背景、填充和覆盖层纹理
2. ✅ **九宫格拉伸**：支持九宫格图片，适应不同尺寸
3. ✅ **多种填充模式**：从左到右、从右到左、从上到下等
4. ✅ **颜色叠加**：可以给纹理添加颜色叠加效果
5. ✅ **完全兼容**：继承自 `ProgressBar`，API完全兼容

## 如何设置自定义素材

### 方法一：在编辑器中设置（推荐）

1. 在 Godot 编辑器中打开 `scenes/ui/hud/BattleHUD.tscn`
2. 选择 `HealthBar` 节点
3. 在 Inspector 面板中找到以下属性：

#### 基础设置
- **Fill Mode**：填充模式
  - `Left to Right` (0) - 从左到右（默认）
  - `Right to Left` (1) - 从右到左
  - `Top to Bottom` (2) - 从上到下
  - `Bottom to Top` (3) - 从下到上

#### 纹理设置
- **Under**：背景纹理（进度条后面的部分）
  - 拖拽您的背景图片到这里
  
- **Progress**：填充纹理（显示生命值的部分）
  - 拖拽您的生命值填充图片到这里
  
- **Over**：覆盖层纹理（可选，显示在进度条上方）
  - 拖拽您的覆盖层图片（如边框、装饰等）到这里

#### 颜色叠加（可选）
- **Tint Under**：背景颜色叠加
- **Tint Progress**：填充颜色叠加
- **Tint Over**：覆盖层颜色叠加

### 方法二：在代码中设置

```csharp
// 在 BattleHUD.cs 的 _Ready() 方法中添加
public override void _Ready()
{
    // ... 其他代码 ...
    
    // 设置纹理
    if (HealthBar != null)
    {
        // 加载纹理资源
        var backgroundTexture = GD.Load<Texture2D>("res://textures/ui/health_bar_bg.png");
        var fillTexture = GD.Load<Texture2D>("res://textures/ui/health_bar_fill.png");
        var overlayTexture = GD.Load<Texture2D>("res://textures/ui/health_bar_overlay.png");
        
        // 设置纹理
        HealthBar.TextureUnder = backgroundTexture;
        HealthBar.TextureProgress = fillTexture;
        HealthBar.TextureOver = overlayTexture;
        
        // 设置填充模式（从左到右）
        HealthBar.FillMode = TextureProgressBar.FillModeEnum.LeftToRight;
    }
}
```

## 素材准备建议

### 1. 背景纹理（Under）
- 显示在进度条后面的部分
- 建议尺寸：与进度条节点尺寸匹配
- 格式：PNG（支持透明）

### 2. 填充纹理（Progress）
- 显示生命值的部分
- 建议尺寸：与进度条节点尺寸匹配
- 格式：PNG（支持透明）
- 注意：这个纹理会根据生命值百分比进行裁剪显示

### 3. 覆盖层纹理（Over，可选）
- 显示在进度条上方，如边框、装饰等
- 建议尺寸：与进度条节点尺寸匹配
- 格式：PNG（支持透明）

### 4. 九宫格设置（如果使用九宫格图片）

如果您的素材是九宫格图片，需要在导入设置中配置：

1. 选择图片资源
2. 在 Import 面板中：
   - 启用 **"Region"** 或使用 **NinePatchRect** 节点
   - 或者使用 `TextureProgressBar` 的 `NinePatchStretch` 属性

## 示例：完整的生命值条设置

### 场景文件示例

```gdscript
[node name="HealthBar" type="TextureProgressBar" parent="."]
layout_mode = 1
offset_left = 10.0
offset_top = 90.0
offset_right = 300.0
offset_bottom = 120.0
max_value = 100.0
value = 100.0
show_percentage = false
fill_mode = 0  # Left to Right
texture_under = ExtResource("health_bg")
texture_progress = ExtResource("health_fill")
texture_over = ExtResource("health_overlay")
```

### 代码示例

```csharp
// 在 BattleHUD.cs 中
private void SetupHealthBar()
{
    if (HealthBar == null) return;
    
    // 设置纹理
    HealthBar.TextureUnder = GD.Load<Texture2D>("res://textures/ui/health_bar_bg.png");
    HealthBar.TextureProgress = GD.Load<Texture2D>("res://textures/ui/health_bar_fill.png");
    HealthBar.TextureOver = GD.Load<Texture2D>("res://textures/ui/health_bar_border.png");
    
    // 设置填充模式
    HealthBar.FillMode = TextureProgressBar.FillModeEnum.LeftToRight;
    
    // 可选：设置颜色叠加
    HealthBar.TintUnder = new Color(0.3f, 0.3f, 0.3f, 1.0f);  // 背景稍微暗一点
    HealthBar.TintProgress = new Color(1.0f, 0.2f, 0.2f, 1.0f);  // 红色生命值
}
```

## 常见问题

### Q: 纹理显示不正确怎么办？
A: 检查以下几点：
1. 纹理路径是否正确
2. 纹理是否已正确导入（检查 Import 面板）
3. 节点尺寸是否与纹理尺寸匹配
4. 填充模式是否正确

### Q: 如何让生命值条从右到左填充？
A: 设置 `FillMode = 1` 或 `FillModeEnum.RightToLeft`

### Q: 可以使用动画纹理吗？
A: 可以！使用 `AnimatedTexture` 或 `Texture2D` 的动画帧，但需要在代码中手动更新。

### Q: 如何实现渐变效果？
A: 在填充纹理中使用渐变图片，或者使用 `TintProgress` 颜色叠加。

## 文件位置

- 场景文件：`scenes/ui/hud/BattleHUD.tscn`
- 脚本文件：`scripts/ui/BattleHUD.cs`
- 建议素材位置：`textures/ui/` 目录

## 下一步

1. 准备您的生命值条素材（背景、填充、覆盖层）
2. 将素材导入到 `textures/ui/` 目录
3. 在编辑器中打开 `BattleHUD.tscn`
4. 选择 `HealthBar` 节点
5. 在 Inspector 中拖拽您的素材到对应的纹理槽
6. 运行游戏测试效果

祝您制作顺利！🎮

