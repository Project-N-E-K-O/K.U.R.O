using System;
using System.Collections.Generic;
using Godot;
using Kuros.Items.Attributes;
using Kuros.Items.Effects;
using Kuros.Items.Weapons;

namespace Kuros.Items
{
    /// <summary>
    /// 基础物品定义资源，用于描述可被背包、装备栏等系统引用的物品模板。
    /// </summary>
    public partial class ItemDefinition : Resource
    {
        [ExportGroup("Identity")]
        [Export] public string ItemId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "Unnamed Item";
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;

        [ExportGroup("Presentation")]
        [Export] public Texture2D? Icon { get; set; }
        [Export] public string Category { get; set; } = "General";
        [Export] public Godot.Collections.Array<string> Tags
        {
            get => _tags;
            set
            {
                _tags = value ?? new();
                _tagCache = null;
            }
        }

        [ExportGroup("Stacking")]
        [Export(PropertyHint.Range, "1,9999,1")] public int MaxStackSize { get; set; } = 99;

        [ExportGroup("Attributes")]
        [Export] public Godot.Collections.Array<ItemAttributeEntry> AttributeEntries
        {
            get => _attributeEntries;
            set
            {
                _attributeEntries = value ?? new();
                _attributeCache = null;
            }
        }

        [ExportGroup("Effects")]
        [Export] public Godot.Collections.Array<ItemEffectEntry> EffectEntries
        {
            get => _effectEntries;
            set => _effectEntries = value ?? new();
        }

        [ExportGroup("Weapon")]
        [Export] public Godot.Collections.Array<Resource> WeaponSkillResources
        {
            get => _weaponSkillResources;
            set => _weaponSkillResources = value ?? new();
        }

        private Godot.Collections.Array<string> _tags = new();
        private HashSet<string>? _tagCache;
        private Godot.Collections.Array<ItemAttributeEntry> _attributeEntries = new();
        private Godot.Collections.Array<ItemEffectEntry> _effectEntries = new();
        private Godot.Collections.Array<Resource> _weaponSkillResources = new();
        private ItemAttributeSet? _attributeCache;

        private const string DefaultWorldSceneDirectory = "res://scenes/properties/";

        [ExportGroup("World")]
        [Export(PropertyHint.File, "*.tscn")] public string WorldScenePath { get; set; } = string.Empty;

        public bool HasTag(string tag) => TagSet.Contains(tag);
        public bool HasAnyTag(IEnumerable<string> tagIds)
        {
            if (tagIds == null) return false;
            foreach (var id in tagIds)
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (HasTag(id)) return true;
            }
            return false;
        }

        public IReadOnlyCollection<string> GetTags() => TagSet;

        private ItemAttributeSet AttributeSet => _attributeCache ??= new ItemAttributeSet(_attributeEntries);
        private HashSet<string> TagSet => _tagCache ??= BuildTagSet();

        public IEnumerable<ItemAttributeValue> GetAttributeValues()
        {
            return AttributeSet.Values;
        }

        public bool TryResolveAttribute(string attributeId, int quantity, out ResolvedItemAttribute attribute)
        {
            if (AttributeSet.TryGetValue(attributeId, out var attributeValue))
            {
                attribute = attributeValue.Resolve(quantity);
                return attribute.IsValid;
            }

            attribute = ResolvedItemAttribute.Empty;
            return false;
        }

        public Dictionary<string, float> GetAttributeSnapshot(int quantity = 1)
        {
            var result = new Dictionary<string, float>();
            foreach (var attributeValue in AttributeSet.Values)
            {
                var resolved = attributeValue.Resolve(quantity);
                if (resolved.IsValid)
                {
                    result[resolved.AttributeId] = resolved.Value;
                }
            }

            return result;
        }

        public IEnumerable<ItemEffectEntry> GetEffectEntries(ItemEffectTrigger trigger)
        {
            foreach (var entry in _effectEntries)
            {
                if (entry == null || entry.EffectScene == null) continue;
                if (entry.Trigger != trigger) continue;
                yield return entry;
            }
        }

        public IEnumerable<WeaponSkillDefinition> GetWeaponSkillDefinitions()
        {
            foreach (var skillResource in _weaponSkillResources)
            {
                if (skillResource is WeaponSkillDefinition skill)
                {
                    yield return skill;
                }
            }
        }

        private HashSet<string> BuildTagSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in _tags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                set.Add(tag.Trim());
            }

            return set;
        }

        public string ResolveWorldScenePath()
        {
            if (!string.IsNullOrWhiteSpace(WorldScenePath))
            {
                return WorldScenePath;
            }

            if (string.IsNullOrWhiteSpace(ItemId))
            {
                return string.Empty;
            }

            return $"{DefaultWorldSceneDirectory}{ItemId}.tscn";
        }
    }
}

