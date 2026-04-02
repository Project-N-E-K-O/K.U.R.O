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
        [Export]
        public string AttributeId
        {
            get => _attributeId;
            set
            {
                _attributeId = value ?? string.Empty;
                // Re-apply guard when id changes to attack_power.
                Value = _value;
            }
        }

        [Export(PropertyHint.Range, "0,999,0.1")]
        public float Value
        {
            get => _value;
            set
            {
                float safe = float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
                if (string.Equals(_attributeId, "attack_power", System.StringComparison.OrdinalIgnoreCase))
                {
                    safe = Mathf.Clamp(safe, 0f, 999f);
                }

                _value = safe;
            }
        }

        [Export] public ItemAttributeOperation Operation { get; set; } = ItemAttributeOperation.Add;

        [Export] public ItemAttributeScalingMode Scaling { get; set; } = ItemAttributeScalingMode.PerItem;

        [Export(PropertyHint.MultilineText)]
        public string Notes { get; set; } = string.Empty;

        private string _attributeId = string.Empty;
        private float _value = 0f;

        public override bool _Set(StringName property, Variant value)
        {
            string key = property.ToString();
            if (key == nameof(Value) || key == "value")
            {
                Value = value.VariantType == Variant.Type.Float || value.VariantType == Variant.Type.Int
                    ? value.AsSingle()
                    : 0f;
                return true;
            }

            if (key == nameof(AttributeId) || key == "attribute_id")
            {
                AttributeId = value.VariantType == Variant.Type.String ? value.AsString() : string.Empty;
                return true;
            }

            return base._Set(property, value);
        }

        internal ItemAttributeValue ToRuntimeValue()
        {
            return new ItemAttributeValue(AttributeId, Value, Operation, Scaling);
        }
    }
}

