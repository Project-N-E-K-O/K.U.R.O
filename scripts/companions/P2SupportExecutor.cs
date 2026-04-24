using System;
using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Items.Tags;

namespace Kuros.Companions
{
    /// <summary>
    /// Applies structured support decisions through a local whitelist.
    /// </summary>
    public partial class P2SupportExecutor : Node
    {
        [Signal] public delegate void DecisionAppliedEventHandler(string decisionJson);
        [Signal] public delegate void DecisionRejectedEventHandler(string reason);
        [Signal] public delegate void LoadoutChangedEventHandler(string supportSkillId, string equipmentId);

        [ExportCategory("References")]
        [Export] public NodePath CompanionControllerPath { get; set; } = new("..");
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");

        [ExportCategory("Support Execution")]
        [Export] public string DefaultSupportSkillAction { get; set; } = "weapon_skill_block";
        [Export] public bool ConsumeOnlyMatchingTag { get; set; } = true;
        [Export(PropertyHint.Range, "0,20,0.1")] public float SupportSkillCooldownSeconds { get; set; } = 3.0f;
        [Export(PropertyHint.Range, "0,20,0.1")] public float SupportItemCooldownSeconds { get; set; } = 6.0f;
        [Export] public bool EnableLogging { get; set; } = false;

        [ExportCategory("Shield VFX")]
        [Export] public Color ShieldBlockFlashColor { get; set; } = new Color(0.55f, 0.85f, 1f, 1f);
        [Export(PropertyHint.Range, "0.05,0.6,0.01")] public float ShieldBlockFlashDuration { get; set; } = 0.16f;
        [Export(PropertyHint.Range, "0.1,1,0.01")] public float ShieldBlockFlashStrength { get; set; } = 0.58f;

        [ExportCategory("P2 Loadout")]
        [Export] public Godot.Collections.Array<P2SupportSkillDefinition> SupportSkills { get; set; } = new();
        [Export] public Godot.Collections.Array<P2SupportEquipmentDefinition> SupportEquipments { get; set; } = new();
        [Export] public string EquippedSupportSkillId { get; set; } = "p2_skill_shield_test";
        [Export] public string EquippedEquipmentId { get; set; } = "p2_equipment_heal_amp_10";

        private P2CompanionController? _companionController;
        private global::SamplePlayer? _player;
        private global::SamplePlayer? _shieldBoundPlayer;

        public string LastAppliedDecisionJson { get; private set; } = string.Empty;
        public string LastRejectedReason { get; private set; } = string.Empty;
        public string LastIntent { get; private set; } = string.Empty;
        public ulong LastDecisionAtMs { get; private set; }
        public string LastResult { get; private set; } = "none";
        public string LastActionDetail { get; private set; } = string.Empty;
        public ulong TotalDecisionRequests { get; private set; }
        public ulong TotalDecisionApplied { get; private set; }
        public ulong TotalDecisionRejected { get; private set; }
        public int TotalShieldAbsorbedDamage { get; private set; }
        public int TotalHealFromSkills { get; private set; }
        public int TotalHealFromEquipBonus { get; private set; }

        private readonly Dictionary<string, ulong> _supportSkillCooldownsMs = new(StringComparer.OrdinalIgnoreCase);
        private ulong _nextSupportItemAtMs;
        private int _activeShieldPoints;
        private ulong _shieldExpireAtMs;
        private Tween? _shieldFlashTween;

        private const string ShieldSkillResourcePath = "res://resources/companions/P2SupportSkill_ShieldTest.tres";
        private const string HealSkillResourcePath = "res://resources/companions/P2SupportSkill_HealTest.tres";
        private const string HealAmpEquipmentResourcePath = "res://resources/companions/P2SupportEquipment_HealAmp10.tres";

        public override void _Ready()
        {
            ResolveDependencies();
            EnsureDefaultLoadoutResources();
            SetProcess(true);
        }

        public override void _ExitTree()
        {
            UnbindShieldInterceptor();
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            base._Process(delta);
            if (_activeShieldPoints <= 0)
            {
                return;
            }

            if (Time.GetTicksMsec() >= _shieldExpireAtMs)
            {
                ClearShieldState(notifyHint: true);
            }
        }

        public Godot.Collections.Array<P2SupportSkillDefinition> GetSupportSkills() => SupportSkills;

        public Godot.Collections.Array<P2SupportEquipmentDefinition> GetSupportEquipments() => SupportEquipments;

        public string GetEquippedSupportSkillId() => EquippedSupportSkillId;

        public string GetEquippedEquipmentId() => EquippedEquipmentId;

        public int GetActiveShieldPoints() => Mathf.Max(0, _activeShieldPoints);

        public float GetShieldRemainingSeconds()
        {
            if (_activeShieldPoints <= 0 || _shieldExpireAtMs == 0)
            {
                return 0f;
            }

            ulong now = Time.GetTicksMsec();
            if (now >= _shieldExpireAtMs)
            {
                return 0f;
            }

            return (_shieldExpireAtMs - now) / 1000f;
        }

        public float GetSupportSkillCooldownRemainingSeconds()
        {
            var skill = FindSkillById(EquippedSupportSkillId);
            return GetSupportSkillCooldownRemainingSeconds(skill);
        }

        public float GetSupportSkillCooldownRemainingSeconds(string skillId)
        {
            var skill = FindSkillById(skillId);
            return GetSupportSkillCooldownRemainingSeconds(skill);
        }

        private float GetSupportSkillCooldownRemainingSeconds(P2SupportSkillDefinition? skill)
        {
            ulong now = Time.GetTicksMsec();
            ulong nextAt = GetSupportSkillNextAvailableAtMs(skill);
            if (now >= nextAt)
            {
                return 0f;
            }

            return (nextAt - now) / 1000f;
        }

        public float GetSupportItemCooldownRemainingSeconds()
        {
            ulong now = Time.GetTicksMsec();
            if (now >= _nextSupportItemAtMs)
            {
                return 0f;
            }

            return (_nextSupportItemAtMs - now) / 1000f;
        }

        public float GetCurrentHealPowerMultiplier()
        {
            var equipment = FindEquipmentById(EquippedEquipmentId);
            if (equipment == null)
            {
                return 1f;
            }

            return Mathf.Max(0.1f, equipment.HealPowerMultiplier);
        }

        public bool EquipSupportSkill(string skillId)
        {
            if (FindSkillById(skillId) == null)
            {
                return false;
            }

            EquippedSupportSkillId = skillId;
            EmitSignal(SignalName.LoadoutChanged, EquippedSupportSkillId, EquippedEquipmentId);
            return true;
        }

        public bool EquipSupportEquipment(string equipmentId)
        {
            if (FindEquipmentById(equipmentId) == null)
            {
                return false;
            }

            EquippedEquipmentId = equipmentId;
            EmitSignal(SignalName.LoadoutChanged, EquippedSupportSkillId, EquippedEquipmentId);
            return true;
        }

        public bool TryExecute(SupportDecision decision)
        {
            ResolveDependencies();
            TotalDecisionRequests++;

            if (_companionController == null)
            {
                TotalDecisionRejected++;
                LastResult = "rejected";
                EmitSignal(SignalName.DecisionRejected, "companion controller not available");
                return false;
            }

            if (decision == null || !decision.IsValid)
            {
                TotalDecisionRejected++;
                LastResult = "rejected";
                EmitSignal(SignalName.DecisionRejected, "invalid support decision");
                return false;
            }

            string intent = decision.Intent.Trim().ToLowerInvariant();
            LastIntent = intent;
            LastDecisionAtMs = Time.GetTicksMsec();
            switch (intent)
            {
                case "show_hint":
                    _companionController.PushHint(decision.Message);
                    if (EnableLogging)
                    {
                        GD.Print($"[P2SupportExecutor] applied show_hint: {decision.Message}");
                    }
                    LastAppliedDecisionJson = decision.ToJson(pretty: false);
                    LastRejectedReason = string.Empty;
                    LastResult = "applied";
                    LastActionDetail = decision.Message;
                    TotalDecisionApplied++;
                    EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
                    return true;

                case "hold":
                    LastAppliedDecisionJson = decision.ToJson(pretty: false);
                    LastRejectedReason = string.Empty;
                    LastResult = "applied";
                    LastActionDetail = "hold";
                    TotalDecisionApplied++;
                    EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
                    return true;

                case "trigger_support_skill":
                    return ExecuteSupportSkill(decision);

                case "use_support_item":
                    return ExecuteSupportItem(decision);

                default:
                    LastRejectedReason = $"intent '{intent}' is not in whitelist";
                    LastResult = "rejected";
                    LastActionDetail = intent;
                    TotalDecisionRejected++;
                    EmitSignal(SignalName.DecisionRejected, $"intent '{intent}' is not in whitelist");
                    return false;
            }
        }

        private bool ExecuteSupportSkill(SupportDecision decision)
        {
            if (_player == null)
            {
                TotalDecisionRejected++;
                LastRejectedReason = "player not available for support skill";
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, "player not available for support skill");
                return false;
            }

            var skill = ResolveSkillForDecision(decision.Target) ?? FindSkillById(EquippedSupportSkillId);
            if (skill == null)
            {
                TotalDecisionRejected++;
                LastRejectedReason = "no equipped support skill available";
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, "no equipped support skill available");
                return false;
            }

            ulong now = Time.GetTicksMsec();
            ulong nextAt = GetSupportSkillNextAvailableAtMs(skill);
            if (now < nextAt)
            {
                float remain = (nextAt - now) / 1000f;
                TotalDecisionRejected++;
                LastRejectedReason = $"support skill on cooldown ({remain:0.0}s)";
                LastResult = "rejected";
                LastActionDetail = skill.SkillId;
                EmitSignal(SignalName.DecisionRejected, LastRejectedReason);
                return false;
            }

            string detail = string.Empty;
            string rejectReason = string.Empty;
            bool executed = skill.Handler?.TryExecute(this, skill, out detail, out rejectReason) == true;
            if (!executed)
            {
                TotalDecisionRejected++;
                LastRejectedReason = string.IsNullOrWhiteSpace(rejectReason)
                    ? $"support skill handler failed: {skill.SkillId}"
                    : rejectReason;
                LastResult = "rejected";
                LastActionDetail = "trigger_support_skill";
                EmitSignal(SignalName.DecisionRejected, LastRejectedReason);
                return false;
            }

            if (EnableLogging)
            {
                GD.Print($"[P2SupportExecutor] applied trigger_support_skill: {detail}");
            }

            LastAppliedDecisionJson = decision.ToJson(pretty: false);
            LastRejectedReason = string.Empty;
            LastResult = "applied";
            LastActionDetail = detail;
            TotalDecisionApplied++;
            SetSupportSkillCooldown(skill, now);
            EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
            return true;
        }

        private bool ExecuteSupportItem(SupportDecision decision)
        {
            if (_player?.InventoryComponent == null)
            {
                TotalDecisionRejected++;
                LastRejectedReason = "inventory component unavailable for support item";
                LastResult = "rejected";
                LastActionDetail = "use_support_item";
                EmitSignal(SignalName.DecisionRejected, "inventory component unavailable for support item");
                return false;
            }

            ulong now = Time.GetTicksMsec();
            if (now < _nextSupportItemAtMs)
            {
                TotalDecisionRejected++;
                LastRejectedReason = "support item on cooldown";
                LastResult = "rejected";
                LastActionDetail = "use_support_item";
                EmitSignal(SignalName.DecisionRejected, "support item on cooldown");
                return false;
            }

            var inventory = _player.InventoryComponent;
            string requiredTag = string.IsNullOrWhiteSpace(decision.ItemTag) ? ItemTagIds.Food : decision.ItemTag;
            int healthBefore = _player.CurrentHealth;
            if (!inventory.TryConsumeFirstTaggedItem(requiredTag, _player))
            {
                TotalDecisionRejected++;
                LastRejectedReason = $"no consumable support item found for tag '{requiredTag}'";
                LastResult = "rejected";
                LastActionDetail = requiredTag;
                EmitSignal(SignalName.DecisionRejected, $"no consumable support item found for tag '{requiredTag}'");
                return false;
            }

            ApplyHealingAmplifierBonus(healthBefore, "support item");

            if (EnableLogging)
            {
                GD.Print($"[P2SupportExecutor] applied use_support_item: tag={requiredTag}");
            }

            LastAppliedDecisionJson = decision.ToJson(pretty: false);
            LastRejectedReason = string.Empty;
            LastResult = "applied";
            LastActionDetail = requiredTag;
            TotalDecisionApplied++;
            _nextSupportItemAtMs = now + SecondsToMs(SupportItemCooldownSeconds);
            EmitSignal(SignalName.DecisionApplied, decision.ToJson(pretty: false));
            return true;
        }

        public bool ApplyShield(int shieldAmount, float durationSeconds, string skillId, out string detail, out string rejectReason)
        {
            rejectReason = string.Empty;
            if (_player == null)
            {
                rejectReason = "player not available for shield skill";
                detail = "shield_player_missing";
                return false;
            }

            int addShield = Mathf.Max(1, shieldAmount);
            float duration = Mathf.Max(0.5f, durationSeconds);

            _activeShieldPoints += addShield;
            _shieldExpireAtMs = Time.GetTicksMsec() + SecondsToMs(duration);
            BindShieldInterceptor();
            _player.SetShieldValue(_activeShieldPoints);

            _companionController?.PushHint($"P2 护盾已施加（{_activeShieldPoints}）");
            detail = $"{skillId}|shield={_activeShieldPoints}|dur={duration:0.0}s";
            return true;
        }

        public bool ApplyHeal(int healAmount, string skillId, out string detail, out string rejectReason)
        {
            rejectReason = string.Empty;
            if (_player == null)
            {
                rejectReason = "player not available for heal skill";
                detail = "heal_player_missing";
                return false;
            }

            if (_player.MaxHealth <= 0 || _player.CurrentHealth >= _player.MaxHealth)
            {
                rejectReason = "player hp already full for heal skill";
                detail = "heal_full_hp";
                return false;
            }

            float multiplier = GetCurrentHealPowerMultiplier();
            int finalHeal = Mathf.Max(1, Mathf.RoundToInt(Mathf.Max(1, healAmount) * multiplier));
            int nextHealth = Mathf.Min(_player.MaxHealth, _player.CurrentHealth + finalHeal);
            _player.RestoreHealth(nextHealth, _player.MaxHealth);
            TotalHealFromSkills += finalHeal;

            _companionController?.PushHint($"P2 恢复 +{finalHeal}");
            detail = $"{skillId}|heal={finalHeal}|mult={multiplier:0.00}";
            return true;
        }

        private void ApplyHealingAmplifierBonus(int healthBefore, string source)
        {
            if (_player == null)
            {
                return;
            }

            float multiplier = GetCurrentHealPowerMultiplier();
            if (multiplier <= 1.001f)
            {
                return;
            }

            int gained = Mathf.Max(0, _player.CurrentHealth - healthBefore);
            if (gained <= 0)
            {
                return;
            }

            int bonus = Mathf.Max(0, Mathf.RoundToInt(gained * (multiplier - 1f)));
            if (bonus <= 0)
            {
                return;
            }

            int nextHealth = Mathf.Min(_player.MaxHealth, _player.CurrentHealth + bonus);
            _player.RestoreHealth(nextHealth, _player.MaxHealth);
            TotalHealFromEquipBonus += bonus;
            _companionController?.PushHint($"装备加成额外恢复 +{bonus}");

            if (EnableLogging)
            {
                GD.Print($"[P2SupportExecutor] {source} heal bonus applied: +{bonus}");
            }
        }

        private bool OnPlayerDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (_activeShieldPoints <= 0)
            {
                return false;
            }

            if (Time.GetTicksMsec() >= _shieldExpireAtMs)
            {
                ClearShieldState(notifyHint: true);
                return false;
            }

            int incoming = Mathf.Max(0, args.Damage);
            if (incoming <= 0)
            {
                return false;
            }

            int absorbed = Mathf.Min(incoming, _activeShieldPoints);
            _activeShieldPoints -= absorbed;
            TotalShieldAbsorbedDamage += absorbed;
            _player?.SetShieldValue(_activeShieldPoints);
            args.Damage = Mathf.Max(0, incoming - absorbed);
            if (args.Damage <= 0)
            {
                args.IsBlocked = true;
            }

            if (absorbed > 0)
            {
                PlayShieldBlockVfx();
            }

            if (_activeShieldPoints <= 0)
            {
                ClearShieldState(notifyHint: true);
            }

            return args.IsBlocked;
        }

        private void ClearShieldState(bool notifyHint)
        {
            if (_activeShieldPoints <= 0 && _shieldExpireAtMs == 0)
            {
                return;
            }

            _activeShieldPoints = 0;
            _shieldExpireAtMs = 0;
            _player?.ClearShield();
            UnbindShieldInterceptor();
            if (notifyHint)
            {
                _companionController?.PushHint("P2 护盾已失效");
            }
        }

        private void BindShieldInterceptor()
        {
            if (_player == null)
            {
                return;
            }

            if (!ReferenceEquals(_shieldBoundPlayer, _player))
            {
                UnbindShieldInterceptor();
                _shieldBoundPlayer = _player;
                _shieldBoundPlayer.DamageIntercepted += OnPlayerDamageIntercepted;
            }
        }

        private void UnbindShieldInterceptor()
        {
            if (_shieldBoundPlayer == null)
            {
                return;
            }

            _shieldBoundPlayer.DamageIntercepted -= OnPlayerDamageIntercepted;
            _shieldBoundPlayer = null;
        }

        private void PlayShieldBlockVfx()
        {
            if (_player == null)
            {
                return;
            }

            if (_shieldFlashTween != null && _shieldFlashTween.IsRunning())
            {
                _shieldFlashTween.Kill();
            }

            Color baseColor = _player.Modulate;
            float strength = Mathf.Clamp(ShieldBlockFlashStrength, 0.1f, 1f);
            Color flashColor = baseColor.Lerp(ShieldBlockFlashColor, strength);

            float total = Mathf.Max(0.05f, ShieldBlockFlashDuration);
            float inDuration = total * 0.35f;
            float outDuration = total - inDuration;

            _shieldFlashTween = CreateTween();
            _shieldFlashTween.TweenProperty(_player, "modulate", flashColor, inDuration);
            _shieldFlashTween.TweenProperty(_player, "modulate", baseColor, outDuration);
        }

        private void EnsureDefaultLoadoutResources()
        {
            if (SupportSkills.Count == 0)
            {
                var shieldSkill = GD.Load<P2SupportSkillDefinition>(ShieldSkillResourcePath);
                var healSkill = GD.Load<P2SupportSkillDefinition>(HealSkillResourcePath);
                if (shieldSkill != null)
                {
                    SupportSkills.Add(shieldSkill);
                }

                if (healSkill != null)
                {
                    SupportSkills.Add(healSkill);
                }
            }

            if (SupportEquipments.Count == 0)
            {
                SupportEquipments.Add(new P2SupportEquipmentDefinition
                {
                    EquipmentId = "p2_equipment_none",
                    DisplayName = "无装备",
                    HealPowerMultiplier = 1.0f
                });

                var healAmp = GD.Load<P2SupportEquipmentDefinition>(HealAmpEquipmentResourcePath);
                if (healAmp != null)
                {
                    SupportEquipments.Add(healAmp);
                }
            }

            if (FindSkillById(EquippedSupportSkillId) == null && SupportSkills.Count > 0)
            {
                EquippedSupportSkillId = SupportSkills[0].SkillId;
            }

            if (FindEquipmentById(EquippedEquipmentId) == null && SupportEquipments.Count > 0)
            {
                EquippedEquipmentId = SupportEquipments[0].EquipmentId;
            }
        }

        private P2SupportSkillDefinition? ResolveSkillForDecision(string rawTarget)
        {
            string target = (rawTarget ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(target) || target == "player" || target == "self")
            {
                return FindSkillById(EquippedSupportSkillId);
            }

            var direct = FindSkillById(target);
            if (direct != null)
            {
                return direct;
            }

            if (target.Contains("heal", StringComparison.Ordinal))
            {
                return FindSkillByType("heal") ?? FindSkillById(EquippedSupportSkillId);
            }

            if (target.Contains("shield", StringComparison.Ordinal) || target.Contains("block", StringComparison.Ordinal))
            {
                return FindSkillByType("shield") ?? FindSkillById(EquippedSupportSkillId);
            }

            return FindSkillById(EquippedSupportSkillId);
        }

        private P2SupportSkillDefinition? FindSkillByType(string type)
        {
            string normalized = (type ?? string.Empty).Trim().ToLowerInvariant();
            for (int i = 0; i < SupportSkills.Count; i++)
            {
                var skill = SupportSkills[i];
                if (skill == null)
                {
                    continue;
                }

                if (skill.GetSkillTypeNormalized() == normalized)
                {
                    return skill;
                }
            }

            return null;
        }

        private P2SupportSkillDefinition? FindSkillById(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return null;
            }

            for (int i = 0; i < SupportSkills.Count; i++)
            {
                var skill = SupportSkills[i];
                if (skill == null || string.IsNullOrWhiteSpace(skill.SkillId))
                {
                    continue;
                }

                if (string.Equals(skill.SkillId, skillId, StringComparison.OrdinalIgnoreCase))
                {
                    return skill;
                }
            }

            return null;
        }

        private P2SupportEquipmentDefinition? FindEquipmentById(string equipmentId)
        {
            if (string.IsNullOrWhiteSpace(equipmentId))
            {
                return null;
            }

            for (int i = 0; i < SupportEquipments.Count; i++)
            {
                var equipment = SupportEquipments[i];
                if (equipment == null || string.IsNullOrWhiteSpace(equipment.EquipmentId))
                {
                    continue;
                }

                if (string.Equals(equipment.EquipmentId, equipmentId, StringComparison.OrdinalIgnoreCase))
                {
                    return equipment;
                }
            }

            return null;
        }

        private void ResolveDependencies()
        {
            if (_companionController != null && IsInstanceValid(_companionController) && _companionController.IsInsideTree())
            {
                ResolvePlayer();
                return;
            }

            _companionController = GetNodeOrNull<P2CompanionController>(CompanionControllerPath)
                ?? GetNodeOrNull<P2CompanionController>(NormalizeRelativePath(CompanionControllerPath));

            ResolvePlayer();
        }

        private void ResolvePlayer()
        {
            if (_player != null && IsInstanceValid(_player) && _player.IsInsideTree())
            {
                return;
            }

            var nextPlayer = GetNodeOrNull<global::SamplePlayer>(PlayerPath)
                ?? GetNodeOrNull<global::SamplePlayer>(NormalizeRelativePath(PlayerPath))
                ?? GetTree().GetFirstNodeInGroup("player") as global::SamplePlayer;

            if (!ReferenceEquals(_player, nextPlayer))
            {
                UnbindShieldInterceptor();
                _player = nextPlayer;
                if (_activeShieldPoints > 0)
                {
                    BindShieldInterceptor();
                }
            }
        }

        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", System.StringComparison.Ordinal))
            {
                return path;
            }

            return new NodePath($"../{text}");
        }

        private static ulong SecondsToMs(float seconds)
        {
            return (ulong)Mathf.RoundToInt(Mathf.Max(0f, seconds) * 1000f);
        }

        private ulong GetSupportSkillNextAvailableAtMs(P2SupportSkillDefinition? skill)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.SkillId))
            {
                return 0;
            }

            if (_supportSkillCooldownsMs.TryGetValue(skill.SkillId, out ulong nextAt))
            {
                return nextAt;
            }

            return 0;
        }

        private void SetSupportSkillCooldown(P2SupportSkillDefinition skill, ulong nowMs)
        {
            if (skill == null || string.IsNullOrWhiteSpace(skill.SkillId))
            {
                return;
            }

            float configured = skill.CooldownSeconds;
            if (configured <= 0f)
            {
                configured = SupportSkillCooldownSeconds;
            }

            _supportSkillCooldownsMs[skill.SkillId] = nowMs + SecondsToMs(configured);
        }
    }
}