using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Environments;

namespace Kuros.Actors.Enemies
{
	/// <summary>
	/// 滑槽磁铁机械臂（Enemy_F1_rogueAI_Magnet 专属脚本）。
	/// 大型敌人的部件：固定在滑槽内只做横向移动，滑槽本身做纵向升降（龙门式两轴），
	/// 因此它没有追击/攻击 AI，只有自己的循环相位：
	///   退场（沿轨道滑到固定一端）→ 进场携带罐子冲向玩家 → 到达玩家位置释放罐子 → 回退场。
	/// 罐子由攻击模板（EnemyMagnetJarAttack）的 Effects 条目管理：Warmup 生成并跟随 marker，
	/// Active 挂起到释放时刻（OnActiveEnd 销毁罐子、OnRecovery 生成外部释放特效）。
	/// 免伤：常态拒绝一切伤害/效果（血量只为控制台能识别与销毁——KillForced 走基类兜底 QueueFree）。
	/// 限位是"单一入口兜底"：本脚本每帧无条件钳位（含 Hit/Frozen 等不驱动移动的状态），不写进任何状态。
	/// </summary>
	[GlobalClass]
	public partial class EnemyF1RogueAIMagnet : SampleEnemy
	{
		public enum RailEnd { Near, Far }
		public enum MagnetPhase { Retreat, Approach }

		[ExportCategory("Phase")]
		/// <summary>
		/// 退场去哪一端（角色语义，来自滑槽）：Near = 贴玩家侧（内侧）端、Far = 场外端。
		/// 左右两条镜像轨共用同一份机械配置——镜像是在**滑槽**上勾 `FlipCarriageEnds` 完成的
		/// （角色互换，Marker 位置/NodePath 都不用动），机械这里始终是"退到场外端"。
		/// </summary>
		[Export] public RailEnd RetreatEnd { get; set; } = RailEnd.Far;
		/// <summary>进场（冲向玩家）时的横向速度。</summary>
		[Export(PropertyHint.Range, "10,2000,10")] public float ApproachSpeed { get; set; } = 400f;
		/// <summary>退场时的横向速度。</summary>
		[Export(PropertyHint.Range, "10,2000,10")] public float RetreatSpeed { get; set; } = 600f;
		/// <summary>到位死区**下限**（像素）：实际死区自动取"当前相位速度 × 一个物理帧"，
		/// 只有速度很慢时才用这个下限兜底（防止死区小到被浮点误差/玩家微动触发抖动）。</summary>
		[Export(PropertyHint.Range, "1,64,1")] public float MinArriveDeadzone { get; set; } = 8f;
		/// <summary>释放点相对玩家的纵向偏移（正值 = 停在玩家略"下方"，俯视角里更贴"正下方"）。</summary>
		[Export(PropertyHint.Range, "-200,200,1")] public float ReleaseYOffset { get; set; } = 0f;
		/// <summary>最大可投标（判定"玩家进入投放区"的部件名）。玩法上：**玩家的 HitArea 与本敌人的
		/// `Sprite2D/AttackArea` 重叠时**立刻触发释放（与普通敌人 IsPlayerInAttackRange 同一套判定），
		/// 所以投放范围直接在场景里缩放 `Sprite2D/AttackArea` 的碰撞形状即可调，无需改这里。</summary>
		[Export] public bool ReleaseOnAttackAreaOverlap { get; set; } = true;
		/// <summary>投放前悬停（秒）：到达投放点后**先在原地停住**（罐子仍挂在臂上、机械臂与滑槽都冻结），
		/// 悬停结束才进入 Active 投放。0 = 抵达即投放（旧行为）。</summary>
		[Export(PropertyHint.Range, "0,5,0.05")] public float ReleaseHoverSeconds { get; set; } = 0.5f;
		/// <summary>释放收尾停顿（秒）：给攻击模板的 Recovery 生成外部特效留出时间再退场。</summary>
		[Export(PropertyHint.Range, "0,2,0.01")] public float ReleaseSettleSeconds { get; set; } = 0.15f;

		[ExportCategory("Progress Scaling")]
		/// <summary>进度（0..1）带来的速度加成：实际速度 = 基础速度 × (1 + Progress × 本值)。
		/// 行程越快 → 退场/进场越短 → 罐子投得越频繁。0 = 不随进度加速（旧行为）。</summary>
		[Export(PropertyHint.Range, "0,5,0.05")] public float ProgressSpeedBonus { get; set; } = 0.5f;

		[ExportCategory("Progress Finish")]
		/// <summary>归位总时长（秒）：横向速度自动 = 各自距离 / 本值，滑槽纵向**共用同一时长**
		/// （走 SlideRailMount.SetTargetInTime）——整体退场耗时固定，速度随距离变。
		/// 0 = 不用时长，退回 <see cref="FinishReturnSpeed"/> 的定速（时间随距离变）。</summary>
		[Export(PropertyHint.Range, "0,20,0.1")] public float FinishReturnDuration { get; set; } = 3f;
		/// <summary>归位横向速度（px/s）；0 = 自动（按 <see cref="FinishReturnDuration"/> 从距离推导）。</summary>
		[Export(PropertyHint.Range, "0,4000,10")] public float FinishReturnSpeed { get; set; } = 0f;
		/// <summary>归位到位后销毁自身（退场用：走 QueueFree，不是死亡——不计击杀/不掉落）。
		/// 注意不可逆：开了它，进度回退也找不回这台机械了。</summary>
		[Export] public bool FreeOnFinishArrive { get; set; } = false;

		private SlideRailMount? _rail;
		private KillProgressAscendController? _progress;
		private SamplePlayer? _player;
		private float _playerRetryTimer;
		private MagnetPhase _phase = MagnetPhase.Retreat;
		private bool _releaseRequested;
		private float _settleRemaining;
		private float _approachElapsed;
		private float _hoverRemaining;
		private bool _resolved;
		// 行程端（角色语义，来自滑槽并按 FlipCarriageEnds 解释）：Near = 玩家侧、Far = 场外
		private float _carriageNearX;
		private float _carriageFarX;
		// 行程区间（排序值，专供 Clamp）
		private float _carriageLoX;
		private float _carriageHiX;
		private float _localYOnRail;
		private float _fallbackY;
		private float _retreatRailY;
		// 生成点（满进度后归位用）：沿机械轴向的世界 X + 滑槽当时的高度
		private float _spawnX;
		private float _spawnRailY;
		private bool _finished;
		// 收工那一刻锁定的归位速度 / 是否已到点（到点后销毁或停住，只做一次）
		private float _finishSpeed;
		private bool _finishArrived;

			/// <summary>攻击模板（EnemyMagnetJarAttack）询问：罐子是否仍应挂在臂上（Warmup/Active 挂起条件）。
		/// 抵达投放点后仍要挂到**悬停结束**，所以悬停期间继续返回 true。</summary>
		public bool ShouldHoldJarPhase => _phase == MagnetPhase.Approach
			&& (!_releaseRequested || _hoverRemaining > 0f);
		public MagnetPhase CurrentPhase => _phase;

		/// <summary>关卡击杀进度（0..1）：没挂进度控制器时恒 0 → 所有 Effective* 等于基础值（旧行为）。</summary>
		private float CurrentProgress
			=> _progress != null && GodotObject.IsInstanceValid(_progress) ? _progress.Progress : 0f;

		/// <summary>进场速度（含进度加成）。</summary>
		public float EffectiveApproachSpeed => ApproachSpeed * (1f + CurrentProgress * ProgressSpeedBonus);
		/// <summary>退场速度（含进度加成）。</summary>
		public float EffectiveRetreatSpeed => RetreatSpeed * (1f + CurrentProgress * ProgressSpeedBonus);
		/// <summary>最长搬运时间（秒）——**自动 = 跑完整条轨道所需的时间**：两端距离 / 当前进场速度。
		/// 语义：连一整条轨道都跑完了还够不到玩家，就别追了，就地在当前位置投放（避免挂起永不结束）。
		/// 进场速度随进度变快 → 这个上限自动收紧，**没有也不需要手动值**；无滑槽时返回 0（不设上限）。</summary>
		public float EffectiveApproachTimeoutSeconds
		{
			get
			{
				if (_rail == null || !GodotObject.IsInstanceValid(_rail)) return 0f;
				float speed = EffectiveApproachSpeed;
				if (speed <= 0.01f) return 0f;
				return Mathf.Abs(_rail.CarriageFar - _rail.CarriageNear) / speed;
			}
		}
		public float CarriageNearX => _carriageNearX;
		public float CarriageFarX => _carriageFarX;

		/// <summary>到位死区（像素）——**自动 = 当前相位的速度 ÷ 物理帧率**（即"一帧的位移"），
		/// 下限 <see cref="MinArriveDeadzone"/>。恒速移动按符号推进：死区若小于一帧位移，一帧就跨过整个
		/// 窗口 → 在目标两侧逐帧抖动；所以它必须跟着速度（含进度加成）一起放大。</summary>
		public float EffectiveArriveDeadzone
		{
			get
			{
				float speed = _finished ? _finishSpeed
					: _phase == MagnetPhase.Approach ? EffectiveApproachSpeed : EffectiveRetreatSpeed;
				float ticksPerSecond = Mathf.Max(1f, Engine.PhysicsTicksPerSecond);
				return Mathf.Max(MinArriveDeadzone, speed / ticksPerSecond);
			}
		}

		public override void _Ready()
		{
			base._Ready();
			// 击退/吸附类强制位移推不动它（限位另有每帧硬钳兜底）
			ActiveImmunities |= ImmunityFlags.ForcedMovement;
		}

		/// <summary>常态免伤：伤害与效果一律拒绝，血量只为控制台能识别/销毁。</summary>
		public override bool CanBeAffected(ActorEffect? effect) => false;

		/// <summary>没有攻击 AI：何时"投送罐子"由相位决定，禁止 Idle/Walk 自动进 Attack。</summary>
		public override bool CanStartAttack() => false;

		public override void _PhysicsProcess(double delta)
		{
			base._PhysicsProcess(delta);

			EnsureResolved();
			EnsureProgressResolved();

			// 进度满 → 收工：回生成点、不再检测玩家、不再走相位循环
			if (!_finished && CurrentProgress >= 1f) BeginFinish();
			if (!_finished) RefreshPlayerIfNeeded((float)delta);

			TickPhase((float)delta);
			TickMovement((float)delta);
		}

		/// <summary>进度满（上升到底）→ 收工：退掉没走完的投放，退回**生成点**并停在那里（或按配置自毁）；
		/// 之后不再解析玩家、不再进/退场（`_finished` 后 TickPhase 直接早退，只剩归位移动）。</summary>
		private void BeginFinish()
		{
			_finished = true;
			_player = null;              // 丢掉玩家引用：彻底不再检测
			_releaseRequested = false;   // 中止未完成的投放
			_hoverRemaining = 0f;
			_settleRemaining = 0f;
			_phase = MagnetPhase.Retreat;   // 复用"退场"把归位做掉（目标改成生成点，见 CurrentCarriageTargetX）

			// 速度只在收工这一刻锁一次（每帧按剩余距离重算 = 芝诺式收敛，永远差一点到不了）
			float distance = Mathf.Abs(_spawnX - GlobalPosition.X);
			_finishSpeed = FinishReturnSpeed > 0f
				? FinishReturnSpeed
				: distance / Mathf.Max(0.05f, FinishReturnDuration);

			// 滑槽纵向也回生成点高度，并**共用同一个固定时长**（拿不到滑槽 = 无操作）。
			// 归位期间 TickMovement 不再每帧写目标——重写会把速度打回滑槽自身 Speed，时长就固定不了。
			_rail?.SetTargetInTime(_spawnRailY, FinishReturnDuration);

			// 若正挂在 Attack 上会被切走：EnemyAttackState.Exit → 模板 Cancel → 挂载的罐子特效随之销毁
			StateMachine?.ChangeState("MagnetRetreat");
		}

		/// <summary>进度控制器补齐：关卡侧先于本机械生成时首帧就能拿到；拿不到就每帧重试（找不到 = 不加速）。</summary>
		private void EnsureProgressResolved()
		{
			if (_progress != null && GodotObject.IsInstanceValid(_progress)) return;
			_progress = KillProgressAscendController.Find(this);
		}

		/// <summary>玩家解析：基类的刷新只发生在它自己的距离查询里（本脚本不调用），所以这里自己按需重解析。</summary>
		private void RefreshPlayerIfNeeded(float delta)
		{
			if (_finished) return;   // 收工后不再检测玩家
			if (PlayerResolved) return;

			_playerRetryTimer -= delta;
			if (_playerRetryTimer > 0f) return;
			_playerRetryTimer = 0.25f;

			_player = PlayerTarget;
			if (!PlayerResolved)
				_player = GetTree()?.GetFirstNodeInGroup("player") as SamplePlayer;
		}

		/// <summary>首物理帧解析（生成器在 AddChild 之后才写 GlobalPosition，_Ready 时还没摆到位）。</summary>
		private void EnsureResolved()
		{
			if (_resolved) return;
			_resolved = true;

			_rail = SlideRailMount.FindFor(this);
			if (_rail != null)
			{
				_rail.ResolveNow();
				// 角色端（滑槽已按 FlipCarriageEnds 解释好）+ 排序区间
				_carriageNearX = _rail.CarriageNear;
				_carriageFarX = _rail.CarriageFar;
				_carriageLoX = _rail.CarriageLo;
				_carriageHiX = _rail.CarriageHi;
				_localYOnRail = Position.Y;
				_retreatRailY = _rail.CurrentRailCoordinate;
				// 生成点（含滑槽当时的纵向高度）：满进度后归位用
				_spawnRailY = _rail.CurrentRailCoordinate;
				_spawnX = _rail.ToGlobal(_rail.SpawnLocalPosition).X;
			}
			else
			{
				// 降级：无滑槽父节点 → 用自身子节点的同名 Marker（没有则锁死不动，便于单独生成测试）
				var near = GetNodeOrNull<Marker2D>("CarriageNearMarker");
				var far = GetNodeOrNull<Marker2D>("CarriageFarMarker");
				if (near != null && far != null)
				{
					_carriageNearX = near.GlobalPosition.X;
					_carriageFarX = far.GlobalPosition.X;
					_carriageLoX = Mathf.Min(_carriageNearX, _carriageFarX);
					_carriageHiX = Mathf.Max(_carriageNearX, _carriageFarX);
				}
				else
				{
					_carriageNearX = _carriageFarX = _carriageLoX = _carriageHiX = GlobalPosition.X;
					GD.PushWarning($"{Name}: 未挂在 SlideRailMount 下且自身没有 CarriageNear/FarMarker，机械不会移动。");
				}
				_fallbackY = GlobalPosition.Y;
				_spawnX = GlobalPosition.X;   // 无滑槽：生成点就是当前位置
			}

			_progress = KillProgressAscendController.Find(this);   // 找不到 = 进度 0（不加速）
			StateMachine?.ChangeState("MagnetRetreat");
		}

		private void TickPhase(float delta)
		{
			if (_finished) return;   // 收工：不再做任何相位切换，只由 TickMovement 把归位走完

			switch (_phase)
			{
				case MagnetPhase.Retreat:
					if (CarriageArrived && RailArrived)
					{
						_phase = MagnetPhase.Approach;
						_releaseRequested = false;
						_approachElapsed = 0f;
						StateMachine?.ChangeState("Attack"); // 进入攻击状态 = 罐子在 Warmup 生成并跟随机械臂
					}
					break;

				case MagnetPhase.Approach:
					if (!PlayerResolved && !_releaseRequested)
					{
						// 玩家不可解析（死亡/换场景边缘）→ 直接回退场，避免卡在进场相位
						BeginRetreat();
						break;
					}

					if (!_releaseRequested)
					{
						_approachElapsed += delta;

						// 投放触发：玩家 HitArea 进入投放区（AttackArea）即立刻释放；
						// 超过最长搬运时间则兜底（玩家一直跑就追不上，不能让挂起永不结束）
						bool inDropZone = ReleaseOnAttackAreaOverlap && PlayerResolved && IsPlayerInAttackRange();
						float carryTimeout = EffectiveApproachTimeoutSeconds;   // 自动：全轨行程时间（随进度收紧）
						bool timedOut = carryTimeout > 0f && _approachElapsed >= carryTimeout;
						if (inDropZone || timedOut)
						{
							// 抵达投放点：冻结移动 + 悬停 ReleaseHoverSeconds 后再结束挂起（进入 Active 投放）
							_releaseRequested = true;
							_hoverRemaining = Mathf.Max(0f, ReleaseHoverSeconds);
							_settleRemaining = Mathf.Max(0f, ReleaseSettleSeconds);
						}
					}

					// 自愈：进场相位里 Attack 状态被别的规则踢走时补回来。
					// 已知触发场景：机械臂刚生成那一帧，Area2D 与玩家的重叠数据还没建立，
					// EnemyAttackState 会判"没检测到玩家"并切 Idle；若不补，整段进场都会没有攻击状态
					// （罐子不会生成、也不会投放），只能等 7 秒超时收场。
					if (!_releaseRequested && StateMachine != null
						&& (StateMachine.CurrentState?.Name ?? string.Empty) != "Attack")
						StateMachine.ChangeState("Attack");

					// 投放前悬停：机械臂与滑槽都已冻结（TickMovement 见 _releaseRequested 即原地不动）
					if (_releaseRequested && _hoverRemaining > 0f)
						_hoverRemaining -= delta;

					if (_releaseRequested && (StateMachine?.CurrentState?.Name ?? string.Empty) != "Attack")
					{
						// 模板跑完（含 Recovery 的外部特效）后收尾停顿 → 退场
						_settleRemaining -= delta;
						if (_settleRemaining <= 0f) BeginRetreat();
					}
					break;
			}
		}

		private void BeginRetreat()
		{
			_phase = MagnetPhase.Retreat;
			_releaseRequested = false;
			_hoverRemaining = 0f;
			// 退场时滑槽保持当前高度（要"回位"就在这里改成固定端点即可）
			_retreatRailY = _rail != null ? _rail.CurrentRailCoordinate : _fallbackY;
			StateMachine?.ChangeState("MagnetRetreat");
		}

		private void TickMovement(float delta)
		{
			// 眩晕/受击不驱动移动；已请求释放但还没开始退场时也原地不动——
			// 投放点不能在释放瞬间随玩家漂移（滑槽同样停住）
			bool holdPosition = IsMovementBlocked()
				|| (_releaseRequested && _phase == MagnetPhase.Approach);

			if (!holdPosition)
			{
				// 滑槽：每帧写目标（退场保持 Y / 进场对到玩家 Y），由滑槽自己恒速滑动 + 硬钳。
				// 收工归位期间不写：那一刻已用 SetTargetInTime 锁定"固定时长"，每帧重写会把速度打回自身 Speed。
				if (!_finished) _rail?.SetTarget(CurrentRailTargetY);

				float dx = CurrentCarriageTargetX - GlobalPosition.X;
				float speed = _finished ? _finishSpeed
					: _phase == MagnetPhase.Approach ? EffectiveApproachSpeed : EffectiveRetreatSpeed;
				Velocity = Mathf.Abs(dx) <= EffectiveArriveDeadzone
					? Vector2.Zero
					: new Vector2(Mathf.Sign(dx) * speed, 0f);

				// 位移只提交一次：进场相位跑在 Attack 状态里，而 EnemyAttackState 每帧已经替模板调过
				// MoveAndSlide()——这里再调一次会让实际速度翻倍（配置 400 实跑 800，超时/周期全部对不上）。
				// 退场/其它状态没人替它移动，才由本脚本提交。
				if (!IsAttackStateActive()) MoveAndSlide();

				if (_phase == MagnetPhase.Approach && PlayerResolved && Mathf.Abs(dx) > EffectiveArriveDeadzone)
					FlipFacing(_player!.GlobalPosition.X > GlobalPosition.X);
			}
			else
			{
				Velocity = Vector2.Zero;
			}

			ClampToRail();

			// 归位到点（横向 + 滑槽纵向都到位）→ 停住或按配置销毁自身，只做一次
			if (_finished && !_finishArrived && CarriageArrived && RailArrived)
			{
				_finishArrived = true;
				Velocity = Vector2.Zero;
				if (FreeOnFinishArrive)
				{
					GD.Print($"{Name}: 满进度归位完成，销毁自身（退场）");
					QueueFree();
				}
				else
				{
					GD.Print($"{Name}: 满进度归位完成（目标 {_spawnX:F1}）");
				}
			}
		}

		/// <summary>限位兜底：每帧无条件执行，与相位/状态无关（Hit/Frozen 不驱动移动时也钳位）。</summary>
		private void ClampToRail()
		{
			if (_rail != null)
			{
				// 滑槽带着臂升降：臂在滑槽内的局部 Y 钉回挂载值（外部推力挪不走）
				var local = Position;
				if (!Mathf.IsEqualApprox(local.Y, _localYOnRail))
				{
					local.Y = _localYOnRail;
					Position = local;
				}
			}

			var pos = GlobalPosition;
			pos.X = Mathf.Clamp(pos.X, _carriageLoX, _carriageHiX); // 区间用排序后的 Lo/Hi，不能用 Near/Far
			if (_rail == null) pos.Y = _fallbackY;
			GlobalPosition = pos;
		}

		/// <summary>本帧横向目标（退场 = RetreatEnd 那一端；进场 = clamp(玩家X) 到行程区间内）。</summary>
		private float CurrentCarriageTargetX
		{
			get
			{
				if (_finished) return _spawnX;   // 收工：目标是生成点

				if (_phase == MagnetPhase.Retreat)
					return RetreatEnd == RailEnd.Near ? _carriageNearX : _carriageFarX;

				float target = PlayerResolved
					? _player!.GlobalPosition.X
					: GlobalPosition.X;
				return Mathf.Clamp(target, _carriageLoX, _carriageHiX); // 区间用 Lo/Hi，不能用角色端当上下界
			}
		}

		/// <summary>本帧滑槽纵向目标（退场 = 保持进场前高度；进场 = clamp(玩家Y + 偏移)）。</summary>
		private float CurrentRailTargetY
		{
			get
			{
				if (_finished) return _spawnRailY;   // 收工：滑槽也回到生成时的高度
				if (_phase != MagnetPhase.Approach || !PlayerResolved) return _retreatRailY;

				float desired = _player!.GlobalPosition.Y + ReleaseYOffset;
				if (_rail != null && _rail.HasSlotLimits)
					return Mathf.Clamp(desired, _rail.SlotStart, _rail.SlotEnd);
				return desired;
			}
		}

		private bool CarriageArrived
			=> Mathf.Abs(GlobalPosition.X - CurrentCarriageTargetX) <= EffectiveArriveDeadzone;

		private bool RailArrived => _rail == null || _rail.Arrived;

		private bool PlayerResolved => _player != null && IsInstanceValid(_player);

		private bool IsMovementBlocked()
		{
			string state = StateMachine?.CurrentState?.Name ?? string.Empty;
			return state == "Hit" || state == "Frozen" || state == "CooldownFrozen"
				|| state == "Dying" || state == "Dead";
		}

		/// <summary>Attack 状态是否在跑：此时 EnemyAttackState 每帧已替模板调过 MoveAndSlide，
		/// 本脚本不能再提交位移（否则一帧走两次，实际速度翻倍）。</summary>
		private bool IsAttackStateActive()
			=> (StateMachine?.CurrentState?.Name ?? string.Empty) == "Attack";
	}
}
