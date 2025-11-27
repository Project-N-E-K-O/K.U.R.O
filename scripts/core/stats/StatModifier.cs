using System;
using Godot;

namespace Kuros.Core.Stats
{
    public enum StatOperation
    {
        Add = 0,
        Multiply = 1
    }

    /// <summary>
    /// 描述对单个属性的数学操作。
    /// </summary>
    [Serializable]
    public partial class StatModifier : Resource
    {
        [Export] public string StatId { get; set; } = string.Empty;

        [Export(PropertyHint.Range, "-9999,9999,0.1")]
        public float Value { get; set; } = 0f;

        [Export] public StatOperation Operation { get; set; } = StatOperation.Add;
    }
}

