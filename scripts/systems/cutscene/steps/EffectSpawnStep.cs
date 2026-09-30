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
    ///   - Position：位置参数，含义根据 SpawnType 改变：
    ///     * PlayerPosition：相对于玩家的偏移
    ///     * GlobalPosition：绝对全局坐标
    ///     * RelativeToNode：相对于目标节点的偏移
    ///   - TargetNodePath：目标节点路径（SpawnType=RelativeToNode 时使用）
    ///   - DestroyAfterDuration：是否在指定秒数后销毁（0 = 不销毁）
    ///   - WaitForCompletion：是否等待特效销毁后再继续（仅 DestroyAfterDuration > 0 时有效）
    /// 
    /// 示例配置：
    ///   1. PlayerPosition + Position(100, -50)：在玩家右上方生成特效
    ///   2. GlobalPosition + Position(500, 300)：在全局坐标 (500,300) 生成特效
    ///   3. RelativeToNode(EnemyPath) + Position(50, -200)：在敌人右上方生成特效
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
        /// <summary>要生成的特效场景（直接拖场景资源进来）</summary>
        [Export] public PackedScene? EffectScene { get; set; }

        [ExportCategory("Spawning")]
        /// <summary>生成方式</summary>
        [Export] public SpawnTypeEnum SpawnType { get; set; } = SpawnTypeEnum.PlayerPosition;

        /// <summary>
        /// 位置参数，含义根据 SpawnType 改变：
        /// - PlayerPosition：相对于玩家的偏移
        /// - GlobalPosition：绝对全局坐标
        /// - RelativeToNode：相对于目标节点的偏移
        /// </summary>
        [Export] public Vector2 Position { get; set; } = Vector2.Zero;

        /// <summary>目标节点路径（SpawnType=RelativeToNode 时使用）</summary>
        [Export] public NodePath TargetNodePath { get; set; } = new NodePath();

        [ExportCategory("Overrides")]
        /// <summary>
        /// 生成时属性覆盖（属性名 → 值），在 AddChild 之前应用——同一个通用场景可以借此生成出
        /// 不同配置的多份实例（如左右两条 SlideRail：分别覆盖 FlipCarriageEnds / CarriagePrefab / 限位路径），
        /// 不必复制出多个变体场景。属性名写错会打警告。
        /// </summary>
        [Export] public Godot.Collections.Dictionary<string, Variant> PropertyOverrides { get; set; } = new();

        [ExportCategory("Skip 跳过")]
        /// <summary>跳过过场时是否仍然生成（默认 true = 快进到最终状态，关键生成用它）。
        /// 纯视觉（爆炸/烟雾）可设 false，避免按跳过时闪现一下。</summary>
        [Export] public bool GenerateOnSkip { get; set; } = true;

        [ExportCategory("Cleanup")]
        /// <summary>
        /// 生成登记标签：生成后把实例**根节点**登记到 CutsceneManager，供后续 **EffectDespawnStep** 按标签销毁
        /// （空 = 只参与"清全部"）。登记根就够了——子树里的东西（如滑槽 Mount 下自动入驻的 CarriagePrefab、
        /// 以及挂在它下面的机械/敌人）随父节点一起释放，不必单独登记。
        /// </summary>
        [Export] public string SpawnTag { get; set; } = "";

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
            if (EffectScene == null)
            {
                GD.PrintErr($"[Cutscene] EffectSpawnStep: EffectScene 未配置");
                return;
            }

            if (ctx.IsSkipping && !GenerateOnSkip)
            {
                GD.Print("[Cutscene] EffectSpawnStep: 跳过过场且 GenerateOnSkip=false，不生成");
                return;
            }

            GD.Print($"[Cutscene] EffectSpawnStep 开始，特效: {EffectScene.ResourcePath}, 生成方式: {SpawnType}");

            try
            {
                // 实例化特效
                var effect = EffectScene.Instantiate();
                if (effect is not Node2D effectNode2D)
                {
                    GD.PrintErr($"[Cutscene] EffectSpawnStep: 特效必须是 Node2D");
                    effect?.QueueFree();
                    return;
                }

                // 计算生成位置
                Vector2 spawnPos = CalculateSpawnPosition(ctx);

                // 属性覆盖必须在入树之前：节点 _Ready 里读取的配置（如滑槽的 FlipCarriageEnds /
                // CarriagePrefab / 限位 Marker 路径）必须已是覆盖后的值
                CutsceneSpawnUtil.ApplyPropertyOverrides(effectNode2D, PropertyOverrides, nameof(EffectSpawnStep));

                // 添加到场景树（基准 = 管理器所在节点的父级）
                var parent = ctx.Manager.GetParent() ?? ctx.Tree.Root;
                parent.AddChild(effectNode2D);
                effectNode2D.GlobalPosition = spawnPos;

                // 登记生成物根（EffectDespawnStep 按 SpawnTag 回收；不登记就只能靠它自己的生命周期）
                ctx.Manager.RegisterSpawnedRoot(effectNode2D, SpawnTag);

                GD.Print($"[Cutscene] EffectSpawnStep: 特效已生成，位置: {spawnPos}，标签: {(string.IsNullOrEmpty(SpawnTag) ? "(无)" : SpawnTag)}");

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
                SpawnTypeEnum.PlayerPosition => GetPlayerPosition(ctx) + Position,
                SpawnTypeEnum.GlobalPosition => Position,
                SpawnTypeEnum.RelativeToNode => GetRelativeToNodePosition(ctx) + Position,
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

            return targetNode.GlobalPosition;
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
