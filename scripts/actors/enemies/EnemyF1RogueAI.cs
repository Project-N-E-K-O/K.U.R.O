using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Environments;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// rogueAI 本体（Enemy_F1_rogueAI 专属脚本）：大型敌人的主体，挂在滑槽上沿轨道滑行。
	/// 移动交给 <see cref="RailChaseMovement"/> 组件（只沿机械轴向追击玩家 + 每帧硬钳在行程内）。
	/// 常态免伤——与磁铁机械臂（EnemyF1RogueAIMagnet）同款：伤害与效果一律拒绝
	/// （CanBeAffected 是所有效果的总闸，TakeDamage 也先过这道闸），血量只为控制台能识别与销毁
	/// （KillForced → 伤害被拒 → 基类兜底 QueueFree）。
	/// 攻击由场景里的 AttackController（EnemyF1RogueAIAttackController）驱动：
	/// 普攻 RogueAISlam 常驻、大招 RogueAIOverload 由关卡击杀进度阈值武装。
	/// 进度满（上升到底）→ **收工**（与磁铁臂 BeginFinish 同构）：不再追踪玩家、不再起招，
	/// 沿机械轴滑回**滑槽原点**（= 滑槽节点的摆放位置）并停在那里。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAI : SampleEnemy
	{
		[ExportCategory("Progress Finish")]
		/// <summary>进度满（<see cref="KillProgressAscendController.Progress"/> = 1）后收工：回滑槽原点 + 不再追踪玩家。
		/// 场景里没有进度源（组 kill_progress）时恒不触发——单独测试本体、或换到别的关卡都可原样使用。</summary>
		[Export] public bool RetreatOnProgressFull { get; set; } = true;
		/// <summary>归位总时长（秒）：速度自动 = 归位距离 / 本值——**时间固定、速度随距离变**。
		/// 0 = 不用时长，退回 <see cref="FinishReturnSpeed"/> 的定速（时间随距离变）。</summary>
		[Export(PropertyHint.Range, "0,20,0.1")] public float FinishReturnDuration { get; set; } = 3f;
		/// <summary>归位速度（px/s）；0 = 自动（按 <see cref="FinishReturnDuration"/> 从距离推导）。</summary>
		[Export(PropertyHint.Range, "0,4000,10")] public float FinishReturnSpeed { get; set; } = 0f;
		/// <summary>归位到位后销毁自身（退场用：走 QueueFree，不是死亡——不计击杀/不掉落，也不发 DeathFinalized）。
		/// 注意不可逆：开了它，进度回退（ResetProgress）也找不到这台本体了。</summary>
		[Export] public bool FreeOnFinishArrive { get; set; } = false;
		/// <summary>归位到位死区**下限**（像素）：实际死区自动取"归位速度 ÷ 物理帧率"（= 一帧的位移），
		/// 只有速度很慢时才用这个下限兜底——死区若小于一帧位移，会在目标两侧逐帧抖动。</summary>
		[Export(PropertyHint.Range, "1,64,1")] public float MinFinishDeadzone { get; set; } = 8f;

		/// <summary>是否已收工（归位中或已停在滑槽原点）。</summary>
		public bool IsFinished { get; private set; }

		private SlideRailMount? _rail;
		private RailChaseMovement? _drive;
		private KillProgressAscendController? _progress;
		private bool _resolved;
		// 进度已满但还在等"已起手的这一招"跑完：这期间只停发新招，不关追踪（关了会把大招中途掐断）
		private bool _finishRequested;
		private bool _arrivedHome;
		private float _originCarriageCoordinate;
		private float _returnCarriageCoordinate;
		private float _originRailCoordinate;
		// 收工那一刻锁定的归位速度（每帧按剩余距离重算会变成芝诺式收敛，永远差一点到不了）
		private float _returnSpeed;

		public override void _Ready()
		{
			base._Ready();
			// 击退/黑洞吸附等强制位移推不动它（限位另有每帧硬钳兜底）
			ActiveImmunities |= ImmunityFlags.ForcedMovement;
		}

		/// <summary>常态免伤：伤害与效果一律拒绝（与 Enemy_F1_rogueAI_Magnet 同一套判定）。</summary>
		public override bool CanBeAffected(ActorEffect? effect) => false;

		/// <summary>收工后"看不见玩家"：追击组件、选招（AttackController.CanStart）、
		/// 以及 Idle/Walk/Attack 各状态的进出判定都读这里，一处关掉即整体停摆。</summary>
		public override bool IsPlayerWithinDetectionRange()
			=> !IsFinished && base.IsPlayerWithinDetectionRange();

		/// <summary>收工后（含"进度已满但还在等这一招跑完"）不再起新招；已起手的招照常跑完。</summary>
		public override bool CanStartAttack()
			=> !(IsFinished || _finishRequested) && base.CanStartAttack();

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			EnsureResolved();
			if (RetreatOnProgressFull) TickProgressFinish((float)delta);
		}

		/// <summary>首物理帧解析（滑槽是本机械的父节点、生成器又是 AddChild 之后才摆位，_Ready 时还没到位）。</summary>
		private void EnsureResolved()
		{
			if (_resolved) return;
			_resolved = true;

			_rail = SlideRailMount.FindFor(this);
			_rail?.ResolveNow();
			_drive = GetNodeOrNull<RailChaseMovement>("RailChaseMovement");

			if (_rail != null)
			{
				// 滑槽原点 = 滑槽节点自身的摆放位置（机械轴 / 滑动轴各记一份，供归位用）
				_originCarriageCoordinate = CarriageCoordinate(_rail.GlobalPosition);
				_originRailCoordinate = _rail.CurrentRailCoordinate;
			}
		}

		// ── 进度满 → 收工归位 ──────────────────────────────────────────────

		private void TickProgressFinish(float delta)
		{
			if (_progress == null || !GodotObject.IsInstanceValid(_progress))
				_progress = KillProgressAscendController.Find(this);   // 关卡晚于本体生成时首帧就能拿到；找不到 = 进度 0

			float progress = CurrentProgress;

			// 进度回退（调试/关卡复位）→ 解除收工，恢复常态
			if ((_finishRequested || IsFinished) && progress < 1f)
			{
				ResumeTracking();
				return;
			}

			if (!_finishRequested && progress >= 1f)
				_finishRequested = true;

			// 正在出招：等这一招自己跑完再收工（大招蓄力/冲刺跑到一半被掐会把特效切得很难看）；
			// 期间已经起不了新招（CanStartAttack 已被 _finishRequested 关掉）
			if (_finishRequested && !IsFinished && !IsAttackStateActive())
				BeginFinish();

			if (IsFinished)
				TickReturnHome(delta);
		}

		/// <summary>真正收工：关掉玩家追踪 + 让追击组件让位 + 开始沿轨道滑回滑槽原点。
		/// 不在这里改状态——追踪一关，Idle/Walk/Attack 各自的判定会自己收敛到 Idle。</summary>
		private void BeginFinish()
		{
			IsFinished = true;
			Velocity = Vector2.Zero;

			// 追击组件彻底让位：此后它只保留每帧硬钳（ClampToRail），不再追玩家、也不再替本脚本提交位移
			SetExternalDrive(true);

			if (_rail != null)
			{
				// 目标夹到行程区间内：滑槽没配限位 Marker 时区间退化成一个点（就是原点），也照样成立
				_returnCarriageCoordinate = Mathf.Clamp(_originCarriageCoordinate, _rail.CarriageLo, _rail.CarriageHi);

				// 速度只在收工这一刻锁一次：定速优先级 > 固定时长推导（时间固定则速度随距离变）
				float distance = Mathf.Abs(_returnCarriageCoordinate - CarriageCoordinate(GlobalPosition));
				_returnSpeed = FinishReturnSpeed > 0f
					? FinishReturnSpeed
					: distance / Mathf.Max(0.05f, FinishReturnDuration);

				// 滑槽自身也回原点高度，并**共用同一个固定时长**（没配 Slot 限位 Marker = 滑槽静止，自动无操作）
				_rail.SetTargetInTime(_originRailCoordinate, FinishReturnDuration);
			}

			GD.Print($"{Name}: 进度已满 → 收工：滑回滑槽原点（{_returnCarriageCoordinate:F1}），"
				+ $"归位速度 {_returnSpeed:F1}px/s，不再追踪玩家");
		}

		/// <summary>归位移动：沿机械轴直线滑回原点。
		/// 直接写位置而不是走 Velocity/MoveAndSlide：追击组件让位后没人再替它提交位移，
		/// 本体也没有启用的碰撞体（CapsuleShape2D 是 disabled）——沿轨道归位不需要碰撞推进，
		/// 写位置还能避开和状态机抢速度（Idle 每帧自己会调一次 MoveAndSlide）。
		/// 随后 RailChaseMovement（物理优先级 100）照旧把它钳在行程内，所以归位也出不了滑槽。</summary>
		private void TickReturnHome(float delta)
		{
			if (_rail == null || _arrivedHome) return;

			float current = CarriageCoordinate(GlobalPosition);
			float distance = _returnCarriageCoordinate - current;
			if (Mathf.Abs(distance) <= FinishDeadzone)
			{
				_arrivedHome = true;
				SetCarriageCoordinate(_returnCarriageCoordinate);
				if (FreeOnFinishArrive)
				{
					GD.Print($"{Name}: 已归位到滑槽原点，销毁自身（退场）");
					QueueFree();
					return;
				}
				GD.Print($"{Name}: 已归位到滑槽原点，停止");
				return;
			}

			SetCarriageCoordinate(current + Mathf.Sign(distance) * _returnSpeed * delta);
		}

		/// <summary>进度回退（调试/复位）→ 解除收工：还给追击组件控制权，之后由状态机正常追人打人。</summary>
		private void ResumeTracking()
		{
			_finishRequested = false;
			_arrivedHome = false;
			if (!IsFinished) return;

			IsFinished = false;
			SetExternalDrive(false);
			GD.Print($"{Name}: 进度回退 → 解除收工，恢复追踪");
		}

		// ── 内部 ──────────────────────────────────────────────────────────

		/// <summary>归位到位死区（像素）——自动 = 归位速度 ÷ 物理帧率（= 一帧的位移），下限 <see cref="MinFinishDeadzone"/>。</summary>
		private float FinishDeadzone
			=> Mathf.Max(MinFinishDeadzone, _returnSpeed / Mathf.Max(1f, Engine.PhysicsTicksPerSecond));

		private float CurrentProgress
			=> _progress != null && GodotObject.IsInstanceValid(_progress) ? _progress.Progress : 0f;

		private bool IsAttackStateActive()
			=> (StateMachine?.CurrentState?.Name ?? string.Empty) == "Attack";

		private void SetExternalDrive(bool value)
		{
			if (_drive != null && GodotObject.IsInstanceValid(_drive))
				_drive.ExternalDrive = value;
		}

		private float CarriageCoordinate(Vector2 worldPosition)
			=> _rail!.CarriageAxis == SlideRailMount.RailAxis.X ? worldPosition.X : worldPosition.Y;

		private void SetCarriageCoordinate(float value)
		{
			var pos = GlobalPosition;
			if (_rail!.CarriageAxis == SlideRailMount.RailAxis.X) pos.X = value;
			else pos.Y = value;
			GlobalPosition = pos;
		}
	}
}
