using Godot;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 传递给每个步骤的上下文对象，包含管理器引用与跳过状态。
    /// </summary>
    public sealed class CutsceneContext
    {
        public CutsceneManager Manager { get; }
        public CutsceneSequence Sequence { get; }
        public SceneTree Tree { get; }
        private readonly Node? _owner;
        private readonly List<Task> _backgroundTasks = new();
        public bool IsBackground => _owner != null;
        public bool IsCancelled => !GodotObject.IsInstanceValid(Manager) || !Manager.IsInsideTree()
            || IsBackground && (!GodotObject.IsInstanceValid(_owner) || !_owner!.IsInsideTree() || _owner.IsQueuedForDeletion());
        public bool IsSkipping => !IsBackground && !IsCancelled && Manager.IsSkipRequested;

        public CutsceneContext(CutsceneManager manager, CutsceneSequence sequence, Node? owner = null)
        {
            Manager = manager;
            Sequence = sequence;
            Tree = manager.GetTree();
            _owner = owner;
        }

        internal void TrackTask(Task task) { if (IsBackground) _backgroundTasks.Add(task); }
        internal async Task FinishBackgroundTasks()
        {
            int count;
            do { count = _backgroundTasks.Count; await Task.WhenAll(_backgroundTasks.ToArray()); }
            while (count != _backgroundTasks.Count);
        }

        /// <summary>等待下一帧（供各步骤在 while 循环中使用）。</summary>
        public SignalAwaiter NextFrame() =>
            Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame);
    }
}
