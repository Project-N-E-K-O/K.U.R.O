# NPC对话系统使用说明

## 在ExampleBattle场景中使用NPC

### 1. 打开场景
打开 `scenes/ExampleBattle.tscn` 场景文件。

### 2. 找到NPC节点
在场景树中找到 `NPC` 节点（位置大约在 (600, 400)）。

### 3. 配置对话数据
1. 选择 `NPC` 节点
2. 在Inspector面板中找到 `NPCInteraction` 组件
3. 在 `Dialogue Data` 字段中，点击下拉菜单或文件夹图标
4. 选择 `res://data/ExampleDialogue.tres` 对话资源

### 4. 调整交互范围（可选）
- `Interaction Range`: 调整玩家需要多近才能交互（默认120像素）
- `Interaction Prompt Text`: 自定义提示文本（默认"按 [E] 对话"）

### 5. 测试交互
1. 运行场景
2. 控制玩家移动到NPC附近（蓝色方块）
3. 当玩家进入交互范围时，会显示"按 [E] 对话"提示
4. 按 `E` 键开始对话
5. 使用空格键继续对话，ESC键跳过对话

## 创建自定义对话

### 方法1：在编辑器中创建
1. 右键点击 `data` 文件夹
2. 选择"新建资源"
3. 搜索并选择 `DialogueData`
4. 配置对话条目和选项

### 方法2：复制示例文件
1. 复制 `data/ExampleDialogue.tres`
2. 重命名并修改对话内容

## 对话数据配置说明

- **DialogueId**: 对话的唯一标识符
- **DialogueName**: 对话的显示名称
- **StartEntryIndex**: 起始对话条目的索引（通常为0）
- **Entries**: 对话条目数组
  - **SpeakerName**: 说话者名称
  - **Text**: 对话文本（支持多行）
  - **SpeakerPortrait**: 说话者头像（可选）
  - **Choices**: 选项数组（可选）
    - **Text**: 选项文本
    - **NextEntryIndex**: 选择后跳转到的对话条目索引（-1表示结束对话）
    - **OnSelectedAction**: 选择时触发的行为ID（可选）

## 注意事项

- NPC节点必须继承自 `GameActor`
- NPCInteraction组件会自动创建交互区域和提示标签
- 对话进行时游戏会自动暂停
- 对话结束后游戏会自动恢复

