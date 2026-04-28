# 伤害飘字系统使用指南

## 系统概述

伤害飘字系统自动监听游戏中所有敌人受伤的事件，并在敌人头顶显示飘动的伤害数字。这个系统已经完全集成到项目中。

## 已添加的文件

1. **scripts/ui/FloatingDamageText.cs** - 飘字显示和动画脚本
2. **scripts/managers/FloatingDamageTextManager.cs** - 管理器，监听伤害事件
3. **scenes/ui/FloatingDamageText.tscn** - 飘字场景
4. **project.godot** - 已添加FloatingDamageTextManager到autoload列表

## 工作原理

1. **自动启动**：FloatingDamageTextManager 在游戏启动时自动加载（通过autoload）
2. **事件监听**：管理器订阅 `GameActor.AnyDamageTaken` 全局事件
3. **飘字生成**：当任何GameActor受伤时，自动在该角色上方生成飘字
4. **动画效果**：飘字向上浮动，同时逐渐淡出（持续时间可配置）

## 功能特性

### 基础功能
- ✅ 显示伤害数字
- ✅ 自动淡出动画
- ✅ 向上浮动效果
- ✅ 可配置的颜色和字体大小
- ✅ 暴击显示（黄色文字，更大）
- ✅ 治疗显示（绿色文字，带+号）

### 可配置参数

在编辑器中选择FloatingDamageTextManager节点，可以调整以下参数：

| 参数 | 默认值 | 说明 |
|------|--------|------|
| FloatingTextScenePath | "res://scenes/ui/FloatingDamageText.tscn" | 飘字场景路径 |
| EnableFloatingText | true | 是否启用飘字显示 |
| OffsetFromTarget | (0, 0) | 相对于目标的显示偏移 |

### FloatingDamageText场景参数

| 参数 | 默认值 | 说明 |
|------|--------|------|
| DurationSeconds | 1.5 | 飘字显示持续时间 |
| FloatHeight | 80.0 | 飘字上升的高度 |
| DamageColor | 红色(1,0,0,1) | 普通伤害文字颜色 |
| CriticalColor | 黄色(1,1,0,1) | 暴击伤害文字颜色 |
| HealColor | 绿色(0,1,0,1) | 治疗文字颜色 |

## 使用示例

### 基础用法（自动触发）

系统已经自动监听所有伤害事件，无需额外配置。当敌人受伤时会自动显示飘字：

```csharp
// 这会自动触发飘字显示
enemy.TakeDamage(25, Vector2.Zero, player);
```

### 手动显示伤害飘字

```csharp
using Kuros.Managers;

// 显示伤害飘字
FloatingDamageTextManager.Instance.ShowFloatingDamage(
    damage: 50,
    position: enemy.GlobalPosition + Vector2.Up * 50,
    isCritical: false
);

// 显示暴击飘字
FloatingDamageTextManager.Instance.ShowFloatingDamage(
    damage: 100,
    position: enemy.GlobalPosition + Vector2.Up * 50,
    isCritical: true
);
```

### 手动显示治疗飘字

```csharp
using Kuros.Managers;

FloatingDamageTextManager.Instance.ShowFloatingHealing(
    amount: 30,
    position: player.GlobalPosition + Vector2.Up * 50
);
```

### 在Effect中使用

如果你在实现特殊效果时需要显示飘字：

```csharp
using Kuros.Core;
using Kuros.Managers;
using Godot;

public partial class MyCustomEffect : ActorEffect
{
    public override void Apply(GameActor actor)
    {
        int damageAmount = 50;
        actor.TakeDamage(damageAmount, actor.GlobalPosition, this.Actor);
        
        // TakeDamage 会自动触发飘字显示
        // 如果需要自定义位置也可以手动调用：
        // FloatingDamageTextManager.Instance.ShowFloatingDamage(damageAmount, actor.GlobalPosition);
    }
}
```

## 自定义配置

### 修改飘字样式

编辑 `scenes/ui/FloatingDamageText.tscn` 中的Label节点：
- 字体选择
- 字体大小
- 边框效果
- 阴影效果

### 修改动画效果

编辑 `FloatingDamageText.cs` 中的 `_Process` 方法：

```csharp
// 修改浮动曲线
float progress = _elapsedTime / DurationSeconds;
float easeProgress = Mathf.Ease(progress, -1.5f); // 调整缓动函数

// 修改淡出曲线
float alpha = Mathf.Lerp(1f, 0f, easeProgress);
```

### 修改显示位置

编辑 `FloatingDamageTextManager.cs` 中的 `OnAnyDamageTaken` 方法：

```csharp
private void OnAnyDamageTaken(GameActor victim, GameActor? attacker, int damage)
{
    if (!EnableFloatingText) return;
    if (victim == null || damage <= 0) return;
    if (_floatingTextScene == null) return;

    // 自定义位置逻辑
    Vector2 textPosition = victim.GlobalPosition + new Vector2(
        GD.Randf() * 40 - 20,  // 随机X偏移
        -60                      // Y偏移
    );

    ShowFloatingDamage(damage, textPosition, isCritical: false);
}
```

## 禁用系统

如需暂时禁用飘字显示，在编辑器中取消选中 FloatingDamageTextManager 节点的 `EnableFloatingText` 属性，或在代码中：

```csharp
FloatingDamageTextManager.Instance.EnableFloatingText = false;
```

## 调试技巧

1. **查看日志**：系统会输出加载情况的日志信息
2. **检查场景路径**：确保 FloatingTextScenePath 指向正确的.tscn文件
3. **验证Autoload**：检查project.godot中是否包含FloatingDamageTextManager

## 常见问题

**Q: 飘字没有显示？**
- A: 检查 project.godot 中是否已添加 FloatingDamageTextManager 到 autoload
- A: 确认 FloatingDoubleTextManager 的 EnableFloatingText 属性为 true
- A: 验证 FloatingTextScenePath 路径是否正确

**Q: 飘字位置不对？**
- A: 调整 OffsetFromTarget 参数
- A: 在 OnAnyDamageTaken 方法中修改位置计算逻辑

**Q: 想要不同的动画效果？**
- A: 修改 FloatingDamageText.cs 中的 _Process 方法
- A: 编辑场景文件添加更多视觉效果（缩放、旋转等）

**Q: 能否针对不同敌人显示不同颜色？**
- A: 可以！修改 ShowFloatingDamage 方法添加敌人类型参数，根据类型选择颜色

## 性能考虑

- 每个飘字在 DurationSeconds 后自动被释放
- 系统使用事件驱动，不会轮询或浪费CPU
- 临时对象会被GC自动清理

## 集成案例

这个系统已经自动集成到现有的伤害系统中：
- ✅ 敌人受伤时自动显示
- ✅ 玩家受伤时自动显示（如果需要）
- ✅ 各种Effect造成的伤害都会显示
- ✅ 武器伤害会显示
- ✅ Skill伤害会显示
