using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Items;
using Kuros.Items.World;
using Kuros.Systems.Inventory;

namespace Kuros.Actors.Enemies
{
    /// <summary>
    /// 近远双段攻击的侍者敌人：距离远时投掷瓷盘，距离近时释放范围伤害并击退。
    /// </summary>
    public partial class EnemyC1WaiterA : SampleEnemy
    {
        [ExportGroup("Waiter Settings")]
        [Export(PropertyHint.Range, "32,600,1")] public float AttackDistanceThreshold { get; set; } = 200f;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float ThrowCooldownSeconds { get; set; } = 1.5f;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float MeleeCooldownSeconds { get; set; } = 1.0f;
        [Export(PropertyHint.Range, "0.1,2,0.1")] public float TurnCooldownSeconds { get; set; } = 0.5f;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float HitStunDuration { get; set; } = 0.5f;

        [ExportGroup("Throw Settings")]
        [Export(PropertyHint.Range, "100,2000,10")] public float PlateThrowImpulse { get; set; } = 900f;
        [Export] public Vector2 ThrowSpawnOffset { get; set; } = new Vector2(20f, -6f);
        [Export] public ItemDefinition? PlateItemDefinition { get; set; }

        [ExportGroup("Melee Settings")]
        [Export(PropertyHint.Range, "0,1000,10")] public float KnockbackStrength { get; set; } = 250f;
        [Export(PropertyHint.Range, "1,10,1")] public int MeleeDamage { get; set; } = 1;
        [Export] public Area2D? MeleeArea { get; set; }

        private SamplePlayer? _player;
        private float _throwTimer;
        private float _meleeTimer;
        private float _turnTimer;
        private float _stunTimer;

        public EnemyC1WaiterA()
        {
            MaxHealth = 4;
        }

        public override void _Ready()
        {
            base._Ready();
            AcquirePlayer();
            PlateItemDefinition ??= ResourceLoader.Load<ItemDefinition>("res://resources/items/Weapon_A0_plate.tres");
            MeleeArea ??= AttackArea;
            _throwTimer = ThrowCooldownSeconds;
            _meleeTimer = 0f;
        }

        public override void _PhysicsProcess(double delta)
        {
            base._PhysicsProcess(delta);
            TickTimers((float)delta);

            if (_stunTimer > 0f)
            {
                return;
            }

            AcquirePlayer();
            if (_player == null)
            {
                return;
            }

            UpdateFacing((float)delta);

            float distance = GlobalPosition.DistanceTo(_player.GlobalPosition);
            if (distance > AttackDistanceThreshold)
            {
                TryPerformThrow();
            }
            else
            {
                TryPerformMelee();
            }
        }

        public override void TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null)
        {
            base.TakeDamage(damage, attackOrigin, attacker);
            if (damage > 0)
            {
                _stunTimer = HitStunDuration;
            }
        }

        private void TickTimers(float delta)
        {
            if (_throwTimer > 0f) _throwTimer -= delta;
            if (_meleeTimer > 0f) _meleeTimer -= delta;
            if (_turnTimer > 0f) _turnTimer -= delta;
            if (_stunTimer > 0f) _stunTimer -= delta;
        }

        private void AcquirePlayer()
        {
            if (_player != null && IsInstanceValid(_player))
            {
                return;
            }

            _player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
        }

        private void UpdateFacing(float delta)
        {
            if (_player == null)
            {
                return;
            }

            bool desiredFacingRight = _player.GlobalPosition.X >= GlobalPosition.X;
            if (desiredFacingRight != FacingRight && _turnTimer <= 0f)
            {
                FlipFacing(desiredFacingRight);
                _turnTimer = TurnCooldownSeconds;
            }
        }

        private void TryPerformThrow()
        {
            if (_throwTimer > 0f || PlateItemDefinition == null || _player == null)
            {
                return;
            }

            var stack = new InventoryItemStack(PlateItemDefinition, 1);
            Vector2 spawnOffset = ThrowSpawnOffset;
            spawnOffset.X = FacingRight ? Mathf.Abs(spawnOffset.X) : -Mathf.Abs(spawnOffset.X);
            Vector2 spawnPosition = GlobalPosition + spawnOffset;

            var entity = WorldItemSpawner.SpawnFromStack(this, stack, spawnPosition);
            if (entity != null)
            {
                entity.LastDroppedBy = this;
                Vector2 direction = (_player.GlobalPosition - spawnPosition).Normalized();
                if (direction == Vector2.Zero)
                {
                    direction = FacingRight ? Vector2.Right : Vector2.Left;
                }
                entity.ApplyThrowImpulse(direction * PlateThrowImpulse);
            }

            _throwTimer = ThrowCooldownSeconds;
            AttackTimer = ThrowCooldownSeconds;
        }

        private void TryPerformMelee()
        {
            if (_meleeTimer > 0f || MeleeArea == null)
            {
                return;
            }

            bool hitTarget = false;
            foreach (var body in MeleeArea.GetOverlappingBodies())
            {
                if (body is GameActor actor && actor != this)
                {
                    actor.TakeDamage(MeleeDamage, GlobalPosition, this);
                    ApplyKnockback(actor);
                    hitTarget = true;
                }
            }

            if (hitTarget)
            {
                _meleeTimer = MeleeCooldownSeconds;
                AttackTimer = MeleeCooldownSeconds;
            }
        }

        private void ApplyKnockback(GameActor target)
        {
            Vector2 direction = (target.GlobalPosition - GlobalPosition).Normalized();
            if (direction == Vector2.Zero)
            {
                direction = FacingRight ? Vector2.Right : Vector2.Left;
            }

            target.Velocity += direction * KnockbackStrength;
        }
    }
}

