using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Effects
{
    /// <summary>
    /// 法棍面包武器效果：攻击命中敌人时有概率从敌人位置抛出一个面包碎块，
    /// 碎块沿抛物线飞行并自旋，接触玩家后恢复生命。
    /// 搭配 ItemDefinition 的 OnEquip 触发器使用。
    /// </summary>
    [GlobalClass]
    public partial class WeaponSlashBaguetteEffect : ActorEffect
    {
        [ExportCategory("Crumb")]
        [Export] public PackedScene? CrumbScene { get; set; }

        [ExportCategory("Spawn")]
        [Export(PropertyHint.Range, "0,100,1")]
        public float SpawnChance { get; set; } = 30f;

        [Export(PropertyHint.Range, "100,2000,10")]
        public float FlightDistance { get; set; } = 400f;

        private bool _subscribed;

        public WeaponSlashBaguetteEffect()
        {
            EffectId = "weapon_slash_baguette";
            DisplayName = "法棍攻击";
            Description = "攻击命中时有概率抛出一个法棍碎块，接触后恢复生命。";
            IsBuff = true;
            Duration = 0f;
            MaxStacks = 1;
        }

        protected override void OnApply()
        {
            base.OnApply();
            if (!_subscribed)
            {
                DamageEventBus.SubscribeWithSource(OnDamageResolved);
                _subscribed = true;
            }
        }

        public override void OnRemoved()
        {
            if (_subscribed)
            {
                DamageEventBus.UnsubscribeWithSource(OnDamageResolved);
                _subscribed = false;
            }
            base.OnRemoved();
        }

        private void OnDamageResolved(GameActor attacker, GameActor target, int damage, DamageSource source)
        {
            if (source != DamageSource.DirectAttack) return;
            if (Actor == null || attacker != Actor) return;
            if (damage <= 0) return;
            if (target.IsDead) return;

            if (GD.Randf() * 100f > SpawnChance) return;
            if (CrumbScene == null) return;

            var crumb = CrumbScene.Instantiate<BaguetteCrumb>();
            if (crumb == null) return;

            var tree = GetTree();
            if (tree?.CurrentScene == null) return;

            var enemyPos = GetEnemyHitCenter(target);
            var attackDir = (enemyPos - Actor.GlobalPosition).Normalized();
            crumb.SetTarget(enemyPos + attackDir * FlightDistance);
            tree.CurrentScene.AddChild(crumb);
            crumb.GlobalPosition = enemyPos;
        }

        private static Vector2 GetEnemyHitCenter(GameActor enemy)
        {
            var hitArea = enemy.GetNodeOrNull<Area2D>("HitArea")
                ?? enemy.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? enemy.GlobalPosition;
        }
    }
}
