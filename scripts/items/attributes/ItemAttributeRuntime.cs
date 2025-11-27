using System;
using System.Collections;
using System.Collections.Generic;

namespace Kuros.Items.Attributes
{
    /// <summary>
    /// 物品属性的运行时定义，保存基础数值与缩放模式。
    /// </summary>
    public readonly struct ItemAttributeValue
    {
        public string AttributeId { get; }
        public float Magnitude { get; }
        public ItemAttributeOperation Operation { get; }
        public ItemAttributeScalingMode Scaling { get; }

        internal ItemAttributeValue(string attributeId, float magnitude, ItemAttributeOperation operation, ItemAttributeScalingMode scaling)
        {
            AttributeId = attributeId;
            Magnitude = magnitude;
            Operation = operation;
            Scaling = scaling;
        }

        public ResolvedItemAttribute Resolve(int quantity)
        {
            if (string.IsNullOrEmpty(AttributeId))
            {
                return ResolvedItemAttribute.Empty;
            }

            int clampedQuantity = Math.Max(0, quantity);
            float resolved = Scaling switch
            {
                ItemAttributeScalingMode.PerItem => Magnitude * clampedQuantity,
                ItemAttributeScalingMode.PerStack => clampedQuantity > 0 ? Magnitude : 0f,
                ItemAttributeScalingMode.Constant => Magnitude,
                _ => Magnitude
            };

            return new ResolvedItemAttribute(AttributeId, resolved, Operation);
        }
    }

    /// <summary>
    /// 已解析好的属性值，包含最终的数值及运算信息。
    /// </summary>
    public readonly struct ResolvedItemAttribute
    {
        public static readonly ResolvedItemAttribute Empty = new(string.Empty, 0f, ItemAttributeOperation.Add);

        public string AttributeId { get; }
        public float Value { get; }
        public ItemAttributeOperation Operation { get; }
        public bool IsValid => !string.IsNullOrEmpty(AttributeId);

        public ResolvedItemAttribute(string attributeId, float value, ItemAttributeOperation operation)
        {
            AttributeId = attributeId;
            Value = value;
            Operation = operation;
        }
    }

    /// <summary>
    /// 保存物品所有属性的集合。
    /// </summary>
    public sealed class ItemAttributeSet : IEnumerable<KeyValuePair<string, ItemAttributeValue>>
    {
        private readonly Dictionary<string, ItemAttributeValue> _values;

        public static ItemAttributeSet Empty { get; } = new ItemAttributeSet(new ItemAttributeEntry[0]);

        public ItemAttributeSet(IEnumerable<ItemAttributeEntry> entries)
        {
            _values = new Dictionary<string, ItemAttributeValue>(StringComparer.Ordinal);

            if (entries == null)
            {
                return;
            }

            foreach (var entry in entries)
            {
                if (entry == null) continue;
                if (string.IsNullOrWhiteSpace(entry.AttributeId)) continue;

                _values[entry.AttributeId] = entry.ToRuntimeValue();
            }
        }

        public bool TryGetValue(string attributeId, out ItemAttributeValue value)
        {
            if (string.IsNullOrWhiteSpace(attributeId))
            {
                value = default;
                return false;
            }

            return _values.TryGetValue(attributeId, out value);
        }

        public IEnumerable<ItemAttributeValue> Values => _values.Values;

        public IEnumerator<KeyValuePair<string, ItemAttributeValue>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// 属性聚合器，可用于把多个属性贡献合并为最终结果。
    /// </summary>
    public sealed class ItemAttributeAccumulator
    {
        private float _additive;
        private float _multiplicative = 1f;
        private float? _overrideValue;

        public bool HasContribution { get; private set; }

        public void Accumulate(in ResolvedItemAttribute attribute)
        {
            if (!attribute.IsValid)
            {
                return;
            }

            HasContribution = true;

            switch (attribute.Operation)
            {
                case ItemAttributeOperation.Add:
                    _additive += attribute.Value;
                    break;
                case ItemAttributeOperation.Multiply:
                    _multiplicative *= attribute.Value;
                    break;
                case ItemAttributeOperation.Override:
                    _overrideValue = attribute.Value;
                    break;
            }
        }

        public float Resolve(float baseValue = 0f)
        {
            if (!HasContribution)
            {
                return baseValue;
            }

            if (_overrideValue.HasValue)
            {
                return _overrideValue.Value;
            }

            return (baseValue + _additive) * _multiplicative;
        }
    }
}

