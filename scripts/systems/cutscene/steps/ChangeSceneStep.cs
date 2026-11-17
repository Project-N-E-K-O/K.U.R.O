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
        internal const string HomeTaxiReturnMeta = "home_taxi_return";
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
        /// <summary>由目标 Stage_home 消费的一次性返程请求。</summary>
        [Export] public bool ReturnHomeByTaxi { get; set; }

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
            string configuredPath = ScenePath.StartsWith("uid://")
                ? ResourceUid.GetIdPath(ResourceUid.TextToId(ScenePath))
                : ScenePath;
            if (ReturnHomeByTaxi && preloaded?.ResourcePath != configuredPath)
                preloaded = null;
            string destination = preloaded?.ResourcePath ?? configuredPath;
            Kuros.UI.CloudSceneTransition? cloud = null;
            if (UseCloudTransition && (preloaded != null || !string.IsNullOrEmpty(ScenePath)))
            {
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

            if (ReturnHomeByTaxi && !string.IsNullOrEmpty(destination))
                tree.Root.SetMeta(HomeTaxiReturnMeta, destination);

            Error error;
            if (preloaded != null)
            {
                GD.Print($"[Cutscene] ChangeSceneStep: 使用预加载场景切换 {preloaded.ResourcePath}");
                error = tree.ChangeSceneToPacked(preloaded);
            }
            else if (!string.IsNullOrEmpty(ScenePath))
            {
                GD.Print($"[Cutscene] ChangeSceneStep: 切换到路径 {ScenePath}");
                error = tree.ChangeSceneToFile(ScenePath);
            }
            else
            {
                GD.PrintErr("[Cutscene] ChangeSceneStep: ScenePath 未设置，跳过");
                error = Error.InvalidParameter;
            }

            if (error == Error.Ok) return;
            if (ReturnHomeByTaxi) tree.Root.RemoveMeta(HomeTaxiReturnMeta);
            cloud?.QueueFree();
        }
    }
}
