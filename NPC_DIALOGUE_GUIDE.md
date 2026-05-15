# NPC对话系统完整指南

本指南详细说明如何在K.U.R.O项目中为NPC添加和管理对话内容。

---

## 目录

1. [项目对话系统概览](#项目对话系统概览)
2. [系统架构](#系统架构)
3. [数据结构](#数据结构)
4. [Entries详解](#entries详解)
5. [对话添加流程](#对话添加流程)
6. [实际配置步骤](#实际配置步骤)
7. [高级功能](#高级功能)
8. [常见问题](#常见问题)

---

## 项目对话系统概览

### K.U.R.O项目中的两个对话系统

项目中实际存在**两套完全不同的对话系统**。为避免混淆，本指南只使用**新系统**。

#### 新系统（✅ 应该使用）

| 项目 | 值 |
|------|-----|
| **位置** | `scripts/data/DialogueData.cs` |
| **命名空间** | `Kuros.Data` |
| **主要类** | `DialogueData`、`DialogueEntry`、`DialogueChoice` |
| **管理器** | `scripts/managers/DialogueManager.cs` |
| **特点** | 支持复杂对话树、分支选项、自动推进、行为触发 |
| **UI** | `scripts/ui/DialogueWindow.cs` |
| **适用** | NPC对话、剧情对话等需要交互的场景 |

**使用场景**：大多数NPC对话、任务对话

---

#### 旧系统（⚠️ 已过时，不建议使用）

| 项目 | 值 |
|------|-----|
| **位置** | `scripts/core/interactions/dialogue/` |
| **主要类** | `DialogueLine`、`DialogueSequence` |
| **特点** | 简单的对话行序列，无交互选项 |
| **适用** | 过场动画、剧情播放（仅显示文本） |

**问题**：功能受限，不支持分支和选项。

---

### 快速判断：应该用哪个系统？

问自己以下问题：

- ❓ **对话有多条吗？** → 是 ✅ 用新系统
- ❓ **玩家需要选择选项吗？** → 是 ✅ 用新系统
- ❓ **对话之间有分支吗？** → 是 ✅ 用新系统
- ❓ **只是简单显示几句台词？** → 是 ⚠️ 旧系统可能够用

**99%的情况下你需要用新系统！**

---

## 系统架构

```
玩家移动
  ↓
玩家GrabArea进入NPC交互区域
  ↓
显示交互提示 "[E] 交互"
  ↓
玩家按E键
  ↓
NPCInteraction.StartInteraction() 触发
  ↓
DialogueManager.StartDialogue(DialogueData)
  ↓
对话UI系统显示对话内容
  ↓
玩家选择选项或对话自动推进
  ↓
DialogueManager.DialogueEnded 信号触发
  ↓
NPC交互结束，时间恢复正常
```

---

## 数据结构

### ⚠️ 重要：项目中的对话系统说明

项目中有**两个对话系统**：

| 系统 | 位置 | 用途 | 状态 |
|------|------|------|------|
| **新系统** | `scripts/data/DialogueData.cs` | 完整对话树，支持选项、分支 | ✅ 应该使用这个 |
| 旧系统 | `scripts/core/interactions/dialogue/` | 简单对话序列 | ⚠️ 已过时 |

**本指南只讲解新系统**（DialogueData、DialogueEntry、DialogueChoice）。

---

### 1. DialogueData（对话总数据）

**位置**: `scripts/data/DialogueData.cs`

**命名空间**: `Kuros.Data`

```csharp
[GlobalClass]
public partial class DialogueData : Resource
{
    // 对话基本信息
    [Export] public string DialogueId { get; set; } = "";        // 对话唯一ID
    [Export] public string DialogueName { get; set; } = "对话";  // 对话名称（显示用）
    
    // 对话条目集合
    [Export] public Godot.Collections.Array Entries { get; set; } = new();
    
    // 对话起点
    [Export] public int StartEntryIndex { get; set; } = 0;  // 从第几条开始
    [Export] public bool CanSkip { get; set; } = true;      // 是否可跳过
}
```

**职责**: 作为容器，存储整个对话的所有条目和全局设置

**编辑位置**: 
- 在Godot编辑器中，将其作为Resource文件导出（`.tres`）
- 或在Scene中作为SubResource内联定义

---

### 2. DialogueEntry（单条对话）

**位置**: `scripts/data/DialogueData.cs`  
**命名空间**: `Kuros.Data`  
**作用**: 代表对话树中的一条具体对话（一条台词）

```csharp
[GlobalClass]
public partial class DialogueEntry : Resource
{
    [ExportGroup("对话内容")]
    [Export] public string SpeakerName { get; set; } = "NPC";
    [Export(PropertyHint.MultilineText)] public string Text { get; set; } = "";
    [Export] public Texture2D? SpeakerPortrait { get; set; }
    
    [ExportGroup("选项")]
    [Export] public Godot.Collections.Array Choices { get; set; } = new();
    
    [ExportGroup("行为")]
    [Export] public string OnDialogueEndAction { get; set; } = "";
    [Export] public bool AutoAdvance { get; set; } = false;
    [Export(PropertyHint.Range, "0,10,0.1")] public float AutoAdvanceDelay { get; set; } = 2.0f;
    [Export] public int NextEntryIndex { get; set; } = -2;
}
```

**参数说明**:

| 参数 | 类型 | 说明 |
|------|------|------|
| `SpeakerName` | string | NPC的名字 |
| `Text` | string | 对话内容（支持\n换行） |
| `SpeakerPortrait` | Texture2D | NPC的头像图片 |
| `Choices` | Array | 玩家选项列表 |
| `AutoAdvance` | bool | 自动推进到下一条（无需玩家选择） |
| `AutoAdvanceDelay` | float | 自动推进的延迟时间 |
| `NextEntryIndex` | int | 指向下一条对话的索引 |

**NextEntryIndex说明**:
- `-2` ：继续下一条（默认，即 `索引+1`）
- `-1` ：对话结束
- `>= 0` ：跳转到指定索引

---

### 3. DialogueChoice（对话选项）

**位置**: `scripts/data/DialogueData.cs`  
**命名空间**: `Kuros.Data`  
**作用**: 代表玩家在对话中的一个选择选项

```csharp
[GlobalClass]
public partial class DialogueChoice : Resource
{
    [Export] public string Text { get; set; } = "选择";
    [Export] public int NextEntryIndex { get; set; } = -1;
    [Export] public string OnSelectedAction { get; set; } = "";
}
```

**工作流**:
```
显示对话条目
  ↓
显示所有Choices（选项）
  ↓
玩家点击一个选项
  ↓
执行OnSelectedAction（如果有）
  ↓
跳转到NextEntryIndex指定的条目
```

---

## Entries详解

### Entries是什么？

**Entries** 是 `DialogueData` 中的一个数组，存储了整个对话的所有**条目（对话行）**。

想象一个对话就像一部电影的剧本：
- **DialogueData** = 整部电影脚本
- **Entries** = 剧本中的所有台词行
- **Entry[0]** = 第一句台词
- **Entry[1]** = 第二句台词
- **Entry[2]** = 第三句台词（等等）

```
对话流程：
Entry[0] "你好，我是商人"
   ↓
Entry[1] "你想买点什么吗？" 
   ↓
Entry[2] "这是我的特价商品"
   ↓
结束
```

### Entries的核心概念

#### 1. **数组索引**

Entries是一个数组，每个Entry都有一个索引号：

```
Entries (数组)：
  [0] - 第一条对话
  [1] - 第二条对话
  [2] - 第三条对话
  [3] - 第四条对话
  ...
```

**记住**：编程中索引从 **0** 开始，不是从 1 开始！

#### 2. **导航机制**

每条Entry通过 `NextEntryIndex` 字段指向下一条：

```
Entry[0]
├─ NextEntryIndex: -2  (继续下一条，即指向Entry[1])
└─ Choices:
     ├─ 选项A → NextEntryIndex: 2  (跳转到Entry[2])
     └─ 选项B → NextEntryIndex: -1 (结束对话)

Entry[1]
├─ NextEntryIndex: 3  (跳转到Entry[3])

Entry[2]
├─ NextEntryIndex: -1 (结束对话)

Entry[3]
├─ NextEntryIndex: -1 (结束对话)
```

#### 3. **NextEntryIndex的三种值**

| 值 | 含义 | 例子 |
|----|------|------|
| `-2` | **继续下一条**（默认）| Entry[0] → Entry[1] → Entry[2] |
| `-1` | **结束对话** | 对话不再推进 |
| `>= 0` | **跳转到指定索引** | `NextEntryIndex: 5` 表示跳到Entry[5] |

---

### 如何添加Entry

#### 方法一：在编辑器中添加（最直观）

**前提条件**：已经打开了包含DialogueData的场景（如 `SimpleNPC.tscn`）

##### 步骤1：打开场景和Inspector

```
1. 打开 SimpleNPC.tscn
2. 在场景树中选中 NPCInteraction 节点
3. 在右侧Inspector面板找到 "Interaction" 分类
4. 展开 DialogueData 资源
```

##### 步骤2：找到Entries数组

在Inspector中看到：

```
Interaction
├── DialogueData: [SubResource]
   └─ 对话信息
   └─ 对话条目
      └─ Entries: Array (大小: 1)  ← 这里
         ├─ [0] (DialogueEntry)
```

这表示Entries数组当前有 **1个元素**（Entry[0]）

##### 步骤3：添加新Entry

点击 **"Entries: Array"** 右侧的 **➕ 添加元素** 按钮。

会弹出一个 **类型选择菜单**。向下滚动找到 **DialogueEntry** 并点击：

```
请选择要添加的类型：

Array
Basis
Color
Dictionary
...
DialogueEntry  ← 点击这个！（可能需要向下滚动）
...
```

**为什么要选 DialogueEntry？**

DialogueEntry 是 `scripts/data/DialogueData.cs` 中定义的类，完整代码如下：

```csharp
[GlobalClass]
public partial class DialogueEntry : Resource
{
    [ExportGroup("对话内容")]
    [Export] public string SpeakerName { get; set; } = "NPC";
    [Export(PropertyHint.MultilineText)] public string Text { get; set; } = "";
    [Export] public Texture2D? SpeakerPortrait { get; set; }
    
    [ExportGroup("选项")]
    [Export] public Godot.Collections.Array Choices { get; set; } = new();
    
    [ExportGroup("行为")]
    [Export] public string OnDialogueEndAction { get; set; } = "";
    [Export] public bool AutoAdvance { get; set; } = false;
    [Export(PropertyHint.Range, "0,10,0.1")] public float AutoAdvanceDelay { get; set; } = 2.0f;
    [Export] public int NextEntryIndex { get; set; } = -2;
}
```

**注意**：DialogueEntry 在同一个文件 `DialogueData.cs` 中定义，不是单独的脚本文件。

---

**如果菜单中找不到 DialogueEntry？**

使用 **方式B：复制现有Entry**：

1. 在Inspector中找到 `Entries` → `[0]`
2. **右键点击** `[0]` → 选择 **"Duplicate"** 或 **"复制"**
3. 粘贴会自动添加到 `[1]`
4. 然后编辑 `[1]` 的具体内容

##### 步骤4：编辑新Entry

展开 `[1]`，填写内容：

```
[1] (DialogueEntry)
├─ SpeakerName: "商人"
├─ Text: "需要什么吗？"
├─ SpeakerPortrait: (空)
├─ Choices: Array (大小: 0)
├─ AutoAdvance: false
├─ AutoAdvanceDelay: 0
├─ NextEntryIndex: -1
└─ OnDialogueEndAction: ""
```

##### 步骤5：继续添加更多Entry

重复步骤3-4，直到有足够的对话条目

---

#### 方法二：通过代码动态创建

如果需要在运行时创建Entry，使用C#：

```csharp
// 在任何脚本中
public void AddNewEntry()
{
    // 获取现有DialogueData
    var dialogueData = GetNode<NPCInteraction>("NPCInteraction").DialogueData;
    
    // 创建新的Entry
    var newEntry = new DialogueEntry();
    newEntry.SpeakerName = "新NPC";
    newEntry.Text = "这是动态添加的对话";
    newEntry.AutoAdvance = false;
    newEntry.NextEntryIndex = -1;  // 对话结束
    
    // 添加到Entries数组
    var entries = dialogueData.Entries;
    entries.Add(newEntry);
    dialogueData.Entries = entries;  // 重新赋值使其生效
}
```

---

### Entries的完整生命周期示例

#### 场景：一个简单的问候对话

**需求**：NPC先问候，然后玩家可以选择"聊天"或"离开"

**Entries结构**：

```
Entry[0] - 问候（无选项，自动推进）
   ↓
Entry[1] - 选择菜单（有两个选项）
   ├─ 选项A"聊天" → Entry[2]
   └─ 选项B"离开" → Entry[3]
   
Entry[2] - 聊天内容（结束）
Entry[3] - 离开反应（结束）
```

**具体配置**：

```
DialogueData:
  DialogueId: "greeting_npc"
  DialogueName: "问候"
  StartEntryIndex: 0  ← 从Entry[0]开始
  CanSkip: true
  
  Entries: Array (大小: 4)
  
    [0]:  ← 第1条对话
      SpeakerName: "旅店老板"
      Text: "欢迎来到我的旅店！"
      Choices: (空)
      AutoAdvance: true
      AutoAdvanceDelay: 1.5
      NextEntryIndex: -2  ← 自动推进到Entry[1]
      
    [1]:  ← 第2条对话（选择菜单）
      SpeakerName: "旅店老板"
      Text: "你想做什么？"
      Choices: Array (大小: 2)
        [0]:
          Text: "和你聊聊"
          NextEntryIndex: 2  ← 跳到Entry[2]
          OnSelectedAction: ""
        [1]:
          Text: "没事，我先看看"
          NextEntryIndex: 3  ← 跳到Entry[3]
          OnSelectedAction: ""
      AutoAdvance: false
      NextEntryIndex: -2  (如果没选择，推进到Entry[2])
      
    [2]:  ← 第3条对话
      SpeakerName: "旅店老板"
      Text: "好的，随时可以来聊天。"
      Choices: (空)
      AutoAdvance: false
      NextEntryIndex: -1  ← 对话结束
      
    [3]:  ← 第4条对话
      SpeakerName: "旅店老板"
      Text: "随便看，有问题就喊我。"
      Choices: (空)
      AutoAdvance: false
      NextEntryIndex: -1  ← 对话结束
```

**对话执行流程**：

```
开始
  ↓
显示 Entry[0]: "欢迎来到我的旅店！"
  ↓ (等待1.5秒自动推进)
显示 Entry[1]: "你想做什么？" + 两个选项
  ↓
玩家选择"和你聊聊" (或什么都不做)
  ↓
显示 Entry[2]: "好的，随时可以来聊天。"
  ↓ (NextEntryIndex = -1，结束)
对话完成，时间恢复正常
```

---

### 常见Entry添加场景

#### 场景A：线性对话（一条接一条）

```csharp
// 每个Entry的NextEntryIndex = -2（继续）
Entry[0] → Entry[1] → Entry[2] → Entry[3] → 结束

配置：
[0]: NextEntryIndex = -2
[1]: NextEntryIndex = -2
[2]: NextEntryIndex = -2
[3]: NextEntryIndex = -1  ← 最后一个
```

#### 场景B：条件分支（根据选择跳转）

```csharp
// 同一条Entry有多个选项，指向不同的Entry
Entry[0]
  ├─ 选项A → Entry[1]
  ├─ 选项B → Entry[2]
  └─ 选项C → Entry[3]

[0]: Choices[0].NextEntryIndex = 1
     Choices[1].NextEntryIndex = 2
     Choices[2].NextEntryIndex = 3
```

#### 场景C：合并汇聚（多条Entry最后指向同一个）

```csharp
// 不同的选择路线最后都回到同一条对话
Entry[1] → Entry[3]
Entry[2] → Entry[3]  ← 都指向Entry[3]
Entry[3]: "谢谢，再见"

[1]: NextEntryIndex = 3
[2]: NextEntryIndex = 3
[3]: NextEntryIndex = -1
```

---

### Entries数组的物理限制

| 限制 | 建议值 |
|------|--------|
| 最大Entry数 | 100+ (取决于内存) |
| 最大Choices数/Entry | 10-15 (编辑器显示) |
| 最长Text字符数 | 1000+ |
| 索引值范围 | -2 ~ 数组大小-1 |

---

## 对话添加流程

### 方式一：在编辑器中编辑（推荐用于快速测试）

#### 步骤1：打开NPC场景

打开 `scenes/actors/npc/SimpleNPC.tscn`

```
SimpleNPC (CharacterBody2D)
├── Sprite2D
├── CollisionShape2D
├── NPCInteraction (Node2D)  ← 这里
└── Area2D
```

#### 步骤2：选择NPCInteraction节点

在场景树中点击 `NPCInteraction` 节点

#### 步骤3：在Inspector中找到DialogueData

在右侧Inspector面板中找到：
```
Interaction
  ├── DialogueData : [SubResource("Resource_60ay7")]
```

#### 步骤4：展开并编辑对话数据

点击 `Resource_60ay7` → 在下方展开资源详情：

```
对话信息
  ├── DialogueId: "example_villager_dialogue"
  └── DialogueName: "村民对话"

对话条目
  └── Entries: [3]
        ├── [0] (DialogueEntry)
        ├── [1] (DialogueEntry)
        └── [2] (DialogueEntry)

默认设置
  ├── StartEntryIndex: 0
  └── CanSkip: true
```

#### 步骤5：编辑第一条对话

展开 `Entries` → 展开 `[0]` → 编辑内容：

```
对话内容
  ├── SpeakerName: "村民"
  ├── Text: "你好，旅行者！\n这里最近出现了怪物。"
  └── SpeakerPortrait: (留空或拖入图片)

行为
  ├── AutoAdvance: false
  ├── AutoAdvanceDelay: 0
  └── NextEntryIndex: -2
```

#### 步骤6：添加选项

选中 `[0]`，找到 `选项` 分类下的 `Choices` 数组，点击 `Add Element`：

**选项1**：
```
Text: "了解更多信息"
NextEntryIndex: 1
OnSelectedAction: ""
```

**选项2**：
```
Text: "谢谢，再见"
NextEntryIndex: -1
OnSelectedAction: ""
```

#### 步骤7：编辑第二条对话（被选项1指向）

展开 `Entries` → 展开 `[1]` → 编辑：

```
SpeakerName: "村民"
Text: "如果你需要帮助，随时来找我。"
AutoAdvance: true
AutoAdvanceDelay: 2.0
NextEntryIndex: -2  (继续→[2])
```

#### 步骤8：编辑第三条对话（自动推进的结果）

展开 `Entries` → 展开 `[2]` → 编辑：

```
SpeakerName: "村民"
Text: "这些怪物通常出现在村庄东边。\n祝你好运！"
AutoAdvance: false
NextEntryIndex: -1  (结束对话)
```

---

### 方式二：通过C#代码动态创建

在运行时或通过脚本生成对话数据：

```csharp
// 在 NPCInteraction 或其他脚本中
private void CreateCustomDialogue()
{
    var dialogueData = new DialogueData();
    dialogueData.DialogueId = "merchant_quest";
    dialogueData.DialogueName = "商人任务对话";
    dialogueData.StartEntryIndex = 0;
    dialogueData.CanSkip = true;
    
    // 创建第一条对话
    var entry1 = new DialogueEntry();
    entry1.SpeakerName = "商人";
    entry1.Text = "嘿！我有个任务给你。";
    entry1.AutoAdvance = false;
    
    // 创建选项
    var choice1 = new DialogueChoice();
    choice1.Text = "接受任务";
    choice1.NextEntryIndex = 1;
    choice1.OnSelectedAction = "start_quest";
    
    var choice2 = new DialogueChoice();
    choice2.Text = "拒绝";
    choice2.NextEntryIndex = -1;
    choice2.OnSelectedAction = "";
    
    entry1.Choices = new Godot.Collections.Array { choice1, choice2 };
    
    // 创建第二条对话（接受任务后）
    var entry2 = new DialogueEntry();
    entry2.SpeakerName = "商人";
    entry2.Text = "太好了！去森林里找到5个苹果。";
    entry2.AutoAdvance = false;
    entry2.NextEntryIndex = -1;
    
    // 添加到对话数据
    dialogueData.Entries = new Godot.Collections.Array { entry1, entry2 };
    
    // 设置到NPC
    var npcInteraction = GetComponent<NPCInteraction>();
    npcInteraction.SetDialogueData(dialogueData);
}
```

---

## 实际配置步骤

### 快速创建一个新NPC

#### 步骤1：场景复制

在Godot中右键点击 `SimpleNPC.tscn` → 复制 → 粘贴为 `VillagerA.tscn`

#### 步骤2：编辑场景

打开 `VillagerA.tscn`，修改：
- Sprite2D的颜色/纹理
- CollisionShape2D的大小
- 名字改为 "VillagerA"

#### 步骤3：编辑对话数据

选中 `NPCInteraction` → Inspector → `DialogueData`

在SubResource中编辑或新建 `.tres` 资源文件

#### 步骤4：保存并放入关卡

将 `VillagerA.tscn` 实例化到关卡中

---

## 高级功能

### 1. 条件分支

通过 `OnSelectedAction` 标记，在C#中实现条件逻辑：

```csharp
// 在DialogueManager中
public void OnDialogueOptionSelected(DialogueChoice choice)
{
    switch (choice.OnSelectedAction)
    {
        case "join_party":
            // 加入队伍逻辑
            AddPlayerToParty(CurrentNPC);
            break;
        case "start_quest":
            // 开始任务
            QuestManager.Instance.StartQuest(CurrentNPC.QuestId);
            break;
    }
    
    // 推进对话
    ShowDialogueEntry(choice.NextEntryIndex);
}
```

### 2. 动态对话内容

根据游戏状态动态修改对话：

```csharp
public void UpdateDialogueForGameState()
{
    var entry = CurrentDialogueData.GetEntry(0);
    
    if (PlayerInventory.HasItem("MagicSword"))
    {
        entry.Text = "哇！那把剑真漂亮！";
    }
    else if (PlayerLevel >= 10)
    {
        entry.Text = "你看起来已经很强大了！";
    }
    else
    {
        entry.Text = "你好，冒险者。";
    }
}
```

### 3. 对话树可视化

在编辑器中查看对话流程：

```
对话开始 (StartEntryIndex = 0)
    ↓
[0] 村民: "你好，旅行者！"
    ├─ 选项A → [1] 村民: "如果你需要帮助..."
    │               ↓
    │          [2] 村民: "怪物在东边。" (自动推进) → 结束
    │
    └─ 选项B → 结束对话
```

---

## 常见问题

### Q1: 如何让对话在特定条件下显示不同内容？

**A**: 在 `NPCInteraction.StartInteraction()` 中先修改对话数据：

```csharp
public void StartInteraction()
{
    // 根据条件修改对话
    if (PlayerHasQuest("Village Quest"))
    {
        DialogueData.GetEntry(0).Text = "你接受了任务吗？";
    }
    
    // ... 继续原流程
    _isInteracting = true;
    UpdatePromptVisibility();
    Engine.TimeScale = 0f;
    DialogueManager.Instance.StartDialogue(DialogueData);
}
```

### Q2: 选项太多，如何组织？

**A**: 使用 `NextEntryIndex` 跳转到菜单条目：

```csharp
// 第一条：问题
entry1.Text = "你想了解什么？";
// 3个选项都指向不同的说明条目

// 第2-4条：三种说明
entry2.Text = "关于任务..."; entry2.NextEntryIndex = 5;
entry3.Text = "关于商店..."; entry3.NextEntryIndex = 5;
entry4.Text = "关于怪物..."; entry4.NextEntryIndex = 5;

// 第5条：返回询问
entry5.Text = "还有其他问题吗？";
// 选项再次指向 entry1
```

### Q3: 如何播放对话语音？

**A**: 在对话条目中添加语音资源引用：

```csharp
public partial class DialogueEntry : Resource
{
    [Export] public AudioStream? SpeechAudio { get; set; }
    [Export(PropertyHint.Range, "0,1,0.05")] public float SpeechVolume { get; set; } = 1.0f;
}
```

在DialogueManager中播放：

```csharp
public void PlayDialogueAudio(DialogueEntry entry)
{
    if (entry.SpeechAudio != null)
    {
        var audioPlayer = GetNode<AudioStreamPlayer>("SpeechPlayer");
        audioPlayer.VolumeDb = Mathf.LinearToDb(entry.SpeechVolume);
        audioPlayer.Stream = entry.SpeechAudio;
        audioPlayer.Play();
    }
}
```

### Q4: 对话结束后想执行某个行为？

**A**: 在 `DialogueEntry` 中使用 `OnDialogueEndAction`：

```csharp
entry.OnDialogueEndAction = "trigger_cutscene";

// 在DialogueManager中监听
public void OnDialogueEnded(DialogueEntry lastEntry)
{
    if (!string.IsNullOrEmpty(lastEntry.OnDialogueEndAction))
    {
        CutsceneManager.Instance.PlayCutscene(lastEntry.OnDialogueEndAction);
    }
}
```

### Q5: 如何测试对话而不用运行整个游戏？

**A**: 
1. 在Editor中单独打开NPC场景
2. 点击运行（F5）
3. 移动到NPC上按E键

---

## 数据流图

```
简化视图：
┌─────────────────────────────┐
│      DialogueData           │
│  (整个对话的容器)            │
└──────────────┬──────────────┘
               │ 包含多个
               ↓
    ┌─────────────────────┐
    │   DialogueEntry[0]  │
    │  (第一条对话)        │
    │ - SpeakerName       │
    │ - Text              │
    │ - Choices[]         │
    └──────────┬──────────┘
               │ 包含多个选项
               ↓
    ┌─────────────────────┐
    │  DialogueChoice[0]  │
    │  (第一个选项)        │
    │ - Text: "继续"      │
    │ - NextEntryIndex: 1 │
    └─────────────────────┘
               │
               ↓ 跳转
    ┌─────────────────────┐
    │   DialogueEntry[1]  │
    │  (第二条对话)        │
    └─────────────────────┘
```

---

## 完整示例

### 村民任务对话

```gdscript
# 在SimpleNPC.tscn中配置

DialogueData:
  DialogueId: "elder_quest"
  DialogueName: "长者任务"
  StartEntryIndex: 0
  CanSkip: true
  
  Entries:
    [0]:  # 问候
      SpeakerName: "长者"
      Text: "欢迎你，年轻的冒险者。\n我有个重要的任务。"
      Choices:
        - Text: "我很感兴趣"
          NextEntryIndex: 1
          OnSelectedAction: ""
        - Text: "也许以后吧"
          NextEntryIndex: -1
          OnSelectedAction: ""
      AutoAdvance: false
      NextEntryIndex: -2
    
    [1]:  # 任务详情
      SpeakerName: "长者"
      Text: "我需要你去山洞找到失落的水晶。"
      Choices:
        - Text: "我会帮你找到它"
          NextEntryIndex: 2
          OnSelectedAction: "accept_quest"
        - Text: "这太危险了"
          NextEntryIndex: -1
          OnSelectedAction: ""
      AutoAdvance: false
      NextEntryIndex: -2
    
    [2]:  # 接受任务
      SpeakerName: "长者"
      Text: "非常感谢！\n水晶应该在山洞的最深处。"
      AutoAdvance: true
      AutoAdvanceDelay: 2.0
      NextEntryIndex: -1
      OnDialogueEndAction: "quest_accepted"
```

---

## 脚本速查表

### 项目中对话相关的所有脚本位置

```
scripts/
├── data/
│   └── DialogueData.cs ⭐ （定义了DialogueEntry、DialogueChoice、DialogueData）
│
├── managers/
│   └── DialogueManager.cs （全局对话管理器，处理对话流程）
│
├── ui/
│   └── DialogueWindow.cs （对话UI界面显示）
│
├── actors/npc/
│   └── NpcDialogueInteractable.cs （NPC互动基类）
│
└── core/interactions/dialogue/ ⚠️ （旧系统，不建议使用）
    ├── DialogueLine.cs （已过时）
    ├── DialogueSequence.cs （已过时）
    └── IDialoguePlayer.cs （已过时）
```

### 核心文件一览

| 文件名 | 功能 | 使用场景 |
|--------|------|---------|
| `DialogueData.cs` ⭐ | 定义DialogueData、DialogueEntry、DialogueChoice 三个类 | 编辑器配置对话、代码创建对话 |
| `DialogueManager.cs` | 管理对话播放流程，监听输入 | 游戏运行时自动处理 |
| `DialogueWindow.cs` | 显示对话UI | 游戏运行时自动处理 |
| `NpcDialogueInteractable.cs` | NPC基类，处理互动时的动画 | 创建新NPC时继承 |

**记住**：所有对话数据定义都在 `DialogueData.cs` 中！

---

## 总结

### 关键要点

1. **只用一个系统** - 使用 `Kuros.Data` 命名空间中的 `DialogueData`、`DialogueEntry`、`DialogueChoice`

2. **三个数据结构的关系**：
   ```
   DialogueData（容器）
   └── Entries[]（多条对话）
       ├── [0] DialogueEntry
       │   └── Choices[]（该条对话的选项）
       │       ├── [0] DialogueChoice
       │       └── [1] DialogueChoice
       │
       ├── [1] DialogueEntry
       │   └── Choices[]
       │       └── [0] DialogueChoice
       │
       └── [2] DialogueEntry
   ```

3. **关键概念**：
   - 使用 `NextEntryIndex` 控制对话流向（-2继续，-1结束，>=0跳转）
   - 使用 `AutoAdvance` 实现自动推进
   - 使用 `OnSelectedAction` 触发游戏事件
   - 在运行时可动态修改，实现条件对话

4. **所有类都在一个文件** - `scripts/data/DialogueData.cs` 中同时定义了三个类，都支持编辑器导出
