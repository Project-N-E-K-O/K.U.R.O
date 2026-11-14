using Godot;
using Kuros.Actors.Enemies;
using Kuros.Core;
using Kuros.Environments;
using Kuros.Fx;

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
		[Export] public Texture2D? IndicatorIcon { get; set; }
		/// <summary>协助追踪期间播放的循环特效，由本攻击清除。</summary>
		[Export] public PackedScene? AssistEffectScene { get; set; }
		/// <summary>相对炮台根节点的挂点。留空时挂在炮台根节点。</summary>
		[Export] public NodePath AssistEffectAnchorPath { get; set; } = new("");
		/// <summary>罐子根节点到地面点的世界偏移；采样验证地面点，生成原点为地面点减去此偏移。</summary>
		[Export] public Vector2 SpawnGroundOffset { get; set; }

		private DestructibleStageController? _activeJar;
		private CanvasLayer? _jarIndicator;
		private Node2D? _assistEffect;
		private float _remaining;
		private bool _tracking;
		private bool _resolved;
		private bool _deathSubscribed;

		protected override void OnInitialized()
		{
			base.OnInitialized();
			SetProcess(true);
		}

		protected override bool CanStartCore()
		{
			if (_tracking) return false;
			if (Enemy is not EnemyF1RogueAITurret) return false;
			return base.CanStartCore();
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

		/// <summary>以敌人为中心的世界坐标采样半径；不随 Spine 或敌人缩放。</summary>
		[Export(PropertyHint.Range, "1,10000,1,or_greater")] public float SpawnRadius { get; set; } = 3000f;
		[Export] public Vector2 SpawnCenterOffset { get; set; }
		/// <summary>相对敌人根节点解析，用其导航地图与导航层，不依赖关卡路径。</summary>
		[Export] public NodePath NavigationAgentPath { get; set; } = new("NavigationAgent2D");
		[Export(PropertyHint.Range, "1,256,1")] public int SpawnSampleAttempts { get; set; } = 48;
		private const float NavigationTolerance = 1f;

		protected override bool TryResolveEffectSpawnPosition(Node2D effect, AttackEffectEntry? entry,
			ref Vector2 position, ref Node2D? marker)
		{
			if (effect is not DestructibleStageController) return base.TryResolveEffectSpawnPosition(effect, entry, ref position, ref marker);
			marker = null; // 罐子使用独立圆形采样，不再使用 entry Marker/EffectOffset。
			var agent = Enemy?.GetNodeOrNull<NavigationAgent2D>(NavigationAgentPath);
			if (agent == null) return SpawnUnavailable("缺少 NavigationAgent2D");
			var map = agent.GetNavigationMap();
			if (!map.IsValid || NavigationServer2D.MapGetIterationId(map) == 0 || agent.NavigationLayers == 0)
				return SpawnUnavailable("导航地图尚未就绪或导航层为空");
			Vector2 center = Enemy!.GlobalPosition + SpawnCenterOffset;
			Vector2 origin = NavigationServer2D.MapGetClosestPoint(map, Enemy.GlobalPosition);
			float radius = Mathf.Max(1, SpawnRadius);
			for (int i = 0; i < Mathf.Clamp(SpawnSampleAttempts, 1, 256); i++)
			{
				// sqrt 随机半径使圆内按面积均匀，不把点堆到圆心或导航边缘。
				Vector2 ground = center + Vector2.FromAngle((float)GD.RandRange(0.0, Mathf.Tau))
					* (Mathf.Sqrt(GD.Randf()) * radius);
				var owner = NavigationServer2D.MapGetClosestPointOwner(map, ground);
				if (!owner.IsValid || !NavigationServer2D.RegionGetEnabled(owner)
					|| (NavigationServer2D.RegionGetNavigationLayers(owner) & agent.NavigationLayers) == 0) continue;
				// 仅用最近点验证在网格内，不把圆外/坑边的无效候选强制吸附到网格。
				if (NavigationServer2D.MapGetClosestPoint(map, ground).DistanceSquaredTo(ground) > NavigationTolerance * NavigationTolerance) continue;
				Vector2 spawn = ground - SpawnGroundOffset;
				var path = NavigationServer2D.MapGetPath(map, origin, ground, true, agent.NavigationLayers);
				if (path.Length == 0 || path[^1].DistanceSquaredTo(ground) > NavigationTolerance * NavigationTolerance) continue;
				position = spawn;
				return true;
			}
			return SpawnUnavailable("圆形范围内未采样到可达的导航落点");
		}

		private bool SpawnUnavailable(string reason)
		{
			if (EnableDebugLogs) GD.Print($"[{AttackName}] {reason}，跳过罐子生成。");
			return false;
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
			_jarIndicator = new CanvasLayer { Name = "JarAssistIndicatorLayer", Layer = 8 };
			_jarIndicator.AddChild(new AssistJarIndicator
			{
				Target = jar.GetNodeOrNull<Marker2D>("IndicatorAnchor") ?? (Node2D)jar,
				Icon = IndicatorIcon
			});
			AddChild(_jarIndicator);
			SpawnAssistEffect();

			if (!_deathSubscribed)
			{
				GameActor.DeathFinalized += OnJarDeathFinalized;
				_deathSubscribed = true;
			}

			if (EnableDebugLogs)
				GD.Print($"[{AttackName}] 罐子已生成，开始计数 {_remaining:F1}s");
		}

		private void SpawnAssistEffect()
		{
			if (AssistEffectScene == null || Enemy == null) return;
			var anchor = AssistEffectAnchorPath.IsEmpty
				? Enemy
				: Enemy.GetNodeOrNull<Node2D>(AssistEffectAnchorPath);
			if (anchor == null)
			{
				GD.PushWarning($"[{AttackName}] 协助特效挂点不存在：{AssistEffectAnchorPath}");
				return;
			}

			var instance = AssistEffectScene.Instantiate();
			if (instance is not Node2D visual)
			{
				instance.Free();
				GD.PushWarning($"[{AttackName}] AssistEffectScene 的根节点必须是 Node2D");
				return;
			}

			_assistEffect = visual;
			anchor.AddChild(visual);
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
			StopTracking();

			EnsureCooldown(AssistSeconds);
			if (Enemy is EnemyF1RogueAITurret host)
				host.OnJarAssistCompleted();

			if (EnableDebugLogs)
				GD.Print($"[{AttackName}] 罐子协助成功");
		}

		private void ResolveFailure()
		{
			if (!_tracking || _resolved) return;
			StopTracking();

			EnsureCooldown(AssistSeconds);
			if (Enemy is EnemyF1RogueAITurret host)
				host.EnterJarAssistStun(StunSeconds);

			if (EnableDebugLogs)
				GD.Print($"[{AttackName}] 罐子被击破 → 眩晕 {StunSeconds:F1}s");
		}

		private void StopTracking()
		{
			if (_assistEffect != null && IsInstanceValid(_assistEffect))
			{
				_assistEffect.Hide();
				_assistEffect.QueueFree();
			}
			_assistEffect = null;
			if (_jarIndicator != null && IsInstanceValid(_jarIndicator))
			{
				_jarIndicator.Hide();
				_jarIndicator.QueueFree();
			}
			_jarIndicator = null;
			_tracking = false;
			_resolved = true;
			_activeJar = null;
			UnsubscribeDeath();
		}

		/// <summary>停机时取消独立协助计时，清理罐子，不触发其死亡结算。</summary>
		public void CancelAssist()
		{
			var jar = _activeJar;
			StopTracking();
			if (GodotObject.IsInstanceValid(jar) && !jar!.IsQueuedForDeletion()) jar.QueueFree();
		}

		private void UnsubscribeDeath()
		{
			if (!_deathSubscribed) return;
			GameActor.DeathFinalized -= OnJarDeathFinalized;
			_deathSubscribed = false;
		}
	}
}
