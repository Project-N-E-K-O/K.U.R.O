# 实际操作指南

## 一、在编辑器中配置敌人生成

### 步骤1：创建或选中 EnemySpawnManager 节点

在 Stage_2 场景中，找到敌人生成区域：

```
World
├── EnemySpawns
│   └── EnemySpawnManager_Zone1  ← 选中这个
```

### 步骤2：配置基础敌人参数

在 Inspector 面板中设置：

```ini
Enemy (分类)
├─ EnemyScene = res://scenes/actors/enemies/Enemy_A1_zhuA.tscn
├─ SpawnCount = 3
└─ SpawnInterval = 0.15
```

### 步骤3：配置触发区域

```ini
Trigger (分类)
├─ TriggerArea = 指向节点或留空以自动创建
├─ AutoConfigureAssignedTriggerArea = true
├─ TriggerGroupName = "player"
├─ TriggerSize = (320, 180)
├─ TriggerCollisionLayer = 1
└─ TriggerCollisionMask = 1
```

### 步骤4：配置生成位置

```ini
Spawn Placement (分类)
├─ UseExplicitSpawnOffsets = false  # 使用随机范围
├─ SpawnAreaExtents = (96, 48)
├─ EnableSmartSpawnPlacement = true
├─ ObstacleCheckMask = 1            # Layer 1（家具层）
└─ MaxSpawnAttempts = 10
```

### 步骤5：配置生成效果（可选）

```ini
Spawn FX (分类)
├─ SpawnBackEffectScene = res://scenes/actors/etc/enemy_spaw_back.tscn
├─ SpawnFrontEffectScene = res://scenes/actors/etc/enemy_spawn_front.tscn
├─ EnemyAppearDelay = 0.2
└─ EnemyAppearGateMode = Delay  # 或 BackEffectFrame / BackEffectFinished
```

### 步骤6：在编辑器中预览

```
显示调试信息:
Debug (分类)
├─ ShowDebugOverlay = true
└─ ShowDebugOverlayInGame = true
```

运行游戏，你会看到触发区域和生成点的可视化。

---

## 二、在代码中配置敌人生成

### 基础配置

```csharp
using Godot;
using Kuros.Controllers;

public partial class BattleStarter : Node
{
    public override void _Ready()
    {
        var spawnManager = GetNode<EnemySpawnManager>("EnemySpawnManager_Zone1");
        
        // 配置敌人
        spawnManager.EnemyScene = GD.Load<PackedScene>("res://scenes/actors/enemies/Enemy_A1_zhuA.tscn");
        spawnManager.SpawnCount = 3;
        spawnManager.SpawnInterval = 0.15f;
        
        // 配置触发
        spawnManager.TriggerGroupName = "player";
        spawnManager.TriggerSize = new Vector2(320, 180);
        
        // 配置生成位置
        spawnManager.SpawnAreaExtents = new Vector2(96, 48);
        spawnManager.EnableSmartSpawnPlacement = true;
        
        // 订阅完成事件
        spawnManager.SpawnCompleted += OnAllEnemiesSpawned;
    }
    
    private void OnAllEnemiesSpawned()
    {
        GD.Print("所有敌人生成完毕，战斗开始！");
        // 启动 BGM、禁用逃脱等
    }
}
```

### 高级配置 - 多个敌人类型

```csharp
public partial class MixedWaveSpawner : Node
{
    public override void _Ready()
    {
        var spawnManager = GetNode<EnemySpawnManager>("MultiEnemySpawner");
        
        // 设置多个敌人类型
        spawnManager.EnemyScenes = new Godot.Collections.Array<PackedScene>
        {
            GD.Load<PackedScene>("res://scenes/actors/enemies/Enemy_A1_zhuA.tscn"),
            GD.Load<PackedScene>("res://scenes/actors/enemies/Enemy_B1_Thin.tscn"),
            GD.Load<PackedScene>("res://scenes/actors/enemies/Enemy_C1_WaiterA.tscn")
        };
        
        spawnManager.MultiEnemySelectionMode = EnemySpawnManager.EnemySelectionMode.Random;
        spawnManager.SpawnCount = 5;
        
        // 自动生成 (不需要触发)
        spawnManager.SpawnOnReady = true;
    }
}
```

### 跟踪每个生成的敌人

```csharp
public partial class EnemyTracker : Node
{
    private List<Node> _spawnedEnemies = new();
    
    public override void _Ready()
    {
        var spawnManager = GetNode<EnemySpawnManager>("EnemySpawnManager");
        spawnManager.EnemySpawned += OnEnemySpawned;
    }
    
    private void OnEnemySpawned(Node enemy, int index)
    {
        GD.Print($"敌人 #{index} 生成: {enemy.Name}");
        _spawnedEnemies.Add(enemy);
        
        // 可以在这里添加特殊处理
        if (enemy is GameActor actor)
        {
            actor.TakeDamage += (amount) =>
            {
                GD.Print($"敌人受伤 {amount} HP");
            };
        }
    }
}
```

---

## 三、配置摄像头多区域系统

### 方法1：编辑器配置

#### 步骤1：选中 CameraZoneManager

```
Scene Tree
├── CameraZoneManager ← 选中这个节点
```

#### 步骤2：设置基础参数

```ini
Exported Properties
├─ TargetCamera = 指向 MainCharacter/Camera2D
├─ Player = 指向 MainCharacter (或留空自动查找)
```

#### 步骤3：配置第一个区域

```ini
相机区域配置 (相机区域配置)
├─ Zone1_LimitLeft = -9300
├─ Zone1_LimitTop = -1500
├─ Zone1_LimitRight = -3650
└─ Zone1_LimitBottom = 1500
```

#### 步骤4：配置第二个区域

```ini
├─ Zone2_LimitLeft = -3650
├─ Zone2_LimitTop = -1500
├─ Zone2_LimitRight = 5000
└─ Zone2_LimitBottom = 1500
```

#### 步骤5：指定触发区域

```ini
Area2D 节点路径 (Area2D 节点路径)
├─ Zone1AreaPath = "World/CameraZones/Zone1_Area2D"
└─ Zone2AreaPath = "World/CameraZones/Zone2_Area2D"
```

### 方法2：代码配置

```csharp
using Godot;
using Kuros.Managers;

public partial class CameraSetup : Node
{
    public override void _Ready()
    {
        var cameraZoneManager = GetNode<CameraZoneManager>("CameraZoneManager");
        
        // 获取相机和玩家引用
        var camera = GetNode<Camera2D>("World/MainCharacter/Camera2D");
        var player = GetNode<Node2D>("World/MainCharacter");
        
        cameraZoneManager.TargetCamera = camera;
        cameraZoneManager.Player = player;
        
        // 手动初始化后调用 _Ready() 中的逻辑
        // (通常不需要，编辑器会自动调用)
    }
}
```

### 方法3：运行时添加新区域

```csharp
public partial class DynamicZoneManager : Node
{
    private CameraZoneManager _cameraZoneManager;
    
    public override void _Ready()
    {
        _cameraZoneManager = GetNode<CameraZoneManager>("CameraZoneManager");
    }
    
    public void AddNewZone(string zoneName, 
        Vector2 limitTopLeft, Vector2 limitBottomRight)
    {
        var newZone = new CameraZoneManager.CameraZone
        {
            Name = zoneName,
            LimitLeft = (int)limitTopLeft.X,
            LimitTop = (int)limitTopLeft.Y,
            LimitRight = (int)limitBottomRight.X,
            LimitBottom = (int)limitBottomRight.Y
        };
        
        // 如果实现了 AddZone 方法
        // _cameraZoneManager.AddZone(newZone);
        
        GD.Print($"新增相机区域: {zoneName}");
    }
    
    public void ManualSwitchZone(int zoneIndex)
    {
        _cameraZoneManager.SwitchToZone(zoneIndex);
    }
    
    public void PrintDebugInfo()
    {
        GD.Print(_cameraZoneManager.GetDebugInfo());
    }
}
```

---

## 四、配置碰撞层

### 碰撞层常量定义

在项目中创建 `scripts/core/PhysicsConstants.cs`：

```csharp
namespace Kuros.Core
{
    /// <summary>
    /// 物理碰撞层定义
    /// </summary>
    public static class PhysicsLayers
    {
        public const uint LAYER_PLAYER = 0;              // 1 << 0 = 1
        public const uint LAYER_FURNITURE = 1;           // 1 << 1 = 2
        public const uint LAYER_ENEMY = 2;               // 1 << 2 = 4
        public const uint LAYER_ATTACK_AREA = 3;         // 1 << 3 = 8
        public const uint LAYER_PICKUP_AREA = 4;         // 1 << 4 = 16
        public const uint LAYER_PROJECTILE = 5;          // 1 << 5 = 32
        
        // 掩码组合 (用于检测)
        public const uint MASK_PLAYER = 1u << 0;
        public const uint MASK_FURNITURE = 1u << 1;
        public const uint MASK_ENEMY = 1u << 2;
        public const uint MASK_ATTACK_AREA = 1u << 3;
        public const uint MASK_PICKUP_AREA = 1u << 4;
        public const uint MASK_PROJECTILE = 1u << 5;
    }
}
```

### 使用常量配置攻击区域

```csharp
public partial class AttackAreaSetup : Node
{
    public override void _Ready()
    {
        var attackArea = GetNode<Area2D>("AttackArea");
        
        // 设置不在任何物理层（纯检测）
        attackArea.CollisionLayer = 0;
        
        // 检测敌人层
        attackArea.CollisionMask = PhysicsLayers.MASK_ENEMY;
        
        GD.Print($"AttackArea 设置完成: Layer={attackArea.CollisionLayer}, Mask={attackArea.CollisionMask}");
    }
}
```

### 敌人碰撞配置

```csharp
public partial class EnemySetup : GameActor
{
    public override void _Ready()
    {
        base._Ready();
        
        // 敌人身体应该在敌人层
        CollisionLayer = PhysicsLayers.LAYER_ENEMY;
        
        // 敌人可以检测玩家和敌人（用于拥挤）
        CollisionMask = PhysicsLayers.MASK_PLAYER 
                      | PhysicsLayers.MASK_FURNITURE;
        
        GD.Print($"敌人碰撞配置: Layer={CollisionLayer}, Mask={CollisionMask}");
    }
}
```

### 验证碰撞配置

```csharp
public partial class CollisionDebugger : Node
{
    public override void _PhysicsProcess(double delta)
    {
        if (Input.IsActionJustPressed("ui_accept"))
        {
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            if (player is CharacterBody2D charBody)
            {
                GD.Print($"玩家碰撞配置:");
                GD.Print($"  Layer: {charBody.CollisionLayer} (binary: {ToBinary(charBody.CollisionLayer)})");
                GD.Print($"  Mask:  {charBody.CollisionMask} (binary: {ToBinary(charBody.CollisionMask)})");
            }
            
            var enemies = GetTree().GetNodesInGroup("enemy");
            foreach (var enemy in enemies)
            {
                if (enemy is CharacterBody2D enemyBody)
                {
                    GD.Print($"敌人 {enemy.Name} 碰撞配置:");
                    GD.Print($"  Layer: {enemyBody.CollisionLayer}");
                    GD.Print($"  Mask:  {enemyBody.CollisionMask}");
                }
            }
        }
    }
    
    private string ToBinary(uint value)
    {
        return System.Convert.ToString(value, 2).PadLeft(32, '0');
    }
}
```

---

## 五、完整集成示例 - 完整关卡初始化

创建 `scripts/core/StageInitializer.cs`：

```csharp
using Godot;
using Kuros.Controllers;
using Kuros.Managers;
using System.Collections.Generic;

namespace Kuros.Core
{
    /// <summary>
    /// 完整关卡初始化器 - 整合敌人生成、摄像头、碰撞系统
    /// </summary>
    public partial class StageInitializer : Node
    {
        private CameraZoneManager _cameraZoneManager;
        private List<EnemySpawnManager> _spawnManagers = new();
        private Dictionary<string, int> _zoneEnemyCounts = new();
        
        public override void _Ready()
        {
            GD.Print("=== 开始初始化关卡 ===");
            
            InitializeCameraSystem();
            InitializeEnemySpawners();
            InitializeCollisionLayers();
            
            GD.Print("=== 关卡初始化完成 ===");
        }
        
        /// <summary>
        /// 初始化摄像头多区域系统
        /// </summary>
        private void InitializeCameraSystem()
        {
            _cameraZoneManager = GetNode<CameraZoneManager>("CameraZoneManager");
            
            if (_cameraZoneManager == null)
            {
                GD.PushError("无法找到 CameraZoneManager!");
                return;
            }
            
            var camera = GetNode<Camera2D>("World/MainCharacter/Camera2D");
            var player = GetNode<Node2D>("World/MainCharacter");
            
            _cameraZoneManager.TargetCamera = camera;
            _cameraZoneManager.Player = player;
            
            GD.Print($"✓ 摄像头系统已初始化，共 2 个相机区域");
        }
        
        /// <summary>
        /// 初始化所有敌人生成管理器
        /// </summary>
        private void InitializeEnemySpawners()
        {
            var spawnContainer = GetNode("World/EnemySpawns");
            
            foreach (var child in spawnContainer.GetChildren())
            {
                if (child is EnemySpawnManager spawnManager)
                {
                    InitializeSpawner(spawnManager);
                    _spawnManagers.Add(spawnManager);
                }
            }
            
            GD.Print($"✓ 敌人生成系统已初始化，共 {_spawnManagers.Count} 个生成管理器");
        }
        
        /// <summary>
        /// 配置单个生成管理器
        /// </summary>
        private void InitializeSpawner(EnemySpawnManager spawnManager)
        {
            spawnManager.SpawnStarted += () =>
            {
                GD.Print($"[{spawnManager.Name}] 敌人波次开始生成");
            };
            
            spawnManager.EnemySpawned += (enemy, index) =>
            {
                GD.Print($"[{spawnManager.Name}] 敌人 #{index} 已生成: {enemy.Name}");
            };
            
            spawnManager.SpawnCompleted += () =>
            {
                GD.Print($"[{spawnManager.Name}] 敌人波次生成完成 ({spawnManager.SpawnCount} 个)");
                OnWaveCompleted(spawnManager.Name);
            };
        }
        
        /// <summary>
        /// 初始化碰撞层
        /// </summary>
        private void InitializeCollisionLayers()
        {
            // 配置玩家
            var player = GetNode<CharacterBody2D>("World/MainCharacter");
            player.CollisionLayer = PhysicsLayers.LAYER_PLAYER;
            player.CollisionMask = PhysicsLayers.MASK_FURNITURE 
                                 | PhysicsLayers.MASK_ENEMY;
            
            // 配置敌人
            var enemySpawns = GetNode("World/EnemySpawns");
            foreach (var spawner in enemySpawns.GetChildren())
            {
                if (spawner is EnemySpawnManager esm)
                {
                    esm.ObstacleCheckMask = PhysicsLayers.MASK_FURNITURE;
                }
            }
            
            GD.Print("✓ 碰撞层已初始化");
        }
        
        /// <summary>
        /// 敌人波次完成回调
        /// </summary>
        private void OnWaveCompleted(string spawnerName)
        {
            _zoneEnemyCounts[spawnerName] = 1;
            
            // 检查所有波次是否完成
            int completedWaves = 0;
            foreach (var spawner in _spawnManagers)
            {
                if (_zoneEnemyCounts.ContainsKey(spawner.Name))
                    completedWaves++;
            }
            
            if (completedWaves == _spawnManagers.Count)
            {
                GD.Print(">>> 所有敌人波次已完成，战斗阶段开始！");
                OnAllWavesCompleted();
            }
        }
        
        /// <summary>
        /// 所有敌人波次完成
        /// </summary>
        private void OnAllWavesCompleted()
        {
            // 启动音乐、禁用逃脱、启动战斗逻辑等
            GD.Print("触发战斗开始事件...");
        }
    }
}
```

### 在 Stage_2.tscn 中使用

```
1. 选中 Stage_2 (根节点)
2. 添加子节点 Node，命名为 "StageInitializer"
3. 附加脚本: StageInitializer.cs
4. 确保节点路径正确
5. 运行游戏
```

---

## 六、调试和排查

### 启用详细日志

```csharp
public partial class DebugEnableScript : Node
{
    public override void _Ready()
    {
        // 启用 EnemySpawnManager 的详细日志
        var spawnManager = GetNode<EnemySpawnManager>("EnemySpawnManager");
        spawnManager.LogSpawnEffectPositions = true;
        spawnManager.ShowDebugOverlayInGame = true;
        
        GD.Print("调试模式已启用");
    }
}
```

### 实时检查敌人数量

```csharp
public override void _PhysicsProcess(double delta)
{
    if (Input.IsActionJustPressed("ui_accept"))
    {
        var enemies = GetTree().GetNodesInGroup("enemy");
        GD.Print($"当前场景敌人数量: {enemies.Count}");
        
        foreach (var enemy in enemies)
        {
            if (enemy is GameActor actor)
            {
                GD.Print($"  - {actor.Name}: HP={actor.CurrentHealth}/{actor.MaxHealth}");
            }
        }
    }
}
```

### 检查相机限制是否生效

```csharp
var camera = GetNode<Camera2D>("Camera2D");
GD.Print($"相机限制: Left={camera.LimitLeft}, Right={camera.LimitRight}, Top={camera.LimitTop}, Bottom={camera.LimitBottom}");
GD.Print($"相机位置: {camera.GlobalPosition}");
GD.Print($"相机缩放: {camera.Zoom}");
```

---

**指南完成**  
最后更新：2026-04-30
