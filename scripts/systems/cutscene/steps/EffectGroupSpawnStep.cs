using System.Threading.Tasks;
using System.Collections.Generic;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 单个特效配置（用于 EffectGroupSpawnStep）
    /// </summary>
    [GlobalClass]
    public partial class EffectConfig : Resource
    {
        /// <summary>特效场景路径</summary>
        [Export] public string EffectScene { get; set; } = "";

        /// <summary>生成方式</summary>
        [Export] public EffectSpawnStep.SpawnTypeEnum SpawnType { get; set; } = EffectSpawnStep.SpawnTypeEnum.PlayerPosition;

        /// <summary>
        /// 位置参数，含义根据 SpawnType 改变：
        /// - PlayerPosition：相对于玩家的偏移
        /// - GlobalPosition：绝对全局坐标
        /// - RelativeToNode：相对于目标节点的偏移
        /// </summary>
        [Export] public Vector2 Position { get; set; } = Vector2.Zero;

        /// <summary>目标节点路径（SpawnType=RelativeToNode 时使用）</summary>
        [Export] public NodePath TargetNodePath { get; set; } = new NodePath();

        /// <summary>生成延迟（秒）。0 = 立即生成</summary>
        [Export(PropertyHint.Range, "0,30,0.1")] public float SpawnDelay { get; set; } = 0f;

        /// <summary>自动销毁时长（秒）。0 = 不自动销毁</summary>
        [Export(PropertyHint.Range, "0,30,0.1")] public float DestroyAfterDuration { get; set; } = 0f;
    }

    /// <summary>
    /// 过场动画中生成多个特效的 Step。
    /// 
    /// 用法：
    ///   配置一个特效列表，每个特效都有独立的：
    ///   - 生成位置（玩家、全局坐标、相对节点）
    ///   - 生成延迟
    ///   - 销毁时长
    ///   
    /// 支持两种执行模式：
    ///   1. 并行执行：所有特效同时按配置生成
    ///   2. 顺序执行：特效依次生成（下一个等前一个完成后）
    /// 
    /// 示例：
    ///   - 创建一个登场特效组：2个身影特效 + 1个冲击波
    ///   - 创建一个爆炸特效组：中心爆炸 + 周围碎片
    /// </summary>
    [GlobalClass]
    public partial class EffectGroupSpawnStep : CutsceneStep
    {
        /// <summary>执行模式</summary>
        public enum ExecutionModeEnum
        {
            /// <summary>所有特效并行生成，根据各自的 SpawnDelay 和 DestroyAfterDuration 执行</summary>
            Parallel,
            /// <summary>特效依次生成，等待前一个完成后再生成下一个</summary>
            Sequential,
        }

        // ── 导出属性 ──────────────────────────────────────────────

        [ExportCategory("Effects")]
        /// <summary>特效配置列表</summary>
        [Export] public Godot.Collections.Array<EffectConfig> Effects { get; set; } = new();

        [ExportCategory("Execution")]
        /// <summary>执行模式</summary>
        [Export] public ExecutionModeEnum ExecutionMode { get; set; } = ExecutionModeEnum.Parallel;

        /// <summary>
        /// 是否等待所有特效完成后再继续。
        /// true：阻塞执行直到所有特效销毁
        /// false：立即返回，特效后台执行
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = true;

        // ── 执行逻辑 ──────────────────────────────────────────────

        public override async Task Execute(CutsceneContext ctx)
        {
            if (Effects == null || Effects.Count == 0)
            {
                GD.PushWarning("[Cutscene] EffectGroupSpawnStep: 特效列表为空");
                return;
            }

            GD.Print($"[Cutscene] EffectGroupSpawnStep 开始，模式: {ExecutionMode}, 特效数量: {Effects.Count}");

            if (ExecutionMode == ExecutionModeEnum.Parallel)
            {
                await ExecuteParallel(ctx);
            }
            else
            {
                await ExecuteSequential(ctx);
            }

            GD.Print("[Cutscene] EffectGroupSpawnStep: 完成");
        }

        // ── 并行模式 ──────────────────────────────────────────────

        private async Task ExecuteParallel(CutsceneContext ctx)
        {
            var effectTasks = new List<Task>();

            foreach (var config in Effects)
            {
                if (config == null || string.IsNullOrEmpty(config.EffectScene))
                {
                    GD.PushWarning("[Cutscene] EffectGroupSpawnStep: 特效配置无效，跳过");
                    continue;
                }

                // 创建异步任务生成和管理这个特效
                effectTasks.Add(SpawnEffectAsync(ctx, config));
            }

            if (!WaitForCompletion)
            {
                GD.Print("[Cutscene] EffectGroupSpawnStep: 异步模式（不等待特效完成）");
                return;
            }

            // 等待所有特效任务完成
            await Task.WhenAll(effectTasks);
            GD.Print("[Cutscene] EffectGroupSpawnStep: 所有特效已完成");
        }

        // ── 顺序模式 ──────────────────────────────────────────────

        private async Task ExecuteSequential(CutsceneContext ctx)
        {
            for (int i = 0; i < Effects.Count; i++)
            {
                if (ctx.IsSkipping)
                {
                    GD.Print("[Cutscene] EffectGroupSpawnStep: 跳过请求，中断剩余特效");
                    break;
                }

                var config = Effects[i];
                if (config == null || string.IsNullOrEmpty(config.EffectScene))
                {
                    GD.PushWarning($"[Cutscene] EffectGroupSpawnStep: 第 {i} 个特效配置无效，跳过");
                    continue;
                }

                GD.Print($"[Cutscene] EffectGroupSpawnStep: 生成第 {i} 个特效");
                await SpawnEffectAsync(ctx, config);
                GD.Print($"[Cutscene] EffectGroupSpawnStep: 第 {i} 个特效完成");
            }
        }

        // ── 单个特效管理 ──────────────────────────────────────────

        private async Task SpawnEffectAsync(CutsceneContext ctx, EffectConfig config)
        {
            try
            {
                // 等待 SpawnDelay
                if (config.SpawnDelay > 0f)
                {
                    var delayTimer = ctx.Tree.CreateTimer(config.SpawnDelay);
                    while (!ctx.IsSkipping && delayTimer.TimeLeft > 0f)
                        await ctx.NextFrame();
                }

                if (ctx.IsSkipping)
                {
                    GD.Print($"[Cutscene] EffectGroupSpawnStep: 特效生成被跳过（{config.EffectScene}）");
                    return;
                }

                // 加载并实例化特效
                var scene = GD.Load<PackedScene>(config.EffectScene);
                if (scene == null)
                {
                    GD.PrintErr($"[Cutscene] EffectGroupSpawnStep: 无法加载特效 {config.EffectScene}");
                    return;
                }

                var effect = scene.Instantiate();
                if (effect is not Node2D effectNode2D)
                {
                    GD.PrintErr($"[Cutscene] EffectGroupSpawnStep: 特效必须是 Node2D（{config.EffectScene}）");
                    effect?.QueueFree();
                    return;
                }

                // 计算生成位置
                Vector2 spawnPos = CalculateSpawnPosition(ctx, config);

                // 添加到场景树
                var parent = ctx.Manager.GetParent() ?? ctx.Tree.Root;
                parent.AddChild(effectNode2D);
                effectNode2D.GlobalPosition = spawnPos;

                GD.Print($"[Cutscene] EffectGroupSpawnStep: 特效已生成 {config.EffectScene} @ {spawnPos}");

                // 管理特效生命周期
                if (config.DestroyAfterDuration > 0f)
                {
                    var destroyTimer = ctx.Tree.CreateTimer(config.DestroyAfterDuration);
                    while (!ctx.IsSkipping && destroyTimer.TimeLeft > 0f && GodotObject.IsInstanceValid(effectNode2D))
                        await ctx.NextFrame();

                    if (GodotObject.IsInstanceValid(effectNode2D))
                        effectNode2D.QueueFree();

                    GD.Print($"[Cutscene] EffectGroupSpawnStep: 特效已销毁 {config.EffectScene}");
                }
            }
            catch (System.Exception ex)
            {
                GD.PrintErr($"[Cutscene] EffectGroupSpawnStep 异常: {ex.Message}");
            }
        }

        // ── 位置计算 ──────────────────────────────────────────────

        private Vector2 CalculateSpawnPosition(CutsceneContext ctx, EffectConfig config)
        {
            return config.SpawnType switch
            {
                EffectSpawnStep.SpawnTypeEnum.PlayerPosition => GetPlayerPosition(ctx) + config.Position,
                EffectSpawnStep.SpawnTypeEnum.GlobalPosition => config.Position,
                EffectSpawnStep.SpawnTypeEnum.RelativeToNode => GetRelativeToNodePosition(ctx, config) + config.Position,
                _ => Vector2.Zero,
            };
        }

        private Vector2 GetPlayerPosition(CutsceneContext ctx)
        {
            var player = ctx.Manager.Player;
            if (player == null)
            {
                GD.PushWarning("[Cutscene] EffectGroupSpawnStep: 玩家节点未找到");
                return Vector2.Zero;
            }
            return player.GlobalPosition;
        }

        private Vector2 GetRelativeToNodePosition(CutsceneContext ctx, EffectConfig config)
        {
            if (config.TargetNodePath.IsEmpty)
                return Vector2.Zero;

            var targetNode = ctx.Manager.GetNodeOrNull<Node2D>(config.TargetNodePath);
            if (targetNode == null)
            {
                GD.PushWarning($"[Cutscene] EffectGroupSpawnStep: 目标节点 {config.TargetNodePath} 未找到");
                return Vector2.Zero;
            }

            return targetNode.GlobalPosition;
        }
    }
}
