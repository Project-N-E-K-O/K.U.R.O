# Machine 构筑卡牌系统

## 实施顺序（6 个 Phase，增量可测试）

---

### Phase 1：数据层 — 字段定义 + 适配现有逻辑

**✅ 已完成**

1. `BuildEffectDefinition.cs`：
   - 新增 `BuildRarity` 枚举 (Common, Rare, Epic)
   - 新增 `Rarity` (BuildRarity)
   - 新增 `Weight` (int)
   - 新增 `MaxStacks` (int)
   - `EffectScene` → `EffectEntries` (Array\<AttackEffectEntry\>)
   - 移除 `StatBonuses`（统一进 EffectEntries 的 PropertyOverrides）
   - 移除 `BuildBranch`（冗余，EffectId 前缀已编码流派）
   - ExportGroup 改为英文

2. `BuildCoreDefinition.cs`：
   - 新增 `AllowedEffectClasses` (Array\<string\>，如 ["Machine", "Generic"])

3. `BuildSelectionManager.cs`（适配新字段，已随 Phase 1 完成）：
   - 所有 `effect.EffectScene` → 遍历 `effect.EffectEntries` + `entry.InstantiateEffect()`
   - 移除 `effect.StatBonuses.Count > 0` 代码路径（两处：ApplyEffectBonuses + RestoreBuildState）
   - 新增 `using Kuros.Actors.Enemies.Attacks` 引用

4. 待后续完成：18 个 `resources/builds/BuildMachine_*.tres` + 3 个 `resources/builds/core/*.tres` 补上新字段（Phase 6）

**验证**：dotnet build 0 error，Inspector 新字段可见

**涉及文件**：
- `scripts/systems/BuildEffectDefinition.cs` ✅
- `scripts/systems/BuildCoreDefinition.cs` ✅
- `scripts/managers/BuildSelectionManager.cs` ✅

---

**✅ 已完成**

**依赖 Phase 1 数据到位**

✅ 1. `PickRandomEffects()` 改为加权随机：
✅    - **有 activeCore 且 `AllowedEffectClasses` 非空**：按数组内容过滤，如 `["Machine", "Generic"]` 只出 Machine + Generic 卡
✅    - **`AllowedEffectClasses` 为空或 null**：仅按 `_playerCoreClass` 过滤（不自动加 Generic）
✅    - 按 `effectiveWeight = Weight * RarityMultiplier[rarity]` 加权抽样
	 - `Weight`（卡牌级）：控制同类稀有度内部的相对概率。如两张 Common 卡 Weight=20 的比 Weight=10 的多出现一倍
	 - `RarityMultiplier`（系统级）：控制不同稀有度之间的整体出现比例。如 Common×3, Epic×0.3 → Common 出现频率是 Epic 的 10 倍
	 - 改单卡概率调 Weight，改整体稀有度分布调 RarityMultiplier，互不干扰
✅    - 排除 `_pickedEffectIds[effectId] >= effect.MaxStacks` 的卡牌
✅    - 加权抽样：累积权重法逐张抽取，抽一张移一张重算权重

2. 新增 export `RarityMultiplier` Dictionary：
   ```csharp
   [Export] public Godot.Collections.Dictionary<string, float> RarityMultiplier { get; set; }
   ```
   默认：`{"Common": 3.0, "Rare": 1.0, "Epic": 0.3}`

**验证**：加权随机分布合理、MaxStacks 到期卡牌不再出现、MachineCore 下不出现 Waiter/Throw 卡牌

**涉及文件**：
- `scripts/managers/BuildSelectionManager.cs`

#### 可选增强：未选核心时默认卡池

当前 `CheckAndTriggerSelection` 第 130 行要求 `_coreSelected == true`，不选核心永远不会触发三选一。如需支持不选核心也能获得卡牌：

✅ 1. 新增 export `DefaultBuildClass`（默认空 = 保持旧行为）：
   ```csharp
   [Export] public string DefaultBuildClass { get; set; } = "";
   ```
✅ 2. `CheckAndTriggerSelection` 守卫改为：`if (!_coreSelected && string.IsNullOrWhiteSpace(DefaultBuildClass)) return;`
✅ 3. `PickRandomEffects` 回退路径中，`_playerCoreClass` 为空时用 `DefaultBuildClass` 替代

不影响已选核心的玩家，核心选择后 `_coreSelected = true`，走 AllowedEffectClasses 路径不变。

---

### Phase 3：效果应用 — 常规基础数值由BuildStatBonusEffect 堆叠

**✅ 已完成**

1. ✅ 效果由 `EffectEntries` 的 PropertyOverrides 驱动（含 `BuildStatBonusEffect.StatBonuses`）
2. ✅ `BuildStatBonusEffect` 新增 `StackMultiplier` export（默认 0.25）
3. ✅ **`BuildStatBonusEffect` 不是必经路径**——`EffectEntries[0].Scene` 是自由 export，指向什么就是什么：
   - 纯数值卡 → BuildStatBonusEffect.tscn（改 GameActor 属性，服务于未来 Normal/Generic）
   - 条件卡 → 自定义 ActorEffect 脚本（如 MachineAttackHeat.tscn，直接访问 MachineCoreEffect）
   - 生成卡 → 自定义脚本（如 MachineFlameRing.tscn，生成场景 + BoomDmg）
   - 18 张 Machine 卡全部是 MachineCore 专属属性/条件触发，**不会用到 BuildStatBonusEffect**
4. ✅ `OnApply`：`_refreshCount=0, scale=1.0`（首次选卡 = 基础值）
4. ✅ `OnStackRefreshed`：`_refreshCount++, scale = 1 + _refreshCount × StackMultiplier`
5. ✅ 第 2 次选 scale=1.25，第 3 次选 scale=1.50（StackMultiplier=0.25 时）
6. ✅ `AllowableEffectClasses` 为空 → 自动退回到 `_playerCoreClass`
7. ✅ `MaxStacks` 到期 → Phase 2 已在 PickRandomEffects 中排除

**验证**：同一卡牌选到第 N 次时效果数值递增

**涉及文件**：
- `scripts/core/effects/BuildStatBonusEffect.cs` ✅
- `resources/effects/BuildStatBonusEffect.tscn` ✅（新建，供 EffectEntries[0].Scene 引用）
- `scripts/managers/BuildSelectionManager.cs`（无需改动，Refresh 已在 Phase 1 接入）

---

### Phase 4：20 张 Machine 卡牌 + 3 个 Core 的 .tres 批量生成 ✅

**已完成。实际生成 20 张（A_001-A_010 + B_001-B_010），3 个 Core（MachineCore / WaiterCore / ThrowCore）。**

#### 现状：已有 13 个 .tres，存在以下问题

| 问题 | 涉及文件 |
|---|---|
| 缺少 `Rarity` | 全部 13 个 |
| 缺少 `Weight` | 全部 13 个 |
| `DisplayName`/`Description` 为占位文本 | B_001, B_002, B_003 |
| 残留废弃 `StatBonuses`（Phase 1 已移除） | B_001, B_003 |
| `EffectEntries[0]` 为 null | A_001 |
| 缺少 `EffectEntries` | 多数 A 系列 |
| 缺少 `Icon` | A_005 等 |

#### MaxStacks 按稀有度

| Rarity | MaxStacks |
|---|---|
| Common | 3 |
| Rare | 2 |
| Epic | 1 |

#### 修复步骤（已有 13 个）

1. 补 `Rarity`（从 CSV）：A_001-A_004,B_001-B_004=Common / A_005-A_007,B_005-B_007=Rare / A_008-A_009,B_008-B_009=Epic
2. 补 `Weight`：Common=10, Rare=10, Epic=5
3. 补 `MaxStacks`：Common=3, Rare=2, Epic=1
4. 修正 `DisplayName`/`Description`（B_001-B_003 当前是占位文本）
5. 移除废弃 `StatBonuses`（B_001, B_003），改为 `EffectEntries`
6. 修复 `EffectEntries[0]` 为 null（A_001）
7. 统一 `EffectEntries`：每张卡一个 entry，Scene 指向各自专属的 ActorEffect 场景

#### 效果脚本目录约定

| 卡类 | 脚本路径 | 场景路径 |
|---|---|---|
| Machine | `scripts/builds/machine/` | `scenes/builds/machine/` |
| Waiter（未来） | `scripts/builds/waiter/` | `scenes/builds/waiter/` |
| Throw（未来） | `scripts/builds/throw/` | `scenes/builds/throw/` |
| Normal/Generic | `scripts/builds/normal/` | `scenes/builds/normal/` |

每个条件卡一张专属 `.tscn` + `.cs`，如：
```
scripts/builds/machine/MachineAttackHeat.cs   → 战斗升温
scenes/builds/machine/MachineAttackHeat.tscn
```

#### 新增（缺失 5 张）

CSV 共 18 张，现有 13 张（A_001-A_010 多了一张 A_010），需补 B_004-B_009（6 张），A_010 按需保留或删除。

#### Core .tres（3 个）

补上 `AllowedEffectClasses`：MachineCore → `["Machine", "Generic"]`

**验证**：18 张 .tres 字段完整，Inspector 可读，BuildSelectionManager 正常加载

**涉及文件**：
- `resources/builds/BuildMachine_A_*.tres` (10 个，修复)
- `resources/builds/BuildMachine_B_*.tres` (9 个，3 个修复 + 6 个新建)
- `resources/builds/core/*.tres` (3 个)

---

### Phase 5：CSV 导出 — ExportCsv.gd

以下两阶段记录构筑卡 CSV 的设计。当前工具支持的表格和回写规则见 [CSV 数据导出与导入指南](CSV_EXPORT_IMPORT_GUIDE.md)。

**依赖 Phase 4，数据模型和 .tres 已稳定**

1. 新增 `_export_builds()` 方法，读取 `resources/builds/*.tres`
2. 先扫一遍所有卡牌取最大 `EffectEntries` 数量 N，动态生成列头
3. CSV 列：
   ```
   file, EffectId, DisplayName, Description, BuildClass, Rarity, Weight, MaxStacks,
   EffectScene_1, EffectOverrides_1, ..., EffectScene_N, EffectOverrides_N
   ```
4. PropertyOverrides 序列化格式：`"key1=val1;key2=val2"`（`;` 分条目 → `=` 拆 key:value）
5. `_run()` 中补上 `_export_builds()` 调用

**验证**：运行导出 → `data/builds.csv` 生成，列头按最大 N 动态扩展

**涉及文件**：
- `scripts/tools/ExportCsv.gd`

---

### Phase 6：CSV 导入 — ImportCsv.gd

**依赖 Phase 5 CSV 格式稳定**

1. 新增 `import_builds_from_csv()` 方法，读取 `data/builds.csv`
2. 按列名 `EffectScene_N` / `EffectOverrides_N` 匹配，重建 `AttackEffectEntry` 数组
3. EffectOverrides 反序列化：`"key1=val1;key2=val2"` → split `;` → split `=` → `effect.Set(key, value)`
4. 回写对应 `.tres` 文件
5. `import_all()` 中补上 `import_builds_from_csv()` 调用

**验证**：修改 CSV → 导入 → `.tres` 同步更新

**涉及文件**：
- `scripts/tools/ImportCsv.gd`

---

### 依赖关系图

```
Phase 1 (数据层) ──→ Phase 2 (选择逻辑) ──→ Phase 3 (效果堆叠)
											  │
											  ▼
									   Phase 4 (.tres 生成)
											  │
									┌─────────┴─────────┐
									▼                   ▼
							  Phase 5 (CSV 导出)  Phase 6 (CSV 导入)
```

Phase 1→2→3→4 严格顺序。Phase 5/6 在 4 之后。Phase 7 独立于其他 Phase，可随时实施。

### 总计

| Phase | 改动文件数 | 复杂度 | 状态 |
|---|---|---|---|
| 1 | 3 C# | 低（加字段 + 适配） | ✅ |
| 2 | 1 C# | 中（核心算法） | ✅ |
| 3 | 1 C# + 1 tscn | 低（堆叠缩放） | ✅ |
| 4 | 20 tres + 3 core tres | 低（机械化生成） | ✅ |
| 5 | 1 GDScript | 中（序列化） | ⬜ |
| 6 | 1 GDScript | 中（反序列化） | ⬜ |
| 7 | 2 C# + 2 tscn + 1 shader | 中（UI 伪 3D 卡片） | ✅ |

---

### Phase 7：BuildSelectionWindow 伪 3D 卡片 HUD ✅

**已完成。** 详见 [BUILD_HUD_DESIGN.md](BUILD_HUD_DESIGN.md)。

已实施：
- `BuildCard.tscn` + `BuildCard.cs` — SubViewport + pseudo_3d_card.gdshader 渲染，鼠标悬停驱动伪 3D 倾斜
- `BuildSelectionWindow.cs` — `CardTemplate` export，动态 N 卡实例化，键盘 1-N + ←→ + Enter 选择
- `shaders/materials/pseudo_3d_card.gdshader` — 透视 shader

待完成（详见 BUILD_HUD_DESIGN.md 末尾）：
- 稀有度差异化：RarityGlow 按 Common/Rare/Epic 换贴图/颜色
- card 的 debug 日志清理
