using Godot;
using System.Collections.Generic;
using Kuros.Core;

namespace Kuros.Core.Effects
{
    /// <summary>
    /// 管理角色身上的所有效果，负责生命周期更新。
    /// </summary>
    public partial class EffectController : Node
    {
        private readonly List<ActorEffect> _effects = new();
        private GameActor? _actor = null;

        public override void _Ready()
        {
            _actor = GetParent<GameActor>();
            if (_actor == null)
            {
                GD.PushError("EffectController must be a child of GameActor.");
                QueueFree();
            }
        }

        public override void _Process(double delta)
        {
            for (int i = _effects.Count - 1; i >= 0; i--)
            {
                _effects[i].Tick(delta);
            }
        }

        public ActorEffect? GetEffect(string effectId)
        {
            return _effects.Find(effect => effect.EffectId == effectId);
        }

        public void AddEffect(ActorEffect effect)
        {
            if (effect == null) return;

            if (_actor == null) return;

            var existing = GetEffect(effect.EffectId);
            if (existing != null)
            {
                existing.Refresh();
                return;
            }

            AddChild(effect);
            _effects.Add(effect);
            effect.Initialize(_actor, this);
        }

        public void RemoveEffect(ActorEffect effect)
        {
            if (!_effects.Remove(effect)) return;
            effect.OnRemoved();
            effect.QueueFree();
        }

        public void ClearAll()
        {
            foreach (var effect in _effects)
            {
                effect.OnRemoved();
                effect.QueueFree();
            }

            _effects.Clear();
        }
    }
}

