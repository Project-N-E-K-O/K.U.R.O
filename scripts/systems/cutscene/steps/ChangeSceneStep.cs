using System.Threading.Tasks;
using Godot;
using Kuros.Environments;
using Kuros.Items.World;
using Kuros.Utils;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 切换到指定场景。此步骤执行后当前场景会被销毁，后续步骤不会执行。
    /// 可选：在切换前等待指定延迟（例如配合 FadeStep 等待淡出完成后再切换）。
    ///
    /// 使用方式：
    ///   优先消费 TaxiController.ConsumePreloadedScene()（预加载缓存，几乎瞬间切换）；
    ///   无缓存时按 ScenePath 切换（ChangeSceneToFile 语义）。
    ///   DelayBeforeChange → 切换前等待的秒数（默认 0，立即切换）
    /// </summary>
    [GlobalClass]
    public partial class ChangeSceneStep : CutsceneStep
    {
        /// <summary>场景切换必须执行，即使过场被 skip 也不跳过。</summary>
        public override bool ExecuteOnSkip => true;

        /// <summary>目标场景路径（res:// 路径或 uid）。</summary>
        [Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";

        /// <summary>切换前等待的秒数（可配合前置 FadeStep 使用）。</summary>
        [Export(PropertyHint.Range, "0,10,0.01")] public float DelayBeforeChange { get; set; } = 0f;

        /// <summary>
        /// 切换前清理 WorldItemSpawner 缓存，防止旧场景资源在新场景中占用内存。
        /// 与 ElevatorController 行为一致，跨关卡切换时建议开启。
        /// </summary>
        [Export] public bool ClearWorldItemCache { get; set; } = true;
        /// <summary>酒店入场：换场前云层盖满，由 StageSession 首层就绪后退场。</summary>
        [Export] public bool UseCloudTransition { get; set; }

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] ChangeSceneStep 开始，ScenePath={ScenePath}, Delay={DelayBeforeChange}");

            if (DelayBeforeChange > 0f)
            {
                float elapsed = 0f;
                while (elapsed < DelayBeforeChange && !ctx.IsSkipping)
                {
                    await ctx.NextFrame();
                    elapsed += (float)ctx.Manager.GetProcessDeltaTime();
                }
            }

            var tree = ctx.Manager.GetTree();

            // 优先使用 TaxiController 预加载的场景（已在内存中，切换几乎瞬间）
            var preloaded = TaxiController.ConsumePreloadedScene();
            Kuros.UI.CloudSceneTransition? cloud = null;
            if (UseCloudTransition && (preloaded != null || !string.IsNullOrEmpty(ScenePath)))
            {
                string destination = preloaded?.ResourcePath ?? ScenePath;
                if (destination.StartsWith("uid://"))
                    destination = ResourceUid.GetIdPath(ResourceUid.TextToId(destination));
                cloud = await Kuros.UI.CloudSceneTransition.CoverAsync(tree, destination);
                if (!GodotObject.IsInstanceValid(ctx.Manager) || !ctx.Manager.IsInsideTree())
                {
                    if (GodotObject.IsInstanceValid(cloud)) cloud.QueueFree();
                    return;
                }
            }

            if (ClearWorldItemCache)
            {
                WorldItemSpawner.ClearCache();
                GD.Print("[Cutscene] ChangeSceneStep: WorldItemSpawner 缓存已清理");
            }

            DialogicUtils.CleanupPersistentState(ctx.Manager);

            if (preloaded != null)
            {
                GD.Print($"[Cutscene] ChangeSceneStep: 使用预加载场景切换 {preloaded.ResourcePath}");
                if (tree.ChangeSceneToPacked(preloaded) != Error.Ok) cloud?.QueueFree();
            }
            else if (!string.IsNullOrEmpty(ScenePath))
            {
                GD.Print($"[Cutscene] ChangeSceneStep: 切换到路径 {ScenePath}");
                if (tree.ChangeSceneToFile(ScenePath) != Error.Ok) cloud?.QueueFree();
            }
            else
            {
                GD.PrintErr("[Cutscene] ChangeSceneStep: ScenePath 未设置，跳过");
            }

            // 场景切换后当前场景树会被销毁，此处不再执行后续代码
        }
    }
}
