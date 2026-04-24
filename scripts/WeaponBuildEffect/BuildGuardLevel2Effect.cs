using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds
{
    /// <summary>
    /// 守护 2 级效果：玩家受到伤害后的无敌时间增加 0.5 秒。
    /// </summary>
    [GlobalClass]
    public partial class BuildGuardLevel2Effect : ActorEffect
    {
        [Export(PropertyHint.Range, "0,5,0.01")] public float InvincibilityDurationBonus { get; set; } = 0.5f;  // 无敌时间增加量（秒）

        private bool _subscribed;
        private Kuros.Actors.Heroes.MainCharacter? _mainCharacter;

        public BuildGuardLevel2Effect()
        {
            EffectId = "build_guard_level2";
            DisplayName = "守护II";
            Description = "玩家受到伤害后的无敌时间增加0.5秒";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null || _subscribed)
            {
                GD.Print("[BuildGuardLevel2Effect] OnApply skipped: Actor is null or already subscribed");
                return;
            }

            _mainCharacter = Actor as Kuros.Actors.Heroes.MainCharacter;
            if (_mainCharacter == null)
            {
                GD.Print("[BuildGuardLevel2Effect] OnApply failed: Actor is not MainCharacter");
                return;
            }

            // 订阅伤害拦截事件，在伤害应用时增加无敌时间
            Actor.DamageIntercepted += OnDamageIntercepted;
            _subscribed = true;
            GD.Print("[BuildGuardLevel2Effect] OnApply called, invincibility duration bonus activated");
        }

        public override void OnRemoved()
        {
            if (_subscribed && Actor != null)
            {
                Actor.DamageIntercepted -= OnDamageIntercepted;
            }

            _subscribed = false;
            _mainCharacter = null;
            GD.Print("[BuildGuardLevel2Effect] OnRemoved called");
            base.OnRemoved();
        }

        /// <summary>
        /// 伤害拦截回调：增加无敌时间。
        /// 当玩家受到伤害时，在伤害应用后增加无敌时间。
        /// </summary>
        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            // 确保只处理针对自身的伤害且伤害大于 0
            if (_mainCharacter == null || args.Target != Actor || args.Damage <= 0)
            {
                return false;
            }

            // 在下一帧应用增加的无敌时间
            // 使用增强的无敌时间调用 StartHitInvincibility
            float enhancedDuration = _mainCharacter.HitInvincibilityDuration + InvincibilityDurationBonus;
            _mainCharacter.CallDeferred(nameof(_mainCharacter.StartHitInvincibility), enhancedDuration);
            
            GD.Print($"[BuildGuardLevel2Effect] Scheduled enhanced invincibility: {enhancedDuration}s");

            return false;
        }
    }
}
