using System.Threading.Tasks;
using Godot;
using Kuros.Environments;
using Kuros.Items.World;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 切换到指定场景。此步骤执行后当前场景会被销毁，后续步骤不会执行。
    /// 可选：在切换前等待指定延迟（例如配合 FadeStep 等待淡出完成后再切换）。
    ///
    /// 使用方式：
    ///   ScenePath → res://scenes/Stage_1.tscn（与 ChangeSceneToFile 语义相同）
    ///   SceneResource → 直接引用 PackedScene 资源（优先级高于 ScenePath）
    ///   DelayBeforeChange → 切换前等待的秒数（默认 0，立即切换）
    /// </summary>
    [GlobalClass]
    public partial class ChangeSceneStep : CutsceneStep
    {
        /// <summary>场景切换必须执行，即使过场被 skip 也不跳过。</summary>
        public override bool ExecuteOnSkip => true;

        /// <summary>目标场景路径（res:// 路径），与 SceneResource 二选一。</summary>
        [Export(PropertyHint.File, "*.tscn")] public string ScenePath { get; set; } = "";

        /// <summary>目标场景资源引用，优先级高于 ScenePath。</summary>
        [Export] public PackedScene? SceneResource { get; set; }

        /// <summary>切换前等待的秒数（可配合前置 FadeStep 使用）。</summary>
        [Export(PropertyHint.Range, "0,10,0.01")] public float DelayBeforeChange { get; set; } = 0f;

        /// <summary>
        /// 切换前清理 WorldItemSpawner 缓存，防止旧场景资源在新场景中占用内存。
        /// 与 ElevatorController 行为一致，跨关卡切换时建议开启。
        /// </summary>
        [Export] public bool ClearWorldItemCache { get; set; } = true;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] ChangeSceneStep 开始，ScenePath={ScenePath}, HasResource={SceneResource != null}, Delay={DelayBeforeChange}");

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

            if (ClearWorldItemCache)
            {
                WorldItemSpawner.ClearCache();
                GD.Print("[Cutscene] ChangeSceneStep: WorldItemSpawner 缓存已清理");
            }

            if (preloaded != null)
            {
                GD.Print($"[Cutscene] ChangeSceneStep: 使用预加载场景切换 {preloaded.ResourcePath}");
                tree.ChangeSceneToPacked(preloaded);
            }
            else if (SceneResource != null)
            {
                GD.Print($"[Cutscene] ChangeSceneStep: 切换到 PackedScene {SceneResource.ResourcePath}");
                tree.ChangeSceneToPacked(SceneResource);
            }
            else if (!string.IsNullOrEmpty(ScenePath))
            {
                GD.Print($"[Cutscene] ChangeSceneStep: 切换到路径 {ScenePath}");
                tree.ChangeSceneToFile(ScenePath);
            }
            else
            {
                GD.PrintErr("[Cutscene] ChangeSceneStep: ScenePath 和 SceneResource 均未设置，跳过");
            }

            // 场景切换后当前场景树会被销毁，此处不再执行后续代码
        }
    }
}
