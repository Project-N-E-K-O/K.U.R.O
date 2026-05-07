using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 默认对话框实现。
    /// 场景结构示例：
    ///   DefaultCutsceneDialoguePanel (Control, CanvasLayer)
    ///     └─ Panel
    ///          ├─ SpeakerLabel  (Label)
    ///          └─ TextLabel     (RichTextLabel)
    ///
    /// 将此脚本挂到对话框场景根节点即可直接使用。
    /// NodePath 可在 Inspector 中修改。
    /// </summary>
    [GlobalClass]
    public partial class DefaultCutsceneDialoguePanel : CutsceneDialoguePanel
    {
        [Export] public NodePath SpeakerLabelPath { get; set; } = new NodePath("Panel/SpeakerLabel");
        [Export] public NodePath TextLabelPath    { get; set; } = new NodePath("Panel/TextLabel");

        /// <summary>确认/继续 的输入动作名称。</summary>
        [Export] public string ConfirmActionName { get; set; } = "ui_accept";

        private Label?         _speakerLabel;
        private RichTextLabel? _textLabel;

        public override void _Ready()
        {
            _speakerLabel = GetNodeOrNull<Label>(SpeakerLabelPath);
            _textLabel    = GetNodeOrNull<RichTextLabel>(TextLabelPath);
            HidePanel();
        }

        public override async Task ShowLine(DialogueLine line, CutsceneContext ctx)
        {
            if (_speakerLabel != null)
                _speakerLabel.Text = line.Speaker;

            if (_textLabel == null) return;

            _textLabel.Text = line.Text;
            int totalChars  = _textLabel.GetTotalCharacterCount();
            _textLabel.VisibleCharacters = 0;

            // ── 阶段 1：逐字显示 ────────────────────────────────────────────
            if (line.RevealSpeed > 0f && totalChars > 0)
            {
                float startMs = Time.GetTicksMsec();

                while (_textLabel.VisibleCharacters < totalChars && !ctx.IsSkipping)
                {
                    await ctx.NextFrame();

                    // 第一次确认 → 立即显示全部
                    if (Input.IsActionJustPressed(ConfirmActionName))
                        break;

                    float elapsed = (Time.GetTicksMsec() - startMs) / 1000f;
                    _textLabel.VisibleCharacters = Mathf.Min(
                        (int)(elapsed * line.RevealSpeed), totalChars);
                }
            }

            // 确保全部显示
            _textLabel.VisibleCharacters = -1;

            if (ctx.IsSkipping) return;

            // ── 阶段 2：等待玩家确认进入下一条台词 ─────────────────────────
            while (!ctx.IsSkipping)
            {
                await ctx.NextFrame();
                if (Input.IsActionJustPressed(ConfirmActionName)) break;
            }
        }
    }
}
