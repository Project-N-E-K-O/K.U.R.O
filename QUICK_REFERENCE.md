# 快速参考卡片

## 📍 文件快速查询

| 功能 | 文件路径 | 关键类 |
|------|---------|--------|
| **敌人生成** | `scripts/controllers/EnemySpawnManager.cs` | `EnemySpawnManager` |
| 敌人运行时管理 | `scripts/controllers/EnemySpawnController.cs` | `EnemySpawnController` |
| 敌人生成标记 | `scripts/controllers/EnemySpawnMarker.cs` | `EnemySpawnMarker` |
| 敌人配置文件 | `scripts/controllers/EnemySpawnProfile.cs` | `EnemySpawnProfile` |
| **摄像头管理** | `scripts/managers/CameraZoneManager.cs` | `CameraZoneManager` |
| 摄像头跟随 | `scripts/managers/CameraFollow.cs` | `CameraFollow` |
| 摄像头抖动 | `scripts/managers/CameraShakeEffect.cs` | `CameraShakeEffect` |
| **伤害检测** | `scripts/actors/heroes/SamplePlayer.cs` | `SamplePlayer` |
| 物品持握 | `scripts/actors/heroes/PlayerItemAttachment.cs` | `PlayerItemAttachment` |
| 世界物品 | `scripts/items/world/RigidBodyWorldItemEntity.cs` | `RigidBodyWorldItemEntity` |

---

## 🎮 导出属性速查

### EnemySpawnManager 关键属性

**敌人配置**:
```csharp
public PackedScene EnemyScene { get; set; }
public Array<PackedScene> EnemyScenes { get; set; }
public int SpawnCount { get; set; } = 1;
public float SpawnInterval { get; set; } = 0.15f;
```

**生成位置**:
```csharp
public bool EnableSmartSpawnPlacement { get; set; } = true;
public uint ObstacleCheckMask { get; set; } = 1u;  // Layer 1
public int MaxSpawnAttempts { get; set; } = 10;
public float SpawnCheckRadius { get; set; } = 60f;
```

### CameraZoneManager 关键属性

```csharp
public Camera2D? TargetCamera { get; set; }
public Node2D? Player { get; set; }
public int Zone1_LimitLeft = -9300;
public int Zone1_LimitRight = -3650;
public int Zone2_LimitLeft = -3650;
public int Zone2_LimitRight = 5000;
```

---

## 📡 信号和事件

### EnemySpawnManager 信号

```csharp
[Signal] public delegate void SpawnStartedEventHandler();
[Signal] public delegate void EnemySpawnedEventHandler(Node enemy, int index);
[Signal] public delegate void SpawnCompletedEventHandler();
```

**使用示例**:
```csharp
spawnManager.SpawnCompleted += () => { OnBattleStart(); };
```

---

## 🔧 常用配置代码片段

### 快速启动敌人生成

```csharp
var spawnManager = GetNode<EnemySpawnManager>("EnemySpawnManager");
spawnManager.StartSpawnSequence();
```

### 切换摄像头区域

```csharp
var cameraManager = GetNode<CameraZoneManager>("CameraZoneManager");
cameraManager.SwitchToZone(0);  // 切换到第一个区域
```

### 配置碰撞层

```csharp
const uint LAYER_ENEMY = 2;
const uint MASK_ENEMY = 1u << 2;

attackArea.CollisionLayer = 0;
attackArea.CollisionMask = MASK_ENEMY;
```

---

## 🎯 碰撞层速查表

```
Layer 0 (bit 0)  → PLAYER          (1)
Layer 1 (bit 1)  → FURNITURE       (2)
Layer 2 (bit 2)  → ENEMY           (4)
Layer 3 (bit 3)  → PLAYER_ATTACK   (8)
Layer 4 (bit 4)  → PICKUP_AREA    (16)
```

**遮罩计算**: `1u << LayerNumber`

---

## ⚠️ 常见错误

| 问题 | 原因 | 解决方案 |
|------|------|---------|
| 敌人无法生成 | EnemyScene 为 null | 在编辑器中分配敌人场景 |
| 敌人生成在地板下 | SmartSpawnPlacement 失效 | 确认 ObstacleCheckMask 指向 Layer 1 |
| 相机不切换区域 | Player 未找到 | 手动分配 Player 或确保其在"player"组中 |
| 伤害无法命中 | 碰撞层/掩码配置错误 | 检查 AttackArea.CollisionMask 是否包含敌人层 |
| 生成位置不合理 | MaxSpawnAttempts 太少 | 增加 MaxSpawnAttempts 或 SpawnCheckRadius |

---

## 📊 系统流程速览

### 敌人生成流程

```
玩家进入 TriggerArea
    ↓
OnTriggerBodyEntered()
    ↓
StartSpawnSequence()
    ↓
SpawnStarted 信号
    ↓
FOR 每个敌人:
    ├─ 计算位置 (智能检测)
    ├─ 播放效果
    ├─ 生成敌人
    └─ EnemySpawned 信号
    ↓
SpawnCompleted 信号
```

### 摄像头切换流程

```
玩家移动到区域边界
    ↓
HitArea 进入 Zone Area
    ↓
AreaEntered 信号
    ↓
IsPlayerHitArea() 验证
    ↓
SwitchToZone()
    ↓
更新 Camera2D.Limit*
    ↓
摄像头下一帧生效
```

---

## 🔍 调试技巧

### 打印生成信息

```csharp
spawnManager.LogSpawnEffectPositions = true;
spawnManager.ShowDebugOverlayInGame = true;
```

### 实时检查敌人

```csharp
var enemies = GetTree().GetNodesInGroup("enemy");
GD.Print($"敌人数量: {enemies.Count}");
```

### 检查摄像头配置

```csharp
var currentZone = cameraManager.GetCurrentZone();
GD.Print($"当前区域: {currentZone?.Name}");
```

---

## 📚 详细文档

| 文件 | 用途 |
|------|------|
| [CODE_STRUCTURE_ANALYSIS.md](CODE_STRUCTURE_ANALYSIS.md) | 详细代码结构分析 |
| [SYSTEM_ARCHITECTURE_VISUAL.md](SYSTEM_ARCHITECTURE_VISUAL.md) | 系统架构关系图 |
| [IMPLEMENTATION_GUIDE.md](IMPLEMENTATION_GUIDE.md) | 实际操作指南 |
| [CAMERA_ZONE_SETUP_GUIDE.md](CAMERA_ZONE_SETUP_GUIDE.md) | 摄像头配置教程 |
| [HITBOX_SYSTEM.md](HITBOX_SYSTEM.md) | 伤害检测系统详解 |

---

## 🚀 快速开始示例

### 最小化敌人生成配置

```csharp
public partial class QuickSpawner : Node
{
    public override void _Ready()
    {
        var manager = GetNode<EnemySpawnManager>("EnemySpawnManager");
        manager.EnemyScene = GD.Load<PackedScene>("res://scenes/actors/enemies/Enemy_A1_zhuA.tscn");
        manager.SpawnCount = 3;
        manager.StartSpawnSequence();
    }
}
```

### 最小化摄像头配置

```csharp
public partial class QuickCamera : Node
{
    public override void _Ready()
    {
        var manager = GetNode<CameraZoneManager>("CameraZoneManager");
        manager.TargetCamera = GetNode<Camera2D>("Camera2D");
        manager.Player = GetNode<Node2D>("Player");
    }
}
```

---

**最后更新**: 2026-04-30  
**生成工具**: GitHub Copilot  
**分析范围**: KURO 项目完整代码库
