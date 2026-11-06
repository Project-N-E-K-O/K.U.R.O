using System;

namespace Kuros.Core
{
    /// <summary>
    /// 角色在特定攻击/状态期间可拥有的免疫类型。
    /// 使用位掩码，方便组合与扩展——新增免疫类型只需在此枚举中追加新值。
    /// </summary>
    [Flags]
    public enum ImmunityFlags
    {
        None                 = 0,
        /// <summary>免疫眩晕（FreezeEffect 等）。</summary>
        Stun                 = 1 << 0,
        /// <summary>免疫强制位移（击退、黑洞吸附等）。</summary>
        ForcedMovement       = 1 << 1,
        /// <summary>免疫减速效果。</summary>
        SpeedSlow            = 1 << 2,
        /// <summary>预热阶段霸体：Warmup 期间受到伤害不进入受击硬直。</summary>
        WarmupSuperArmor     = 1 << 3,
        /// <summary>生效阶段霸体：Active 期间受到伤害不进入受击硬直。</summary>
        ActiveSuperArmor     = 1 << 4,
        /// <summary>恢复阶段霸体：Recovery 期间受到伤害不进入受击硬直。</summary>
        RecoverySuperArmor   = 1 << 5,
        /// <summary>免疫投掷武器伤害（ThrowableDirectAttack + ThrowImpact）。</summary>
        ThrowableDamage      = 1 << 7,
        /// <summary>免疫所有非投掷伤害（仅 Throwable 来源可穿透）。若需免疫全部伤害，同时勾选此项与 ThrowableDamage。</summary>
        NonThrowableDamage   = 1 << 8,
        /// <summary>独立的地形坠落免疫；Stun / ForcedMovement 不包含此能力。</summary>
        Fall                 = 1 << 9,
        /// <summary>攻击预热阶段免坠落；由攻击模组按阶段查询，不作为全程免疫写入 ActiveImmunities。</summary>
        WarmupFallImmunity   = 1 << 10,
        /// <summary>攻击生效阶段免坠落。</summary>
        ActiveFallImmunity   = 1 << 11,
        /// <summary>攻击恢复阶段免坠落。</summary>
        RecoveryFallImmunity = 1 << 12,

    }
}
