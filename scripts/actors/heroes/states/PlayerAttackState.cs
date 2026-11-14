using Godot;
using System.Collections.Generic;
using Kuros.Actors.Heroes.Attacks;

namespace Kuros.Actors.Heroes.States
{
	public partial class PlayerAttackState : PlayerState
	{

		public float AttackAnimationSpeed = 1.2f;
		private float _originalSpeedScale = 1.0f;
		
		private readonly List<PlayerAttackTemplate> _attackTemplates = new();
		private PlayerAttackTemplate? _activeTemplate;

		public IReadOnlyList<PlayerAttackTemplate> AttackTemplates => _attackTemplates;
		/// <summary>仅当前正在执行的攻击模板可以提供阶段坠落免疫。</summary>
		public bool ProvidesFallImmunity => _activeTemplate?.IsRunning == true && _activeTemplate.ProvidesFallImmunity;

		/// <summary>
		/// 当前攻击模板是否处于 Recovery（后摇）阶段：攻击判定已完成，
		/// 该阶段是连击之间的窗口期，允许切换武器（预输入应用点）。
		/// </summary>
		public bool IsInRecovery => _activeTemplate?.IsInRecovery == true;

		protected override void _ReadyState()
			{
			base._ReadyState();

			foreach (Node child in GetChildren())
			{
				if (child is PlayerAttackTemplate template)
			{
					template.Initialize(Player);
					_attackTemplates.Add(template);
				}
			}

			if (_attackTemplates.Count == 0)
			{
				GD.PushWarning($"{Name}: No PlayerAttackTemplate found. Attach at least one attack to this state.");
			}
			}

		public override void Enter()
		{
			// 速度归零：攻击前移动速度由移动状态写入 CurrentMoveSpeed（Run/Walk/Dash），模板启动时查询——无需在此捕获
			Player.Velocity = Vector2.Zero;
			AlignFacingToInput();
			
			// Save original speed scale before modifying
			if (Actor.AnimPlayer != null)
			{
				_originalSpeedScale = Actor.AnimPlayer.SpeedScale;
				// Set animation playback speed only for attack animation
				Actor.AnimPlayer.SpeedScale = AttackAnimationSpeed;
			}

			if (!TryStartTemplateAttack())
			{
				ChangeState("Idle");
			}
		}

		public override void Exit()
		{
			_activeTemplate?.Cancel(clearCooldown: true);
			_activeTemplate = null;
			
			// Restore original animation speed when leaving attack state
			if (Actor.AnimPlayer != null)
			{
				Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
			}
		}

		public override void PhysicsUpdate(double delta)
		{
			if (_activeTemplate == null)
			{
				ChangeState("Idle");
				return;
			}

			// 缓冲/消费 dash 预输入
		if (IsActionJustPressed("dash"))
		{
			if (_activeTemplate.CanDashCancel)
			{
				_activeTemplate.Cancel(clearCooldown: true);
				ChangeState("Dash");
				return;
			}
			BufferInput("dash", DashPriority);
		}

		// Recovery 期间检查缓冲区
		if (_activeTemplate.IsInRecovery)
		{
			var buffered = ConsumeBufferedInput();
			if (buffered == "dash")
			{
				_activeTemplate.Cancel(clearCooldown: true);
				ChangeState("Dash");
				return;
			}

			// Run 打断：Recovery 期间按住移动键直接切 Run（长按实时检测，无需预输入——
			// dash 是瞬时按下需缓冲，移动键按住期间持续可读）。
			// 必须在模板 Tick 之前检查：Tick 内 Recovery 移动会走 _wantsMove（攻击结束→Idle→Run 两帧中转），
			// 这里直接切 Run 更顺滑。按住攻击键时让位给连击重启（Tick 内处理）。
			if (_activeTemplate.AllowRecoveryCancel && !IsActionPressed("attack"))
			{
				Vector2 moveInput = GetMovementInput();
				if (moveInput.LengthSquared() > 0.01f)
				{
					_activeTemplate.Cancel(clearCooldown: true);
					ChangeState("Run");
					return;
				}
			}
		}

		_activeTemplate.Tick(delta);

			Player.MoveAndSlide();
			Player.ClampPositionToScreen();

			if (!_activeTemplate.IsRunning)
			{
				if (_activeTemplate.WantsRestart && TryStartTemplateAttack())
					return;
				ChangeState("Idle");
			}
		}
		
		private bool TryStartTemplateAttack()
		{
			string requestedState = Player.ConsumeAttackRequestSource();
			if (string.IsNullOrEmpty(requestedState))
			{
				requestedState = Player.LastMovementStateName;
			}

			var selectedStack = Player.InventoryComponent?.GetSelectedQuickBarStack();
			bool throwOnCooldown = selectedStack?.IsThrowOnCooldown == true;

			foreach (var template in _attackTemplates)
			{
				template.SetTriggerSourceState(requestedState);
				if (!template.HasWeaponRequirement || !template.IsWeaponRequirementSatisfied())
				{
					continue;
				}

				if (throwOnCooldown && template.HasWeaponRequirement)
					continue;

				if (template.TryStart(checkInput: false))
				{
					_activeTemplate = template;
					return true;
				}
			}

			foreach (var template in _attackTemplates)
			{
				template.SetTriggerSourceState(requestedState);
				if (template.HasWeaponRequirement)
				{
					continue;
				}

				if (template.TryStart(checkInput: false))
				{
					_activeTemplate = template;
					return true;
			}
			}

			return false;
		}

		private void AlignFacingToInput()
		{
			Vector2 input = GetMovementInput();
			if (Mathf.Abs(input.X) > 0.01f)
			{
				Player.FlipFacing(input.X > 0f);
			}
		}
	}
}
