using Godot;
using Kuros.Actors.Enemies;
using Kuros.Core;
using Kuros.Environments;

namespace Kuros.Actors.Enemies.Attacks
{
	/// <summary>
	/// 炮台的罐子协助攻击：沿用 <see cref="EnemyAttackTemplate.Effects"/> 在 Active 生成
	/// Jar_mozhua_assist，并在**生成之后**开始独立计数。
	///   · 计数内罐子被致命伤害击破（IsDeadOrDying）= 失败 → 炮台进入 N 秒 JarAssistStun；
	///   · 罐子自然演完自毁（DestroySelf，未经过死亡）= 成功 → 调用强化接口；
	///   · 计时结束仍存活同样算成功。
	/// 判定是单次的：失败锁定后忽略 jar_destroy 动画结束时的那次自毁。
	/// </summary>
	public partial class JarAssistAttack : EnemyAttackTemplate
	{
		/// <summary>罐子生成后的计数时长（秒）。</summary>
		[Export(PropertyHint.Range, "0.5,20,0.1")] public float AssistSeconds { get; set; } = 7.5f;
		/// <summary>罐子被击破后炮台的眩晕时长（秒）。</summary>
		[Export(PropertyHint.Range, "0.5,20,0.1")] public float StunSeconds { get; set; } = 7.5f;
		[Export] public bool EnableDebugLogs { get; set; } = false;

		private DestructibleStageController? _activeJar;
		private float _remaining;
		private bool _tracking;
		private bool _resolved;
		private bool _deathSubscribed;

		protected override void OnInitialized()
		{
			base.OnInitialized();
			SetProcess(true);
		}

		public override bool CanStart()
		{
			if (_tracking) return false;
			if (Enemy is not EnemyF1RogueAITurret) return false;
			return base.CanStart();
		}

		protected override void OnAttackStarted()
		{
			base.OnAttackStarted();
			if (!_tracking)
			{
				_resolved = false;
				_activeJar = null;
			}
		}

		protected override void OnActivePhase()
		{
			// 只生成 Effects（罐子），不调用 base（避免 PerformAttackNow 结算伤害）。
			SpawnEffectAtEnemy(EffectSpawnTiming.OnActive);
		}

		/// <summary>罐子要留在原地并与玩家同一层级（世界层）：生成在玩家父级（World）下——
		/// 不能挂在敌人父级（滑槽 Mount 下会继承滑轨变换随炮台漂移，z 排序也跟着滑轨）。
		/// 必须在 AddChild 之前决定父级：事后再 Reparent 不会重跑 _Ready，罐子的动画订阅会断。</summary>
		protected override Node? ResolveEffectParent(AttackEffectEntry? entry, Node2D effect)
		{
			if (effect is DestructibleStageController)
			{
				var player = Enemy?.PlayerTarget;
				if (player != null && GodotObject.IsInstanceValid(player) && player.GetParent() is Node world)
					return world;

				if (GetTree()?.GetFirstNodeInGroup("player") is Node2D groupPlayer && groupPlayer.GetParent() is Node groupWorld)
					return groupWorld;
			}

			return base.ResolveEffectParent(entry, effect);
		}

		protected override void OnAttackEffectSpawned(Node2D effect, AttackEffectEntry? entry)
		{
			base.OnAttackEffectSpawned(effect, entry);

			if (_tracking) return;
			if (effect is not DestructibleStageController jar) return;

			_activeJar = jar;
			_remaining = Mathf.Max(0.1f, AssistSeconds);
			_tracking = true;
			_resolved = false;

			if (!_deathSubscribed)
			{
				GameActor.DeathFinalized += OnJarDeathFinalized;
				_deathSubscribed = true;
			}

			if (EnableDebugLogs)
				GD.Print($"[{AttackName}] 罐子已生成，开始计数 {_remaining:F1}s");
		}

		private void OnJarDeathFinalized(GameActor actor)
		{
			if (!_tracking || _resolved) return;
			if (actor != _activeJar) return;
			ResolveFailure();
		}

		public override void _Process(double delta)
		{
			if (!_tracking || _resolved) return;

			if (Enemy == null || !GodotObject.IsInstanceValid(Enemy) || Enemy.IsDeadOrDying)
			{
				StopTracking();
				return;
			}

			// 没有经过死亡就被释放 = 自然自毁 → 成功
			if (_activeJar == null || !GodotObject.IsInstanceValid(_activeJar))
			{
				ResolveSuccess();
				return;
			}

			// 兜底（正常由 DeathFinalized 立即锁定）
			if (_activeJar.IsDeadOrDying)
			{
				ResolveFailure();
				return;
			}

			_remaining -= (float)delta;
			if (_remaining <= 0f)
				ResolveSuccess();
		}

		public override void _ExitTree()
		{
			StopTracking();
			base._ExitTree();
		}

		private void ResolveSuccess()
		{
			if (!_tracking || _resolved) return;
			_resolved = true;
			_tracking = false;
			UnsubscribeDeath();
			_activeJar = null;

			EnsureCooldown(AssistSeconds);
			if (Enemy is EnemyF1RogueAITurret host)
				host.OnJarAssistCompleted();

			if (EnableDebugLogs)
				GD.Print($"[{AttackName}] 罐子协助成功");
		}

		private void ResolveFailure()
		{
			if (!_tracking || _resolved) return;
			_resolved = true;
			_tracking = false;
			UnsubscribeDeath();
			_activeJar = null;

			EnsureCooldown(AssistSeconds);
			if (Enemy is EnemyF1RogueAITurret host)
				host.EnterJarAssistStun(StunSeconds);

			if (EnableDebugLogs)
				GD.Print($"[{AttackName}] 罐子被击破 → 眩晕 {StunSeconds:F1}s");
		}

		private void StopTracking()
		{
			_tracking = false;
			_resolved = true;
			_activeJar = null;
			UnsubscribeDeath();
		}

		private void UnsubscribeDeath()
		{
			if (!_deathSubscribed) return;
			GameActor.DeathFinalized -= OnJarDeathFinalized;
			_deathSubscribed = false;
		}
	}
}
