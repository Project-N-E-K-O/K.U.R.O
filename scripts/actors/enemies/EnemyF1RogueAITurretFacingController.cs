using System;
using Godot;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// 机械桩炮台的"转身"组件（炮台 / 以后机枪台共用）：按玩家位置决定朝向，并用 `turn`/`d_turn` 动画完成翻转。
	///
	/// 这套骨架的 `d_*` 是美术**手绘的镜像孪生**（`d_idle` 与 `idle` 逐骨骼相同，只多 `bone2.scale.y = -1`；
	/// `turn` 在片中间把 bone2 翻成 -1 = 右→左，`d_turn` 反之）。所以：
	///   · **翻转完全靠动画**——这里绝不碰 `SpineSprite.scale.x`（碰了就是双重镜像）；
	///   · 静止动画按"当前朝向"取名（朝左 = `d_` 前缀）；
	///   · `turn` 系列按**当前**朝向选片（当前朝右 → 播 `turn`）。
	/// 朝向本身写在宿主（`GameActor.FacingRight`，protected setter），本组件只负责"何时翻、播哪支片、何时提交"。
	///
	/// 死区 + 迟滞 + 冷却：玩家绕桩跑动时不会来回抽；转身前段（<see cref="CancelWindow"/>）允许取消。
	/// </summary>
	public partial class EnemyF1RogueAITurretFacingController : Node
	{
		public enum FacingCommitMode { TurnStart, TurnEnd }

		[ExportCategory("Turn 转身")]
		/// <summary>起转死区（px）：|玩家 X − 自己 X| 小于它就不起转（玩家在正上方时不抽）。</summary>
		[Export(PropertyHint.Range, "0,1500,10")] public float FlipDeadzone { get; set; } = 200f;
		/// <summary>迟滞（px）：转身前段若玩家又退回死区内侧这么多，就取消这次转身。</summary>
		[Export(PropertyHint.Range, "0,500,10")] public float FlipHysteresis { get; set; } = 80f;
		/// <summary>两次转身之间的冷却（秒）。</summary>
		[Export(PropertyHint.Range, "0,3,0.05")] public float TurnCooldown { get; set; } = 0.25f;
		/// <summary>转身动画时长（秒）——`FacingCommitMode.TurnEnd` 时提交的时点（片子时长约 0.333）。</summary>
		[Export(PropertyHint.Range, "0,2,0.01")] public float TurnDuration { get; set; } = 0.333f;
		/// <summary>朝向在哪一刻提交：片尾（默认，看起来自然）/ 片头。**目视不对就换另一档**。</summary>
		[Export] public FacingCommitMode CommitMode { get; set; } = FacingCommitMode.TurnEnd;
		/// <summary>转身动画基名（按当前朝向取：朝右播它本身、朝左播 `d_` 版本）。</summary>
		[Export] public string TurnAnimation { get; set; } = "turn";
		/// <summary>可被取消的窗口（占 TurnDuration 的比例）。</summary>
		[Export(PropertyHint.Range, "0,1,0.05")] public float CancelWindow { get; set; } = 0.4f;
		/// <summary>这些状态下拒绝起转（正在出招/受击/冻结/死亡时不转）。</summary>
		[Export] public Godot.Collections.Array<string> BlockingStates { get; set; } = new()
		{
			"Attack", "Hit", "Frozen", "CooldownFrozen", "Dying", "Dead",
		};
		[Export] public bool EnableDebugLogs { get; set; } = false;

		/// <summary>正在转身（动画控制器据此优先播 turn 片）。</summary>
		public bool IsTurning => _turning;
		/// <summary>本次转身起飞时的朝向（选 `turn` / `d_turn` 用）。</summary>
		public bool TurnOriginFacing { get; private set; } = true;

		/// <summary>朝向已提交（参数 = 新朝向 true 右 / false 左）。宿主据此镜像前向开火区等。</summary>
		public event Action<bool>? FacingCommitted;

		private SampleEnemy? _enemy;
		private float _elapsed;
		private float _cooldown;
		/// <summary>开局那次「直接提交朝向」还没做（不播 turn 片：炮台醒来的第一眼就该对着玩家）。</summary>
		private bool _initialFacingPending = true;

		public override void _Ready()
		{
			_enemy = GetParent<SampleEnemy>();
			if (_enemy == null)
			{
				GD.PushWarning($"{Name}: 必须挂在 SampleEnemy 下（朝向写在宿主上）");
				return;
			}

			// 初始朝向由宿主设定（炮台根脚本），这里只记下来用于选片
			TurnOriginFacing = _enemy.FacingRight;
		}

		public override void _PhysicsProcess(double delta)
		{
			var dt = (float)delta;
			if (_cooldown > 0f) _cooldown -= dt;

			if (_enemy == null || !GodotObject.IsInstanceValid(_enemy)) return;
			if (_enemy.IsDead || _enemy.IsDeathSequenceActive) return;

			var player = ResolvePlayer();
			if (player == null) return;

			float dx = player.GlobalPosition.X - _enemy.GlobalPosition.X;

			// 开局第一次：直接提交朝向（不播转身片）——否则炮台会先朝默认侧、再当着玩家面转一次
			if (_initialFacingPending)
			{
				_initialFacingPending = false;
				bool wantRightAtSpawn = dx >= 0f;
				if (wantRightAtSpawn != _enemy.FacingRight)
				{
					_pendingFacing = wantRightAtSpawn;
					CommitFacing();
				}
				return;
			}

			if (_turning)
			{
				TickTurn(dt, dx);
				return;
			}

			if (_cooldown > 0f) return;
			if (IsBlockedState()) return;
			if (Mathf.Abs(dx) < FlipDeadzone) return;      // 死区：目标侧还不够远

			bool wantRight = dx > 0f;
			if (wantRight == _enemy.FacingRight) return;

			BeginTurn(wantRight);
		}

		private void BeginTurn(bool targetRight)
		{
			_turning = true;
			_elapsed = 0f;
			TurnOriginFacing = _enemy!.FacingRight;
			_pendingFacing = targetRight;

			if (CommitMode == FacingCommitMode.TurnStart)
				CommitFacing();

			if (EnableDebugLogs)
			{
				string fromTo = TurnOriginFacing ? "右→左" : "左→右";
				string clip = TurnOriginFacing ? TurnAnimation : "d_" + TurnAnimation;
				GD.Print($"{Name}: 起转 {fromTo}（片子 {clip}）");
			}
		}

		private bool _turning;
		private bool _pendingFacing = true;

		private void TickTurn(float dt, float dx)
		{
			_elapsed += dt;

			// 前段可取消：玩家又回到死区内侧 → 放弃这次转身（保持原朝向）
			if (CommitMode != FacingCommitMode.TurnStart && _elapsed < TurnDuration * CancelWindow)
			{
				if (Mathf.Abs(dx) < FlipDeadzone - FlipHysteresis)
				{
					_turning = false;
					_cooldown = TurnCooldown;
					if (EnableDebugLogs) GD.Print($"{Name}: 转身取消（玩家回到死区内）");
					return;
				}
			}

			if (_elapsed < TurnDuration) return;

			if (CommitMode == FacingCommitMode.TurnEnd)
				CommitFacing();

			_turning = false;
			_cooldown = TurnCooldown;
		}

		/// <summary>提交朝向：**朝向本身由宿主写**（`FacingRight` 的 setter 是 protected，只有 GameActor 子类能写；
		/// 本组件是平级 Node）。宿主收到 <see cref="FacingCommitted"/> 后自己写 `FacingRight` 并做前向锚点镜像。</summary>
		private void CommitFacing()
		{
			if (_enemy == null || !GodotObject.IsInstanceValid(_enemy)) return;

			if (EnableDebugLogs) GD.Print($"{Name}: 朝向已提交 = {(_pendingFacing ? "右" : "左")}");
			FacingCommitted?.Invoke(_pendingFacing);
		}

		/// <summary>取玩家：优先用基类缓存的 PlayerTarget，取不到就自己按组解析。
		/// （基类的引用只在它自己的检测查询里刷新，而本组件不调那些查询——不自己解析会永远看不到玩家。）</summary>
		private SamplePlayer? ResolvePlayer()
		{
			var player = _enemy?.PlayerTarget;
			if (player != null && GodotObject.IsInstanceValid(player)) return player;

			return GetTree()?.GetFirstNodeInGroup("player") as SamplePlayer;
		}

		private bool IsBlockedState()
		{
			string current = _enemy?.StateMachine?.CurrentState?.Name ?? string.Empty;
			foreach (var state in BlockingStates)
				if (state == current) return true;
			return false;
		}
	}
}
