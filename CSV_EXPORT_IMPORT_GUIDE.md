# CSV 数据导出与导入指南

项目使用 CSV 批量查看和调整资源参数。游戏运行时读取资源和场景，编辑表格后要运行回写工具才会更新这些源文件。

| 操作 | 入口 | 结果 |
|---|---|---|
| 导出项目数据 | [run_export.ps1](scripts/tools/run_export.ps1) 调用 [ExportCsv.gd](scripts/tools/ExportCsv.gd) | 从 .tres 和角色 .tscn 生成 data/ 下的五张 CSV |
| 回写项目数据 | 在 Godot 编辑器运行 [RunImportCsv.gd](scripts/tools/RunImportCsv.gd) | [ImportCsv.gd](scripts/tools/ImportCsv.gd) 更新已有 .tres 和角色 .tscn |
| 回写房间目录 | 在 Godot 编辑器运行 [RunImportRoomCatalogCsv.gd](scripts/tools/RunImportRoomCatalogCsv.gd) | 校验并更新已有房间记录 |
| Godot 文件系统中的「重新导入」 | [csv-data-importer](addons/csv-data-importer/import_plugin.gd) 的 CSV Data 导入器 | 从 CSV 或 TSV 生成可读取的 CsvData 资源，源 .tres 和 .tscn 保持原值 |

## 导出

在项目根目录运行：

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts\tools\run_export.ps1
~~~

脚本中的 Godot 可执行文件路径需要和本机一致。本地已配置 VS Code 任务时，也可以通过 **Ctrl+Shift+B** 运行 **ExportCsv: 导出全部数据**。

启动脚本会临时停用 CSV Data 插件并清理上次遗留的临时文件。ExportCsv.gd 读取源文件，将数据写到 .csv.tmp。Godot 成功退出后，PowerShell 用临时文件替换 CSV，恢复插件配置并检查输出。房间目录使用 Godot .NET 读取，修改 C# 后应先构建项目。表格中尚未回写的修改应在重新导出前处理。

## 导入并回写源文件

1. 用最新导出表作为编辑起点。保留原表头，保存为 UTF-8、逗号分隔的 CSV。
2. 通过文件系统打开 scripts/tools/RunImportCsv.gd，在 Godot 脚本编辑器的 **文件 → 运行** 执行该 EditorScript。
3. 查看输出面板。工具按技能、物品、掉落、角色的顺序批量回写。运行后检查相应 .tres 和 .tscn 的差异。

物品、技能和角色通过 file 列匹配文件名，掉落通过 table_file 和 entry_index 匹配。调整 ItemId 或 SkillId 时，对应的字符串字段也会被写回。

空单元格的处理取决于字段。物品和技能的普通字符串字段会写入空字符串，缺列时也如此。数值、布尔、数组等由对应的回写函数处理，空值通常跳过。角色列为空时保留场景原值。修改单个字段时，建议保留表内其他字段和值。

## 支持的表格和字段

| CSV | 源文件 | 回写定位 |
|---|---|---|
| data/items.csv | resources/items/*.tres | file，不带 .tres 的文件名 |
| data/skills.csv | resources/items/skills/*.tres | file，不带 .tres 的文件名 |
| data/loot.csv | resources/loot/*.tres | table_file，不带 .tres 的文件名。条目按从 1 开始的 entry_index 匹配 |
| data/characters.csv | scenes/actors/characters/*.tscn | file，不带 .tscn 的文件名 |
| data/hotel_room_catalog.csv | resources/stages/hotel_room_catalog.tres | ScenePath，接受 res:// 或 uid:// |

### 物品

- 基础字段包括 ItemId、DisplayName、Description、Category、Tags、MaxStackSize 与 attack_power。attack_power 更新已有 AttributeEntries 中匹配的属性条目。
- 投掷字段包括 IsThrowable、IsThrowWeapon、PreventDropDuringCooldown、SpawnEffectOnThrow、ThrowStartOffset、ThrowParabolicDuration、ThrowParabolicPeakHeight、ThrowHorizontalDistance、ThrowParabolicLandingYOffset、ThrowWeaponCooldown、UnpickedLifetime 和 ThrowTier。
- Tags 用竖线分隔，ThrowStartOffset 用「X|Y」表示。SkillRefs 导出关联技能文件名供查看，关联关系在 Godot Inspector 中编辑。

### 武器技能

- 基础字段包括 SkillId、DisplayName、SkillType、AnimationName、DamageMultiplier、CooldownSeconds、ShowHitboxDebug、Description、ActivationAction 和 AllowHoldContinuousAttack。SkillType 的 0 为 Passive，1 为 Active。
- Warmup、Active、Recovery 的 Duration 和 AnimationSpeed 列分别控制各阶段时长和播放速度。冲刺攻击另有带 Dash 前缀的阶段列，以及 DashAnimationName、DashDamageMultiplier、DashAttackSpeedSource、DashAttackFixedSpeed、DashAttackSpeedMultiplier 和 DashAttackDecayWindow。各字段的继承规则见 [WeaponSkillDefinition.cs](scripts/items/weapons/WeaponSkillDefinition.cs)。
- AttackFallImmunities 和 DashAttackFallImmunities 分别配置普通攻击和冲刺攻击的免坠落阶段。0 表示无，1 为 Warmup，2 为 Active，4 为 Recovery，可相加组合。导出缺省值为 0，导入缺列或空值时保留原值，填写 0 会清除配置。
- Effects、DashEffects 和攻击阶段特效条目在 Godot Inspector 中维护。

### 掉落表

- 同一个 table_file 可有多行。表级字段 SelectionMode、GlobalDropChance、MaxDrops、ScatterRadius 和 DefaultImpulse 使用该表第一行的值。SelectionMode 的 0 为 Sequential，1 为 PickOne。
- 每条掉落按 entry_index 回写 DropChance、MaxStacks、ImpulseStrength 和 ImpulseSpreadDegrees。item_file 用于查看掉落物引用，修改引用和增删条目在 Godot Inspector 中完成。

### 角色

当前处理场景根节点的 Speed、AttackDamage、AttackCooldown、MaxHealth 和 AiDescription。前三项以浮点形式回写。导出只读取 .tscn 根节点里显式配置的字段，空值表示该根节点没有对应配置。

## Godot 的 CSV Data 导入器

编辑器可将 CSV 或 TSV 导入为 [CsvData](addons/csv-data-importer/csv_data.gd)。在导入面板开启 Headers 后，records 的每行是按列名访问的字典。可按需要开启 Detect Numbers。代码通过原始 CSV 的 res:// 路径加载导入结果，例如：

~~~gdscript
var table := load("res://data/items.csv") as CsvData
for row in table.records:
	print(row["ItemId"])
~~~

## 酒店房间目录

每个 `RoomDefinition` 导出一行。可以按楼层主题、类型和路线标签筛选。实际房间排列由 [StageConfig](scripts/systems/stage/StageConfig.cs) 和 [StageGeneratorManager](scripts/managers/StageGeneratorManager.cs) 决定。

| 列名 | 格式 |
|---|---|
| `ScenePath` | 用于匹配已有房间。导出显示 `res://` 路径 |
| `FloorTheme` | 非空楼层主题，如 `C`、`F` 或 `HiddenA` |
| `RoomType` | 区分大小写的枚举名，如 `Begin`、`Combat`、`End` 或 `BossCore` |
| `RouteTags` | 多个标签用竖线分隔，可留空清除标签 |
| `ElevatorPreviewName` | 电梯显示名，可留空清除自定义名称 |
| `ElevatorPreviewIcon` | 贴图资源路径，可留空移除图标。`@embedded` 保留该房间已有的内嵌图标 |

先保存 Inspector 里对目录的修改，再编辑 [房间 CSV](data/hotel_room_catalog.csv) 并在 Godot 脚本编辑器运行 `RunImportRoomCatalogCsv.gd`。可以调整 CSV 行顺序，也可以仅回写部分房间。新增、删除和替换场景通过 Inspector 完成。

回写前会将 `uid://` 和 `res://` 统一为同一场景身份。检测到重复房间、未登记的场景、无效类型或无法读取的图标时，保留原目录。整表验证通过后才保存，资源 UID 和房间顺序保持原值。
