using System.Threading.Tasks;
using Godot;

namespace Kuros.Systems.Cutscene
{
    /// <summary>
    /// 过场动画的单个步骤基类，所有步骤（等待、对话、镜头移动等）均继承此类。
    /// </summary>
    [GlobalClass]
    public abstract partial class CutsceneStep : Resource
    {
        public abstract Task Execute(CutsceneContext ctx);
    }
}
