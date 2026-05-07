using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 过场对话框的抽象基类，继承 Control。
    /// 创建自定义对话框场景时，根节点脚本继承此类并实现 ShowLine。
    /// </summary>
    [GlobalClass]
    public abstract partial class CutsceneDialoguePanel : Control
    {
        /// <summary>
        /// 显示单条台词。实现应处理：
        /// 1. 逐字显示文字；
        /// 2. 首次确认按键 → 立即显示全部文字；
        /// 3. 二次确认按键 / ctx.IsSkipping → 返回（进入下一条台词）。
        /// </summary>
        public abstract Task ShowLine(DialogueLine line, CutsceneContext ctx);

        /// <summary>隐藏对话框（避免与 Control.Hide() 同名冲突）。</summary>
        public virtual void HidePanel() => Hide();
    }
}
