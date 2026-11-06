using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Items.Effects;

namespace Kuros.Items.Weapons
{
    /// <summary>玩家武器攻击阶段坠落免疫；CSV 使用可组合的 0/1/2/4 位值。</summary>
    [System.Flags]
    public enum WeaponAttackFallPhases
    {
        None = 0,
        Warmup = 1,
        Active = 2,
        Recovery = 4
    }
    /// <summary>冲刺攻击速度来源（CSV 以 int 存序号，只增不改）。</summary>
    public enum DashAttackSpeedSource
    {
        TemplateDefault = -1,
        Inherit = 0,
        Fixed = 1
    }

    /// <summary>冲刺攻击速度衰减窗口（CSV 以 int 存序号，只增不改）。</summary>
    public enum DashAttackDecayWindow
    {
        TemplateDefault = -1,
        Warmup = 0,
        Active = 1,
        Recovery = 2,
        WarmupActive = 3,
        ActiveRecovery = 4,
        FullAttack = 5,
        None = 6
    }

    /// <summary>
    /// 武器技能定义，可配置主动/被动效果、动画、数值等。
    /// </summary>
    [GlobalClass]
    public partial class WeaponSkillDefinition : Resource
    {
        [Export] public string SkillId { get; set; } = string.Empty;
        [Export] public string DisplayName { get; set; } = "Weapon Skill";
        // 默认 Active：攻击技能占绝大多数，缺省字段时按 Passive 处理会让效果在装备时常驻、
        // 冲刺分流（DashEffects）永不生效（RiotGlove 曾因此 DashEffects 白配）。真正被动的技能显式写 0。
        [Export] public WeaponSkillType SkillType { get; set; } = WeaponSkillType.Active;
        [Export] public string AnimationName { get; set; } = string.Empty;
        [Export(PropertyHint.Range, "0,5,0.1")] public float DamageMultiplier { get; set; } = 1f;
        /// <summary>冲刺攻击伤害倍率。-1 = 用普通 DamageMultiplier。</summary>
        [Export(PropertyHint.Range, "-1,5,0.1")] public float DashDamageMultiplier { get; set; } = -1f;
        [Export(PropertyHint.Range, "0,30,0.1")] public float CooldownSeconds { get; set; } = 0.5f;
        [ExportGroup("Hitbox Debug")]
        [Export] public bool ShowHitboxDebug { get; set; } = true;
        [Export] public Color HitboxDebugColor { get; set; } = new Color(1f, 0.28f, 0.18f, 0.95f);
        [Export(PropertyHint.Range, "0.05,5,0.05")] public float HitboxDebugDuration { get; set; } = 0.6f;
        [Export(PropertyHint.Range, "1,12,0.5")] public float HitboxDebugLineWidth { get; set; } = 3f;
        [Export(PropertyHint.MultilineText)] public string Description { get; set; } = string.Empty;
        [Export] public Godot.Collections.Array<ItemEffectEntry> Effects { get; set; } = new();
        /// <summary>冲刺攻击专属效果（与普通攻击效果分流）。空 = 回退共用 Effects（向后兼容）。</summary>
        [Export] public Godot.Collections.Array<ItemEffectEntry> DashEffects { get; set; } = new();

        [ExportGroup("Attack FX Spawn")]
        [Export(PropertyHint.MultilineText)] public string AttackFxNote { get; set; } = "攻击阶段生成的场景特效（子弹/场地破坏等）：与 Effects 并行——Effects 挂 ActorEffect 到玩家，这里按攻击阶段在场景中生成节点（复用敌人 AttackEffectEntry 机制）";
        /// <summary>普通攻击阶段特效条目（AttackEffectEntry：场景 + SpawnTiming + 偏移 + 生命周期绑定）。</summary>
        [Export] public Godot.Collections.Array<AttackEffectEntry> AttackFxEntries { get; set; } = new();
        /// <summary>冲刺攻击专属阶段特效。空 = 回退 AttackFxEntries（与 DashEffects 同语义）。</summary>
        [Export] public Godot.Collections.Array<AttackEffectEntry> DashAttackFxEntries { get; set; } = new();
        /// <summary>模板级默认生成时机（entry SpawnTiming = Inherit 时回退到此值）。</summary>
        [Export] public EffectSpawnTiming AttackFxSpawnTiming { get; set; } = EffectSpawnTiming.OnActive;
        /// <summary>模板级默认生成偏移（entry EffectOffset 为 NaN 时回退）。</summary>
        [Export] public Vector2 AttackFxOffset { get; set; } = Vector2.Zero;
        /// <summary>模板级默认朝向翻转（entry FlipMode = Inherit 时回退）。</summary>
        [Export] public bool AttackFxFlipWithFacing { get; set; } = true;
        [Export] public Godot.Collections.Array<string> StateWhitelist { get; set; } = new();
        [Export] public bool UseDefaultAttackAnimationFallback { get; set; } = true;
        [Export] public string ActivationAction { get; set; } = string.Empty;
        [Export] public bool AllowHoldContinuousAttack { get; set; } = true;

        [ExportGroup("Fall Immunity")]
        /// <summary>普通攻击免坠落阶段；默认无，不影响普通 Dash 状态免疫。</summary>
        [Export(PropertyHint.Flags, "Warmup:1,Active:2,Recovery:4")]
        public WeaponAttackFallPhases AttackFallImmunities { get; set; } = WeaponAttackFallPhases.None;
        /// <summary>冲刺攻击免坠落阶段；默认无，空配置不会继承普通攻击。</summary>
        [Export(PropertyHint.Flags, "Warmup:1,Active:2,Recovery:4")]
        public WeaponAttackFallPhases DashAttackFallImmunities { get; set; } = WeaponAttackFallPhases.None;

        [ExportGroup("Attack Timing Override")]
        [Export(PropertyHint.MultilineText)] public string AttackTimingNote { get; set; } = "负数 = 使用攻击模板的默认值";
        [Export(PropertyHint.Range, "-1,5,0.01")] public float WarmupDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float ActiveDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float RecoveryDuration { get; set; } = -1f;

        [Export(PropertyHint.Range, "0.1,3,0.1")]
        public float WarmupAnimationSpeed { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0.1,3,0.1")]
        public float ActiveAnimationSpeed { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0.1,3,0.1")]
        public float RecoveryAnimationSpeed { get; set; } = 1.0f;

        [ExportGroup("Dash Attack Override")]
        [Export(PropertyHint.MultilineText)] public string DashAttackTimingNote { get; set; } = "冲刺攻击（EnableDashMovement）专属配置；负数/空 = 使用普通攻击配置";
        /// <summary>冲刺攻击动画名（Spine）。空 = 用普通攻击动画（AnimationName）。</summary>
        [Export] public string DashAnimationName { get; set; } = string.Empty;
        /// <summary>冲刺动画阶段时长（秒）。-1 = 用普通阶段（WarmupDuration 或模板默认）。</summary>
        [Export(PropertyHint.Range, "-1,5,0.01")] public float DashWarmupDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float DashActiveDuration { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,5,0.01")] public float DashRecoveryDuration { get; set; } = -1f;
        /// <summary>冲刺动画各阶段播放速度。-1 = 用普通动画速度。</summary>
        [Export(PropertyHint.Range, "-1,3,0.1")] public float DashWarmupAnimationSpeed { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,3,0.1")] public float DashActiveAnimationSpeed { get; set; } = -1f;
        [Export(PropertyHint.Range, "-1,3,0.1")] public float DashRecoveryAnimationSpeed { get; set; } = -1f;

        [ExportGroup("Dash Attack Movement Override")]
        [Export(PropertyHint.MultilineText)] public string DashAttackMoveTimingNote { get; set; } = "冲刺攻击位移专属配置；-1/负数 = 使用攻击模板默认（旧行为）";
        /// <summary>冲刺攻击速度来源：TemplateDefault(-1)=模板默认（继承 Burst 峰值快照）；Inherit=继承冲刺 Burst 峰值快照；Fixed=固定速度。</summary>
        [Export(PropertyHint.Enum, "TemplateDefault,Inherit,Fixed")] public DashAttackSpeedSource DashAttackSpeedSource { get; set; } = DashAttackSpeedSource.TemplateDefault;
        /// <summary>Fixed 模式下的冲刺攻击起步速度（-1 = 未配置）。</summary>
        [Export(PropertyHint.Range, "-1,6000,1")] public float DashAttackFixedSpeed { get; set; } = -1f;
        /// <summary>冲刺攻击起步速度峰值倍率（-1 = 未配置，回退模板）。</summary>
        [Export(PropertyHint.Range, "-1,3,0.05")] public float DashAttackSpeedMultiplier { get; set; } = -1f;
        /// <summary>冲刺攻击速度衰减窗口：TemplateDefault(-1)=模板默认（旧行为：ShouldDecayDashSpeed 控制 Warmup 衰减 + RecoverySpeed 滑行）。</summary>
        [Export(PropertyHint.Enum, "TemplateDefault,Warmup,Active,Recovery,WarmupActive,ActiveRecovery,FullAttack,None")] public DashAttackDecayWindow DashAttackDecayWindow { get; set; } = DashAttackDecayWindow.TemplateDefault;

        public bool IsUsableInState(string stateName)
        {
            if (StateWhitelist.Count == 0) return true;
            return StateWhitelist.Contains(stateName);
        }
    }
}

