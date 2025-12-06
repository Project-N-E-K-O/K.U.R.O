using System;
using System.Threading.Tasks;
using Godot;
using Kuros.Core;
using Kuros.Items;
using Kuros.Items.World;
using Kuros.Systems.Inventory;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 连续远程齐射攻击：根据瞄准模式锁定方向，分批次投掷物品或直接对玩家造成伤害。
    /// </summary>
    public partial class EnemyVolleyRangedAttack : EnemyAttackTemplate
    {
        public enum AimRefreshMode
        {
            OncePerAttack,
            PerVolley,
            PerShot
        }

        [ExportGroup("Volley Config")]
        [Export(PropertyHint.Range, "1,10,1")] public int VolleyCount { get; set; } = 2;
        [Export(PropertyHint.Range, "1,10,1")] public int ShotsPerVolley { get; set; } = 3;
        [Export(PropertyHint.Range, "0,5,0.01")] public float ShotIntervalSeconds { get; set; } = 0.2f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float VolleyIntervalSeconds { get; set; } = 0.6f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float PostRecoverySeconds { get; set; } = 0.5f;
        [Export] public AimRefreshMode AimMode { get; set; } = AimRefreshMode.PerVolley;

        [ExportGroup("Damage / Projectile")]
        [Export(PropertyHint.Range, "0,50,1")] public int DamagePerShot { get; set; } = 1;
        [Export(PropertyHint.Range, "100,4000,10")] public float ProjectileImpulse { get; set; } = 800f;
        [Export] public Vector2 SpawnOffset { get; set; } = new Vector2(16, -4);
        [Export] public ItemDefinition? ProjectileItem { get; set; }

        private bool _sequenceRunning;
        private bool _cancelRequested;
        private Vector2 _cachedAttackDirection = Vector2.Right;
        private Vector2 _cachedVolleyDirection = Vector2.Right;

        public EnemyVolleyRangedAttack()
        {
            AttackName = "VolleyRanged";
            WarmupDuration = 0f;
            ActiveDuration = 3f;
            RecoveryDuration = PostRecoverySeconds;
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
            RecoveryDuration = PostRecoverySeconds;
        }

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            _cancelRequested = false;
            _sequenceRunning = true;
            float totalAttackTime = Mathf.Max(0.01f, 
                VolleyCount * (ShotsPerVolley - 1) * ShotIntervalSeconds + 
                (VolleyCount - 1) * VolleyIntervalSeconds);
            ActiveDuration = totalAttackTime;
            _ = RunSequenceAsync().ContinueWith(t =>
            {
                if (t.Exception != null)
                {
                    GD.PrintErr($"[{AttackName}] Sequence error: {t.Exception.InnerException?.Message}");
                    StopSequence();
                }
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            StopSequence();
        }

        private async Task RunSequenceAsync()
        {
            _cachedAttackDirection = ResolveAimDirection();
            _cachedVolleyDirection = _cachedAttackDirection;

            for (int volley = 0; volley < VolleyCount && !_cancelRequested; volley++)
            {
                if (AimMode == AimRefreshMode.PerVolley)
                {
                    _cachedVolleyDirection = ResolveAimDirection();
                }
                else if (AimMode == AimRefreshMode.OncePerAttack && volley == 0)
                {
                    _cachedVolleyDirection = _cachedAttackDirection;
                }

                for (int shot = 0; shot < ShotsPerVolley && !_cancelRequested; shot++)
                {
                    Vector2 direction = ResolveShotDirection(volley, shot);
                    FireShot(direction);

                    if (shot < ShotsPerVolley - 1 && ShotIntervalSeconds > 0f)
                    {
                        await DelaySeconds(ShotIntervalSeconds);
                    }
                }

                if (volley < VolleyCount - 1 && VolleyIntervalSeconds > 0f)
                {
                    await DelaySeconds(VolleyIntervalSeconds);
                }
            }

            ForceEnterRecoveryPhase();
            StopSequence();
        }

        private void FireShot(Vector2 direction)
        {
            if (direction == Vector2.Zero)
            {
                direction = Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            }

            direction = direction.Normalized();
            if (ProjectileItem != null)
            {
                SpawnProjectile(direction);
            }
            else if (Player != null && DamagePerShot > 0)
            {
                Player.TakeDamage(DamagePerShot, Enemy.GlobalPosition, Enemy);
            }
        }

        private void SpawnProjectile(Vector2 direction)
        {
            if (ProjectileItem == null)
            {
                return;
            }

            var stack = new InventoryItemStack(ProjectileItem, 1);
            Vector2 offset = SpawnOffset;
            offset.X = Enemy.FacingRight ? Mathf.Abs(offset.X) : -Mathf.Abs(offset.X);
            Vector2 spawnPos = Enemy.GlobalPosition + offset;

            var entity = WorldItemSpawner.SpawnFromStack(Enemy, stack, spawnPos);
            if (entity == null)
            {
                return;
            }

            entity.LastDroppedBy = Enemy;
            entity.ApplyThrowImpulse(direction * ProjectileImpulse);
        }

        private Vector2 ResolveShotDirection(int volleyIndex, int shotIndex)
        {
            switch (AimMode)
            {
                case AimRefreshMode.PerShot:
                    return ResolveAimDirection();
                case AimRefreshMode.PerVolley:
                    return _cachedVolleyDirection;
                case AimRefreshMode.OncePerAttack:
                    return _cachedAttackDirection;
                default:
                    return _cachedVolleyDirection;
            }
        }

        private Vector2 ResolveAimDirection()
        {
            if (Player == null)
            {
                return Enemy.FacingRight ? Vector2.Right : Vector2.Left;
            }

            Vector2 delta = Player.GlobalPosition - Enemy.GlobalPosition;
            return delta == Vector2.Zero ? (Enemy.FacingRight ? Vector2.Right : Vector2.Left) : delta.Normalized();
        }

        private async Task DelaySeconds(float seconds)
        {
            if (seconds <= 0f || _cancelRequested || !IsInstanceValid(Enemy))
            {
                return;
            }

            var timer = Enemy.GetTree().CreateTimer(seconds);
            try
            {
                await ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            }
            catch (ObjectDisposedException)
            {
                _cancelRequested = true;
            }
        }

        private void StopSequence()
        {
            if (!_sequenceRunning)
            {
                return;
            }

            _cancelRequested = true;
            _sequenceRunning = false;
        }
    }
}

