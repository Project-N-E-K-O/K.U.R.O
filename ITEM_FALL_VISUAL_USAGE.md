# 通用物品坠落替身

## 运行结构

Animation_item.tscn 保留原 UID 和路径，当前仅 7 个节点：

```text
AnimationItem (ItemFallVisual)
├─ ItemPosition
│  └─ Sprite2D
├─ effect
│  ├─ star1
│  └─ star2
└─ AnimationPlayer
   ├─ RESET
   └─ fall
```

物品原来的 fall_xxx 名称现在是外观配置键，不再是 AnimationPlayer 中的动画名。原场景已验证的 52 条曲线去除节点名和无关隐藏轨道后完全相同，统一为一条 12 轨的 fall，保留原下沉、缩放、旋转、层级与星光曲线。物品差异仅写入 Sprite2D，不会被父节点 ItemPosition 的动画覆盖。

## 配置

现有 GroundObjectController 的 FallEffectScene 仍指向 Animation_item.tscn，FallAnimationName 仍可填写 fall_emoji_bomb、fall_bed_a 等原名称，不必逐个迁移。FallPhysics 在实例进入场景树之前解析外观，再实际播放 fall 并按区域持续时间缩放速度。

其他生成链直接实例化该场景、设置根节点 AnimationName=fall_xxx 后 AddChild，也由 ItemFallVisual.Ready 兼容。直接对 AnimationPlayer.Play("fall_xxx") 的调用应改为根 ConfigureForKey(key)，再播放 fall；动画库只包含 RESET 和 fall。

外观表位于 resources/effects/item_fall/visual_catalog.tres，包含 53 个配置项：52 个原映射名称，以及 fall_small → fall_briefcase 的历史兼容别名。表是实例共享的 Resource，不会为每次坠落创建 53 棵节点树。表会引用各贴图，当前优化主要针对节点和动画处理，不意味着只加载当前一张纹理。

单个物品可在 GroundObjectController.FallVisualProfile 拖入独立的 ItemFallVisualProfile 资源，优先覆盖名称映射。也可在表的 Profiles 中新增配置键。

ItemFallVisualProfile 包含真实贴图、Sprite 局部位置、基础缩放、旋转、Offset、Centered、翻转、区域裁切、帧配置、颜色和材质。默认从原动画场景提取真实 Sprite 参数，不以背包 Icon 替代。位置参数与 GroundObjectController.FallEffectOffset 的世界生成偏移独立。

优先级：调用方显式 FallVisualProfile → 替身根节点 VisualProfile → Catalog 中的配置键 → fall_small 兜底。未知名称会警告。若只填写 fall，未提供显式外观时使用 fall_small。新物品建议直接指定外观资源，避免维护字符串映射。

星光材质每个实例独立复制，防止不同播放进度互相覆盖 shader alpha。贴图和外观表仍共享。根下 AnimationPlayer 只操作通用动画变换，不改写 Sprite 外观基础变换。

## 备份与验证

迁移时使用的原始大场景临时备份已在验证完成后删除，不参与运行。原动画映射表中的两个逗号语法错误已在迁移过程中修复。

迁移后场景从 109 个节点变为 7 个；Animation_item.tscn 从约 1.86 MB 变为约 10.5 KB，外观表约 20.3 KB。尚未进行实际帧耗时基准测试，不据此承诺固定 FPS 提升。

1622 项运行断言通过，包括 52 个原名称各 7 个时间点的外观、变换、层级、星光变换与 alpha 等价性，进入场景树前配置、2 条动画/7 节点约束、fall_small 兼容、持续时间缩放、并发材质隔离、独立回收及显式外观覆盖。临时迁移与测试场景、脚本、日志在验证后清理。
