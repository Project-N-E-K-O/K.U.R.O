using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Builds
{
    /// <summary>
    /// 守护 3 级效果：每 8 秒充能一次（最多 1 次），消耗 1 次充能来抵消下次伤害。
    /// </summary>
    [GlobalClass]
    public partial class BuildGuardLevel3Effect : ActorEffect, ICooldownEffect
    {
        [Export(PropertyHint.Range, "0,60,0.1")] public float ChargeInterval { get; set; } = 8.0f; // 充能间隔时间（秒）
        [Export] public int MaxCharges { get; set; } = 1;   // 最大充能次数

        public int CurrentCharges { get; private set; }
        public float ChargeTimer { get; private set; }

        public float CooldownDuration => ChargeInterval;
        public float CooldownRemaining => CurrentCharges >= MaxCharges ? 0f : Mathf.Max(0f, ChargeInterval - ChargeTimer);
        public bool IsOnCooldown => CurrentCharges < MaxCharges;

        private bool _subscribed;

        public BuildGuardLevel3Effect()
        {
            EffectId = "build_guard_level3";
            DisplayName = "守护III";
            Description = "每8秒充能1次（最多1次），消耗1次充能来抵消下次伤害";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null || _subscribed)
            {
                GD.Print("[BuildGuardLevel3Effect] OnApply skipped: Actor is null or already subscribed");
                return;
            }

            CurrentCharges = 0;
            ChargeTimer = 0f;

            // 订阅伤害拦截事件，使用充能来抵消伤害
            Actor.DamageIntercepted += OnDamageIntercepted;
            _subscribed = true;
            GD.Print("[BuildGuardLevel3Effect] OnApply called, damage absorption shield activated");
        }

        protected override void OnTick(double delta)
        {
            base.OnTick(delta);

            if (CurrentCharges >= MaxCharges)
            {
                return;
            }

            ChargeTimer += (float)delta;
            if (ChargeTimer >= ChargeInterval)
            {
                CurrentCharges = Mathf.Min(CurrentCharges + 1, MaxCharges);
                ChargeTimer = 0f;
                GD.Print($"[BuildGuardLevel3Effect] Charged! Current charges: {CurrentCharges}/{MaxCharges}");
            }
        }

        public override void OnRemoved()
        {
            if (_subscribed && Actor != null)
            {
                Actor.DamageIntercepted -= OnDamageIntercepted;
            }

            _subscribed = false;
            CurrentCharges = 0;
            ChargeTimer = 0f;
            GD.Print("[BuildGuardLevel3Effect] OnRemoved called");
            base.OnRemoved();
        }

        /// <summary>
        /// 伤害拦截回调：使用充能来完全抵消伤害。
        /// </summary>
        private bool OnDamageIntercepted(GameActor.DamageEventArgs args)
        {
            // 确保只处理针对自身的伤害
            if (Actor == null || args.Target != Actor || args.Damage <= 0)
            {
                return false;
            }

            // 如果有充能，则使用一次充能来完全拦截伤害
            if (CurrentCharges > 0)
            {
                CurrentCharges = Mathf.Max(0, CurrentCharges - 1);
                ChargeTimer = 0f;
                args.Damage = 0;
                args.IsBlocked = true;

                if (Actor is Kuros.Actors.Heroes.MainCharacter mainCharacter)
                {
                    mainCharacter.ClearHitInvincibility();
                    mainCharacter.StartHitInvincibility(0.05f);
                }

                GD.Print($"[BuildGuardLevel3Effect] Damage completely absorbed and blocked! Remaining charges: {CurrentCharges}/{MaxCharges}");
                return false;
            }

            return false;
        }
    }
}
