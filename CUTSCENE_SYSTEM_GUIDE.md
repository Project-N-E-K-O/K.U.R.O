# 过场动画系统 (Cutscene System) 使用手册

## 概述

本系统为 KURO 项目提供统一的过场动画管理，支持以下特性：

- **统一跳过逻辑**：按一个键跳过整段过场，各步骤内部均响应跳过信号
- **剧情对话**：逐字显示台词 → 第一次确认键立即显示全文 → 第二次确认进入下一条
- **镜头移动**：接管 Camera2D，平滑移动到任意世界坐标或目标节点位置，结束后自动归位
- **AnimationPlayer 播放**：触发任意节点上的 AnimationPlayer 动画，可等待完成再继续
- **黑幕淡变**：统一的淡入/淡出（FadeStep）
- **等待**：固定时长等待（跳过时立即结束）
- **分散触发**：各房间放 `CutsceneTrigger`（Area2D），玩家进入后自动播放指定序列

---

## 文件结构

```
scripts/systems/cutscene/
├── CutsceneManager.cs              ← 主管理器，挂到 Stage_2
├── CutsceneSequence.cs             ← 过场序列 Resource（.tres 配置文件）
├── CutsceneStep.cs                 ← 步骤抽象基类
├── CutsceneContext.cs              ← 步骤执行上下文（非 Node）
├── CutsceneTrigger.cs              ← Area2D 触发器，放在各房间
├── CutsceneDialoguePanel.cs        ← 对话框抽象基类（Control）
├── DefaultCutsceneDialoguePanel.cs ← 默认对话框实现
└── steps/
	├── WaitStep.cs                 ← 等待 N 秒
	├── DialogueLine.cs             ← 单条台词 Resource
	├── DialogueStep.cs             ← 显示一组台词
	├── CameraMoveStep.cs           ← 镜头移动
	├── PlayAnimationStep.cs        ← 播放动画
	└── FadeStep.cs                 ← 黑幕淡变
```

---

## 快速上手

### 第一步：在 Stage_2 中添加 CutsceneManager

`Stage_2.tscn` 已自动添加 `CutsceneManager` 节点，路径为 `BattleScene/CutsceneManager`。

在 Inspector 中配置（已预填）：

| 属性 | 值 | 说明 |
|------|----|------|
| `PlayerPath` | `../World/MainCharacter` | 玩家节点路径（过场期间禁用其输入） |
| `CameraPath` | `../World/MainCharacter/Camera2D` | 摄像机路径（镜头移动时接管） |
| `SkipActionName` | `ui_cancel` | 跳过按键（默认 Esc） |
| `DialoguePanelPath` | *(可选)* | 对话框节点路径 |
| `FadeOverlayPath` | *(可选)* | 全屏黑幕 CanvasItem 路径 |

> **如需对话框 / 黑幕**：先按"进阶配置"章节创建对应节点，再填写路径。

---

### 第二步：创建过场序列资源

1. 在 Godot 编辑器中，**FileSystem → 右键 → New Resource**
2. 类型选择 `CutsceneSequence`，保存为 `.tres`（如 `res://data/cutscenes/B_begin_intro.tres`）
3. 在 Inspector 中配置：

| 属性 | 说明 |
|------|------|
| `SequenceId` | 唯一字符串 ID，如 `"b_begin_intro"`（用于信号区分） |
| `Steps` | 步骤数组，按顺序添加 |
| `DisablePlayerInput` | `true` = 过场期间玩家无法移动（推荐） |
| `TakeOverCamera` | `true` = 启用镜头移动步骤（需要 CutsceneManager.CameraPath 已设置） |

---

### 第三步：添加步骤

在 `Steps` 数组中添加步骤 Resource，类型与属性如下：

#### WaitStep — 等待
```
类型: WaitStep
Duration: 2.0   ← 等待秒数（跳过时立即结束）
```

#### FadeStep — 黑幕淡变
```
类型: FadeStep
TargetAlpha: 1.0   ← 淡入全黑（0.0 = 淡出为透明）
Duration: 0.5      ← 过渡时长（秒）
```

#### DialogueStep — 对话
```
类型: DialogueStep
Lines:
  [0] DialogueLine
		Speaker: "主角"
		Text:    "这里是对话内容……"
		RevealSpeed: 40   ← 每秒显示字符数（0 = 立即全显）
  [1] DialogueLine
		Speaker: "NPC"
		Text:    "另一条台词"
```

#### CameraMoveStep — 镜头移动
```
类型: CameraMoveStep
TargetPath:         (NodePath，相对于 CutsceneManager) ← 优先级高于 TargetPosition
TargetPosition:     Vector2(1000, 200)                  ← TargetPath 为空时使用
TargetZoom:         0.8                                 ← 相机缩放级别（>0 启用，1.0=无缩放）
Duration:           2.0                                 ← 动画时长（秒）
Ease:               IN_OUT                              ← 缓动类型
Transition:         CUBIC                               ← 过渡类型
WaitForCompletion:  false                               ← false=异步（后续步骤立刻执行）
```

> **需要**：`CutsceneSequence.TakeOverCamera = true`
>
> **新特性**：
> - **Zoom 缩放**：设置 `TargetZoom > 0` 时启用，与位置移动 **并行执行**
> - **异步模式**：`WaitForCompletion = false` 时，镜头动画在后台进行，**不阻塞后续步骤**（如对话、Taxi 出现等同时进行）。skip 时自动完成。

#### PlayAnimationStep — 播放动画
```
类型: PlayAnimationStep
AnimationPlayerPath: "../BBegin/Taxi/AnimationPlayer"   ← 相对于 CutsceneManager（BBegin 是 StageGeneratorManager 实例化后挂载的房间根节点名）
AnimationName:       "taxi_intro"
WaitForCompletion:   true   ← true = 等动画播完再继续下一步
```

> **注意（Taxi 特殊情况）**：`Moving_A1_taxi.tscn` 已设置 `autoplay = "taxi_intro"`，
> 场景加载后动画会**自动播放**，`TaxiController` 也会在播完后自动切换到 `taxi_idle`。
> 因此针对 Taxi 过场，**不需要 PlayAnimationStep**，直接用 `WaitStep` 等待其动画时长（约 6 秒）即可：
>
> ```
> WaitStep  Duration: 6.0   ← 与 taxi_intro 动画时长一致
> ```
>
> `PlayAnimationStep` 适用于**不自动播放、需要由过场系统主动触发**的动画节点。

#### FadeStep（淡出）
```
类型: FadeStep
TargetAlpha: 0.0
Duration: 0.5
```

---

### 第四步：在房间中放置触发器

1. 在房间场景（如 `B_begin.tscn`）中添加 `CutsceneTrigger` 节点（继承 Area2D）
2. 给它添加 `CollisionShape2D`，设置触发区域大小
3. 设置 `collision_mask = 4`（检测玩家 HitArea）
4. Inspector 配置：

| 属性 | 说明 |
|------|------|
| `Sequence` | 拖入对应的 `.tres` 序列文件 |
| `TriggerOnce` | `true` = 只触发一次（推荐） |
| `PlayerGroup` | 默认 `"player"`，与玩家节点所在组一致 |

---

## 典型过场结构示例

### B_begin 开场（Taxi 入场 + 对话）

```
CutsceneSequence: B_begin_intro.tres
  SequenceId:          "b_begin_intro"
  DisablePlayerInput:  true
  TakeOverCamera:      true

Steps:
  [0] FadeStep           TargetAlpha=1.0, Duration=0.0   ← 初始全黑
  [1] FadeStep           TargetAlpha=0.0, Duration=0.5   ← 淡入场景（Taxi autoplay 已自动开始播放）
  [2] CameraMoveStep     TargetPath="../BBegin/Taxi", Duration=1.5  ← 镜头跟随 Taxi
  [3] WaitStep           Duration=6.0                    ← 等待 taxi_intro 播完（约 6 秒）
  [4] DialogueStep       Lines=[{Speaker:"???", Text:"今晚有点不对劲……"}]
  [5] CameraMoveStep     TargetPath="../World/MainCharacter", Duration=1.0
  [6] WaitStep           Duration=0.3
```

---

## 跳过逻辑详解

| 场景 | 行为 |
|------|------|
| 按 `ui_cancel`（Esc） | `IsSkipRequested = true`，所有步骤在下一帧检测到后提前退出 |
| 对话中按 `ui_accept` | 若文字未显示完 → 立即显示全文；若已全显 → 进入下一条台词 |
| 跳过时的动画/镜头 | Tween 被 Kill，属性直接跳到目标值（不会留在中间状态） |
| 跳过后 | 玩家输入自动恢复，摄像机自动归位，对话框自动隐藏 |

---

## CameraMoveStep 属性详解

### TargetPath 与 TargetPosition

**两者都是指定镜头目标位置的方法，有优先级关系：**

- **`TargetPath`** ✅ 推荐，优先级高
  - 类型：`NodePath`，相对于 `CutsceneManager` 节点（即 Stage_2 根节点）
  - 作用：自动获取某个**节点的当前世界坐标**
  - 示例：`"../BBegin/Taxi"` → 镜头移动到 Taxi 当前位置
  - 优势：目标动态更新（Taxi 移动时镜头跟随）
  
- **`TargetPosition`** 备选，优先级低
  - 类型：`Vector2` 固定坐标
  - 作用：**若 TargetPath 为空**，使用此绝对世界坐标
  - 示例：`Vector2(1000, 500)` → 镜头移动到 (1000, 500)
  - 使用场景：镜头扫过某个固定地点

**优先级规则**：
```csharp
if (!TargetPath.IsEmpty)
	target = GetNodeOrNull<Node2D>(TargetPath).GlobalPosition;  // ✓ 使用此值
else
	target = TargetPosition;  // ✓ 退而求其次
```

### Tween.EaseType 与 Tween.TransitionType

**两者控制动画的"缓动曲线"，决定了镜头移动的**感觉**。**

#### Ease（缓入缓出类型）

| 值 | 含义 | 效果 |
|----|------|------|
| `IN` | 缓入 | 开始快 → 结束慢（急刹车） |
| `OUT` | 缓出 | 开始慢 → 结束快（加速离开） |
| `IN_OUT` ⭐ 推荐 | 缓入缓出 | 开始慢 + 中间快 + 结束慢（自然顺滑） |
| `OUT_IN` | 缓出缓入 | 开始快 + 结束快（生硬，不推荐） |

#### Transition（过渡曲线类型）

| 值 | 含义 | 速度曲线 |
|----|------|---------|
| `LINEAR` | 线性 | 匀速直线（机械感） |
| `SINE` | 正弦 | 平缓S形 |
| `QUAD` | 二次 | 缓慢起伏 |
| `CUBIC` ⭐ 推荐 | 三次 | 流畅S形（电影质感） |
| `QUART` | 四次 | 更加夸张 |
| `QUINT` | 五次 | 非常夸张 |

**常见组合示例**：

```
Ease=IN_OUT + Transition=CUBIC   ← 标准（流畅自然）
Ease=IN_OUT + Transition=SINE    ← 柔和温顺
Ease=IN_OUT + Transition=QUAD    ← 略显机械
Ease=IN     + Transition=CUBIC   ← 急停效果
Ease=OUT    + Transition=CUBIC   ← 冲刺效果
```

### TargetZoom（相机缩放）

**相机缩放级别，控制画面放大/缩小：**

| 值 | 效果 | 用途 |
|----|------|------|
| `0.5` | 放大 2 倍（看近景） | 特写镜头（NPC 脸部） |
| `1.0` | 100%（正常） | 标准距离 |
| `1.5` | 缩小（看远景） | 俯视全景 |
| `2.0` | 缩小 2 倍（更远） | 鸟瞰视角 |
| `<= 0` | 不改变 | 保持当前缩放 |

**使用示例**：
```csharp
// 移动到 NPC 并放大
TargetPath: "NPC_Node"
TargetZoom: 0.8       // 放大特写
Duration: 1.5
```

### WaitForCompletion（异步控制）

**控制动画是否阻塞后续步骤：**

| 值 | 行为 | 何时使用 |
|----|------|---------|
| `true` | **阻塞** — 镜头动画完成后才执行下一步 | 镜头需要先到位才能对话 |
| `false` | **异步** — 镜头动画在后台进行，立刻执行下一步 | 镜头跟随 + NPC 对话 同时进行（电影感） |

**电影式示例**（WaitForCompletion=false）：
```
CameraMoveStep     TargetPath=Taxi, Duration=2.0, WaitForCompletion=false  ← 镜头开始跟 Taxi
  ↓ (同时进行，不等待)
DialogueStep       Speaker="???", Text="发生什么了？"  ← 同时播放对话
  ↓
WaitStep           Duration=0.5   ← 继续等待
```

**传统阻塞示例**（WaitForCompletion=true）：
```
CameraMoveStep     TargetPath=Taxi, Duration=2.0, WaitForCompletion=true  ← 镜头完全到位（等2秒）
  ↓ (然后)
DialogueStep       Speaker="???", Text="发生什么了？"  ← 才开始对话
```

---

## 信号

`CutsceneManager` 发出两个信号，可在 Stage_2 或其他节点中连接：

```csharp
// 过场开始
cutsceneManager.CutsceneStarted += (id) => GD.Print($"过场开始: {id}");

// 过场结束（包括跳过后）
cutsceneManager.CutsceneFinished += (id) => GD.Print($"过场结束: {id}");
```

---

## 进阶配置

### 配置对话框（DialoguePanel）

1. 创建新场景，根节点类型 `Control`，挂载 `DefaultCutsceneDialoguePanel.cs`
2. 子节点结构（参考）：
   ```
   Control (DefaultCutsceneDialoguePanel)
	 └─ Panel
		  ├─ SpeakerLabel  (Label)
		  └─ TextLabel     (RichTextLabel)
   ```
3. 在 Inspector 中修改 `SpeakerLabelPath` / `TextLabelPath` 对应实际节点路径
4. 将此场景实例放到 `Stage_2.tscn` 的 `CanvasLayer` 下
5. 在 `CutsceneManager.DialoguePanelPath` 中填入对应路径

> 如需自定义对话框外观，继承 `CutsceneDialoguePanel`（抽象类）并实现 `ShowLine()` 方法。

### 配置黑幕（FadeOverlay）

1. 在 `Stage_2.tscn` 中的 `CanvasLayer` 下添加全屏 `ColorRect`
   - 颜色：`(0, 0, 0, 1)`（纯黑），`modulate.a = 0`（初始透明）
   - 锚点设为全屏 Fill
2. 在 `CutsceneManager.FadeOverlayPath` 中填入路径
3. 使用 `FadeStep(TargetAlpha=1.0)` 淡入黑幕，`FadeStep(TargetAlpha=0.0)` 淡出

### 自定义步骤

继承 `CutsceneStep`，实现 `Execute(CutsceneContext ctx)`：

```csharp
[GlobalClass]
public partial class MyCustomStep : CutsceneStep
{
	[Export] public string SomeConfig { get; set; } = "";

	public override async Task Execute(CutsceneContext ctx)
	{
		if (ctx.IsSkipping) return;

		// 做一些事情……
		await ctx.NextFrame();  // 等一帧

		// 检查跳过
		while (!ctx.IsSkipping && /* 条件 */)
			await ctx.NextFrame();
	}
}
```

---

## 注意事项

- `CutsceneTrigger` 使用 `GetTree().GetFirstNodeInGroup("cutscene_manager")` 定位管理器，确保 `CutsceneManager` 已添加到场景树中
- `CutsceneManager` 在 `_Ready` 中调用 `AddToGroup("cutscene_manager")`，无需手动设置
- `CameraMoveStep.AnimationPlayerPath` / `PlayAnimationStep.AnimationPlayerPath` 均为**相对于 CutsceneManager 节点**的路径（即相对于 Stage_2 根节点）
- 同一时间只能播放一段过场；若已在播放，`PlayCutscene()` 调用会被忽略
- 过场结束后（包括跳过），`DisablePlayerInput` 和摄像机状态均会自动恢复
