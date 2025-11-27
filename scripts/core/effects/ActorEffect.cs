using Godot;
using System;
using Kuros.Core;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 角色身上的 Buff/Debuff 基类。
    /// </summary>
    public abstract partial class ActorEffect : Node
    {
        [ExportGroup("Metadata")]
        [Export] public string EffectId = Guid.NewGuid().ToString();
        [Export] public string DisplayName = "Effect";
        [Export(PropertyHint.MultilineText)] public string Description = "";
        [Export] public bool IsBuff = true;

        [ExportGroup("Timing")]
        [Export(PropertyHint.Range, "0,600,0.1")] public float Duration = 5.0f;
        [Export(PropertyHint.Range, "0,10,1")] public int MaxStacks = 1;

        protected GameActor Actor { get; private set; } = null!;
        protected EffectController Controller { get; private set; } = null!;

        private float _elapsed = 0f;
        private int _currentStacks = 0;

        public bool IsExpired => Duration > 0 && _elapsed >= Duration;
        public int CurrentStacks => _currentStacks;

        public void Initialize(GameActor actor, EffectController controller)
        {
            Actor = actor;
            Controller = controller;
            _currentStacks = 1;
            _elapsed = 0f;
            OnApply();
        }

        public void Refresh(int additionalStacks = 1)
        {
            _currentStacks = Mathf.Clamp(_currentStacks + additionalStacks, 1, Math.Max(MaxStacks, 1));
            _elapsed = 0f;
            OnStackRefreshed();
        }

        public void Tick(double delta)
        {
            _elapsed += (float)delta;
            OnTick(delta);

            if (IsExpired)
            {
                Controller.RemoveEffect(this);
            }
        }

        public virtual void OnRemoved()
        {
            OnExpire();
        }

        protected virtual void OnApply() { }
        protected virtual void OnStackRefreshed() { }
        protected virtual void OnTick(double delta) { }
        protected virtual void OnExpire() { }
    }
}

