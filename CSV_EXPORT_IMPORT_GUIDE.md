# CSV 数据导出 / 导入说明

本文档说明项目中 CSV 工作流的原理、使用方法及各 CSV 文件的字段含义。

---

## 工作流概览

```
.tres 资源文件                   CSV 文件                    游戏运行时
─────────────────────────────────────────────────────────────────────
resources/items/*.tres  ──┐
resources/builds/*.tres ──┤  [导出 Export]   data/items.csv
resources/items/skills/  ──┤  ExportCsv.gd   data/builds.csv
resources/loot/*.tres   ──┘  (GDScript)     data/skills.csv
                                             data/loot.csv
                                                 │
                                                 │  [导入 Import]
                                                 │  csv-data-importer 插件
                                                 ▼
                                         data/items.res  ← Array[Dictionary]
                                         data/builds.res
                                         data/skills.res
                                         data/loot.res
```

| 方向 | 工具 | 说明 |
|------|------|------|
| **Export** (.tres → CSV) | `ExportCsv.gd` + `run_export.ps1` | 读取所有资源文件，提取关键字段写入 CSV |
| **Import** (CSV → .res) | `csv-data-importer` 插件 | Godot 编辑器自动处理，CSV 保存后立即生效 |

> **注意：** csv-data-importer 插件将 CSV 导入为 `Array[Dictionary]` 资源（`.res` 文件），
> 不会修改原始的 `.tres` 资源文件。如需用 CSV 数据驱动运行时逻辑，需在游戏代码中
> 读取 `data/items.res` 等资源。

---

## 导出（Export）

### 快速使用

在 VS Code 中按 **Ctrl+Shift+B**，选择 **"ExportCsv: 导出全部数据"** 任务即可。

或在终端手动运行：
```powershell
powershell -ExecutionPolicy Bypass -File scripts\tools\run_export.ps1
```

### 运行流程

`run_export.ps1` 按以下步骤执行：

1. **暂停插件** — 从 `project.godot` 中临时移除 `csv-data-importer`，避免 headless 模式下文件句柄冲突
2. **运行 Godot headless** — 执行 `ExportCsv.gd` 脚本，扫描 `.tres` 文件并生成 CSV
3. **恢复插件** — 将 `project.godot` 还原，插件恢复正常状态
4. **验证输出** — 检查 4 个 CSV 文件是否成功生成并显示文件大小

### 源文件

| 文件 | 说明 |
|------|------|
| `scripts/tools/ExportCsv.gd` | GDScript 导出工具，以文本解析方式读取 `.tres`（不依赖 C# 程序集） |
| `scripts/tools/run_export.ps1` | PowerShell 执行器，管理插件状态并调用 Godot headless |

---

## 导入（Import）

### 原理

`csv-data-importer` 插件（`addons/csv-data-importer/`）是一个 **EditorImportPlugin**，
它监听 `.csv` / `.tsv` 文件的变化，当文件被添加或修改时自动生成对应的 `.res` 资源。

生成的 `.res` 资源类型为 `CsvData`（继承 `Resource`），包含：
```gdscript
@export var records: Array  # Array[Dictionary] — 每行一个字典，键为表头列名
```

### 触发方式

在 Godot 编辑器中保存或重新导入任意 CSV 文件时自动触发。也可以在
**文件系统面板** 右键 CSV 文件 → **重新导入** 手动触发。

### 插件配置（可在编辑器导入对话框修改）

| 选项 | 默认值 | 说明 |
|------|--------|------|
| `delimiter` | Comma | 分隔符（逗号 / Tab） |
| `headers` | false | 第一行是否作为字典键（建议开启） |
| `detect_numbers` | false | 是否将纯数字字段转为 int/float |
| `force_float` | true | detect_numbers 开启时，强制转为 float |

> 推荐设置：`headers = true`，`detect_numbers = false`（保持字符串，方便与 .tres 数据对比）

### 在游戏代码中读取 CSV 资源（示例）

```gdscript
# 加载已导入的 CSV 资源
var csv = load("res://data/items.res") as CsvData
for record in csv.records:
    print(record["ItemId"], " - ", record["DisplayName"])
```

---

## CSV 文件字段说明

### `data/items.csv` — 物品定义（ItemDefinition）

来源：`resources/items/*.tres`

| 列名 | 类型 | 说明 |
|------|------|------|
| `file` | String | .tres 文件名（不含扩展名） |
| `ItemId` | String | 物品唯一 ID |
| `DisplayName` | String | 显示名称 |
| `Description` | String | 描述文本 |
| `Category` | String | 类别（Weapon / Supplies / Furniture 等） |
| `Tags` | String | 标签列表，用 `\|` 分隔（如 `tag_weapon\|build_machine`） |
| `MaxStackSize` | int | 最大堆叠数量 |
| `BuildClass` | String | 归属构筑类型（guard / machine 等） |
| `LevelCount` | int | 武器等级档次 |
| `IsThrowable` | bool | 是否可投掷 |
| `IsFurniture` | bool | 是否为家具 |
| `attack_power` | float | 攻击力属性值（来自 AttributeEntries） |
| `MaxDurability` | int | 最大耐久度（来自 DurabilityConfig） |
| `DamagePerHit` | int | 每次命中消耗耐久（来自 DurabilityConfig） |
| `SkillRefs` | String | 关联技能文件名，用 `\|` 分隔 |

---

### `data/builds.csv` — 构筑效果（BuildLevelEffectEntry）

来源：`resources/builds/*.tres`

| 列名 | 类型 | 说明 |
|------|------|------|
| `file` | String | .tres 文件名 |
| `BuildId` | String | 构筑唯一 ID |
| `BuildClass` | String | 构筑类型（guard / machine 等） |
| `BuildName` | String | 构筑显示名称 |
| `Level` | int | 构筑等级（1-N） |
| `RequiredPoints` | int | 激活所需点数 |
| `EffectId` | String | 效果 ID（对应 C# 效果类） |
| `EffectScript` | String | 效果 C# 类名 |
| `Description` | String | 效果描述 |

---

### `data/skills.csv` — 武器技能（WeaponSkillDefinition）

来源：`resources/items/skills/*.tres`

| 列名 | 类型 | 说明 |
|------|------|------|
| `file` | String | .tres 文件名 |
| `SkillId` | String | 技能唯一 ID |
| `DisplayName` | String | 显示名称 |
| `AnimationName` | String | 对应动画名称 |
| `DamageMultiplier` | float | 伤害倍率（默认 1.0） |
| `CooldownSeconds` | float | 冷却时间（秒） |
| `ShowHitboxDebug` | bool | 是否显示判定框调试 |
| `Description` | String | 技能描述 |
| `ActivationAction` | String | 触发动作名（如 `weapon_skill_block`） |

---

### `data/loot.csv` — 掉落表（LootDropTable）

来源：`resources/loot/*.tres`

每个掉落条目（Entry）占一行，同一张表会有多行。

| 列名 | 类型 | 说明 |
|------|------|------|
| `table_file` | String | 掉落表 .tres 文件名 |
| `SelectionMode` | int | 选取模式：`0`=Sequential，`1`=PickOne |
| `GlobalDropChance` | float | 整体触发概率（0-1） |
| `MaxDrops` | int | 最大掉落数量（0=不限） |
| `ScatterRadius` | float | 物品散落半径（像素） |
| `DefaultImpulse` | float | 默认弹射力度 |
| `entry_index` | int | 条目序号（从 1 开始） |
| `item_file` | String | 掉落物品的 .tres 文件名 |
| `DropChance` | float | 该条目的掉落概率 |
| `MaxStacks` | int | 最多掉落堆叠数 |
| `ImpulseStrength` | float | 该条目的弹射力度 |
| `ImpulseSpreadDegrees` | float | 弹射散开角度（度） |

---

## 常见问题

**Q: 为什么导出脚本要暂停插件？**  
A: `csv-data-importer` 插件在 Godot headless 模式下可能对 CSV 文件持有文件句柄，
导致写入失败。暂停插件可确保导出过程无冲突。

**Q: CSV 数据改变后需要重新运行导出吗？**  
A: 不需要。CSV 数据流向是单向的：`.tres` → CSV。
修改 `.tres` 资源后重新运行导出，CSV 会被更新。
CSV 文件本身仅供查阅或通过插件导入到游戏运行时使用，不会反向修改 `.tres`。

**Q: 导出后 Godot 输出了 "ObjectDB instances leaked" 警告，正常吗？**  
A: 正常。这是 Godot headless 退出时的已知警告，不影响导出结果，退出码为 0 即成功。
