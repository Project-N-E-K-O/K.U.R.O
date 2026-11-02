using Godot;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Actors.Enemies.States;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// 机械桩族"弱点窗口"组件（cannon / machinegun 已挂，以后的 rogueAI 要窗口就同样挂一个）：
	/// 常态免伤类敌人的**可受击窗口按"正在跑的招"细分**——攻击名 → (起, 止) 秒（从该招起手 TryStart 计），
	/// 只有表内且落在窗口内时才开放受击；**表里没列出的招 = 整个攻击期间开放**（回退现状）。
	/// 没有招在跑（招间空档 / 被打断后）→ 攻击窗口关闭。
	/// Hit 纯伤害由独立 StateWindowTable / IsHitOpen 判断；不改变状态效果的原有规则。
	///
	/// 计时自包含：每帧观测 AttackController 下"在跑的招"（与动画控制器同款解析），换招即清零重计——
	/// 攻击脚本零依赖、零改动。消费方：宿主敌人的 <c>CanBeAffected</c>
	/// （<see cref="EnemyF1RogueAITurret"/> 经 FindChildByType 取本组件；缺失时回退"整个 Attack 状态可受击"）。
	/// </summary>
	public partial class EnemyWeakpointWindow : Node
	{
		/// <summary>弱点窗口表：攻击名（AttackName）→ (起, 止) 秒，从该招起手计。
		/// 未列出的招 = 整个攻击期间开放；空表 = 所有招整个攻击期间开放（等价现状）。</summary>
		[Export] public Godot.Collections.Dictionary<string, Vector2> WindowTable { get; set; } = new();

		/// <summary>状态伤害窗口，目前支持 Hit。默认从本轮首次进入 Hit 计，可选择重入刷新。
		/// 未配置 Hit 时，IsHitOpen 返回 true，保持 Cannon 原有全程可受伤行为。</summary>
		[Export] public Godot.Collections.Dictionary<string, Vector2> StateWindowTable { get; set; } = new();
		/// <summary>开启后，成功重入 Hit 会重置伤害窗口；被连击保护抑制的重入不刷新。</summary>
		[Export] public bool ResetHitWindowOnReentry { get; set; } = false;
		/// <summary>有剩余 JarAssist 眩晕时，Hit 不受状态窗口限制，保持打断罐子的输出奖励。</summary>
		[Export] public bool BypassHitWindowWhileJarStunned { get; set; } = true;

		/// <summary>Hit 纯伤害入口专用；实时读取状态计时，避免物理回调顺序造成一帧误判。</summary>
		public bool IsHitOpen
		{
			get
			{
				if (_enemy?.StateMachine?.CurrentState is not EnemyHitState hit) return false;
				if (BypassHitWindowWhileJarStunned
					&& _enemy.StateMachine.GetNodeOrNull<EnemyJarAssistStunState>("JarAssistStun")?.IsActive == true)
					return true;
				if (StateWindowTable == null || !StateWindowTable.TryGetValue("Hit", out var window)) return true;
				double elapsed = ResetHitWindowOnReentry ? hit.EntryElapsedSeconds : hit.CycleElapsedSeconds;
				return elapsed >= window.X && elapsed < window.Y;
			}
		}

		/// <summary>弱点是否开放（宿主 CanBeAffected 读这个）。首帧前为 false（常态免伤）。</summary>
		public bool IsOpen => _enemy?.StateMachine?.CurrentState?.Name == "Attack" && _attackIsOpen;
		private bool _attackIsOpen;

		private SampleEnemy? _enemy;
		private EnemyAttackController? _controller;
		private EnemyAttackTemplate? _running;
		private float _elapsed;

		public override void _Ready()
		{
			_enemy = GetParent() as SampleEnemy;
			if (_enemy == null)
				GD.PushWarning($"{Name}: 必须挂在 SampleEnemy 下（与 FacingController 同款约定）");
		}

		public override void _PhysicsProcess(double delta)
		{
			if (_enemy == null || !GodotObject.IsInstanceValid(_enemy)) return;

			var attack = ResolveRunningAttack();
			if (attack != _running)
			{
				// 换招（或从无到有 / 收招）→ 重置计时："从该招起手计"的起点
				_running = attack;
				_elapsed = 0f;
			}
			else if (attack != null)
			{
				_elapsed += (float)delta;
			}

			_attackIsOpen = attack != null && IsWindowOpenFor(attack);
		}

		private bool IsWindowOpenFor(EnemyAttackTemplate attack)
		{
			if (WindowTable == null || WindowTable.Count == 0) return true;
			if (!WindowTable.TryGetValue(attack.AttackName, out Vector2 window)) return true;
			return _elapsed >= window.X && _elapsed < window.Y;
		}

		/// <summary>在跑的招：状态机在 Attack 且 AttackController 子节点里有 IsRunning 的模板。
		/// 解析与动画控制器 ResolveRunningTemplate 同款；控制器节点缓存、失效时重找。</summary>
		private EnemyAttackTemplate? ResolveRunningAttack()
		{
			if (_enemy!.StateMachine?.CurrentState?.Name != "Attack") return null;

			if (_controller == null || !GodotObject.IsInstanceValid(_controller))
				_controller = _enemy.GetNodeOrNull<EnemyAttackController>("StateMachine/Attack/AttackController");
			if (_controller == null) return null;

			foreach (var child in _controller.GetChildren())
				if (child is EnemyAttackTemplate template && template.IsRunning)
					return template;
			return null;
		}
	}
}
