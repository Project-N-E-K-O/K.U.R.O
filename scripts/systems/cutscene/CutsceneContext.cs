using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 传递给每个步骤的上下文对象，包含管理器引用与跳过状态。
    /// </summary>
    public sealed class CutsceneContext
    {
        public CutsceneManager Manager { get; }
        public CutsceneSequence Sequence { get; }
        public SceneTree Tree => Manager.GetTree();
        public bool IsSkipping => Manager.IsSkipRequested;

        public CutsceneContext(CutsceneManager manager, CutsceneSequence sequence)
        {
            Manager = manager;
            Sequence = sequence;
        }

        /// <summary>等待下一帧（供各步骤在 while 循环中使用）。</summary>
        public SignalAwaiter NextFrame() =>
            Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }
}
