using Godot;
using Kuros.Core;

namespace Kuros.Actors.Enemies.States
{
    /// <summary>
    /// 攻击后短暂后撤，结束后回到 Walk。
    /// </summary>
    public partial class EnemyDashBackState : EnemyState
    {
        [Export(PropertyHint.Range, "10,2000,10")]
        public float DashSpeed = 500.0f;

        [Export(PropertyHint.Range, "0.05,2,0.01")]
        public float DashDuration = 0.2f;

        [Export]
        public string AnimationName = "animations/walk";

        [Export]
        public string NextStateName = "Walk";

        [ExportCategory("Obstacle Avoidance")]
        [Export(PropertyHint.Range, "10,500,10")]
        public float RaycastDistance = 100f;

        [Export]
        public bool EnableObstacleAvoidance = true;

        [ExportCategory("Interrupt")]
        [Export]
        public bool EnableDamageImmunity = true;

        [Export]
        public bool EnableSuperArmor = true;

        private float _timer;
        private Vector2 _dashDirection = Vector2.Zero;
        private bool? _previousIgnoreHitStateOnDamage;

        public override void Enter()
        {
            _timer = Mathf.Max(DashDuration, 0.01f);

            Vector2 preferredDirection = Vector2.Zero;
            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (toPlayer != Vector2.Zero)
            {
                preferredDirection = -toPlayer;
            }
            else
            {
                preferredDirection = Enemy.FacingRight ? Vector2.Left : Vector2.Right;
            }

            // 在后撤前检测障碍并确定最终方向
            if (EnableObstacleAvoidance && preferredDirection != Vector2.Zero)
            {
                _dashDirection = FindClearDirection(preferredDirection).Normalized();
            }
            else
            {
                _dashDirection = preferredDirection.Normalized();
            }

            if (EnableSuperArmor)
            {
                _previousIgnoreHitStateOnDamage = Enemy.IgnoreHitStateOnDamage;
                Enemy.IgnoreHitStateOnDamage = true;
            }
            else
            {
                _previousIgnoreHitStateOnDamage = null;
            }

            Enemy.DamageIntercepted -= OnEnemyDamageIntercepted;
            if (EnableDamageImmunity)
            {
                Enemy.DamageIntercepted += OnEnemyDamageIntercepted;
            }

            if (Enemy.AnimPlayer != null && !string.IsNullOrEmpty(AnimationName) && Enemy.AnimPlayer.HasAnimation(AnimationName))
            {
                Enemy.AnimPlayer.Play(AnimationName);
            }
        }

        public override void Exit()
        {
            if (Enemy != null && GodotObject.IsInstanceValid(Enemy))
            {
                Enemy.Velocity = Vector2.Zero;
                Enemy.DamageIntercepted -= OnEnemyDamageIntercepted;

                if (_previousIgnoreHitStateOnDamage.HasValue)
                {
                    Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage.Value;
                    _previousIgnoreHitStateOnDamage = null;
                }
            }
        }

        public override bool CanExitTo(string nextStateName)
        {
            if (_timer <= 0f)
            {
                return true;
            }

            if (nextStateName == "Dying" || nextStateName == "Dead")
            {
                return true;
            }

            if (EnableSuperArmor)
            {
                return false;
            }

            return nextStateName == "Hit"
                || nextStateName == "Frozen"
                || nextStateName == "CooldownFrozen";
        }

        private bool OnEnemyDamageIntercepted(GameActor.DamageEventArgs args)
        {
            if (!EnableDamageImmunity || _timer <= 0f)
            {
                return false;
            }

            args.IsBlocked = true;
            return true;
        }

        public override void PhysicsUpdate(double delta)
        {
            if (Enemy == null || !GodotObject.IsInstanceValid(Enemy))
            {
                return;
            }

            _timer -= (float)delta;

            // 直线后撤，方向已在 Enter() 中确定
            Enemy.Velocity = _dashDirection * DashSpeed;
            Enemy.MoveAndSlide();

            if (_timer <= 0f)
            {
                Enemy.Velocity = Vector2.Zero;

                if (Enemy.StateMachine != null)
                {
                    if (!string.IsNullOrEmpty(NextStateName) && Enemy.StateMachine.HasState(NextStateName))
                    {
                        Enemy.StateMachine.ChangeState(NextStateName);
                    }
                    else
                    {
                        Enemy.StateMachine.ChangeState("Walk");
                    }
                }
            }
        }

        /// <summary>
        /// 检测给定方向是否通畅，如果不通畅则尝试替代方向。
        /// 优先级：主方向 > 左前45° > 右前45° > 左90° > 右90°
        /// </summary>
        private Vector2 FindClearDirection(Vector2 preferredDirection)
        {
            if (preferredDirection == Vector2.Zero)
            {
                return Vector2.Zero;
            }

            preferredDirection = preferredDirection.Normalized();

            // 需要尝试的方向列表（角度偏移）
            var directionsToTry = new[]
            {
                0f,      // 主方向
                -45f,    // 左前45°
                45f,     // 右前45°
                -90f,    // 左90°
                90f,     // 右90°
                180f,    // 后退180°
            };

            foreach (float angleDelta in directionsToTry)
            {
                Vector2 testDirection = preferredDirection.Rotated(Mathf.DegToRad(angleDelta));
                if (IsDirectionClear(testDirection))
                {
                    return testDirection;
                }
            }

            // 所有方向都有障碍，返回原方向（让游戏逻辑处理碰撞）
            return preferredDirection;
        }

        /// <summary>
        /// 使用射线检测判断给定方向是否通畅。
        /// </summary>
        private bool IsDirectionClear(Vector2 direction)
        {
            if (Enemy == null || direction == Vector2.Zero)
            {
                return false;
            }

            var query = PhysicsRayQueryParameters2D.Create(
                Enemy.GlobalPosition,
                Enemy.GlobalPosition + direction.Normalized() * RaycastDistance
            );

            // 排除自身和玩家的碰撞检测
            query.CollisionMask = Enemy.CollisionMask;

            var result = Enemy.GetWorld2D().DirectSpaceState.IntersectRay(query);

            // 如果没有碰撞则返回 true（方向通畅）
            return result.Count == 0;
        }
    }
}
