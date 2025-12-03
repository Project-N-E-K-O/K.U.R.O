using Godot;

namespace Kuros.Items.Attributes
{
    /// <summary>
    /// 运算模式，描述不同属性值如何叠加。
    /// </summary>
    public enum ItemAttributeOperation
    {
        Add = 0,
        Multiply = 1,
        Override = 2
    }

    /// <summary>
    /// 决定属性值在多数量物品下的缩放方式。
    /// </summary>
    public enum ItemAttributeScalingMode
    {
        PerItem = 0,
        PerStack = 1,
        Constant = 2
    }

    /// <summary>
    /// 可在 Godot 编辑器中配置的物品属性条目。
    /// </summary>
    [GlobalClass]
    public partial class ItemAttributeEntry : Resource
    {
        [Export] public string AttributeId { get; set; } = string.Empty;

        [Export(PropertyHint.Range, "-999999,999999,0.1")]
        public float Value { get; set; } = 0f;

        [Export] public ItemAttributeOperation Operation { get; set; } = ItemAttributeOperation.Add;

        [Export] public ItemAttributeScalingMode Scaling { get; set; } = ItemAttributeScalingMode.PerItem;

        [Export(PropertyHint.MultilineText)]
        public string Notes { get; set; } = string.Empty;

        internal ItemAttributeValue ToRuntimeValue()
        {
            return new ItemAttributeValue(AttributeId, Value, Operation, Scaling);
        }
    }
}

