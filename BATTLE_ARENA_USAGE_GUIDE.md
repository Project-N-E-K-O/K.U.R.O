# 战斗区域系统（Battle Arena System）- 使用指南

## 概述

战斗区域系统是一个完整的区域战斗管理方案，包含以下功能：

1. **空气墙边界** — 阻挡玩家和敌人离开战斗区域
2. **相机限制** — 自动切换相机限制到战斗区域
3. **敌人追踪** — 监听敌人死亡事件
4. **自动清理** — 所有敌人击杀完成后自动移除空气墙和恢复相机

## 核心组件

### 1. BattleArenaManager.cs
**位置:** `scripts/managers/BattleArenaManager.cs`

战斗区域的主管理器，负责：
- 创建空气墙边界
- 管理相机区域切换
- 监听敌人死亡
- 清理资源

**关键方法：**
```csharp
InitializeBattleArea(
    Rect2 arenaRect,           // 战斗区域矩形（世界坐标）
    List<GameActor> enemies,   // 生成的敌人列表
    CameraZoneManager camera,  // 相机管理器
    string arenaId             // 区域标识（可选）
)
```

### 2. BattleArenaBoundary.cs
**位置:** `scripts/managers/BattleArenaBoundary.cs`

空气墙实现，由 4 个 StaticBody2D 组成：
- TopWall（上边界）
- BottomWall（下边界）
- LeftWall（左边界）
- RightWall（右边界）

### 3. EnemySpawnManager.cs（修改版本）
**新增 Battle Arena 分类配置：**
```
Enable Battle Arena    - 启用战斗区域
Battle Arena Size      - 战斗区域大小（宽 × 高）
Battle Arena Offset    - 战斗区域相对于生成点的偏移
```

### 4. CameraZoneManager.cs（扩展版本）
**新增方法：**
```csharp
CreateAndSwitchTemporaryCameraZone(Rect2 arenaRect, string zoneName)
RemoveTemporaryCameraZone(string zoneName)
SwitchToZone(string zoneName)                    // 按名称切换
CurrentZoneName { get; }                         // 获取当前区域名
```

## 使用步骤

### 步骤 1：配置 EnemySpawnManager

在场景中选择 `EnemySpawnManager` 节点，设置以下参数：

**Inspector 面板：**
```
Battle Arena
├── Enable Battle Arena = true          ✓ 启用
├── Battle Arena Size = (800, 600)      // 调整为合适大小
└── Battle Arena Offset = (0, 0)        // 相对于生成点的偏移（可选）
```

### 步骤 2：测试生成

运行场景，当玩家触发 EnemySpawnManager 的触发区域时：

1. **敌人生成** → 列表被记录
2. **空气墙创建** → 4 面边界立即出现
3. **相机切换** → 视角限制到战斗区域
4. **敌人击杀** → 监听死亡事件
5. **全灭时** → 空气墙消失，相机恢复

## 配置示例

### 示例 1：小房间战斗（800×600）
```
Enable Battle Arena = true
Battle Arena Size = (800, 600)
Battle Arena Offset = (0, 0)
```

### 示例 2：宽敞大厅（1200×600）
```
Enable Battle Arena = true
Battle Arena Size = (1200, 600)
Battle Arena Offset = (100, -50)  // 向右偏移100，向上偏移50
```

## 信号和事件

### BattleArenaManager 信号

```csharp
// 战斗开始
BattleArenaStarted(string arenaId)

// 战斗完成
BattleArenaCompleted(string arenaId)
```

### 使用示例

```csharp
var battleArena = new BattleArenaManager();
battleArena.BattleArenaCompleted += (arenaId) =>
{
    GD.Print($"战斗完成: {arenaId}");
};
```

## 高级配置

### 调整边界厚度

编辑 `BattleArenaManager` 导出属性：
```
Boundary Thickness = 2              // 默认 2 像素
```

### 调整碰撞层

```
Boundary Collision Layer = 5        // 空气墙所在层
Boundary Collision Mask = 0b101     // Layer 0 (玩家) + Layer 2 (敌人)
```

### 手动初始化

如果不想通过 EnemySpawnManager 自动初始化，可手动调用：

```csharp
var battleManager = new Kuros.Managers.BattleArenaManager();
GetTree().Root.AddChild(battleManager);

battleManager.InitializeBattleArea(
    new Rect2(spawnPos - arenaSize/2, arenaSize),
    spawnedEnemies,
    cameraZoneManager,
    "my_battle_arena"
);
```

## 问题排查

### 问题 1：空气墙不出现
**解决方案：**
- 检查 `Enable Battle Arena` 是否设为 `true`
- 确保有敌人生成（`_spawnedEnemies.Count > 0`）
- 查看控制台日志是否有错误信息

### 问题 2：相机不切换
**解决方案：**
- 确保 `CameraZoneManager` 存在于场景（通常在 UIManager）
- 检查 TargetCamera 是否正确设置
- 查看日志信息 `"CameraZoneManager not found"`

### 问题 3：战斗完成后空气墙不消失
**解决方案：**
- 确保敌人正确继承自 `GameActor` 类
- 检查敌人的 `Died` 信号是否正常触发
- 手动测试：在编辑器中将敌人设为 dead 状态

## 文件清单

**新增文件：**
- `scripts/managers/BattleArenaManager.cs`
- `scripts/managers/BattleArenaBoundary.cs`

**修改文件：**
- `scripts/managers/CameraZoneManager.cs`
- `scripts/controllers/EnemySpawnManager.cs`

## 扩展建议

1. **添加进入/退出动画** — 在空气墙出现时播放特效
2. **战斗音乐** — 战斗开始时切换背景音乐
3. **UI 提示** — 显示剩余敌人数量
4. **奖励系统** — 战斗完成时发放奖励

---

**最后更新:** 2026-04-30  
**版本:** 1.0.0
