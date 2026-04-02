using System;
using Godot;
using Kuros.Items;
using Kuros.Items.Attributes;
using Kuros.Items.Weapons;

namespace Kuros.Tools
{
    /// <summary>
    /// Editor helper for weapon authoring:
    /// 1) Create/update ItemDefinition + WeaponSkillDefinition in one click.
    /// 2) Validate all weapon item resources to catch broken links early.
    /// </summary>
    [Tool]
    [GlobalClass]
    public partial class WeaponConfigToolkit : Node
    {
        [ExportGroup("Create Weapon Package")]
        [Export] public string ItemId { get; set; } = "Weapon_Stab_New";
        [Export] public string ItemDisplayName { get; set; } = "New Weapon";
        [Export(PropertyHint.MultilineText)] public string ItemDescription { get; set; } = "";
        [Export(PropertyHint.File, "*.tscn")] public string WeaponScenePath { get; set; } = "res://scenes/weapons/Weapon_Stab_umbrella.tscn";
        [Export] public Texture2D? ItemIcon { get; set; }
        [Export] public string ItemCategory { get; set; } = "Weapon";
        [Export(PropertyHint.Range, "0,999999,0.1")] public float WeaponAttackPower { get; set; } = 3f;
        [Export] public bool OverwriteAttackPowerIfExists { get; set; } = true;

        [Export] public string SkillId { get; set; } = "weapon_stab_new";
        [Export] public string SkillDisplayName { get; set; } = "New Weapon Skill";
        [Export] public string SkillAnimationName { get; set; } = "attack_thrust";
        [Export(PropertyHint.Range, "0,30,0.1")] public float SkillCooldownSeconds { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0,5,0.1")] public float SkillDamageMultiplier { get; set; } = 1f;
        [Export] public string SkillActivationAction { get; set; } = "weapon_skill_block";

        [Export(PropertyHint.Dir)] public string ItemOutputDirectory { get; set; } = "res://resources/items";
        [Export(PropertyHint.Dir)] public string SkillOutputDirectory { get; set; } = "res://resources/items/skills";

        [Export]
        public bool CreateOrUpdateWeaponPackage
        {
            get => _createOrUpdateWeaponPackage;
            set
            {
                if (_createOrUpdateWeaponPackage == value) return;
                _createOrUpdateWeaponPackage = value;
                if (value)
                {
                    RunCreateOrUpdateWeaponPackage();
                    _createOrUpdateWeaponPackage = false;
                    NotifyPropertyListChanged();
                }
            }
        }

        [ExportGroup("Validation")]
        [Export(PropertyHint.Dir)] public string ValidateItemsDirectory { get; set; } = "res://resources/items";

        [Export]
        public bool ValidateAllWeaponItems
        {
            get => _validateAllWeaponItems;
            set
            {
                if (_validateAllWeaponItems == value) return;
                _validateAllWeaponItems = value;
                if (value)
                {
                    RunValidateAllWeaponItems();
                    _validateAllWeaponItems = false;
                    NotifyPropertyListChanged();
                }
            }
        }

        [Export]
        public bool RepairAllWeaponAttackPowerValues
        {
            get => _repairAllWeaponAttackPowerValues;
            set
            {
                if (_repairAllWeaponAttackPowerValues == value) return;
                _repairAllWeaponAttackPowerValues = value;
                if (value)
                {
                    RunRepairAllWeaponAttackPowerValues();
                    _repairAllWeaponAttackPowerValues = false;
                    NotifyPropertyListChanged();
                }
            }
        }

        private bool _createOrUpdateWeaponPackage;
        private bool _validateAllWeaponItems;
        private bool _repairAllWeaponAttackPowerValues;

        private void RunCreateOrUpdateWeaponPackage()
        {
            if (!Engine.IsEditorHint())
            {
                GD.PrintErr("[WeaponConfigToolkit] Create/update can only be run in editor.");
                return;
            }

            string normalizedItemId = (ItemId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedItemId))
            {
                GD.PrintErr("[WeaponConfigToolkit] ItemId is required.");
                return;
            }

            string normalizedSkillId = (SkillId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedSkillId))
            {
                normalizedSkillId = ToSkillId(normalizedItemId);
                SkillId = normalizedSkillId;
            }

            string itemPath = BuildResourcePath(ItemOutputDirectory, $"{normalizedItemId}.tres");
            string skillFileName = BuildSkillFileName(normalizedItemId);
            string skillPath = BuildResourcePath(SkillOutputDirectory, skillFileName);

            EnsureDirectoryExists(ItemOutputDirectory);
            EnsureDirectoryExists(SkillOutputDirectory);

            var loadedSkillResource = ResourceLoader.Load<Resource>(skillPath, string.Empty, ResourceLoader.CacheMode.Ignore);
            var skill = loadedSkillResource as WeaponSkillDefinition;
            if (skill == null)
            {
                if (loadedSkillResource != null)
                {
                    GD.Print($"[WeaponConfigToolkit] Skill file exists but is not WeaponSkillDefinition, recreating: {skillPath}");
                }
                skill = new WeaponSkillDefinition();
            }
            skill.SkillId = normalizedSkillId;
            skill.DisplayName = string.IsNullOrWhiteSpace(SkillDisplayName) ? normalizedItemId : SkillDisplayName;
            skill.AnimationName = SkillAnimationName ?? string.Empty;
            skill.CooldownSeconds = SkillCooldownSeconds;
            skill.DamageMultiplier = SkillDamageMultiplier;
            skill.ActivationAction = SkillActivationAction ?? string.Empty;

            var skillSaveResult = ResourceSaver.Save(skill, skillPath);
            if (skillSaveResult != Error.Ok)
            {
                GD.PrintErr($"[WeaponConfigToolkit] Failed to save skill resource: {skillPath}, error={skillSaveResult}");
                return;
            }

            var loadedItemResource = ResourceLoader.Load<Resource>(itemPath, string.Empty, ResourceLoader.CacheMode.Ignore);
            var item = loadedItemResource as ItemDefinition;
            if (item == null)
            {
                if (loadedItemResource != null)
                {
                    GD.Print($"[WeaponConfigToolkit] Item file exists but is not ItemDefinition, recreating: {itemPath}");
                }
                item = new ItemDefinition();
            }
            item.ItemId = normalizedItemId;
            item.DisplayName = string.IsNullOrWhiteSpace(ItemDisplayName) ? normalizedItemId : ItemDisplayName;
            item.Description = ItemDescription ?? string.Empty;
            item.Category = string.IsNullOrWhiteSpace(ItemCategory) ? "Weapon" : ItemCategory;
            item.MaxStackSize = 1;
            if (ItemIcon != null)
            {
                item.Icon = ItemIcon;
            }

            if (!string.IsNullOrWhiteSpace(WeaponScenePath) && WeaponScenePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                item.WorldScenePath = WeaponScenePath;
            }

            float safeAttackPower = SanitizeAttackPowerValue(WeaponAttackPower);

            var skills = item.WeaponSkillResources ?? new Godot.Collections.Array<Resource>();
            skills.Clear();
            skills.Add(skill);
            item.WeaponSkillResources = skills;
            ApplyAttackPowerAttribute(item, safeAttackPower);
            EnforceNonNegativeAttackPower(item, safeAttackPower);

            var itemSaveResult = ResourceSaver.Save(item, itemPath);
            if (itemSaveResult != Error.Ok)
            {
                GD.PrintErr($"[WeaponConfigToolkit] Failed to save item resource: {itemPath}, error={itemSaveResult}");
                return;
            }

            GD.Print($"[WeaponConfigToolkit] Created/updated weapon package: item={itemPath}, skill={skillPath}");
        }

        private void RunValidateAllWeaponItems()
        {
            if (!Engine.IsEditorHint())
            {
                GD.PrintErr("[WeaponConfigToolkit] Validation can only be run in editor.");
                return;
            }

            int itemCount = 0;
            int errorCount = 0;
            ValidateDirectoryRecursively(NormalizeDirectoryPath(ValidateItemsDirectory), ref itemCount, ref errorCount);
            GD.Print($"[WeaponConfigToolkit] Validation complete. items={itemCount}, errors={errorCount}");
        }

        private void RunRepairAllWeaponAttackPowerValues()
        {
            if (!Engine.IsEditorHint())
            {
                GD.PrintErr("[WeaponConfigToolkit] Repair can only be run in editor.");
                return;
            }

            int fixedCount = 0;
            RepairDirectoryRecursively(NormalizeDirectoryPath(ValidateItemsDirectory), ref fixedCount);
            GD.Print($"[WeaponConfigToolkit] Repair complete. fixedItems={fixedCount}");
        }

        private void ValidateDirectoryRecursively(string directoryPath, ref int itemCount, ref int errorCount)
        {
            var dir = DirAccess.Open(directoryPath);
            if (dir == null)
            {
                GD.PrintErr($"[WeaponConfigToolkit] Cannot open directory: {directoryPath}");
                return;
            }

            dir.ListDirBegin();
            while (true)
            {
                string name = dir.GetNext();
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                if (name == "." || name == "..")
                {
                    continue;
                }

                string childPath = $"{directoryPath}/{name}";
                if (dir.CurrentIsDir())
                {
                    ValidateDirectoryRecursively(childPath, ref itemCount, ref errorCount);
                    continue;
                }

                if (!name.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ValidateItemResource(childPath, ref itemCount, ref errorCount);
            }

            dir.ListDirEnd();
        }

        private void RepairDirectoryRecursively(string directoryPath, ref int fixedCount)
        {
            var dir = DirAccess.Open(directoryPath);
            if (dir == null)
            {
                GD.PrintErr($"[WeaponConfigToolkit] Cannot open directory: {directoryPath}");
                return;
            }

            dir.ListDirBegin();
            while (true)
            {
                string name = dir.GetNext();
                if (string.IsNullOrEmpty(name))
                {
                    break;
                }

                if (name == "." || name == "..")
                {
                    continue;
                }

                string childPath = $"{directoryPath}/{name}";
                if (dir.CurrentIsDir())
                {
                    RepairDirectoryRecursively(childPath, ref fixedCount);
                    continue;
                }

                if (!name.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                RepairItemAttackPower(childPath, ref fixedCount);
            }

            dir.ListDirEnd();
        }

        private void RepairItemAttackPower(string resourcePath, ref int fixedCount)
        {
            var item = ResourceLoader.Load<ItemDefinition>(resourcePath, string.Empty, ResourceLoader.CacheMode.Ignore);
            if (item == null)
            {
                return;
            }

            if (!item.ItemId.StartsWith("Weapon_", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Category, "Weapon", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            bool changed = false;
            if (item.MaxStackSize != 1)
            {
                item.MaxStackSize = 1;
                changed = true;
            }

            if (!TryGetAttackPowerAttribute(item, out var attackPowerEntry))
            {
                ApplyAttackPowerAttribute(item, 3f);
                changed = true;
            }
            else
            {
                float clamped = SanitizeAttackPowerValue(attackPowerEntry.Value);
                if (Mathf.Abs(clamped - attackPowerEntry.Value) > 0.0001f)
                {
                    attackPowerEntry.Value = clamped;
                    changed = true;
                }
            }

            if (!changed)
            {
                return;
            }

            var saveResult = ResourceSaver.Save(item, resourcePath);
            if (saveResult == Error.Ok)
            {
                fixedCount++;
            }
            else
            {
                GD.PrintErr($"[WeaponConfigToolkit] Failed to save repaired item: {resourcePath}, error={saveResult}");
            }
        }

        private void ValidateItemResource(string resourcePath, ref int itemCount, ref int errorCount)
        {
            var item = ResourceLoader.Load<ItemDefinition>(resourcePath, string.Empty, ResourceLoader.CacheMode.Ignore);
            if (item == null)
            {
                return;
            }

            if (!item.ItemId.StartsWith("Weapon_", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(item.Category, "Weapon", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            itemCount++;

            if (string.IsNullOrWhiteSpace(item.ItemId))
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: ItemId is empty.");
            }

            if (item.MaxStackSize != 1)
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: MaxStackSize should be 1 for weapon items, current={item.MaxStackSize}");
            }

            if (string.IsNullOrWhiteSpace(item.WorldScenePath))
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: WorldScenePath is empty.");
            }
            else
            {
                if (!item.WorldScenePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                {
                    errorCount++;
                    GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: WorldScenePath must use res:// path, current={item.WorldScenePath}");
                }
                else if (!ResourceLoader.Exists(item.WorldScenePath))
                {
                    errorCount++;
                    GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: WorldScenePath not found: {item.WorldScenePath}");
                }
                else
                {
                    ValidateWeaponScene(resourcePath, item.WorldScenePath, ref errorCount);
                }
            }

            if (item.WeaponSkillResources == null || item.WeaponSkillResources.Count == 0)
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: WeaponSkillResources is empty.");
            }
            else
            {
                for (int i = 0; i < item.WeaponSkillResources.Count; i++)
                {
                    if (item.WeaponSkillResources[i] is not WeaponSkillDefinition skill)
                    {
                        errorCount++;
                        GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: WeaponSkillResources[{i}] is not WeaponSkillDefinition.");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(skill.SkillId))
                    {
                        errorCount++;
                        GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: WeaponSkillResources[{i}] SkillId is empty.");
                    }
                }
            }

            if (!TryGetAttackPowerAttribute(item, out var attackPowerEntry))
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: missing attack_power in AttributeEntries.");
            }
            else if (attackPowerEntry.Value < 0f)
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {resourcePath}: attack_power cannot be negative, current={attackPowerEntry.Value}");
            }
        }

        private void ValidateWeaponScene(string itemResourcePath, string scenePath, ref int errorCount)
        {
            var scene = ResourceLoader.Load<PackedScene>(scenePath, string.Empty, ResourceLoader.CacheMode.Ignore);
            if (scene == null)
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {itemResourcePath}: Cannot load scene: {scenePath}");
                return;
            }

            var instance = scene.Instantiate();
            if (instance == null)
            {
                errorCount++;
                GD.PrintErr($"[WeaponConfigToolkit] {itemResourcePath}: Cannot instantiate scene: {scenePath}");
                return;
            }

            try
            {
                var attackArea = instance.GetNodeOrNull<Area2D>("AttackArea")
                    ?? instance.FindChild("AttackArea", recursive: true, owned: false) as Area2D;
                if (attackArea == null)
                {
                    errorCount++;
                    GD.PrintErr($"[WeaponConfigToolkit] {itemResourcePath}: Scene has no AttackArea: {scenePath}");
                    return;
                }

                var collisionShape = attackArea.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                if (collisionShape == null)
                {
                    foreach (Node child in attackArea.GetChildren())
                    {
                        if (child is CollisionShape2D shape)
                        {
                            collisionShape = shape;
                            break;
                        }
                    }
                }

                if (collisionShape == null || collisionShape.Shape == null)
                {
                    errorCount++;
                    GD.PrintErr($"[WeaponConfigToolkit] {itemResourcePath}: AttackArea has no valid CollisionShape2D: {scenePath}");
                }
            }
            finally
            {
                instance.QueueFree();
            }
        }

        private void ApplyAttackPowerAttribute(ItemDefinition item, float safeAttackPower)
        {
            var entries = item.AttributeEntries ?? new Godot.Collections.Array<ItemAttributeEntry>();

            int existingIndex = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                if (string.Equals(entry.AttributeId, "attack_power", StringComparison.OrdinalIgnoreCase))
                {
                    existingIndex = i;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                if (OverwriteAttackPowerIfExists)
                {
                    entries[existingIndex].Value = safeAttackPower;
                    entries[existingIndex].Operation = ItemAttributeOperation.Add;
                    entries[existingIndex].Scaling = ItemAttributeScalingMode.PerItem;
                }
                else if (SanitizeAttackPowerValue(entries[existingIndex].Value) != entries[existingIndex].Value)
                {
                    entries[existingIndex].Value = SanitizeAttackPowerValue(entries[existingIndex].Value);
                }
            }
            else
            {
                var newEntry = new ItemAttributeEntry
                {
                    AttributeId = "attack_power",
                    Value = safeAttackPower,
                    Operation = ItemAttributeOperation.Add,
                    Scaling = ItemAttributeScalingMode.PerItem,
                    Notes = "Generated by WeaponConfigToolkit"
                };
                entries.Add(newEntry);
            }

            item.AttributeEntries = entries;
        }

        private static void EnforceNonNegativeAttackPower(ItemDefinition item, float fallbackValue)
        {
            var entries = item.AttributeEntries;
            if (entries == null)
            {
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null || !string.Equals(entry.AttributeId, "attack_power", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                float sanitized = SanitizeAttackPowerValue(entry.Value);
                if (sanitized != entry.Value)
                {
                    entry.Value = fallbackValue;
                }
            }
        }

        private static float SanitizeAttackPowerValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return 0f;
            }

            return Mathf.Clamp(value, 0f, 999f);
        }

        private static bool TryGetAttackPowerAttribute(ItemDefinition item, out ItemAttributeEntry entry)
        {
            var entries = item.AttributeEntries;
            if (entries != null)
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    var candidate = entries[i];
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(candidate.AttributeId, "attack_power", StringComparison.OrdinalIgnoreCase))
                    {
                        entry = candidate;
                        return true;
                    }
                }
            }

            entry = null!;
            return false;
        }

        private static string BuildSkillFileName(string itemId)
        {
            string suffix = itemId.StartsWith("Weapon_", StringComparison.OrdinalIgnoreCase)
                ? itemId.Substring("Weapon_".Length)
                : itemId;
            return $"WeaponSkill_{suffix}.tres";
        }

        private static string ToSkillId(string itemId)
        {
            return itemId.Replace("Weapon_", "weapon_", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        }

        private static string BuildResourcePath(string directory, string fileName)
        {
            string normalized = NormalizeDirectoryPath(directory);
            return $"{normalized}/{fileName}";
        }

        private static string NormalizeDirectoryPath(string path)
        {
            string value = string.IsNullOrWhiteSpace(path) ? "res://" : path.Trim();
            value = value.Replace('\\', '/').TrimEnd('/');
            if (!value.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                value = $"res://{value.TrimStart('/')}";
            }
            return value;
        }

        private static void EnsureDirectoryExists(string directory)
        {
            string normalized = NormalizeDirectoryPath(directory);
            var makeResult = DirAccess.MakeDirRecursiveAbsolute(normalized);
            if (makeResult != Error.Ok && makeResult != Error.AlreadyExists)
            {
                GD.PrintErr($"[WeaponConfigToolkit] Failed to ensure directory: {normalized}, error={makeResult}");
            }
        }
    }
}
