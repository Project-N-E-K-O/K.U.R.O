using Godot;
using System.Collections.Generic;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyAttackState : EnemyState
    {
        private const float WINDUP_TIME = 0.2f;
        private const float RECOVERY_TIME = 0.35f;

        private readonly List<EnemyAttackTemplate> _attackTemplates = new();
        private EnemyAttackTemplate? _activeTemplate;

        private float _windupTimer;
        private float _recoveryTimer;
        private bool _attackPerformed;

        protected override void _ReadyState()
        {
            base._ReadyState();

            foreach (Node child in GetChildren())
            {
                if (child is EnemyAttackTemplate template)
                {
                    template.Initialize(Enemy);
                    _attackTemplates.Add(template);
                }
            }
        }

        public override void Enter()
        {
            Enemy.Velocity = Vector2.Zero;

            if (!TryStartTemplateAttack())
            {
                Enemy.AnimPlayer?.Play("animations/attack");
                _windupTimer = WINDUP_TIME;
                _recoveryTimer = RECOVERY_TIME;
                _attackPerformed = false;
            }
        }

        public override void Exit()
        {
            _activeTemplate?.Cancel(clearCooldown: true);
            _activeTemplate = null;
        }

        public override void PhysicsUpdate(double delta)
        {
            if (!HasPlayer)
            {
                ChangeState("Idle");
                return;
            }

            if (ProcessTemplateAttack(delta))
            {
                return;
            }

            Vector2 toPlayer = Enemy.GetDirectionToPlayer();
            if (toPlayer.X != 0)
            {
                Enemy.FlipFacing(toPlayer.X > 0);
            }

            Enemy.MoveAndSlide();

            if (!_attackPerformed)
            {
                _windupTimer -= (float)delta;
                if (_windupTimer <= 0)
                {
                    Enemy.PerformAttack();
                    _attackPerformed = true;
                }
                return;
            }

            _recoveryTimer -= (float)delta;
            if (_recoveryTimer > 0) return;

            ChangeToNextState();
        }

        private bool TryStartTemplateAttack()
        {
            if (_attackTemplates.Count == 0) return false;

            _activeTemplate = SelectTemplate();
            if (_activeTemplate == null) return false;

            if (_activeTemplate.TryStart())
            {
                return true;
            }

            _activeTemplate = null;
            return false;
        }

        private EnemyAttackTemplate? SelectTemplate()
        {
            foreach (var template in _attackTemplates)
            {
                if (template.CanStart())
                {
                    return template;
                }
            }

            return null;
        }

        private bool ProcessTemplateAttack(double delta)
        {
            if (_activeTemplate == null) return false;

            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();

            _activeTemplate.Tick(delta);
            if (_activeTemplate.IsRunning)
            {
                return true;
            }

            _activeTemplate = null;

            if (TryStartTemplateAttack())
            {
                return true;
            }

            ChangeToNextState();
            return true;
        }

        private void ChangeToNextState()
        {
            if (Enemy.IsPlayerWithinDetectionRange())
            {
                if (Enemy.IsPlayerInAttackRange() && Enemy.AttackTimer <= 0)
                {
                    ChangeState("Attack");
                }
                else
                {
                    ChangeState("Walk");
                }
            }
            else
            {
                ChangeState("Idle");
            }
        }
    }
}

