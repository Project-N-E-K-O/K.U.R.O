# 引力手雷（GravityGrenade）功能设计文档

## 概览

引力手雷（`Weapon_Throw_GravityGrenade`）是机械系投掷武器，使用参数化抛物线飞行轨迹，落地后触发引力效果。

---

## 相关文件

| 类型 | 路径 |
|------|------|
| ItemDefinition 资源 | `resources/items/Weapon_Throw_GravityGrenade.tres` |
| 场景文件 | `scenes/weapons/Weapon_Throw_gravityGrenade.tscn` |
| 飞行逻辑（主体） | `scripts/items/world/RigidBodyWorldItemEntity.cs` |
| 投掷参数定义 | `scripts/items/ItemDefinition.cs` |
| 轨迹预览 | `scripts/items/world/ThrowTrajectoryPreview.cs` |

---

## ItemDefinition 参数（`Weapon_Throw_GravityGrenade.tres`）

```
ItemId            = "Weapon_Throw_GravityGrenade"
DisplayName       = "引力手雷"
Category          = "Weapon"
Tags              = ["tag_weapon", "build_machine", "tag_throw"]
BuildClass        = "machine"
IsThrowable       = true
```

### 投掷物理参数（Throw Physics）

| 参数 | 当前值 | 说明 |
|------|--------|------|
| `ThrowStartOffset` | `Vector2(0, -300)` | 投掷起点相对玩家的偏移（单位：像素） |
| `ThrowParabolicDuration` | `0.4` 秒 | 投掷物从投出到落地的总飞行时间 |
| `ThrowHorizontalDistance` | `700.0` px | 水平飞行距离；实际水平速度 = 距离 ÷ 时间 |
| `ThrowParabolicPeakHeight` | `100` px（默认值） | 飞行最高点相对起始点的高度（Godot Y 轴向下，峰值向上） |
| `ThrowParabolicLandingYOffset` | `200.0` px | 落地点相对抛出点的垂直偏移（向下为正） |

---

## 飞行轨迹实现（RigidBodyWorldItemEntity）

### 核心逻辑（`_PhysicsProcess`）

1. **`ApplyThrowImpulse(velocity)`** — 投掷触发入口：
   - 根据 `ThrowHorizontalDistance / ThrowParabolicDuration` 计算实际水平速度 `_throwHorizontalVelocity`
   - 记录投掷起始 Y 坐标 `_throwStartY`
   - 激活伤害检测（`_impactArmed = true`）、设置飞行碰撞层、隐藏阴影
   - 若为 `IsThrowWeapon`：进入 CD 状态、预占快捷栏槽位、写入 EmptyItem 占位防止槽位被覆盖

2. **抛物线轨迹公式（`_inFlight` 阶段）**：

   ```
   phase = flightTimer / ThrowParabolicDuration   ∈ [0, 1]
   
   upDown = sin(phase × π)                         // 对称钟形曲线，起点/峰值/落点速度连续
   
   Y(phase) = Lerp(startY, landingY, phase)         // 线性插值从起点到落点
            - upDown × PeakHeight                   // 减去钟形峰值（向上飞）
   
   X(phase) = X_prev + horizontalVelocity × dt      // 匀速水平移动
   ```

   - 使用 `sin(phase × π)` 替代分段处理，消除速度不连续导致的抖动
   - `phase >= 1.0` 时，`_inFlight = false`，位置精确对齐到 `(newX, landingY)`

3. **落地后流程**：
   - `LandingHideDelay` 计时器到期 → `HideItemAtLanding()`（投掷武器仅隐藏，不销毁）
   - `ThrowWeaponCooldown` 计时器到期 → `ReturnToInventory()` + `QueueFree()`

---

## 场景结构（`Weapon_Throw_gravityGrenade.tscn`）

```
WeaponThrowGravityGrenade (Node2D) [RigidBodyWorldItemEntity]
└── RigidBody2D
    ├── Shadow (Sprite2D)            — 地面阴影，飞行中隐藏
    ├── Outline (Sprite2D)           — 描边外观（sprite_outline.gdshader）
    │   └── Sprite2D                 — 主图（引力手雷.png）
    ├── CollisionArea (Area2D)       — 玩家拾取检测区域（CircleShape2D, r=100）
    └── AttackArea (Area2D)          — 伤害命中检测区域（RectangleShape2D 199×318）
```

**重要设置：**
- `RigidBody2D.gravity_scale = 0` — 物理引擎不施加重力，完全由代码控制轨迹
- `RigidBody2D.freeze = true` + `freeze_mode = 1` — 初始冻结，投掷时通过代码解冻
- `ThrowDamage = 0.0`，`MinDamageVelocity = 0.0` — 暂不使用碰撞伤害；伤害由引力效果触发
- `StopOnHit = true` — 命中后停止移动
- `IsThrowWeapon = true` — 使用 CD 机制归还背包而不销毁
- `ThrowWeaponCooldown = 10.0` — 10 秒 CD 后归还背包

---

## 投掷武器 CD 机制

```
投出 → 进入飞行 → 落地
  ↓
LandingHideDelay (0.5s) 到期 → 视觉隐藏（HideItemAtLanding）
  ↓
ThrowWeaponCooldown (10s) 到期 → ReturnToInventory() → QueueFree()
```

- 飞行期间，快捷栏对应槽位写入 `EmptyItem` 占位，防止被其他物品覆盖
- 归还背包后，`EmptyItem` 被替换为原物品，CD 进度条由 `ThrowCooldownProgress` 提供（UI 读取）

---

## 本次迭代调试历史（会话摘要）

| 问题 | 原因 | 修复方式 |
|------|------|----------|
| 手雷直接坠落，Y 速度不受控 | `_vel.Y = FlightGravity` 每帧覆盖而非累加 | 改为 `_vel.Y += gravity * dt` |
| 手雷几乎不上升 | `FlightGravity = 200`（过小） | 调大到 2200，后改为无重力纯水平 |
| `Mathf.Max` 三参数编译错误 | C# 不支持 3 参数版本 | 改为嵌套 `Mathf.Max(a, Mathf.Max(b, c))` |
| 最终方案 | 改为参数化抛物线（sin 曲线） | 使用 `ThrowParabolicDuration` + `ThrowHorizontalDistance` 控制，无需手动调重力 |

---

## 扩展点

- **引力效果**：落地后触发黑洞/引力区域，当前尚未实现，`AttackArea` 节点已预留
- **峰值高度**：`ThrowParabolicPeakHeight` 在 `ItemDefinition` 中可调，同一脚本支持不同弹道风格
- **伤害配置**：若需碰撞伤害，调整 `ThrowDamage` 和 `MinDamageVelocity` 即可，无需修改代码
