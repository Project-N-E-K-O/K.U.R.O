# 菜单流程文档

## 当前流程 (v1)

```
启动游戏
  → MainMenu.tscn（主菜单）
    → 新游戏 → ModeSelectionMenu.tscn → 选模式 → BattleScene
    → 读取存档 → SaveSlotSelection.tscn → 选槽位 → BattleScene
    → 设置 → SettingsMenu
    → 退出
```

BattleMenu（局内暂停菜单）：恢复 / 设置 / 图鉴 / 保存 / 读取 / 返回主菜单 / 退出

---

## 改造后流程 (v2)

```
启动游戏
  → SaveSlotSelection.tscn（3 槽位，选槽位）
    → 选空槽位 → 新游戏（剧情空、图鉴空）→ Stage 1
    → 选有数据槽位 → 继续游戏（剧情/图鉴/进度从盘恢复）→ MaxStageReached 对应场景
```

BattleMenu（局内暂停菜单）：**返回游戏 / 设置 / 图鉴 / 返回主菜单 / 退出游戏**

---

## 涉及场景

| 场景 | v1 | v2 |
|---|---|---|
| MainMenu.tscn | 启动首屏 | 暂不接入流程，保留场景 |
| ModeSelectionMenu.tscn | 模式选择（剧情/街机/无尽） | 暂不接入流程，保留场景 |
| SaveSlotSelection.tscn | 12 槽位 + Save/Load 双模式 | **3 槽位，去模式切换，启动首屏，每个槽位有删除按钮** |
| BattleMenu.tscn | 含保存/读取按钮 | **移除保存/读取** |

## 槽位选择逻辑

```
空槽位 → SaveManager.NewGame(slot) 写初始数据 → PerformSceneChange(Stage_home)
有数据 → SaveManager.SetCurrentGameData(LoadGame(slot)) → PerformSceneChange(MaxStageReached 对应场景)

### 删除存档

每个有数据的槽位卡片右下角有删除按钮，点击后二次确认，确认后调用 `SaveManager.DeleteSave(slot)` 并刷新卡片为空槽位状态。
```

## 槽位卡片显示内容

每个有数据的槽位卡片显示以下永久进度（数据驱动，新增字段不影响 UI 代码）：

| 显示项 | 字段 | 说明 |
|---|---|---|
| 游戏时间 | PlayTimeSeconds | 累计游玩时长 |
| 通关次数 | ClearCount | 通关结算次数 |
| 循环次数 | CycleCount | 累计游戏循环次数（死亡后重新开始计数） |

### 数据驱动实现

`SaveSlotData.GetDisplayRows()` 返回 `List<(label, value)>`，卡片 `UpdateDisplay()` 遍历列表动态生成 Label。新增显示项只需在 `GetDisplayRows()` 中加一行 `rows.Add(("标签", 值))`，不改卡片 UI 代码。
