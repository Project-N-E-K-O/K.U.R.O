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
        None              = 0,
        /// <summary>免疫眩晕（FreezeEffect 等）。</summary>
        Stun              = 1 << 0,
        /// <summary>免疫强制位移（击退、黑洞吸附等）。</summary>
        ForcedMovement    = 1 << 1,
        /// <summary>免疫减速效果。</summary>
        SpeedSlow         = 1 << 2,
        /// <summary>超级护甲：受到伤害时不进入受击硬直状态（等同于 IgnoreHitStateOnDamage）。</summary>
        SuperArmor        = 1 << 3,
        /// <summary>免疫所有伤害（TakeDamage 直接返回）。</summary>
        Damage            = 1 << 4,
    }
}
