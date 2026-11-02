using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Actors.Enemies.States
{
	/// <summary>罐子协助失败触发的炮台眩晕：计时结束回 Idle。
	/// 动画分为 Down → Loop → Up，三个阶段合计仍为 Duration。</summary>
	public partial class EnemyJarAssistStunState : EnemyState
	{
		[Export(PropertyHint.Range, "0,20,0.1")] public float Duration { get; set; } = 7.5f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float DownDuration { get; set; } = 0.6666667f;
		[Export(PropertyHint.Range, "0,5,0.01")] public float UpDuration { get; set; } = 0.6666667f;

		public enum StunPhase { Down, Loop, Up }
		public StunPhase CurrentPhase { get; private set; }
		public int CycleId { get; private set; }
		public float DownTimeScale => _downTime > 0f ? DownDuration / _downTime : 1f;
		public float UpTimeScale => _upTime > 0f ? UpDuration / _upTime : 1f;
		private float _downTime;
		private float _upTime;
		private float _elapsed;

		private float _timer;
		private EnemyF1RogueAITurretAttackController? _controller;
		private bool _previousIgnoreHitStateOnDamage;
		private bool _running;
		private bool _skipDown;
		public bool IsActive => _running && _timer > 0f;
		public float RemainingTime => Mathf.Max(_timer, 0f);

		public override void Enter()
		{
			if (_running)
			{
				// Hit 返回不重播起步，也不重新计时；临近结束时直接进入恢复段。
				_skipDown = true;
				CurrentPhase = _timer <= _upTime ? StunPhase.Up : StunPhase.Loop;
				Enemy.Velocity = Vector2.Zero;
				return;
			}
			_running = true;
			_skipDown = false;
			_timer = Mathf.Max(0.1f, Duration);
			_elapsed = 0f;
			CycleId++;
			// 极短眩晕按比例压缩起步/恢复，不额外延长战斗眩晕时间。
			float scale = Mathf.Min(1f, _timer / Mathf.Max(DownDuration + UpDuration, 0.001f));
			_downTime = Mathf.Max(DownDuration, 0f) * scale;
			_upTime = Mathf.Max(UpDuration, 0f) * scale;
			CurrentPhase = _downTime > 0f ? StunPhase.Down : StunPhase.Loop;
			Enemy.Velocity = Vector2.Zero;

			// 眩晕允许受击硬直；倒计时由本节点独立推进，Hit 不会延长眩晕。
			_previousIgnoreHitStateOnDamage = Enemy.IgnoreHitStateOnDamage;
			Enemy.IgnoreHitStateOnDamage = false;

			// 炮台 AI 会在空闲循环里强制切回 Attack，必须暂停攻击控制器，否则眩晕会被立刻顶掉。
			_controller = Enemy.GetNodeOrNull<EnemyF1RogueAITurretAttackController>("StateMachine/Attack/AttackController");
			if (_controller != null)
			{
				_controller.Cancel(clearCooldown: true);
				_controller.SetPhysicsProcess(false);
			}
		}

		public override void Exit()
		{
			// Exit 执行时 CurrentState 仍是本状态，延后检查才能识别目标是否为 Hit。
			Callable.From(ReconcileExit).CallDeferred();
		}

		private void ReconcileExit()
		{
			if (!_running || !IsInsideTree()) return;
			var current = Enemy.StateMachine?.CurrentState;
			if (current != this && current?.Name != "Hit") FinishCycle();
		}

		private void FinishCycle()
		{
			if (!_running) return;
			_running = false;
			Enemy.IgnoreHitStateOnDamage = _previousIgnoreHitStateOnDamage;
			if (_controller != null && GodotObject.IsInstanceValid(_controller))
				_controller.SetPhysicsProcess(true);
			_controller = null;
		}

		public override void PhysicsUpdate(double delta)
		{
			Enemy.Velocity = Vector2.Zero;
		}

		// StateMachine 只更新当前状态；节点回调使 Hit 期间的眩晕时间继续流逝。
		public override void _PhysicsProcess(double delta)
		{
			if (!_running) return;
			var current = Enemy.StateMachine?.CurrentState;
			if (Enemy.IsDeadOrDying || (current != this && current?.Name != "Hit"))
			{
				FinishCycle();
				return;
			}
			_timer -= (float)delta;
			_elapsed += (float)delta;
			if (_timer <= 0f)
			{
				_timer = 0f;
				// Hit 自己完整播放；其结束时发现无剩余眩晕便不再恢复。
				if (current == this)
				{
					ChangeState("Idle");
					FinishCycle();
				}
				return;
			}
			CurrentPhase = !_skipDown && _elapsed < _downTime ? StunPhase.Down
				: _timer <= _upTime ? StunPhase.Up : StunPhase.Loop;
		}

		public bool TryResumeAfterHit()
		{
			if (!_running) return false;
			if (_timer <= 0f || Enemy.IsDeadOrDying)
			{
				FinishCycle();
				return false;
			}
			return Enemy.StateMachine?.ChangeState("JarAssistStun") == true;
		}

		public override void _ExitTree()
		{
			FinishCycle();
			base._ExitTree();
		}
	}
}
