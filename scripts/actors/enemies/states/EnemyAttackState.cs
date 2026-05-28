using Godot;
using System.Collections.Generic;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyAttackState : EnemyState
    {
        [Export] public bool ExitToWalkWhenOutOfAttackRange = false;

        private readonly List<EnemyAttackTemplate> _attackTemplates = new();
        private EnemyAttackTemplate? _activeTemplate;

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
			TryStartTemplateAttack();
        }

        public override void Exit()
        {
            // clearCooldown: false 以保留子攻击的冷却计时，避免退出攻击状态时意外重置 CD。
            // EnemyAttackController 内部已按需处理玩家离开/被打断时的 CD 清理逻辑。
            _activeTemplate?.Cancel(clearCooldown: false);
            _activeTemplate = null;
        }

        public override void PhysicsUpdate(double delta)
        {
            bool playerDetected = Enemy.IsPlayerWithinDetectionRange();
            bool playerInAttackRange = Enemy.IsPlayerInAttackRange();

            //使用 IsPlayerWithinDetectionRange 检查玩家，这会刷新玩家引用
            // 如果玩家不在检测范围内且不在攻击范围内，直接切换到 Idle 状态
            if (!playerDetected && !playerInAttackRange)
            {
                ChangeState("Idle");
                return;
            }

            // 如果玩家在攻击范围外但在检测范围内，并且设置了 ExitToWalkWhenOutOfAttackRange，则切换到 Walk 状态
            if (ExitToWalkWhenOutOfAttackRange && playerDetected && !playerInAttackRange)
            {
                ChangeState("Walk");
                return;
            }

		if (!ProcessTemplateAttack(delta))
            {
                // 没有活跃模板时直接退出到合适状态，等Walk/Idle检测CanStartAttack()后再重入
                ChangeToNextState();
            }
        }

        protected virtual bool TryStartTemplateAttack()
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
			var template = _activeTemplate;
			if (template == null) return false;

            Enemy.MoveAndSlide();
            Enemy.ClampPositionToScreen();

			template.Tick(delta);
			if (template.IsRunning)
            {
                return true;
            }

            // 攻击模板执行完毕：立即退出到 Walk/Idle，由它们检测 CanStartAttack() 重新发起攻击
            _activeTemplate = null;
            ChangeToNextState();
            return true;
        }

        protected virtual void ChangeToNextState()
        {
            bool playerDetected = Enemy.IsPlayerWithinDetectionRange();
            bool playerInAttackRange = Enemy.IsPlayerInAttackRange();

            if (!playerDetected)
            {
                ChangeState("Idle");
                return;
            }

            // 攻击完成后始终退出到 Walk/Idle（ChangeState("Attack") 在同状态下是no-op）
            // Walk/Idle 每帧检查 CanStartAttack()，CD到期后自动重新进入 Attack
            if (playerInAttackRange)
            {
                // 玩家在攻击范围内：原地等待CD（Idle 会在 CanStartAttack() 为真时切回 Attack）
                ChangeState("Idle");
            }
            else
            {
                // 玩家在检测范围内但不在攻击范围：追击（Walk 同样检查 CanStartAttack()）
                ChangeState("Walk");
            }
        }
    }
}
