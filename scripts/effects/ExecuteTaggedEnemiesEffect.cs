using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 框架：根据标签执行秒杀逻辑（后续细化）。
    /// 当前仅提供配置占位，后续可接入实际敌人标签系统。
    /// </summary>
    [GlobalClass]
    public partial class ExecuteTaggedEnemiesEffect : ActorEffect
    {
        [Export] public string TargetTagId { get; set; } = string.Empty;
        [Export(PropertyHint.Range, "0,5,0.1")] public float ExecutionDelay { get; set; } = 0f;

        protected override void OnApply()
        {
            base.OnApply();
            // 未来将根据 TargetTagId 查找敌人并执行秒杀逻辑。
            Controller?.RemoveEffect(this);
        }
    }
}

