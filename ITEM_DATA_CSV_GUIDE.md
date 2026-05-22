# ItemDataCsv 工具使用说明

`scripts/tools/ItemDataCsv.gd` 是一个 Godot 编辑器脚本工具，用于将项目中的 `.tres` 资源文件批量导出为 CSV 表格，修改后可一键回写，无需逐个打开 `.tres` 文件编辑。

---

## 支持的资源类型

| 资源类型 | 来源目录 | 输出文件 | 标识字段 |
|---|---|---|---|
| `ItemDefinition`（道具/武器/家具） | `resources/items/` | `data/items.csv` | `ItemId` |
| `BuildLevelEffectEntry`（Build 等级效果） | `resources/builds/` | `data/builds.csv` | `BuildId` |
| `WeaponSkillDefinition`（武器技能） | `resources/items/skills/` | `data/skills.csv` | `SkillId` |
| `LootDropTable`（掉落表） | `resources/loot/` | `data/loot.csv` | 文件名 |

---

## 使用步骤

### 第一步：在 Godot 编辑器中打开脚本

在 **FileSystem** 面板中找到 `scripts/tools/ItemDataCsv.gd`，双击打开到脚本编辑器。

### 第二步：设置 MODE

在脚本顶部找到 `MODE` 常量，改为你需要的操作：

```gdscript
const MODE := "export_items"   # ← 修改这里
```

可选值见下方"模式一览"。

### 第三步：点击运行

点击脚本编辑器**右上角的 ▶ 按钮**（Run）执行脚本。

执行结果会显示在 Godot 底部的**输出面板**中，例如：
```
ItemDataCsv [export_items] 完成：47 条 → res://data/items.csv
```

---

## 模式一览

### 导出模式（.tres → CSV）

| MODE | 说明 |
|---|---|
| `"export_items"` | 导出所有道具/武器定义到 `data/items.csv` |
| `"export_builds"` | 导出所有 Build 等级效果到 `data/builds.csv` |
| `"export_skills"` | 导出所有武器技能定义到 `data/skills.csv` |
| `"export_loot"` | 导出所有掉落表到 `data/loot.csv` |

### 导入模式（CSV → .tres）

| MODE | 说明 |
|---|---|
| `"import_items"` | 读取 `data/items.csv`，更新对应 `.tres` 文件 |
| `"import_builds"` | 读取 `data/builds.csv`，更新对应 `.tres` 文件 |
| `"import_skills"` | 读取 `data/skills.csv`，更新对应 `.tres` 文件 |
| `"import_loot"` | 读取 `data/loot.csv`，更新掉落概率等字段 |

---

## CSV 字段说明

### items.csv（ItemDefinition）

| 列名 | 类型 | 说明 | 可回写 |
|---|---|---|---|
| `ItemId` | String | 唯一标识，用于匹配 `.tres` 文件 | ✗ |
| `DisplayName` | String | 显示名称 | ✓ |
| `Description` | String | 描述文本 | ✓ |
| `Category` | String | 分类（Weapon / Furniture 等） | ✓ |
| `Tags` | String | 标签，多个用 `\|` 分隔，如 `tag_weapon\|build_guard` | ✓ |
| `attack_power` | float | 攻击力数值（来自 AttributeEntries） | ✓ |
| `BuildClass` | String | 所属 Build 类别（guard / machine 等） | ✓ |
| `LevelCount` | int | 武器连招段数 | ✓ |
| `IsThrowable` | bool | 是否可投掷 | ✓ |
| `IsFurniture` | bool | 是否为家具类道具 | ✓ |
| `MaxDurability` | int | 最大耐久度（来自 DurabilityConfig） | ✓ |
| `ThrowHorizontalDistance` | float | 投掷水平距离 | ✓ |
| `ThrowParabolicDuration` | float | 投掷抛物线时长（秒） | ✓ |
| `ThrowParabolicLandingYOffset` | float | 投掷落点 Y 偏移 | ✓ |

> **不导出的字段**：`Icon`（图标引用）、`EffectEntries`（场景引用）、`WeaponSkillResources`（技能引用）、`DurabilityConfig` 的其他子字段、`ThrowStartOffset`。这些字段含资源路径引用，需在 Godot Inspector 中直接修改。

---

### builds.csv（BuildLevelEffectEntry）

| 列名 | 类型 | 说明 | 可回写 |
|---|---|---|---|
| `BuildId` | String | 唯一标识 | ✗ |
| `BuildClass` | String | 所属 Build 类别 | ✓ |
| `BuildName` | String | 显示名称 | ✓ |
| `Level` | int | Build 等级（1/2/3） | ✓ |
| `RequiredPoints` | int | 激活所需积分 | ✓ |
| `EffectId` | String | 效果标识符 | ✓ |
| `EffectScript` | String | 对应的 C# 效果脚本类名 | ✓ |
| `Description` | String | 描述文本 | ✓ |

---

### skills.csv（WeaponSkillDefinition）

| 列名 | 类型 | 说明 | 可回写 |
|---|---|---|---|
| `SkillId` | String | 唯一标识 | ✗ |
| `DisplayName` | String | 显示名称 | ✓ |
| `AnimationName` | String | 对应动画名称 | ✓ |
| `ActivationAction` | String | 触发输入动作名（如 `weapon_skill_block`） | ✓ |
| `Description` | String | 描述文本 | ✓ |

> **不导出的字段**：`Effects`（含 PropertyOverrides 的复杂子资源），需在 Godot Inspector 中配置。

---

### loot.csv（LootDropTable）

| 列名 | 类型 | 说明 | 可回写 |
|---|---|---|---|
| `LootTableFile` | String | `.tres` 文件名（不含扩展名），用于定位文件 | ✗ |
| `MaxDrops` | int | 最多掉落数量 | ✓ |
| `ScatterRadius` | float | 掉落物散布半径（像素） | ✓ |
| `DefaultImpulse` | float | 掉落物弹出初速度 | ✓ |
| `Entries` | String | 掉落条目，格式：`ItemId:概率\|ItemId:概率`，如 `Weapon_Stab_drill:0.2500\|Weapon_Throw_MetalSpike:0.2500` | ✓（仅概率） |

> **Entries 回写限制**：只能修改现有条目的 `DropChance`（概率）；无法通过 CSV **新增或删除**掉落条目，此操作需在 Godot Inspector 中进行。

---

## 典型工作流

### 批量调整武器攻击力

```
1. MODE = "export_items"  →  运行  →  生成 data/items.csv
2. 用 Excel 打开 data/items.csv，筛选 Category = "Weapon"
3. 修改 attack_power 列的数值
4. 保存 CSV（注意保持 UTF-8 编码）
5. MODE = "import_items"  →  运行  →  所有 .tres 自动更新
```

### 调整掉落概率

```
1. MODE = "export_loot"  →  运行  →  生成 data/loot.csv
2. 找到目标掉落表行，修改 Entries 列中对应 ItemId 后面的概率值
   例：将 Weapon_Stab_drill:0.2500 改为 Weapon_Stab_drill:0.4000
3. MODE = "import_loot"  →  运行
```

### 修改 Build 说明文本

```
1. MODE = "export_builds"  →  运行
2. 编辑 data/builds.csv 中的 BuildName / Description 列
3. MODE = "import_builds"  →  运行
```

---

## 注意事项

- **CSV 编码**：保存时请使用 **UTF-8**（Excel 另存为时选"CSV UTF-8"），否则中文会乱码。
- **不要修改 ItemId / BuildId / SkillId / LootTableFile 列**：这些字段是工具定位 `.tres` 文件的依据，改动后该行将被跳过。
- **回写不影响其他字段**：脚本只更新表中列出的字段，`.tres` 文件里的 UID、脚本引用、图标路径、场景引用等均不会被改动。
- **导入前建议先导出**：确保 CSV 是最新数据的基础上再修改，避免覆盖掉手动在 Inspector 里做的修改。
- **Godot 编辑器中刷新**：导入完成后，若编辑器中已打开相关 `.tres`，需手动重新打开或重启编辑器以看到最新值。
