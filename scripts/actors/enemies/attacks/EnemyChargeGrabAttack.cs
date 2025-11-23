using Godot;
using Kuros.Actors.Heroes.States;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 冲刺抓取攻击：
    /// 1. 检测区域内发现玩家后进入预热（定身）；
    /// 2. 预热结束后向前冲刺；
    /// 3. 冲刺完成后若玩家处于抓取区域，则令其进入 Frozen 状态并触发逃脱流程；
    /// 4. 玩家逃脱失败时受到伤害，随后进入冷却。
    /// </summary>
    public partial class EnemyChargeGrabAttack : EnemyAttackTemplate
    {
        [ExportCategory("Areas")]
        [Export] public NodePath DetectionAreaPath = new NodePath();
        [Export] public NodePath GrabAreaPath = new NodePath();

        [ExportCategory("Dash")]
        [Export(PropertyHint.Range, "10,2000,10")] public float DashSpeed = 600f;
        [Export(PropertyHint.Range, "10,2000,10")] public float DashDistance = 220f;
        [Export] public bool LockFacingDuringDash = true;

        [ExportCategory("Effects")]
        [Export(PropertyHint.Range, "0,10,0.1")] public float AppliedFrozenDuration = 2.0f;
        [Export(PropertyHint.Range, "0,1000,1")] public int DamageOnEscapeFailure = 20;

        private Area2D? _detectionArea;
        private Area2D? _grabArea;
        private EnemyAttackController? _controller;
        private Vector2 _dashDirection = Vector2.Right;

        protected override void OnInitialized()
        {
            base.OnInitialized();
            _controller = GetParent() as EnemyAttackController;

            if (!DetectionAreaPath.IsEmpty)
            {
                _detectionArea = Enemy.GetNodeOrNull<Area2D>(DetectionAreaPath);
                if (_detectionArea != null)
                {
                    _detectionArea.BodyEntered += OnDetectionAreaBodyEntered;
                }
            }

            if (!GrabAreaPath.IsEmpty)
            {
                _grabArea = Enemy.GetNodeOrNull<Area2D>(GrabAreaPath);
            }

            if (_grabArea == null)
            {
                _grabArea = AttackArea;
            }
        }

        public override void _ExitTree()
        {
            if (_detectionArea != null)
            {
                _detectionArea.BodyEntered -= OnDetectionAreaBodyEntered;
            }
            base._ExitTree();
        }

        public override bool CanStart()
        {
            if (!base.CanStart()) return false;

            var player = Enemy.PlayerTarget;
            if (player == null) return false;

            var area = _detectionArea ?? AttackArea;
            if (area == null) return true;

            return area.OverlapsBody(player);
        }

        protected override void OnActivePhase()
        {
            PrepareDash();
        }

        protected override void OnRecoveryStarted()
        {
            base.OnRecoveryStarted();
            Enemy.Velocity = Vector2.Zero;
            TryExecuteGrab();
        }

        private void PrepareDash()
        {
            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            float horizontal = toPlayer.X;
            if (Mathf.Abs(horizontal) < 0.01f)
            {
                horizontal = Enemy.FacingRight ? 1f : -1f;
            }

            _dashDirection = new Vector2(Mathf.Sign(horizontal), 0f);

            if (LockFacingDuringDash && _dashDirection.X != 0)
            {
                Enemy.FlipFacing(_dashDirection.X > 0);
            }

            float dashTime = Mathf.Max(DashDistance / DashSpeed, 0.05f);
            ActiveDuration = dashTime;

            Enemy.Velocity = _dashDirection * DashSpeed;
        }

        private void TryExecuteGrab()
        {
            var player = Enemy.PlayerTarget;
            if (player == null) return;

            if (!IsPlayerInsideGrabZone(player))
            {
                return;
            }

            ApplyFrozenState(player);

            bool escaped = EvaluateEscapeSequence(player);
            if (!escaped)
            {
                player.TakeDamage(DamageOnEscapeFailure);
            }

            ReleasePlayer(player);
        }

        private bool IsPlayerInsideGrabZone(SamplePlayer player)
        {
            if (_grabArea != null)
            {
                return _grabArea.OverlapsBody(player);
            }

            return AttackArea != null && AttackArea.OverlapsBody(player);
        }

        private void ApplyFrozenState(SamplePlayer player)
        {
            var frozenState = player.StateMachine?.GetNodeOrNull<PlayerFrozenState>("Frozen");
            if (frozenState != null)
            {
                frozenState.FrozenDuration = AppliedFrozenDuration;
            }

            player.StateMachine?.ChangeState("Frozen");
        }

        private void ReleasePlayer(SamplePlayer player)
        {
            if (player.StateMachine?.CurrentState?.Name == "Frozen")
            {
                player.StateMachine.ChangeState("Idle");
            }
        }

        private void OnDetectionAreaBodyEntered(Node body)
        {
            if (Enemy == null || body != Enemy.PlayerTarget) return;
            if (_controller == null) return;
            if (IsRunning || IsOnCooldown) return;
            if (_controller.PeekQueuedAttack() != this) return;

            var currentState = Enemy.StateMachine?.CurrentState?.Name;
            if (currentState != "Attack")
            {
                Enemy.StateMachine?.ChangeState("Attack");
            }
        }

        /// <summary>
        /// 逃脱流程，默认返回 false。后续可在这里实现“左右输入若干次”等判定。
        /// </summary>
        protected virtual bool EvaluateEscapeSequence(SamplePlayer player)
        {
            // TODO: 接入自定义逃脱判定逻辑（例如统计特定输入次数）
            return false;
        }
    }
}

