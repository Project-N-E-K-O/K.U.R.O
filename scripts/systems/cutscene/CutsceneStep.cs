using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 过场动画的单个步骤基类，所有步骤（等待、对话、镜头移动等）均继承此类。
    ///
    /// 可用的 Step 实现列表：
    ///   - WaitStep：等待指定时长（支持异步）
    ///   - DialogueStep：显示对话文本
    ///   - FadeStep：全屏淡出/淡入效果
    ///   - CameraMoveStep：镜头平滑移动到目标
    ///   - MovePlayerStep：把玩家移动到指定节点位置（同场景换房间/换站位；0 秒 = 瞬移）
    ///   - PlayAnimationStep：播放角色动画
    ///   - EffectSpawnStep：生成单个特效（支持延迟、自动销毁）
    ///   - EffectGroupSpawnStep：生成多个特效组合（并行/顺序执行）
    ///   - EffectDespawnStep：销毁之前生成的场景/特效（按 SpawnTag，留空 = 清全部；根节点销毁 = 子树连坐）
    ///   - ChangeSceneStep：切换到目标场景（执行后当前场景销毁，后续步骤不执行）
    ///
    /// 使用示例：
    ///   var sequence = new CutsceneSequence
    ///   {
    ///       Steps = new Godot.Collections.Array&lt;CutsceneStep&gt;
    ///       {
    ///           new FadeStep { FadeDuration = 0.5f, TargetAlpha = 1f },
    ///           new EffectGroupSpawnStep { ... },
    ///           new DialogueStep { ... },
    ///       }
    ///   };
    /// </summary>
    [GlobalClass]
    public abstract partial class CutsceneStep : Resource
    {
        /// <summary>
        /// 跳过时是否仍调用本步骤，默认 **true**。
        ///
        /// 语义：**跳过 = 快进到最终状态**——被跳过的过场会把剩余步骤逐个执行一遍，
        /// 每个步骤需在 `Execute` 里自行处理 `ctx.IsSkipping`（瞬时落终态 / 立即完成 / 什么都不做），
        /// 而不是进入等待循环。例如：
        ///   · PlayAnimationStep → 直接 Play + Seek 到末帧
        ///   · CameraMoveStep / FadeStep → 直接设到目标位置 / 目标透明度
        ///   · DialogueStep / DialogicStep → 直接 return（跳过时不该弹对话）
        ///   · EffectSpawnStep / EffectGroupSpawnStep → 照常生成但不等待（纯视觉可用 GenerateOnSkip 关掉生成）
        /// 想被管理器**整步取消**的步骤，覆写本属性为 false 即可。
        /// </summary>
        public virtual bool ExecuteOnSkip => true;

        public abstract Task Execute(CutsceneContext ctx);
    }
}
