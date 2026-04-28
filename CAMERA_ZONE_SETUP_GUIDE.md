# 相机多区域切换系统使用指南

## 系统概述
已为 Stage_2 场景配置了多相机区域系统。玩家在进入不同的区域时，相机会自动切换到对应的区域限制范围。

## 当前配置

### 相机区域 1 (Zone_1_左侧房间)
- **区域范围**：X: -9500 到 -3850, Y: -1600 到 1500
- **相机限制**：
  - Left: -9300
  - Top: -1500
  - Right: -3650
  - Bottom: 1500
- **大小**：5650 x 3100 像素

### 相机区域 2 (Zone_2_右侧房间) - 您要添加的新区域
- **区域范围**：X: -3650 到 5000, Y: -1600 到 1500  
- **相机限制**：
  - Left: -3650
  - Top: -1500
  - Right: 5000
  - Bottom: 1500
- **大小**：8650 x 3100 像素

## 文件结构

```
Stage_2.tscn
├── BattleScene (根节点)
│   ├── CameraZoneManager (新增)
│   │   └── 配置相机区域和玩家追踪
│   └── World
│       └── MainCharacter
│           └── Camera2D (被管理的相机)
```

## 工作原理

```
步骤1: 玩家移动
    ↓
步骤2: CameraZoneManager 检测玩家位置
    ↓
步骤3: 判断玩家是否进入新区域
    ↓
步骤4: 更新 Camera2D 的限制范围
    ↓
步骤5: CameraFollow 根据新限制范围跟随玩家（平滑过渡）
```

## 如何在编辑器中配置

### 方式1：通过 Inspector 面板配置（推荐）

1. **在 Godot 编辑器中打开 Stage_2.tscn**
2. **在场景树中选中 "CameraZoneManager" 节点**
3. **在右侧 Inspector 面板中找到 "相机区域配置" (Camera Zones) 属性**
4. **展开数组并编辑各个区域：**
   - **Name**: 区域名称 (例如: Zone_1_左侧房间)
   - **区域边界**:
     - BoundsX: 区域左边界 X 坐标
     - BoundsY: 区域上边界 Y 坐标
     - BoundsWidth: 区域宽度
     - BoundsHeight: 区域高度
   - **相机限制**:
     - LimitLeft: 相机最左位置
     - LimitTop: 相机最上位置
     - LimitRight: 相机最右位置
     - LimitBottom: 相机最下位置

### 方式2：直接修改脚本

编辑 [scripts/managers/CameraZoneManager.cs](scripts/managers/CameraZoneManager.cs) 文件中的 `CameraZones` 数组定义。

## 添加第三个相机区域的步骤

### 步骤1：在编辑器中配置

1. 选中 CameraZoneManager 节点
2. 在 Inspector 中找到 "相机区域配置" 
3. 点击数组大小从 2 改为 3
4. 编辑新区域的所有参数

### 步骤2：配置新区域参数

假设您要添加第三个区域（Zone_3），用来管理右侧的新区域：

```
Zone_3_右侧房间
├─ BoundsX: 5000
├─ BoundsY: -1600
├─ BoundsWidth: 8000
├─ BoundsHeight: 3100
├─ LimitLeft: 5000
├─ LimitTop: -1500
├─ LimitRight: 13000
└─ LimitBottom: 1500
```

## 参数解释详解

### 区域边界 (Bounds)

这四个参数定义了一个矩形区域，当玩家进入该区域时，相机会切换到对应的限制范围。

- **BoundsX**: 矩形左上角的 X 坐标
- **BoundsY**: 矩形左上角的 Y 坐标
- **BoundsWidth**: 矩形的宽度
- **BoundsHeight**: 矩形的高度

**计算区域范围：**
- 左边界 = BoundsX
- 上边界 = BoundsY
- 右边界 = BoundsX + BoundsWidth
- 下边界 = BoundsY + BoundsHeight

### 相机限制 (Camera Limits)

这四个参数定义了相机在该区域内可以移动的范围。

- **LimitLeft**: 相机中心能到达的最左 X 坐标
- **LimitTop**: 相机中心能到达的最上 Y 坐标
- **LimitRight**: 相机中心能到达的最右 X 坐标
- **LimitBottom**: 相机中心能到达的最下 Y 坐标

**通常情况：**
- 相机限制 ≈ 场景可见区域的边界
- 这样可以防止相机超出场景可见范围

## 调试和可视化

### 编辑器中的视觉反馈

在 Godot 编辑器中运行场景时，各个相机区域会以矩形的形式显示在 Scene 视图中：

- **绿色矩形 + 区域标签**: 当前激活的相机区域
- **黄色矩形 + 区域标签**: 其他的相机区域（非活跃区域）

**透明度说明：**
- 矩形内部是半透明的（20% 不透明度），便于查看背景
- 矩形边框是实线（2 像素宽），边界清晰可见

### 日志输出

系统会在以下时刻输出日志信息（查看 Output 面板）：

```
[INFO] CameraZoneManager: 相机区域管理器已初始化，共有 2 个区域
[INFO] CameraZoneManager: ✓ 切换到相机区域: Zone_1_左侧房间 (Left:-9300, Top:-1500, Right:-3650, Bottom:1500)
[INFO] CameraZoneManager: ✓ 切换到相机区域: Zone_2_右侧房间 (Left:-3650, Top:-1500, Right:5000, Bottom:1500)
```

## 调整相机平滑过渡

虽然区域切换是即时的，但相机会平滑地过渡到新的限制范围内。您可以调整这个过渡速度：

1. 选中 **World/MainCharacter/Camera2D** 节点
2. 在 Inspector 中找到 **CameraFollow** 脚本设置
3. 调整 **FollowSpeed** 属性：
   - 数值越小，过渡越慢
   - 数值越大，过渡越快
   - 推荐值范围：1.0 - 10.0

## 常见问题

### Q1: 相机不切换怎么办？

**检查清单：**
1. ✓ CameraZoneManager 的 TargetCamera 是否正确指向 Camera2D？
2. ✓ Player 是否正确指向 MainCharacter？
3. ✓ 玩家位置是否确实在某个区域的边界内？
4. ✓ 查看 Output 面板是否有错误信息

**调试方法：**
- 在 Scene 视图中查看黄色/绿色矩形是否正确显示区域边界
- 实时拖动 MainCharacter 节点，观察区域颜色是否改变
- 查看 Console 中的日志是否有"切换到相机区域"的消息

### Q2: 相机突然跳转而不是平滑过渡

这是正常现象。系统设计是：
1. **区域切换**（即时）：当玩家进入新区域时，相机限制立即更新
2. **相机平滑移动**（渐进）：相机根据新限制平滑地移动到目标位置

如果您希望减少不适感，可以：
- 增加相机区域的重叠区域
- 调整 FollowSpeed 使过渡更缓和
- 调整区域边界使其与场景构成相匹配

### Q3: 区域之间有重叠或间隙怎么办

**建议做法：**
- 使区域相邻但不重叠（推荐）
- 或者让区域有适当的重叠区域（100-200 像素）

**验证方法：**
- 运行场景并在 Scene 视图中查看矩形边界
- 确保整个可玩区域都被至少一个区域覆盖

### Q4: 如何禁用某个区域？

**临时禁用：**
- 将区域的宽度或高度设置为 0
- 或者将其移动到远离玩家的位置

**永久删除：**
- 在 Inspector 中减少数组大小
- 或在脚本中删除相应的 CameraZone 定义

### Q5: 能否添加相机过渡动画（淡入淡出）？

当前系统不包含这个功能。如果需要：
1. 修改 CameraZoneManager 在切换时调用特殊方法
2. 创建一个 ScreenTransition 系统
3. 或使用 Tween 动画库实现过渡效果

## 性能注意事项

- CameraZoneManager 在每一帧检查玩家位置（O(n) 复杂度，n = 区域数）
- 对于大多数游戏，少于 10 个区域不会造成性能问题
- 如果有超过 20 个区域，建议优化检测算法

## 高级用法

### 运行时添加/移除区域

```csharp
// 添加新区域
var newZone = new CameraZoneManager.CameraZone
{
    Name = "Dynamic_Zone",
    BoundsX = 0,
    BoundsY = 0,
    BoundsWidth = 1000,
    BoundsHeight = 1000,
    LimitLeft = 0,
    LimitTop = 0,
    LimitRight = 1000,
    LimitBottom = 1000
};
cameraZoneManager.AddZone(newZone);

// 移除区域
cameraZoneManager.RemoveZone(2);

// 获取当前区域
var currentZone = cameraZoneManager.GetCurrentZone();
```

### 按名称查找区域

```csharp
var zone = cameraZoneManager.GetZoneByName("Zone_1_左侧房间");
if (zone != null)
{
    // 区域存在
}
```

## 相关文件

- 主脚本：[scripts/managers/CameraZoneManager.cs](scripts/managers/CameraZoneManager.cs)
- 场景文件：[scenes/Stage_2.tscn](scenes/Stage_2.tscn)
- 相机脚本：[scripts/managers/CameraFollow.cs](scripts/managers/CameraFollow.cs)

## 测试场景流程

1. **启动场景**
   - 查看 Output 是否有"已初始化"消息
   - 查看 Scene 中是否显示区域矩形

2. **移动玩家到第一个区域**
   - 查看相机限制是否正确应用
   - 观察 Output 中的切换日志

3. **移动玩家到第二个区域**
   - 观察相机是否平滑过渡
   - 查看相机限制是否更新

4. **添加新区域后**
   - 重复以上步骤
   - 确保新区域的边界正确可视化

## 下一步计划

- [x] 实现基础的多区域切换
- [x] 添加编辑器可视化
- [x] 支持动态区域管理
- [ ] 添加区域过渡动画
- [ ] 添加区域相关的触发事件系统
- [ ] 创建区域编辑器工具
