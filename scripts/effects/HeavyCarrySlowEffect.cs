using Godot;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 负重减速效果：将移动速度乘以 SpeedMultiplierPerStack，叠加时按乘算累乘。
    ///
    /// 减速**走 SharedSpeedSlowManager**（与区域减速/连线减速共用同一份记录），不再自己快照原始速度：
    /// 自己快照的话，和别的减速源重叠时会互相覆盖——例如"负重中走进减速区、出来前先丢掉家具"，
    /// 后移除的那一方会把速度写回自己那份**过期快照**，原始速度就永久丢了。
    /// </summary>
    [GlobalClass]
    public partial class HeavyCarrySlowEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0.1,1,0.01")]
        public float SpeedMultiplierPerStack { get; set; } = 0.7f;

        /// <summary>当前已向共享管理器登记的乘数（层数变化时要先撤掉旧的那一档）。</summary>
        private float _registeredMultiplier = 1f;

        protected override void OnApply()
        {
            base.OnApply();
            Recalculate();
        }

        protected override void OnStackRefreshed()
        {
            base.OnStackRefreshed();
            Recalculate();
        }

        public override void OnRemoved()
        {
            // 只撤自己的那一档：其他减速源还在时继续生效，最后一个撤掉时才由管理器还原原始速度
            if (Actor != null)
                SharedSpeedSlowManager.Remove(Actor, _registeredMultiplier);
            _registeredMultiplier = 1f;
            base.OnRemoved();
        }

        private void Recalculate()
        {
            if (Actor == null) return;

            float totalMultiplier = Mathf.Pow(SpeedMultiplierPerStack, Mathf.Max(1, CurrentStacks));

            if (_registeredMultiplier < 1f)
                SharedSpeedSlowManager.Remove(Actor, _registeredMultiplier);

            SharedSpeedSlowManager.Apply(Actor, totalMultiplier);
            _registeredMultiplier = totalMultiplier;
        }
    }
}

