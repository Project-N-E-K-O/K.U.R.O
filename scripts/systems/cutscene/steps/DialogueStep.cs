using System.Threading.Tasks;
using Godot;
using Godot.Collections;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 显示一组台词。需要 CutsceneManager 中设置 DialoguePanelPath。
    /// </summary>
    [GlobalClass]
    public partial class DialogueStep : CutsceneStep
    {
        [Export] public Array<DialogueLine> Lines { get; set; } = new();

        public override async Task Execute(CutsceneContext ctx)
        {
            GD.Print($"[Cutscene] DialogueStep 开始，Lines数量={Lines?.Count ?? 0}");
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
            GD.Print("[Cutscene] DialogueStep 完成");
        }
    }
}
