using Godot;
using Kuros.Core;
using Kuros.Actors.Heroes;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// WaiterA 碟子投掷物的单个实例。
    /// 负责：
    ///   1. 执行参数化抛物线运动（不依赖玩家位置）
    ///   2. 检测与玩家的碰撞
    ///   3. 造成伤害和击退效果
    ///   4. 到达目标点后销毁自身
    /// 
    /// 参数说明：
    ///   - Duration, PeakHeight, RotationSpeed, Damage, Knockback* 等由此脚本控制
    ///   - 发射方向由 EnemyWaiterA02WheelAttackController 设置
    /// </summary>
    public partial class EnemyWaiterA02ProjectileInstance : Node2D
    {
        /// <summary>每个碟子的发射距离（像素）。</summary>
        [Export] public float ProjectileDistance { get; set; } = 800f;

        /// <summary>飞行总时长（秒）。</summary>
        [Export] public float Duration { get; set; } = 0.8f;

        /// <summary>抛物线峰值高度（像素，正值向上）。</summary>
        [Export] public float PeakHeight { get; set; } = 300f;

        /// <summary>飞行途中每秒旋转角度（度）。正值顺时针，负值逆时针，0 不旋转。</summary>
        [Export] public float RotationDegreesPerSecond { get; set; } = 360f;

        /// <summary>击中玩家时造成的伤害。</summary>
        [Export] public int Damage { get; set; } = 1;

        /// <summary>击退距离（像素）。</summary>
        [Export] public float KnockbackDistance { get; set; } = 50f;

        /// <summary>击退时长（秒）。</summary>
        [Export] public float KnockbackDuration { get; set; } = 0.18f;

        /// <summary>击退速度（像素/秒）。</summary>
        [Export] public float KnockbackSpeed { get; set; } = 1000f;

        /// <summary>检测碰撞的 Area2D 节点路径（相对于此根节点）。</summary>
        [Export] public NodePath HitboxPath = new NodePath("Area2D");

        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _elapsed;
        private bool _launched;   // 第一帧后才开始移动
        private Area2D? _hitbox;
        private bool _hasHit;     // 防止多次碰撞

        public override void _Ready()
        {
            // 获取 hitbox（用于检测碰撞）
            if (!HitboxPath.IsEmpty)
            {
                _hitbox = GetNodeOrNull<Area2D>(HitboxPath);
            }

            if (_hitbox != null)
            {
                _hitbox.AreaEntered += OnHitboxAreaEntered;
                _hitbox.BodyEntered += OnHitboxBodyEntered;
            }

            SetPhysicsProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (!_launched)
            {
                // 第一帧：记录起点
                _startPos = GlobalPosition;
                _launched = true;
                return;
            }

            _elapsed += (float)delta;
            float t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);

            // 参考 EnemyWaiterAThrowProjectile 的参数化抛物线公式
            float upDown = Mathf.Sin(t * Mathf.Pi);
            float x = Mathf.Lerp(_startPos.X, _targetPos.X, t);
            float y = Mathf.Lerp(_startPos.Y, _targetPos.Y, t) - upDown * PeakHeight;
            GlobalPosition = new Vector2(x, y);

            // 旋转
            if (Mathf.Abs(RotationDegreesPerSecond) > 0.01f)
            {
                RotationDegrees += RotationDegreesPerSecond * (float)delta;
            }

            // 到达终点
            if (t >= 1f)
            {
                OnArrived();
            }
        }

        /// <summary>
        /// 初始化落点。由创建者（EnemyWaiterA02WheelAttackController）调用。
        /// </summary>
        public void SetTargetPosition(Vector2 targetPos)
        {
            _targetPos = targetPos;
        }

        /// <summary>
        /// 初始化发射方向。由创建者调用，用于计算落点。
        /// </summary>
        public void SetDirectionAndDistance(Vector2 direction, float distance)
        {
            // 相对于起点沿方向移动指定距离
            _targetPos = GlobalPosition + direction.Normalized() * distance;
        }

        public override void _ExitTree()
        {
            if (_hitbox != null)
            {
                _hitbox.AreaEntered -= OnHitboxAreaEntered;
                _hitbox.BodyEntered -= OnHitboxBodyEntered;
            }

            base._ExitTree();
        }

        private void OnHitboxAreaEntered(Area2D area)
        {
            if (_hasHit) return;

            // 检测是否击中玩家（玩家的 Area2D）
            if (area.IsInGroup("player_hitbox") || area.Owner is SamplePlayer)
            {
                DealDamageAndKnockback(area.Owner as GameActor);
            }
        }

        private void OnHitboxBodyEntered(Node body)
        {
            if (_hasHit) return;

            // 通过 Body 检测玩家
            if (body is SamplePlayer player)
            {
                DealDamageAndKnockback(player);
            }
        }

        private void DealDamageAndKnockback(GameActor? victim)
        {
            if (victim == null || _hasHit) return;

            _hasHit = true;

            // 造成伤害
            victim.TakeDamage(Damage);

            // 尝试应用击退（仅对玩家）
            if (victim is SamplePlayer player)
            {
                TryApplyPlayerKnockback(player);
            }

            // 碟子击中后立即销毁
            QueueFree();
        }

        private void TryApplyPlayerKnockback(SamplePlayer player)
        {
            // 计算击退方向（从碟子指向玩家）
            Vector2 knockbackDir = (player.GlobalPosition - GlobalPosition).Normalized();
            if (knockbackDir == Vector2.Zero)
            {
                knockbackDir = Vector2.Right;
            }

            // 计算击退速度
            float clampedDuration = Mathf.Max(KnockbackDuration, 0.01f);
            float clampedDistance = Mathf.Max(0f, KnockbackDistance);
            float clampedSpeed = Mathf.Max(0f, KnockbackSpeed);

            if (clampedDistance <= 0f && clampedSpeed <= 0f)
            {
                return;
            }

            float speed = clampedSpeed > 0f ? clampedSpeed : clampedDistance / clampedDuration;
            if (speed <= 0f)
            {
                return;
            }

            // 对于 MainCharacter，检查是否消耗了待处理击退标记
            if (player is Kuros.Actors.Heroes.MainCharacter mainCharacter)
            {
                if (!mainCharacter.ConsumePendingHitKnockback())
                {
                    return;  // 无敌帧或护盾格挡，不应用击退
                }
            }

            // 设置玩家速度（击退）
            Vector2 knockbackVelocity = knockbackDir * speed;
            player.Velocity = knockbackVelocity;

            // 如果玩家在 Frozen 状态，应用外部位移
            ApplyFrozenExternalDisplacement(player, knockbackVelocity, clampedDuration);
        }

        private void ApplyFrozenExternalDisplacement(SamplePlayer player, Vector2 velocity, float duration)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<Kuros.Actors.Heroes.States.PlayerFrozenState>("Frozen");
            if (frozenState == null)
            {
                return;
            }

            if (player.StateMachine?.CurrentState != frozenState)
            {
                return;
            }

            if (!frozenState.AllowExternalDisplacementWhileFrozen)
            {
                return;
            }

            frozenState.ApplyExternalDisplacement(velocity, duration);
        }

        private void OnArrived()
        {
            // 到达落点后销毁
            SetPhysicsProcess(false);
            QueueFree();
        }
    }
}
