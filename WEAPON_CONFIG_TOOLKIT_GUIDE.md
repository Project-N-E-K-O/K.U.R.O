# WeaponConfigToolkit 使用教程与注意事项

本文档介绍如何使用一键配置武器 ItemDefinition 的工具脚本 `WeaponConfigToolkit`，并总结常见问题与排查方式。

## 1. 工具目标

`WeaponConfigToolkit` 用于减少手工配置武器资源的重复操作，主要提供三类能力：

- 一键创建/更新武器资源包（ItemDefinition + WeaponSkillDefinition）
- 一键校验全部武器资源配置
- 一键修复武器攻击力与堆叠等历史异常数据

---

## 2. 前置准备

请确保以下文件和路径有效：

- 武器场景 `.tscn` 已存在，且包含 `AttackArea`
- `AttackArea` 下至少有一个可用 `CollisionShape2D`
- 目标输出目录可写（默认：`res://resources/items`、`res://resources/items/skills`）

---

## 3. 如何使用（一步一步）

### 3.1 挂载工具节点

1. 在任意编辑器场景里新建一个普通 `Node`。
2. 将脚本设置为 `res://scripts/tools/WeaponConfigToolkit.cs`。
3. 选中该节点，在 Inspector 填写参数。

### 3.2 配置创建参数

在 `Create Weapon Package` 分组填写：

- `ItemId`：例如 `Weapon_Stab_drill`
- `ItemDisplayName`：武器显示名
- `ItemDescription`：描述文本
- `WeaponScenePath`：武器场景路径（必须是 `res://...`）
- `ItemIcon`：图标（可选）
- `ItemCategory`：通常为 `Weapon`
- `WeaponAttackPower`：攻击力，限制范围 `0-999`
- `OverwriteAttackPowerIfExists`：已有攻击力是否覆盖

技能相关参数：

- `SkillId`：例如 `weapon_stab_drill`
- `SkillDisplayName`
- `SkillAnimationName`
- `SkillCooldownSeconds`
- `SkillDamageMultiplier`
- `SkillActivationAction`

### 3.3 一键生成/更新

将 `CreateOrUpdateWeaponPackage` 勾选为 `true`，工具会自动执行并恢复为 `false`。

执行后将自动：

- 创建或更新 ItemDefinition 资源
- 创建或更新 WeaponSkillDefinition 资源
- 绑定 `WorldScenePath`
- 绑定 `WeaponSkillResources`
- 强制 `MaxStackSize = 1`
- 写入或修正 `attack_power`

---

## 4. 校验与修复

### 4.1 校验全部武器配置

将 `ValidateAllWeaponItems` 勾选为 `true`。

会检查以下问题：

- `ItemId` 为空
- `WorldScenePath` 为空、不是 `res://`、或资源不存在
- 武器场景缺少 `AttackArea`
- `AttackArea` 缺少有效 `CollisionShape2D`
- `WeaponSkillResources` 为空或类型错误
- `SkillId` 为空
- `MaxStackSize` 非 1
- 缺失 `attack_power` 或 `attack_power < 0`

### 4.2 一键修复历史异常

将 `RepairAllWeaponAttackPowerValues` 勾选为 `true`。

会自动修复：

- `MaxStackSize != 1`
- 缺失 `attack_power`
- `attack_power` 非法值（负数、异常值）

---

## 5. attack_power 特别说明

当前系统对 `attack_power` 做了双重保护：

- 工具层：写入前会将值限制为 `0-999`
- 数据模型层（`ItemAttributeEntry`）：当 `AttributeId = attack_power` 时，任何来源写入都会被限制为 `0-999`

这意味着即使手动输入异常值，也会被自动修正。

---

## 6. 常见错误与排查

### 问题 A：点击创建时报类型转换异常

症状：`Unable to cast object of type 'Godot.Resource' to WeaponSkillDefinition`

原因：历史资源类型与当前脚本类型不一致。

处理：

- 已在工具中做了安全加载与重建逻辑
- 再次执行 `CreateOrUpdateWeaponPackage` 即可自动重建

### 问题 B：武器判定与预期不一致

优先检查：

- `ItemDefinition.WorldScenePath` 是否绑定到正确武器场景
- 武器场景 `AttackArea` 与 `CollisionShape2D` 是否存在
- 是否使用校验功能发现错误

### 问题 C：攻击力出现异常值

处理顺序：

1. 执行 `RepairAllWeaponAttackPowerValues`
2. 再执行一次 `CreateOrUpdateWeaponPackage`
3. 打开对应 `.tres` 确认 `attack_power` 在 `0-999`

---

## 7. 推荐工作流（团队）

每次新增武器建议按以下顺序：

1. 先做武器场景并配置 `AttackArea`
2. 用 `WeaponConfigToolkit` 一键创建资源
3. 运行 `ValidateAllWeaponItems`
4. 提交前再执行一次 `RepairAllWeaponAttackPowerValues`

这样可以最大程度避免“场景做了但资源绑定漏项”的问题。

---

## 8. 相关脚本

- `res://scripts/tools/WeaponConfigToolkit.cs`
- `res://scripts/items/attributes/ItemAttributeEntry.cs`

如需进一步升级为顶部菜单插件（无需挂节点），可在后续迭代中实现 EditorPlugin 入口。
