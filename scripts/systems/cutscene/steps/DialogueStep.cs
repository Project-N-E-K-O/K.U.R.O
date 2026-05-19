using System.Threading.Tasks;
using Godot;
using Godot.Collections;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 显示一组台词。需要 CutsceneManager 中设置 DialoguePanelPath。
    /// 支持异步模式：WaitForCompletion=false 时不阻塞后续步骤。
    /// </summary>
    [GlobalClass]
    public partial class DialogueStep : CutsceneStep
    {
        [Export] public Array<DialogueLine> Lines { get; set; } = new();

        /// <summary>
        /// 是否等待所有对话显示完毕。
        /// true（默认）：阻塞执行，等所有台词播完才执行下一步。
        /// false：启动对话后立即返回，对话在后台进行。
        /// </summary>
        [Export] public bool WaitForCompletion { get; set; } = false;

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] DialogueStep 开始，Lines数量={Lines?.Count ?? 0}, WaitForCompletion={WaitForCompletion}");
            if (ctx.IsSkipping)
            {
                GD.Print("[Cutscene] DialogueStep 跳过");
                return;
            }

            var panel = ctx.Manager.DialoguePanel;
            if (panel == null)
            {
                GD.PrintErr("[Cutscene] DialogueStep: DialoguePanelPath 未设置或节点不存在，对话无法显示");
                return;
            }

            panel.Show();
            GD.Print("[Cutscene] DialogueStep: 对话框已显示");

            if (!WaitForCompletion)
            {
                // 异步模式：启动对话后立即返回，后台进行
                _ = PlayDialoguesAsync(ctx, panel);
                GD.Print("[Cutscene] DialogueStep 异步执行（WaitForCompletion=false），对话在后台进行");
                return;
            }

            // 阻塞模式：等待所有对话完成
            await PlayAllDialogues(ctx, panel);
            GD.Print("[Cutscene] DialogueStep 完成");
        }

        /// <summary>
        /// 阻塞模式：显示所有对话
        /// </summary>
        private async Task PlayAllDialogues(CutsceneContext ctx, CutsceneDialoguePanel panel)
        {
            int i = 0;
            foreach (var line in Lines)
            {
                if (ctx.IsSkipping) break;
                if (line == null) { i++; continue; }
                GD.Print($"[Cutscene] DialogueStep: 显示第 {i} 条台词 — Speaker={line.Speaker}");
                await panel.ShowLine(line, ctx);
                i++;
            }

            panel.HidePanel();
        }

        /// <summary>
        /// 异步模式：在后台显示所有对话，监听skip
        /// </summary>
        private async Task PlayDialoguesAsync(CutsceneContext ctx, CutsceneDialoguePanel panel)
        {
            int i = 0;
            foreach (var line in Lines)
            {
                if (ctx.IsSkipping) break;
                if (line == null) { i++; continue; }
                GD.Print($"[Cutscene] DialogueStep 后台: 显示第 {i} 条台词 — Speaker={line.Speaker}");
                await panel.ShowLine(line, ctx);
                i++;
            }

            panel.HidePanel();
            GD.Print("[Cutscene] DialogueStep 后台对话完成");
        }
    }
}
