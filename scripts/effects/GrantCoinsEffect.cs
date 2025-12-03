using Godot;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 即时给予金币的效果，适用于拾取或损坏触发的奖励。
    /// </summary>
    [GlobalClass]
    public partial class GrantCoinsEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "1,999999,1")]
        public int CoinAmount { get; set; } = 10;

        protected override void OnApply()
        {
            base.OnApply();

            if (CoinAmount <= 0 || Actor == null)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            if (Actor is global::SamplePlayer player)
            {
                player.AddGold(CoinAmount);
            }

            Controller?.RemoveEffect(this);
        }
    }
}

