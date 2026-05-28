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
    ///   - PlayAnimationStep：播放角色动画
    ///   - EffectSpawnStep：生成单个特效（支持延迟、自动销毁）
    ///   - EffectGroupSpawnStep：生成多个特效组合（并行/顺序执行）
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
        /// 若为 true，即使过场被 skip，该步骤仍会执行。
        /// 用于必须完成的关键步骤（如场景切换），默认 false。
        /// </summary>
        public virtual bool ExecuteOnSkip => false;

        public abstract Task Execute(CutsceneContext ctx);
    }
}
