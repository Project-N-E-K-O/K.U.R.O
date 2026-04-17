# Hitbox（攻击检测）系统详解

## 概述

K.U.R.O 的 Hitbox 系统是一套**多层级的分级查询机制**，用于检测角色攻击是否命中敌人。该系统支持：
- 多个攻击区（武器区 + 角色区）
- 自动 Fallback 机制（武器不命中时回退到角色攻击区）
- 三层伤害检测（Area重叠 → 形状查询 → Body检测）
- 动态武器装备系统（持握物品时自动替换 AttackArea）

---

## 核心架构

### 1. 整体流程图

```
玩家执行攻击
	↓
PerformAttackCheck() (MainCharacter 或 SamplePlayer)
	↓
ApplyDamageWithArea()
	↓
ResolveAttackAreaForHitDetection() - 确定使用哪个 AttackArea
	├─ 优先级1️⃣: PlayerItemAttachment.GetEquippedAttackArea()
	├─ 优先级2️⃣: LeftHandAttachment 下的武器 AttackArea  
	└─ 优先级3️⃣: 角色自身 AttackArea (备降)
	↓
ApplyDamageWithSpecificArea() - 执行三层伤害检测
	├─ 第1层: DealDamageFromHitAreas()      - Area2D.GetOverlappingAreas()
	├─ 第2层: DealDamageViaShapeQuery()     - PhysicsShapeQueryParameters2D
	└─ 第3层: DealDamageFromBodies()        - Area2D.GetOverlappingBodies()
	↓
对命中的敌人应用伤害
```

---

## 关键类和组件

### PlayerItemAttachment 类

**文件**: `scripts/actors/heroes/PlayerItemAttachment.cs`

**人物**：负责动态管理"当前持握物品"的攻击区。当玩家拾取、切换或投掷物品时，自动更新 AttackArea。

#### 核心方法

#### `UpdateEquippedAttackArea(ItemDefinition? item)`
**功能**：加载物品的武器场景，提取并配置其 AttackArea。

**工作流程**：
```csharp
1. 验证 item 是否为 null（为 null 则清空）
2. item.ResolveWorldScenePath() - 获取武器场景路径
3. ResourceLoader.Load<PackedScene>() - 加载武器场景
4. 在场景中查找 AttackArea 节点
5. Duplicate() 拷贝 AttackArea（避免修改原资源）
6. ConfigureEquippedAttackArea() - 配置碰撞参数
7. AttachEquippedAttackAreaToIcon() - 挂在 icon sprite 上
```

**关键配置**（ConfigureEquippedAttackArea）：
```csharp
area.TopLevel = true;              // 独立坐标系
area.Monitoring = true;            // 启用碰撞检测
area.Monitorable = false;          // 不被其他 Area 检测
area.CollisionLayer = 0;           // 不在任何图层上
area.CollisionMask = player.AttackArea.CollisionMask;  // 使用玩家的碰撞掩码
```

#### `GetEquippedAttackArea()`
**功能**：返回当前装备的攻击区。
```csharp
返回条件：
- _equippedAttackArea != null
- 节点有效（IsInstanceValid）
- 节点在场景树中（IsInsideTree）
```

#### `TryGetEquippedAttackAreaTemplate(out Shape2D? shape, out Transform2D transform, out uint collisionMask)`
**功能**：获取 AttackArea 的碰撞形状模板。用于 SamplePlayer 的形状查询。

#### `UpdateEquippedAttackAreaTransform()`
**功能**：每帧更新 AttackArea 的位置和旋转。

**逻辑**：
```csharp
if (_iconSprite != null)
{
	// 攻击区跟随 icon sprite 位置和旋转，但不继承缩放
	Transform2D iconNoScale = RemoveScaleFromTransform(_iconSprite.GlobalTransform);
	_equippedAttackArea.GlobalTransform = iconNoScale * _equippedAttackAreaLocalTransform;
}
else
{
	// 降级：放在 attachment parent 或角色身上
	var anchor = (_attachmentParent as Node2D) ?? (_actor as Node2D);
	_equippedAttackArea.GlobalTransform = anchor.GlobalTransform * _equippedAttackAreaLocalTransform;
}
```

**重要**：使用 `RemoveScaleFromTransform()` 移除缩放信息，防止 icon sprite 的缩放影响 hitbox 大小。

### SamplePlayer 类 - 伤害检测逻辑

**文件**: `scripts/actors/heroes/SamplePlayer.cs`

#### `ResolveAttackAreaForHitDetection(out string areaSource)`

**功能**：按优先级确定使用哪个 AttackArea 进行伤害检测。

**优先级**：
```csharp
1️⃣ 同步模式
   if (SyncMainAttackAreaWithEquippedWeaponArea)
	   return AttackArea;  // 使用玩家区域

2️⃣ 持握武器区（最优）
   var itemAttachment = GetNodeOrNull<PlayerItemAttachment>("ItemAttachment");
   var attachedWeaponArea = itemAttachment?.GetEquippedAttackArea();
   if (IsAttackAreaUsable(attachedWeaponArea))
	   return attachedWeaponArea;

3️⃣ 左手武器区
   var weaponArea = FindUsableWeaponAttackArea(leftHandAttachment);
   if (weaponArea != null)
	   return weaponArea;

4️⃣ 备降：玩家自身 AttackArea
   return AttackArea;
```

#### `ApplyDamageWithArea(float damageAmount, Action<GameActor, bool>? onHit)`

**功能**：执行伤害检测和应用。

**流程**：
```csharp
1. activeAttackArea = ResolveAttackAreaForHitDetection()
2. hitCount = ApplyDamageWithSpecificArea(activeAttackArea, damageAmount, onHit)
3. 如果 hitCount == 0 且有备降区：
   hitCount = ApplyDamageWithSpecificArea(AttackArea, damageAmount, onHit)
4. 返回命中数
```

#### `ApplyDamageWithSpecificArea(Area2D attackArea, float damageAmount, Action<GameActor, bool>? onHit)`

**功能**：三层伤害检测机制。

**第1层：DealDamageFromHitAreas()**
```csharp
// 使用 Area2D 的重叠检测，最高效
var overlappingAreas = attackArea.GetOverlappingAreas();
foreach (var area in overlappingAreas)
{
	var target = area.GetParent() as GameActor;
	if (target != null)
	{
		target.TakeDamage(damageAmount, ...);
		hitCount++;
	}
}
```

**第2层：DealDamageViaShapeQuery()**
```csharp
// 形状查询，更精确，用于 Area 检测失败时
var shapeQuery = new PhysicsShapeQueryParameters2D();
shapeQuery.Shape = attackArea 的碰撞形状;
shapeQuery.Transform = attackArea 的全局变换;
var results = GetWorld2D().IntersectShape(shapeQuery);
// 遍历结果并应用伤害
```

**第3层：DealDamageFromBodies()**
```csharp
// 检测重叠的 RigidBody2D，最后的备用
var overlappingBodies = attackArea.GetOverlappingBodies();
foreach (var body in overlappingBodies)
{
	var target = body as GameActor;
	if (target != null)
	{
		target.TakeDamage(damageAmount, ...);
		hitCount++;
	}
}
```

---

## 工作流程详解

### 场景 1：角色持握可投掷物品

```
1. 玩家拾取物品
   ↓
   PlayerInventoryComponent.ItemPicked 信号触发
   ↓
   PlayerItemAttachment.OnItemPicked()
   ↓
   UpdateAttachmentIcon()
   ↓
   UpdateEquippedAttackArea(item)
   ↓
   加载 item.WorldScenePath（如 res://scenes/weapons/Weapon_Throw_rock.tscn）
   ↓
   提取该场景中的 AttackArea
   ↓
   配置并挂在角色身上

2. 玩家攻击（投掷、持握攻击等）
   ↓
   PerformAttackCheck()
   ↓
   ResolveAttackAreaForHitDetection()
	 → 优先使用 GetEquippedAttackArea() 返回的武器区
   ↓
   ApplyDamageWithArea()
   ↓
   三层检测找到敌人
   ↓
   enemy.TakeDamage()
```

### 场景 2：武器不命中，回退到角色区

```
1. 调用 ApplyDamageWithArea()
2. activeAttackArea = 持握武器的 AttackArea
3. hitCount = ApplyDamageWithSpecificArea(activeAttackArea, ...)
4. 三层检测均未命中，hitCount == 0
5. 判断条件：hitCount == 0 && AttackArea != null && activeAttackArea != AttackArea
6. 再次执行：ApplyDamageWithSpecificArea(AttackArea, ...)
7. 使用玩家自身的 AttackArea 进行检测
8. 返回最终伤害数
```

### 场景 3：物品图标缩放不影响 Hitbox

```
PlayerItemAttachment._iconSprite.Scale = (0.5, 0.5)  // 显示很小

但在 UpdateEquippedAttackAreaTransform() 中：
  Transform2D iconNoScale = RemoveScaleFromTransform(...)
  // 移除了缩放信息，hitbox 大小不变

结果：
  ✓ 视觉：小物品
  ✓ Hitbox：保持原大小
```

---

## 配置要点

### ItemDefinition 配置

物品定义必须包含：
```csharp
public string ItemId { get; set; }
public Texture2D Icon { get; set; }
public string WorldScenePath { get; set; }  // 必须指向含 AttackArea 的场景
```

### 武器场景结构

世界场景应该包含 `AttackArea` 节点：
```
Weapon_Example.tscn
├─ Sprite2D (可选)
├─ AnimationPlayer (可选)
└─ AttackArea (Area2D) ⭐ 必须存在
   └─ CollisionShape2D
	   ├─ RectangleShape2D / CircleShape2D / CapsuleShape2D
```

### 碰撞层配置

```
CollisionLayer:
  - 玩家 AttackArea: 设为某层（如层1）
  - 敌人 HitBox: 必须在同一层或相交

CollisionMask:
  - PlayerItemAttachment 配置的 AttackArea: 继承自 player.AttackArea.CollisionMask
  - 默认应指向敌人碰撞层
```

---

## 调试和诊断

### 启用详细日志

PlayerItemAttachment 中的 `LogSourceAttackArea()` 会输出：
```
[PlayerItemAttachment] EquipAttackArea item=Stone, scene=res://scenes/weapons/Weapon_Throw_stone.tscn, 
areaTransform=..., shapeTransform=..., shape=RectangleShape2D rectSize=(32, 32)
```

SamplePlayer 中的日志：
```
[SamplePlayer] AttackArea hit test: /root/Player/ItemAttachment/AttackArea -> 1 hit(s)
[SamplePlayer] Fallback hit test: /root/Player/AttackArea -> 0 hit(s)
```

### 常见问题排除

| 问题 | 原因 | 解决方案 |
|------|------|--------|
| 武器完全无法命中 | AttackArea 未正确加载或挂接 | 检查 WorldScenePath，确保场景存在 AttackArea |
| 只有玩家 AttackArea 能命中 | GetEquippedAttackArea() 返回 null | 检查 ItemDefinition 是否正确配置 |
| Hitbox 位置不对 | UpdateEquippedAttackAreaTransform() 未被调用 | 确保 _Process() 每帧调用此方法 |
| Hitbox 大小跟着 icon 变化 | RemoveScaleFromTransform() 有问题 | 检查 Transform2D 计算逻辑 |
| 敌人无法检测到伤害 | 碰撞层/掩码配置错误 | 检查敌人 HitBox 的碰撞层是否与 AttackArea.CollisionMask 相符 |

---

## 性能考虑

### 优化层级

1. **第1层最快**：Area 重叠（O(1)）
2. **第2层中等**：形状查询（需要物理计算）
3. **第3层最慢**：Body 检测（全扫描）

### 最佳实践

```csharp
✓ 优先确保 Area 重叠检测能工作
✓ 使用简单形状（Rectangle > Circle > Capsule）
✓ 避免复杂的多边形碰撞体
✓ 及时清理无效的 AttackArea（持握完毕时）
```

---

## 相关文件

| 文件 | 用途 |
|------|------|
| PlayerItemAttachment.cs | 管理持握物品的 AttackArea |
| SamplePlayer.cs | 伤害检测核心逻辑 |
| MainCharacter.cs | 玩家攻击入口 |
| ItemDefinition | 物品定义（包含 WorldScenePath） |

---

## 扩展建议

1. **支持多武器 Hitbox**：同时装备两个武器时，合并两个 AttackArea
2. **分帧检测**：在不同帧检查不同部位的 Hitbox，提高精度
3. **Hitbox 可视化**：Debug 模式下绘制 AttackArea 边界
4. **伤害类型**：基于武器类型应用不同的伤害计算规则
5. **Hitbox 缩放独立控制**：为物品添加 `HitboxScale` 属性，独立于显示缩放
