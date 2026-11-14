using Godot;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 放在各房间场景中的 Area2D 触发器，玩家进入后自动播放指定过场动画序列。
    /// 在 Inspector 中设置 Sequence（.tres 资源）即可。
    /// </summary>
    [GlobalClass]
    public partial class CutsceneTrigger : Area2D
    {
        [Export] public CutsceneSequence? Sequence { get; set; }
        public enum ExecutionModeEnum { ExclusiveCutscene, BackgroundSteps }
        /// <summary>后台步骤独立执行，不接管相机、UI 或输入。</summary>
        [Export] public ExecutionModeEnum ExecutionMode { get; set; } = ExecutionModeEnum.ExclusiveCutscene;

        /// <summary>只触发一次。</summary>
        [Export] public bool TriggerOnce { get; set; } = true;

        /// <summary>初始未武装：需外部（如 KillProgressAscendController）调用 <see cref="Arm"/> 之后才对玩家生效。
        /// 默认 false → 现有触发器的行为完全不变。</summary>
        [Export] public bool StartsDisarmed { get; set; } = false;

        [Export] public string PlayerGroup { get; set; } = "player";

        private bool _triggered = false;
        private bool _armed = true;
        private bool _running;
        private readonly CancellationTokenSource _lifetime = new();

        public override void _Ready()
        {
            // 使用 BodyEntered（与 EnemySpawnManager 同款），检测玩家物理体而非 HitArea。
            // BodyEntered 比 AreaEntered 更可靠：不依赖玩家 HitArea 的 Monitorable 设置，
            // 且与碰撞层矩阵直接匹配，配置更直观。
            BodyEntered += OnBodyEntered;
            Monitoring = true;
            _armed = !StartsDisarmed;
            GD.Print($"[Cutscene] CutsceneTrigger Ready — 节点: {Name}, Sequence: {(Sequence != null ? Sequence.SequenceId : "null")}, Armed: {_armed}");
        }

        /// <summary>
        /// 武装本触发器：之后玩家进入即按既有流程播放过场；**若玩家此刻已在区内，立即触发**（不等下一次 BodyEntered）。
        /// 用于"满足某个条件后才允许触发"的场景（例如进度满 → 到站过场）：触发区覆盖平台 = 立刻播，
        /// 触发区放到出口 = 玩家自己走过去才播，两种语义只由区域摆放决定。
        /// 幂等：已武装时直接返回。碰撞形状必须保持启用（否则 GetOverlappingBodies 拿不到任何体）。
        /// </summary>
        public void Arm()
        {
            if (_armed) return;
            _armed = true;

            foreach (Node2D body in GetOverlappingBodies())
            {
                if (body.IsInGroup(PlayerGroup))
                {
                    OnBodyEntered(body);
                    break;
                }
            }
        }

        private void OnBodyEntered(Node2D body)
        {
            if (!_armed) return;   // 未武装：不响应（放在最顶行，未武装时不刷日志）

            GD.Print($"[Cutscene] BodyEntered — body: {body.Name}");

            if (_running || TriggerOnce && _triggered)
            {
                GD.Print("[Cutscene] 已触发过，跳过");
                return;
            }
            if (Sequence == null)
            {
                GD.PrintErr("[Cutscene] Sequence 未设置！");
                return;
            }

            // BodyEntered 直接拿到玩家物理体根节点，无需向上查父节点
            if (!body.IsInGroup(PlayerGroup))
            {
                GD.Print($"[Cutscene] 非玩家 body，忽略（PlayerGroup={PlayerGroup}）");
                return;
            }

            GD.Print($"[Cutscene] 玩家进入触发区，准备播放: {Sequence.SequenceId}");

            var manager = GetTree().GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
            if (manager == null)
            {
                GD.PrintErr("[Cutscene] 未找到 CutsceneManager！确认 Stage_2 中已添加该节点。");
                return;
            }
            if (ExecutionMode == ExecutionModeEnum.BackgroundSteps)
            {
                foreach (var step in Sequence.Steps)
                    if (step != null && step is not EffectGroupSpawnStep && step is not EffectSpawnStep
                        && step is not WaitStep && step is not PlayAnimationStep)
                    {
                        GD.PushWarning($"[Cutscene] {Sequence.SequenceId}: 后台模式不支持 {step.GetType().Name}，未启动。");
                        return;
                    }
            }

            _triggered = true;
            _running = true;
            var sequence = Sequence;
            var mode = ExecutionMode;
            // 避开物理 Area 信号回调中的建树/碰撞修改。
            Callable.From(() => { _ = ExecuteRequest(manager, sequence, mode); }).CallDeferred();
        }

        private async Task ExecuteRequest(CutsceneManager manager, CutsceneSequence sequence, ExecutionModeEnum mode)
        {
            try
            {
                if (_lifetime.IsCancellationRequested || !IsInsideTree() || IsQueuedForDeletion()
                    || !GodotObject.IsInstanceValid(manager) || !manager.IsInsideTree()) return;
                if (mode == ExecutionModeEnum.BackgroundSteps)
                {
                    var ctx = new CutsceneContext(manager, sequence, this);
                    foreach (var step in sequence.Steps)
                    {
                        if (ctx.IsCancelled) return;
                        if (step != null) await step.Execute(ctx);
                    }
                    await ctx.FinishBackgroundTasks();
                }
                else
                {
                    // 多个等待者被同一完成信号唤醒时，每个都重新检查，仍只会独占播放。
                    while (manager.IsPlaying)
                        if (!await WaitForManager(manager)) return;
                    if (_lifetime.IsCancellationRequested || IsQueuedForDeletion() || !IsInsideTree()
                        || !GodotObject.IsInstanceValid(manager) || !manager.IsInsideTree()) return;
                    await manager.PlayCutscene(sequence);
                }
            }
            catch (Exception ex) { GD.PushError($"[Cutscene] {sequence.SequenceId} 执行失败: {ex.Message}"); }
            finally { _running = false; }
        }

        private async Task<bool> WaitForManager(CutsceneManager manager)
        {
            var wake = new TaskCompletionSource<bool>();
            void Finished(string id) => wake.TrySetResult(true);
            void ManagerExited() => wake.TrySetResult(false);
            manager.CutsceneFinished += Finished;
            manager.TreeExiting += ManagerExited;
            using var cancel = _lifetime.Token.Register(() => wake.TrySetResult(false));
            try { return await wake.Task; }
            finally
            {
                if (GodotObject.IsInstanceValid(manager))
                {
                    manager.CutsceneFinished -= Finished;
                    manager.TreeExiting -= ManagerExited;
                }
            }
        }

        public override void _ExitTree()
        {
            BodyEntered -= OnBodyEntered;
            _lifetime.Cancel();
        }
    }
}
