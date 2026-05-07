# KURO 项目代码结构分析

**分析时间**: 2026年4月30日  
**项目类型**: Godot C# (GodotSharp)  
**游戏引擎**: Godot 4.5.1

---

## 目录

1. [敌人生成系统](#1-敌人生成系统)
2. [摄像头管理系统](#2-摄像头管理系统)
3. [碰撞与物理系统](#3-碰撞与物理系统)
4. [系统集成方案](#4-系统集成方案)

---

## 1. 敌人生成系统

### 1.1 核心文件位置

```
scripts/controllers/
├── EnemySpawnManager.cs        ⭐ 主生成管理器
├── EnemySpawnController.cs     📍 运行时初始化与复活
├── EnemySpawnMarker.cs         🎯 编辑器辅助标记
├── EnemySpawnProfile.cs        ⚙️ 生成配置文件
└── EnemyThinSkillSpawnManager.cs  特殊敌人技能生成
```

### 1.2 EnemySpawnManager 详解

**文件**: [scripts/controllers/EnemySpawnManager.cs](scripts/controllers/EnemySpawnManager.cs)

#### 核心特性

```csharp
public partial class EnemySpawnManager : Node2D
```

#### 关键导出属性

**敌人配置**:
- `EnemyScene` (PackedScene) - 单个敌人场景
- `EnemyScenes` (Array<PackedScene>) - 多个敌人场景列表
- `MultiEnemySelectionMode` - 敌人选择模式 (Sequential/Random)
- `SpawnCount` (1-100) - 生成敌人数量
- `SpawnInterval` (0-10秒) - 敌人间隔生成时间

**触发配置**:
- `TriggerArea` (Area2D) - 触发区域
- `AutoConfigureAssignedTriggerArea` - 自动配置触发区域
- `TriggerGroupName` ("player") - 检测的物体组名
- `TriggerSize` (Vector2) - 触发区域大小
- `TriggerCollisionLayer/Mask` - 碰撞层和遮罩配置

**生成位置**:
- `UseExplicitSpawnOffsets` - 使用明确的生成位置偏移
- `SpawnOffsets` (Array<Vector2>) - 显式生成点偏移列表
- `SpawnAreaExtents` - 随机生成范围
- `EnemySpawnOffset` (Vector2) - 敌人相对于锚点的偏移

**智能落点检测**:
- `EnableSmartSpawnPlacement` - 启用智能生成位置检测
- `ObstacleCheckMask` (默认=Layer 1) - 检测家具/静态体的碰撞层
- `MaxSpawnAttempts` (1-30) - 最多尝试生成次数
- `SpawnCheckRadius` (4-500) - 检测障碍物的搜索半径

**生成效果**:
- `SpawnBackEffectScene` - 背景出场效果
- `SpawnFrontEffectScene` - 前景出场效果
- `EnemyAppearDelay` (0-5秒) - 敌人出现延迟
- `EnemyAppearGateMode` - 出现门控模式 (Delay/BackEffectFrame/BackEffectFinished)
- `BackEffectAppearFrame` (0-300帧) - 背景效果出现帧数

#### 核心信号

```csharp
[Signal] public delegate void SpawnStartedEventHandler();           // 生成开始
[Signal] public delegate void EnemySpawnedEventHandler(Node enemy, int index); // 单个敌人生成完成
[Signal] public delegate void SpawnCompletedEventHandler();        // 全部生成完成
```

#### 关键方法

```csharp
public void StartSpawnSequence()          // 启动生成序列
public void ResetTrigger()                // 重置触发状态

// 私有异步方法
private async System.Threading.Tasks.Task SpawnSequenceAsync()    // 异步生成流程
private Node? SpawnEnemy(PackedScene enemyScene, Vector2 spawnPosition, int spawnIndex)
private List<PackedScene> BuildSpawnQueue()    // 构建敌人队列
private Vector2 ResolveSpawnPosition(int index) // 计算生成位置
private SpawnEffectRefs PlaySpawnEffects(Vector2 position) // 播放生成效果
```

#### 生成流程

```
触发（TriggerArea接收身体进入事件）
    ↓
OnTriggerBodyEntered() 触发
    ↓
StartSpawnSequence() 启动
    ↓
EmitSignal(SpawnStarted)
    ↓
BuildSpawnQueue() 构建敌人队列
    ↓
对每个敌人循环：
    ├─ ResolveSpawnPosition(i) 计算位置
    ├─ PlaySpawnEffects() 播放背景效果
    ├─ WaitForEnemyAppearGateAsync() 等待出现条件
    ├─ SpawnEnemy() 生成敌人实例
    ├─ EmitSignal(EnemySpawned, enemy, i)
    └─ 等待 SpawnInterval 后生成下一个
    ↓
EmitSignal(SpawnCompleted)
```

### 1.3 EnemySpawnController 运行时管理

**文件**: [scripts/controllers/EnemySpawnController.cs](scripts/controllers/EnemySpawnController.cs)

```csharp
public partial class EnemySpawnController : Node2D
{
    [Export] public bool AutoRespawn = false;
    [Export(PropertyHint.Range, "0.5,60,0.5")] public float RespawnDelay = 5f;
    private readonly List<GameActor> _managedActors = new();
}
```

**职责**:
- 管理作为子节点放置的敌人实例
- 运行时初始化敌人
- 支持敌人自动复活（可配置复活延迟）

**关键特性**:
- 自动追踪场景树中的 GameActor 子节点
- 当敌人死亡/离开时，可自动在设定时间后复活
- 支持动态添加新的子敌人节点

### 1.4 配置生成区域的方法

**方法1：通过编辑器Inspector配置**
1. 选中 EnemySpawnManager 节点
2. 在 Inspector 中展开 "Enemy" 分类
3. 设置 TriggerSize (例如 320x180)
4. 设置 SpawnAreaExtents (例如 96x48)

**方法2：程序化配置**
```csharp
var spawnManager = GetNode<EnemySpawnManager>("path/to/EnemySpawnManager");
spawnManager.EnemyScene = enemyScene;
spawnManager.SpawnCount = 3;
spawnManager.SpawnInterval = 0.15f;
spawnManager.TriggerSize = new Vector2(320, 180);
spawnManager.SpawnAreaExtents = new Vector2(96, 48);
```

### 1.5 生成完成回调

使用信号系统：

```csharp
// 在父节点中
[Export] public NodePath SpawnManagerPath = new("path/to/EnemySpawnManager");

public override void _Ready()
{
    var spawnManager = GetNode<EnemySpawnManager>(SpawnManagerPath);
    spawnManager.SpawnStarted += OnSpawnStarted;
    spawnManager.EnemySpawned += OnEnemySpawned;
    spawnManager.SpawnCompleted += OnSpawnCompleted;
}

private void OnSpawnCompleted()
{
    GD.Print("所有敌人生成完成！");
    // 触发下一阶段逻辑
}
```

---

## 2. 摄像头管理系统

### 2.1 核心文件位置

```
scripts/managers/
├── CameraZoneManager.cs        ⭐ 多区域相机管理
├── CameraFollow.cs             📍 相机跟随逻辑
└── CameraShakeEffect.cs        🎬 相机抖动效果
```

### 2.2 CameraZoneManager 详解

**文件**: [scripts/managers/CameraZoneManager.cs](scripts/managers/CameraZoneManager.cs)

#### 核心类结构

```csharp
public partial class CameraZoneManager : Node
{
    [System.Serializable]
    public class CameraZone
    {
        [Export] public string Name { get; set; } = "Zone";
        [Export] public int LimitLeft { get; set; } = 0;
        [Export] public int LimitTop { get; set; } = 0;
        [Export] public int LimitRight { get; set; } = 0;
        [Export] public int LimitBottom { get; set; } = 0;
    }
}
```

#### 关键导出属性

**相机配置**:
- `TargetCamera` (Camera2D) - 被管理的相机
- `Player` (Node2D) - 玩家节点（自动查找）

**区域配置** (当前示例):
- Zone 1: 左侧房间
  - LimitLeft: -9300, Top: -1500, Right: -3650, Bottom: 1500
- Zone 2: 右侧房间
  - LimitLeft: -3650, Top: -1500, Right: 5000, Bottom: 1500

**Area2D 路径**:
- `Zone1AreaPath` - Zone 1 对应的Area2D节点路径
- `Zone2AreaPath` - Zone 2 对应的Area2D节点路径

#### 区域定义区间

- **LimitLeft**: 相机中心能到达的最左 X 坐标
- **LimitTop**: 相机中心能到达的最上 Y 坐标
- **LimitRight**: 相机中心能到达的最右 X 坐标
- **LimitBottom**: 相机中心能到达的最下 Y 坐标

#### 区域切换工作原理

```
玩家移动
    ↓
CameraZoneManager._Process() 每帧检查
    ↓
通过 Area2D 检测玩家 HitArea 进入/离开
    ↓
OnZoneXAreaEntered() 触发
    ↓
SwitchToZone(int zoneIndex)
    ↓
更新 Camera2D 的四个 Limit 属性
    ↓
CameraFollow 在后续帧使用新限制
```

#### 核心方法

```csharp
// 公开 API
public void SwitchToZone(int zoneIndex)              // 切换到指定区域
public CameraZone? GetCurrentZone()                  // 获取当前区域
public CameraZone? GetZoneByName(string name)       // 按名称获取区域
public string GetDebugInfo()                         // 获取调试信息

// 内部方法
private void InitializeAreas()                       // 初始化 Area2D 节点
private void SubscribeAreaSignals()                  // 订阅区域信号
private void OnZone1AreaEntered(Area2D area)        // Zone1 进入回调
private void OnZone2AreaEntered(Area2D area)        // Zone2 进入回调
private bool IsPlayerHitArea(Area2D area)           // 判断是否为玩家 HitArea
```

#### 区域切换日志示例

```
[INFO] CameraZoneManager: 相机区域管理器已初始化，共有 2 个区域
[INFO] CameraZoneManager: ✓ 切换到相机区域: Zone_1_左侧房间 (Left:-9300, Top:-1500, Right:-3650, Bottom:1500)
[INFO] CameraZoneManager: ✓ 切换到相机区域: Zone_2_右侧房间 (Left:-3650, Top:-1500, Right:5000, Bottom:1500)
```

### 2.3 动态修改区域

**支持动态修改**：是的，完全支持！

```csharp
// 获取 CameraZoneManager
var cameraZoneManager = GetNode<CameraZoneManager>("path/to/CameraZoneManager");

// 方式1：添加新区域
var newZone = new CameraZoneManager.CameraZone
{
    Name = "Zone_3_新区域",
    LimitLeft = 5000,
    LimitTop = -1500,
    LimitRight = 13000,
    LimitBottom = 1500
};
cameraZoneManager.AddZone(newZone);

// 方式2：移除区域
cameraZoneManager.RemoveZone(2);

// 方式3：获取当前区域
var currentZone = cameraZoneManager.GetCurrentZone();
if (currentZone != null)
{
    GD.Print($"当前区域: {currentZone.Name}");
}

// 方式4：按名称查询
var zone = cameraZoneManager.GetZoneByName("Zone_1_左侧房间");
if (zone != null)
{
    cameraZoneManager.SwitchToZone(0);
}
```

#### 区域检测机制

```
玩家在 Stage_2 中：
    ↓
玩家有 HitArea (Area2D) 子节点
    ↓
两个 Zone Area2D 监听 AreaEntered 信号
    ↓
当玩家 HitArea 进入 Zone1Area 时：
    ├─ OnZone1AreaEntered() 触发
    ├─ IsPlayerHitArea() 验证是玩家
    └─ SwitchToZone(0) 切换相机
```

### 2.4 CameraZone 的初始化方式

在 `_Ready()` 中硬编码：

```csharp
CameraZones = new CameraZone[]
{
    new CameraZone 
    { 
        Name = "Zone_1_左侧房间",
        LimitLeft = Zone1_LimitLeft,
        LimitTop = Zone1_LimitTop,
        LimitRight = Zone1_LimitRight,
        LimitBottom = Zone1_LimitBottom
    },
    new CameraZone 
    { 
        Name = "Zone_2_右侧房间",
        LimitLeft = Zone2_LimitLeft,
        LimitTop = Zone2_LimitTop,
        LimitRight = Zone2_LimitRight,
        LimitBottom = Zone2_LimitBottom
    }
};
```

---

## 3. 碰撞与物理系统

### 3.1 碰撞层配置

#### Godot 碰撞层体系

在 Godot 中，每个物体有两个碰撞相关属性：

**CollisionLayer** (图层):
- 物体本身所在的物理图层 (bit 0-31)
- 用来标识 "我是谁"

**CollisionMask** (遮罩):
- 物体检测的其他图层 (bit 0-31)
- 用来标识 "我检测谁"

#### 项目中的碰撞层分配

**已使用的碰撞层**：

| 层 | 名称 | 用途 | 示例 |
|---|------|------|------|
| Layer 0 | Player | 玩家身体 | SamplePlayer.cs |
| Layer 1 | Furniture/Environment | 家具、环保障碍物 | 生成检测障碍物 |
| Layer 2 | Enemy | 敌人身体 | EnemyA1_zhuA.cs 等 |
| Layer 3 | PlayerAttack | 玩家攻击区域 | AttackArea (不在任何图层) |
| Layer 4 | PickupArea | 可拾取物品区域 | WorldItemSpawner |
| Layer 5+ | 未确定 | 待扩展 | - |

#### Stage_2 具体配置示例

从 Stage_2.tscn 中的 EnemySpawnManager：

```
TriggerCollisionLayer = 1        # Layer 1（家具层）
TriggerCollisionMask = 1         # 检测 Layer 1
```

#### 伤害检测碰撞配置

从 [HITBOX_SYSTEM.md](HITBOX_SYSTEM.md)：

```csharp
// 玩家的 AttackArea 配置
area.CollisionLayer = 0;                           // 不在任何图层（不产生物理碰撞）
area.CollisionMask = player.AttackArea.CollisionMask;  // 检测敌人所在的图层
```

**关键设计模式**：
- AttackArea 的 CollisionLayer = 0（纯检测用，不参与物理）
- AttackArea 的 CollisionMask 指向敌人碰撞层

#### 物品拾取碰撞配置

从 [RigidBodyWorldItemEntity.cs](scripts/items/world/RigidBodyWorldItemEntity.cs)：

```csharp
[Export] public uint GrabAreaCollisionLayer { get; set; } = 1u << 1;  // Layer 2
[Export] public uint GrabAreaCollisionMask { get; set; } = 1u;        // Layer 1 (检测玩家)
```

#### 重力炸弹吸附碰撞配置

从 [GravityGrenadeBlackHole.cs](scripts/items/world/GravityGrenadeBlackHole.cs)：

```csharp
// 设置碰撞层配置
_attractArea.CollisionLayer = 0;
_attractArea.CollisionMask = 1u << 1;  // 检测敌人层 (Layer 2)
```

### 3.2 "空气墙" 机制

**目前的发现**：
- ❌ 没有发现专门的"空气墙"系统
- ✅ 相机限制（Camera Limits）实现了视觉边界
- ✅ Godot 的 Camera2D 使用 `LimitLeft/Top/Right/Bottom` 限制相机范围
- ✅ 敌人生成智能检测障碍物（Layer 1）

**实现边界的方式**：

1. **通过 Area2D 检测**:
   - 在关卡边界放置 Area2D
   - 监听 AreaEntered 信号
   - 将玩家/敌人弹回

2. **通过 StaticBody2D/CharacterBody2D 碰撞**:
   - 在关卡边界放置物理体
   - 让玩家与其碰撞而不穿过

3. **通过代码检查**:
   ```csharp
   if (player.GlobalPosition.X < LimitLeft)
   {
       player.GlobalPosition = new Vector2(LimitLeft, player.GlobalPosition.Y);
   }
   ```

### 3.3 玩家和敌人碰撞层配置

#### 玩家碰撞配置

玩家通常在 **Layer 0**，具有基础物理碰撞。

#### 敌人碰撞配置

敌人在 **Layer 2**（从生成检测可以推断）：
- `ObstacleCheckMask = 1u << 1` 表示检测 Layer 1 的障碍物
- 敌人的 HitBox 应该在 Layer 2

#### 伤害检测层间关系

```
玩家 AttackArea (Layer 0)
    ↓ 检测 →
敌人 HitBox (Layer 2)
    
敌人 AttackArea (某个层)
    ↓ 检测 →
玩家 (Layer 0)
```

### 3.4 智能落点检测原理

从 EnemySpawnManager：

```csharp
[Export] public bool EnableSmartSpawnPlacement { get; set; } = true;
[Export(PropertyHint.Layers2DPhysics)] public uint ObstacleCheckMask { get; set; } = 1u;
[Export(PropertyHint.Range, "1,30,1")] public int MaxSpawnAttempts { get; set; } = 10;
[Export(PropertyHint.Range, "4,500,2")] public float SpawnCheckRadius { get; set; } = 60f;
```

**流程**：
1. 尝试在 SpawnAreaExtents 范围内生成敌人
2. 对生成点进行圆形物理查询（半径 SpawnCheckRadius）
3. 检查是否与 ObstacleCheckMask (Layer 1) 相交
4. 如果有碰撞，重新尝试，最多 MaxSpawnAttempts 次
5. 所有尝试失败则在原位置生成

---

## 4. 系统集成方案

### 4.1 敌人生成 + 摄像头管理集成

**场景结构示例**：

```
Stage_2.tscn
├── CameraZoneManager              # 摄像头多区域管理
│   └── 配置两个/多个相机区域
├── World
│   ├── MainCharacter
│   │   └── Camera2D               # 实际相机（受CameraZoneManager管理）
│   ├── EnemySpawns
│   │   ├── EnemySpawnManager_Zone1
│   │   │   ├── TriggerArea
│   │   │   └── SpawnEffects
│   │   └── EnemySpawnManager_Zone2
│   └── Enemies (敌人生成的父节点)
└── UI
```

**集成步骤**：
1. 在每个区域的出入口放置 EnemySpawnManager
2. EnemySpawnManager 的 TriggerArea 检测玩家进入
3. 敌人生成到 "Enemies" 节点
4. CameraZoneManager 并行管理相机区域切换

### 4.2 生成完成后的逻辑连接

```csharp
// 在 Stage_2 管理脚本中
public override void _Ready()
{
    var spawnManager = GetNode<EnemySpawnManager>("EnemySpawnManager_Zone1");
    spawnManager.SpawnCompleted += () =>
    {
        GD.Print("Zone 1 敌人全部生成完成");
        // 可以在这里触发：
        // - 关闭通道
        // - 启动音乐
        // - 显示提示
        // - 启动事件序列
    };
}
```

### 4.3 碰撞系统的最佳实践

**推荐做法**：
1. 清晰定义每个碰撞层的用途
2. 在代码中使用 Constants 而不是魔法数字：
   ```csharp
   public const uint LAYER_PLAYER = 0;
   public const uint LAYER_FURNITURE = 1;
   public const uint LAYER_ENEMY = 2;
   public const uint LAYER_ATTACK_AREA = 3;
   ```

3. 为 AttackArea 类创建工厂方法
4. 定期在调试器中检查碰撞关系

### 4.4 动态系统示例

```csharp
public partial class StageController : Node
{
    private CameraZoneManager _cameraZoneManager;
    private List<EnemySpawnManager> _spawnManagers = new();

    public override void _Ready()
    {
        _cameraZoneManager = GetNode<CameraZoneManager>("CameraZoneManager");
        
        // 收集所有生成管理器
        foreach (var spawnManager in GetTree().GetNodesInGroup("spawn_managers"))
        {
            if (spawnManager is EnemySpawnManager esm)
            {
                _spawnManagers.Add(esm);
                esm.SpawnCompleted += OnSpawnCompleted;
            }
        }
    }

    private void OnSpawnCompleted()
    {
        GD.Print("检查所有生成管理器的状态...");
    }

    public void AddCameraZone(string zoneName, Vector2 boundTopLeft, Vector2 boundBottomRight)
    {
        var newZone = new CameraZoneManager.CameraZone
        {
            Name = zoneName,
            LimitLeft = (int)boundTopLeft.X,
            LimitTop = (int)boundTopLeft.Y,
            LimitRight = (int)boundBottomRight.X,
            LimitBottom = (int)boundBottomRight.Y
        };
        // _cameraZoneManager.AddZone(newZone);  // 如果有此方法
    }
}
```

---

## 5. 快速参考

### 5.1 关键文件查询表

| 功能 | 文件 | 关键类 |
|------|------|--------|
| 敌人生成 | scripts/controllers/EnemySpawnManager.cs | EnemySpawnManager |
| 运行时敌人管理 | scripts/controllers/EnemySpawnController.cs | EnemySpawnController |
| 摄像头多区域 | scripts/managers/CameraZoneManager.cs | CameraZoneManager |
| 伤害检测 | scripts/actors/heroes/SamplePlayer.cs | SamplePlayer |
| 物品持握 | scripts/actors/heroes/PlayerItemAttachment.cs | PlayerItemAttachment |

### 5.2 信号大全

**EnemySpawnManager 信号**:
- `SpawnStarted` - 开始生成
- `EnemySpawned(Node enemy, int index)` - 敌人生成完成
- `SpawnCompleted` - 全部生成完成

**CameraZoneManager**: 无内部信号，但使用 Area2D 的信号

### 5.3 常见问题解决

| 问题 | 检查项 |
|------|--------|
| 敌人无法生成 | EnemyScene 是否设置、TriggerArea 是否正确 |
| 敌人生成在地板下 | EnableSmartSpawnPlacement、ObstacleCheckMask |
| 相机不切换 | CameraZoneManager 的 TargetCamera 和 Player 是否设置 |
| 伤害无法命中敌人 | CollisionMask 配置是否正确 |

---

**文档完成**  
更新时间：2026-04-30 11:45
