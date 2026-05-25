using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 过场动画中生成特效的 Step。
    /// 
    /// 用法：
    ///   - EffectScene：要生成的特效预制体路径
    ///   - SpawnType：生成方式（PlayerPosition / GlobalPosition / RelativeToNode）
    ///   - GlobalSpawnPos：全局生成坐标（SpawnType=GlobalPosition 时使用）
    ///   - TargetNodePath：目标节点路径（SpawnType=RelativeToNode 时使用）
    ///   - OffsetFromTarget：相对目标的偏移量
    ///   - DestroyAfterDuration：是否在指定秒数后销毁（0 = 不销毁，让特效自行销毁）
    ///   - WaitForCompletion：是否等待特效销毁后再继续（仅 DestroyAfterDuration > 0 时有效）
    /// 
    /// 示例配置：
    ///   1. 在玩家位置生成登场特效（在 WaitStep 或 PlayAnimationStep 之后）
    ///   2. 在指定全局坐标生成爆炸特效
    ///   3. 在敌人节点处生成技能特效
    /// </summary>
    [GlobalClass]
    public partial class EffectSpawnStep : CutsceneStep
    {
        public enum SpawnTypeEnum
        {
            /// <summary>在玩家当前位置生成</summary>
            PlayerPosition,
            /// <summary>在指定的全局坐标生成</summary>
            GlobalPosition,
            /// <summary>相对于指定节点生成（支持本地坐标偏移）</summary>
            RelativeToNode,
        }

        // ── 导出属性 ──────────────────────────────────────────────

        [ExportCategory("Effect")]
        /// <summary>要生成的特效场景资源路径（.tscn）</summary>
        [Export] public string EffectScene { get; set; } = "";

        [ExportCategory("Spawning")]
        /// <summary>生成方式</summary>
        [Export] public SpawnTypeEnum SpawnType { get; set; } = SpawnTypeEnum.PlayerPosition;

        /// <summary>全局生成坐标（SpawnType=GlobalPosition 时使用）</summary>
        [Export] public Vector2 GlobalSpawnPos { get; set; } = Vector2.Zero;

        /// <summary>目标节点路径（SpawnType=RelativeToNode 时使用，相对于 CutsceneManager）</summary>
        [Export] public NodePath TargetNodePath { get; set; } = new NodePath();

        /// <summary>相对于目标节点的偏移量（本地坐标）</summary>
        [Export] public Vector2 OffsetFromTarget { get; set; } = Vector2.Zero;

        [ExportCategory("Cleanup")]
        /// <summary>
        /// 是否在指定秒数后自动销毁生成的特效。
        /// 0 = 不销毁（让特效依据其自身生命周期销毁）
        /// > 0 = 在此秒数后销毁
        /// </summary>
        [Export(PropertyHint.Range, "0,30,0.1")] public float DestroyAfterDuration { get; set; } = 0f;

        /// <summary>
        /// 是否等待特效完全销毁后再继续下一步。
        /// 仅在 DestroyAfterDuration > 0 时生效。
        /// true：阻塞执行直到特效销毁
        /// false：立即返回，特效后台销毁
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = true;

        // ── 执行逻辑 ──────────────────────────────────────────────

        public override async Task Execute(CutsceneContext ctx)
        {
            if (string.IsNullOrEmpty(EffectScene))
            {
                GD.PrintErr($"[Cutscene] EffectSpawnStep: EffectScene 未配置");
                return;
            }

            GD.Print($"[Cutscene] EffectSpawnStep 开始，特效: {EffectScene}, 生成方式: {SpawnType}");

            try
            {
                // 加载特效场景
                var scene = GD.Load<PackedScene>(EffectScene);
                if (scene == null)
                {
                    GD.PrintErr($"[Cutscene] EffectSpawnStep: 无法加载特效场景 {EffectScene}");
                    return;
                }

                // 实例化特效
                var effect = scene.Instantiate();
                if (effect is not Node2D effectNode2D)
                {
                    GD.PrintErr($"[Cutscene] EffectSpawnStep: 特效必须是 Node2D");
                    effect?.QueueFree();
                    return;
                }

                // 计算生成位置
                Vector2 spawnPos = CalculateSpawnPosition(ctx);

                // 添加到场景树
                var parent = ctx.Manager.GetParent() ?? ctx.Tree.Root;
                parent.AddChild(effectNode2D);
                effectNode2D.GlobalPosition = spawnPos;

                GD.Print($"[Cutscene] EffectSpawnStep: 特效已生成，位置: {spawnPos}");

                // 若无需自动销毁，直接返回
                if (DestroyAfterDuration <= 0f)
                {
                    GD.Print("[Cutscene] EffectSpawnStep: 完成（特效生命周期自管理）");
                    return;
                }

                // 若需要自动销毁，创建计时器
                var timer = ctx.Tree.CreateTimer(DestroyAfterDuration);

                if (!WaitForCompletion)
                {
                    // 异步销毁：立即返回，后台计时
                    _ = DestroyEffectAsync(effectNode2D, timer, ctx);
                    GD.Print($"[Cutscene] EffectSpawnStep: 异步销毁模式（{DestroyAfterDuration}秒后销毁）");
                    return;
                }

                // 阻塞等待销毁
                while (!ctx.IsSkipping && timer.TimeLeft > 0f)
                    await ctx.NextFrame();

                if (GodotObject.IsInstanceValid(effectNode2D))
                    effectNode2D.QueueFree();

                GD.Print("[Cutscene] EffectSpawnStep: 完成（特效已销毁）");
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[Cutscene] EffectSpawnStep 异常: {ex.Message}");
            }
        }

        // ── 私有辅助方法 ──────────────────────────────────────────

        private Vector2 CalculateSpawnPosition(CutsceneContext ctx)
        {
            return SpawnType switch
            {
                SpawnTypeEnum.PlayerPosition => GetPlayerPosition(ctx),
                SpawnTypeEnum.GlobalPosition => GlobalSpawnPos,
                SpawnTypeEnum.RelativeToNode => GetRelativeToNodePosition(ctx),
                _ => Vector2.Zero,
            };
        }

        private Vector2 GetPlayerPosition(CutsceneContext ctx)
        {
            var player = ctx.Manager.Player;
            if (player == null)
            {
                GD.PushWarning("[Cutscene] EffectSpawnStep: 玩家节点未找到，使用零点");
                return Vector2.Zero;
            }
            return player.GlobalPosition;
        }

        private Vector2 GetRelativeToNodePosition(CutsceneContext ctx)
        {
            if (TargetNodePath.IsEmpty)
            {
                GD.PushWarning("[Cutscene] EffectSpawnStep: TargetNodePath 未配置，使用零点");
                return Vector2.Zero;
            }

            var targetNode = ctx.Manager.GetNodeOrNull<Node2D>(TargetNodePath);
            if (targetNode == null)
            {
                GD.PushWarning($"[Cutscene] EffectSpawnStep: 目标节点 {TargetNodePath} 未找到，使用零点");
                return Vector2.Zero;
            }

            return targetNode.GlobalPosition + OffsetFromTarget;
        }

        private async Task DestroyEffectAsync(Node2D effect, SceneTreeTimer timer, CutsceneContext ctx)
        {
            while (!ctx.IsSkipping && timer.TimeLeft > 0f && GodotObject.IsInstanceValid(effect))
                await ctx.NextFrame();

            if (GodotObject.IsInstanceValid(effect))
            {
                effect.QueueFree();
                GD.Print("[Cutscene] EffectSpawnStep: 特效已异步销毁");
            }
        }
    }
}
