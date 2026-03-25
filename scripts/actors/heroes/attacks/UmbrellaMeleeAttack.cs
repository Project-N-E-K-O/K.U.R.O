namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 雨伞专属近战模板。
    /// 仅承载雨伞的行为差异；动画、冷却、连击次数等数据配置统一来自 WeaponSkillDefinition。
    /// </summary>
    public partial class UmbrellaMeleeAttack : PlayerBasicMeleeAttack
    {
        // 当前版本暂无雨伞专属行为差异。
        // 该模板保留为专属扩展点，后续若需要实现刺击位移、格挡反击、多段命中修正等逻辑，可在此覆盖。
    }
}
