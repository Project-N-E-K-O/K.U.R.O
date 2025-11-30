using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core.Effects;

namespace Kuros.Core.Stats
{
    /// <summary>
    /// 用于在 tscn 上配置的角色属性集合，会在初始化时注入 Actor。
    /// </summary>
    [GlobalClass]
    public partial class CharacterStatProfile : Resource
    {
        [Export] public Godot.Collections.Array<StatModifier> BaseModifiers { get; set; } = new();
        [Export] public Godot.Collections.Array<PackedScene> AttachedEffects { get; set; } = new();

        public IEnumerable<StatModifier> GetModifiers() => BaseModifiers;

        public IEnumerable<PackedScene> GetAttachedEffectScenes() => AttachedEffects;
    }
}

