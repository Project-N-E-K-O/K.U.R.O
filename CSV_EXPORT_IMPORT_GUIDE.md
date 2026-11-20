# CSV 数据导出与导入指南

项目使用 CSV 批量查看和调整资源参数。游戏运行时读取资源和场景，编辑表格后要运行回写工具才会更新这些源文件。

| 操作 | 入口 | 结果 |
|---|---|---|
| 导出项目数据 | [run_export.ps1](scripts/tools/run_export.ps1) 调用 [ExportCsv.gd](scripts/tools/ExportCsv.gd) | 从 .tres 和角色 .tscn 生成 data/ 下的四张 CSV |
| 回写项目数据 | 在 Godot 编辑器运行 [RunImportCsv.gd](scripts/tools/RunImportCsv.gd) | [ImportCsv.gd](scripts/tools/ImportCsv.gd) 更新已有 .tres 和角色 .tscn |
| Godot 文件系统中的「重新导入」 | [csv-data-importer](addons/csv-data-importer/import_plugin.gd) 的 CSV Data 导入器 | 从 CSV 或 TSV 生成可读取的 CsvData 资源，源 .tres 和 .tscn 保持原值 |

## 导出

在项目根目录运行：

~~~powershell
powershell -ExecutionPolicy Bypass -File scripts\tools\run_export.ps1
~~~

脚本中的 Godot 可执行文件路径需要和本机一致。本地已配置 VS Code 任务时，也可以通过 **Ctrl+Shift+B** 运行 **ExportCsv: 导出全部数据**。

启动脚本会临时停用 CSV Data 插件并清理旧输出。ExportCsv.gd 以文本方式读取源文件，将数据写到 .csv.tmp。Godot 退出后，PowerShell 将临时文件改为 .csv，恢复插件配置并检查输出。重新导出会替换现有表格。表格中尚未回写的修改应先处理。

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

## 待接入的表格

当前批处理范围是上述四张表。构筑卡表和房间目录尚未接入。

房间目录计划从 [hotel_room_catalog.tres](resources/stages/hotel_room_catalog.tres) 的 Rooms 导出，每个 RoomDefinition 占一行，包含 ScenePath、FloorTheme、RoomType、RouteTags、ElevatorPreviewName 和 ElevatorPreviewIcon。RoomType 使用枚举名称，RouteTags 使用竖线分隔，图标列使用资源路径。实际房间排列仍由 [StageConfig](scripts/systems/stage/StageConfig.cs) 和 [StageGeneratorManager](scripts/managers/StageGeneratorManager.cs) 决定。

建议先提供只读导出，再加入显式回写。回写前将 uid:// 和 res:// 解析为同一场景身份，检查重复、非法类型和无法加载的资源。首版按场景更新已有条目，新增、删除和替换场景仍在 Inspector 中操作。校验通过后统一保存目录。
