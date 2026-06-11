# 存档系统改造方案

## 一、概述

从「局内手动存档」改为「局外永久进度自动保存」。设计目标：
- 3 个存档槽，独立保存永久进度
- 不保存局内状态（断点续存等移动端再说）
- 设置数据全局共享，不跟随存档

## 二、当前 vs 改造后

| | 当前 | 改造后 |
|---|---|---|
| 槽位数量 | 12 | 3 |
| 存档时机 | 游戏中手动 | 一局结束后自动 |
| 保存内容 | 局内 HP / 武器名 / 场景名 | 永久进度 |
| 读档效果 | 回到存档点继续打 | 从进度起点重开新局 |
| 局内断点 | 支持（不完整） | 不支持 |
| 主菜单「继续」 | 无 | 读取上次存档，进入对应关卡 |
| 设置持久化 | 已有（GameSettingsManager）✓ | 保持不变 |

## 三、存档槽设计

### 元数据（存档选择界面显示）

| 字段 | 说明 |
|---|---|
| 槽位编号 | 0 / 1 / 2 |
| 是否使用中 | isUsed |
| 创建时间 | 首次存档时写入 |
| 最后游玩时间 | 每次更新存档时刷新 |
| 总游戏时长 | 累计 playtime |

### 局外永久进度

| 字段 | 类型 | 说明 |
|---|---|---|
| HighScore | int | 最高分 |
| ClearCount | int | 通关次数 |
| TotalKills | int | 累计击杀数 |
| UnlockedCharacterIds | string[] | 已解锁角色 |
| UnlockedStageIds | string[] | 已解锁场景 |
| MaxStageReached | int | 达到的最远场景编号 |
| CompendiumData | 见下 | 图鉴发现数据 |

### 图鉴发现数据 (CompendiumData)

| 字段 | 类型 | 说明 |
|---|---|---|
| KilledEnemyIds | string[] | 击杀过的敌人 ID |
| CollectedWeaponIds | string[] | 拾取过的武器 ID |
| MetNpcIds | string[] | 对话过的 NPC ID |

### 不保存的内容

- 局内武器、背包、构筑效果、当前分数
- 局内 HP、位置、敌人状态
- 场景中的物件状态
- 构筑效果点数

## 四、GameSaveData 改造

### 旧结构（删除）

```
CurrentHealth, MaxHealth     → 删除（局内状态）
WeaponName                   → 删除（局内状态）
LevelProgress                → 删除（局内状态）
Level (hardcoded 1)          → 删除，改用 MaxStageReached
```

### 新结构

```json
{
  "version": 2,
  "data": {
    "SlotIndex": 0,
    "CreateTime": "2026-06-11 14:00:00",
    "LastPlayTime": "2026-06-11 16:30:00",
    "PlayTimeSeconds": 5400,
    "HighScore": 12500,
    "ClearCount": 3,
    "TotalKills": 847,
    "MaxStageReached": 4,
    "UnlockedCharacterIds": ["samurai", "ninja"],
    "UnlockedStageIds": ["stage_1", "stage_2", "stage_3"],
    "CompendiumData": {
      "KilledEnemyIds": ["Enemy_Normal_guard1", "Enemy_B1_fat"],
      "CollectedWeaponIds": ["Weapon_Slash_baguette", "Weapon_Stab_drill"],
      "MetNpcIds": ["P2"]
    }
  }
}
```

### 增量更新的方法

```csharp
// 一局结束后调用，只更新变化的字段
public void RecordRun(int score, bool cleared, int kills, int stageReached,
    HashSet<string> newEnemyKills, HashSet<string> newWeaponPicks)
{
    HighScore = Math.Max(HighScore, score);
    if (cleared) ClearCount++;
    TotalKills += kills;
    MaxStageReached = Math.Max(MaxStageReached, stageReached);
    CompendiumData.Merge(newEnemyKills, newWeaponPicks);
    LastPlayTime = DateTime.Now;
}
```

## 五、存档/读档时机

### 自动存档（非阻塞）

| 时机 | 触发 |
|---|---|
| 通关一局后 | 统计界面关闭后自动写盘 |
| 死亡后 | 结算界面关闭后自动写盘 |
| 手动退出前 | 确认退出弹窗后写盘 |
| 解锁新角色/场景时 | 即时写盘 |

### 手动读档（主菜单）

| 操作 | 流程 |
|---|---|
| 「新游戏」 | 选空槽位 → 写入初始数据 → 从 Stage 1 开始 |
| 「继续游戏」 | 读上次保存的槽位 → 从 MaxStageReached 对应的场景开始 |
| 「读取存档」 | 选槽位 → 读盘 → 启动对应场景 |

### 局内不存档

- 移除 BattleMenu 中的「保存/读取」按钮
- 移除 `GetCurrentGameData()` 的局内快照采集
- 电梯场景切换的 `InventoryTransitData` 保留（内存传递，不写盘）

## 六、SaveManager 改造清单

| 改动 | 说明 |
|---|---|
| `GameSaveData` 重写 | 新字段，`FromDictionary`/`ToDictionary` 更新 |
| `SaveSlotsCount` 改为 3 | `const int SaveSlotsCount = 3` |
| 新增 `RecordRun()` | 一局结束后增量更新 |
| 新增 `GetLastSlot()` | 上次用哪个槽（用于「继续游戏」） |
| 删除 `GetCurrentGameData()` | 不再采集局内快照 |
| 删除 Debug 测试存档 | `#if DEBUG` 的 slot 0 假数据 |
| 保持 JSON 格式 | 不改二进制，方便调试 |
| 新增 `DeleteSave(int slot)` | 删除槽位（UI 需要） |

## 七、UI 改造清单

### SaveSlotSelection

| 改动 | 说明 |
|---|---|
| 3 槽位 | GridContainer 从 4 列改为 1 行 3 列 |
| 显示元数据 | 创建时间、最后游玩、时长、最高分、进度 |
| 「切换模式」按钮 | 删除（不再区分 Save/Load 模式） |
| 新增「删除」按钮 | 长按或二次确认删除 |

### BattleMenu

| 改动 | 说明 |
|---|---|
| 删除「保存」按钮 | 局内不再手动存档 |
| 删除「读取」按钮 | 局内不再手动读档 |

### MainMenu

| 改动 | 说明 |
|---|---|
| 新增「继续游戏」 | 读上次槽位，进入 MaxStageReached 场景 |
| 「新游戏」改为选槽位 | 点击后打开 SaveSlotSelection，选空槽位 |
| 「读取存档」 | 打开 SaveSlotSelection，选已用槽位 |

## 八、兼容性

- `version: 2` 新格式，与 `version: 1` 不兼容
- 无需迁移旧的 v1 存档（当前只在 DEBUG 模式下有假数据）
- 暂无旧存档需要迁移

## 九、不做的

- 断点续存（局内状态序列化）
- 存档加密
- Steam 云存档
- 存档截图/缩略图
- 12 槽位的旧存档兼容
