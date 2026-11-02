using System;
using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Companions
{
    /// <summary>
    /// P2（2P 伴随角色）控制器：负责跟随/自由移动、朝向、渲染层级、Spine 动画播放与受击处理。
    /// 移动采用双模式（自由游走 / 跟随接近），状态机（P2.tscn 的 StateMachine）只做行为/动画层，
    /// 位移唯一由本类驱动。对话气泡逻辑已独立到 P2DialogueController（AI_Dialogue 节点）。
    /// </summary>
    public partial class P2CompanionController : CharacterBody2D, ICompanionStateSource
    {
        [ExportCategory("Companion State")]
        /// <summary>伴随角色定位名（供外部系统识别用途）。</summary>
        [Export] public string CompanionRoleName { get; set; } = "support";
        /// <summary>报告的最大生命（模拟值，非 GameActor 管线）。</summary>
        [Export(PropertyHint.Range, "1,9999,1")] public int ReportedMaxHp { get; set; } = 100;
        /// <summary>报告的当前生命（受击扣减）。</summary>
        [Export(PropertyHint.Range, "0,9999,1")] public int ReportedCurrentHp { get; set; } = 100;

        /// <summary>角色名（ICompanionStateSource 接口）。</summary>
        public string CompanionName => Name;
        /// <summary>钳制后的当前生命（0~Max）。</summary>
        public int CurrentHp => Mathf.Clamp(ReportedCurrentHp, 0, Mathf.Max(1, ReportedMaxHp));
        /// <summary>最大生命（至少 1）。</summary>
        public int MaxHp => Mathf.Max(1, ReportedMaxHp);
        /// <summary>是否可用（在场景树内且可见）。</summary>
        public bool IsCompanionAvailable => IsInsideTree() && Visible;
        /// <summary>角色定位（ICompanionStateSource 接口）。</summary>
        public string CompanionRole => CompanionRoleName;

        [ExportCategory("Follow")]
        /// <summary>玩家节点路径（默认兄弟节点 MainCharacter）。</summary>
        [Export] public NodePath PlayerPath { get; set; } = new("../MainCharacter");
        /// <summary>玩家身上的跟随锚点节点（MainCharacter 无此节点时回退玩家位置）。</summary>
        [Export] public NodePath CompanionAnchorPath { get; set; } = new("CompanionAnchor");
        /// <summary>跟随模式距离带下限：P2 与玩家距离低于此值时向后退回带内。</summary>
        [Export(PropertyHint.Range, "0,1000,10")] public float FollowRangeMin { get; set; } = 300f;
        /// <summary>跟随模式距离带上限：P2 与玩家距离高于此值时靠近；动作在进入此范围内触发。</summary>
        [Export(PropertyHint.Range, "0,2000,10")] public float FollowRangeMax { get; set; } = 500f;
        /// <summary>跟随模式最大持续时间（秒）：超过后恢复自由模式，避免无限跟随。</summary>
        [Export(PropertyHint.Range, "0.5,30,0.5")] public float FollowMaxDuration { get; set; } = 5f;
        /// <summary>跟随速度上限（px/秒）：跟随模式靠近玩家用，应大于 FreeRoamSpeed 保证追上玩家。</summary>
        [Export(PropertyHint.Range, "10,5000,10")] public float FollowSpeed { get; set; } = 700f;
        /// <summary>始终跟随玩家背后（偏移取玩家朝向反侧）。</summary>
        [Export] public bool AlwaysFollowBehindPlayer { get; set; } = true;
        /// <summary>保持固定在玩家朝向侧（转身不穿越玩家）。</summary>
        [Export] public bool KeepCompanionOnFacingSide { get; set; } = false;
        /// <summary>跟随偏移（相对玩家，X 按朝向反侧取符号，Y 叠加浮动）。</summary>
        [Export] public Vector2 FollowOffset { get; set; } = new(320f, -80f);
        /// <summary>Lerp 收敛平滑度：越大越快地接近目标。</summary>
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float FollowSmoothing { get; set; } = 8.5f;
        /// <summary>目标点平滑速度：对移动目标做指数平滑，消除目标突变（跟随/游走/搬运切换）造成的方向跳变。</summary>
        [Export(PropertyHint.Range, "1,30,0.5")] public float TargetSmoothing { get; set; } = 10f;

        [ExportCategory("Floating")]
        /// <summary>跟随点的正弦浮动振幅（px）。</summary>
        [Export(PropertyHint.Range, "0,200,0.1")] public float FloatAmplitude { get; set; } = 22f;
        /// <summary>浮动频率（Hz）。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float FloatFrequency { get; set; } = 1.8f;

        [ExportCategory("Render Layer")]
        /// <summary>动态层级开关：按 Y 差/朝向切换 ZIndex（前后遮挡）。</summary>
        [Export] public bool EnableDynamicLayering { get; set; } = true;
        /// <summary>在前（低于玩家）时的 ZIndex 增量。</summary>
        [Export(PropertyHint.Range, "0,200,1")] public int FrontLayerDelta { get; set; } = 0;
        /// <summary>在后（高于玩家）时的 ZIndex 增量。</summary>
        [Export(PropertyHint.Range, "-200,0,1")] public int BackLayerDelta { get; set; } = -1;
        /// <summary>层级切换的死区：超过此差值才切换前后层（防抖动）。</summary>
        [Export(PropertyHint.Range, "0,100,0.1")] public float LayerSwitchDeadZone { get; set; } = 8f;
        /// <summary>按朝向而非 Y 差判断前后层。</summary>
        [Export] public bool LayerByFacingDirection { get; set; } = false;

        [ExportCategory("Boundary")]
        /// <summary>空气墙射线检测开关：自由游走时向移动方向发射线，命中墙体则贴墙停住（类似 ECore）。</summary>
        [Export] public bool ClampToBoundary { get; set; } = true;
        /// <summary>射线碰撞层（空气墙 layer，如 6）；默认全层检测。</summary>
        [Export(PropertyHint.Layers2DPhysics)] public uint BoundaryCollisionMask { get; set; } = uint.MaxValue;

        [ExportCategory("Free Roam")]
        /// <summary>自由移动开关：在玩家周围环形区域内游走（决策/脚本目标点）。关闭则纯跟随。</summary>
        [Export] public bool EnableFreeRoam { get; set; } = true;
        /// <summary>自由游走速度上限（px/秒）：游走/随机目标移动用。</summary>
        [Export(PropertyHint.Range, "10,3000,10")] public float FreeRoamSpeed { get; set; } = 300f;
        /// <summary>自由模式环形区域内半径：随机游走点的最小半径。</summary>
        [Export(PropertyHint.Range, "0,1000,10")] public float MoveRangeMin { get; set; } = 500f;
        /// <summary>自由模式环形区域外半径：超出此距离切换到跟随模式。</summary>
        [Export(PropertyHint.Range, "0,2000,10")] public float MoveRangeMax { get; set; } = 2000f;
        /// <summary>空闲游走间隔（秒）：每隔一段时间随机生成新目标点。</summary>
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float WanderInterval { get; set; } = 2.0f;
        /// <summary>到达目标点的判定距离（px）。</summary>
        [Export(PropertyHint.Range, "5,100,5")] public float ArriveDistance { get; set; } = 20f;

        [ExportCategory("Combat")]
        /// <summary>受击免疫窗口（秒）：受击后此期间忽略后续伤害。</summary>
        [Export(PropertyHint.Range, "0,5,0.1")] public float HitInvincibilityDuration { get; set; } = 0.5f;

        [ExportCategory("Visual")]
        /// <summary>要镜像/翻转的精灵路径（场景节点名 SpineSprite）。</summary>
        [Export] public NodePath SpritePath { get; set; } = new("SpineSprite");

        [ExportCategory("Dialogue")]
        /// <summary>对话控制器节点路径（P2.tscn 的 AI_Dialogue，气泡逻辑已独立）。</summary>
        [Export] public NodePath DialogueControllerPath { get; set; } = new("AI_Dialogue");

        [ExportCategory("Action Effects")]
        /// <summary>
        /// 动作特效场景数组（PackedScene），按用途约定索引，各动作独立配置、互不共用：
        ///   [0] 护盾施加（ApplyShield 触发，P2 放盾时在玩家身上生成）
        ///   [1] 治疗（ApplyHeal 触发，P2 回血时在玩家身上生成）
        ///   [2] 预留（原装备加成特效，食物路径已废弃）
        /// 行为说明：
        /// - 触发时机 = 动作成功执行瞬间（与飘字/气泡同一帧），特效挂载在玩家节点下跟随移动
        /// - 未配置的索引（空槽/越界）直接跳过，不影响动作本身（飘字、气泡、回血照常）
        /// - 特效节点需自带生命周期管理（如 FadeInOutDestroy 淡入保持淡出销毁，或 EffectAutoDestroy），
        ///   否则会一直挂在玩家身上
        /// - 特效尺寸可通过场景根节点 Scale 调整（挂玩家后继承玩家自身的缩放）
        /// </summary>
        [Export] public Godot.Collections.Array<PackedScene> ActionEffectScenes { get; set; } = new();

        [ExportCategory("Debug")]
        /// <summary>调试热键开关（按下推送 "combat" 气泡）。</summary>
        [Export] public bool EnableDebugHintHotkey { get; set; } = true;
        /// <summary>调试热键。</summary>
        [Export] public Key DebugHintKey { get; set; } = Key.F7;

        private MainCharacter? _player;
        private Node2D? _companionAnchor;
        private Node2D? _sprite;
        private float _hoverClock;      // 跟随浮动时钟
        private int _layerSign = 1;     // 当前前后层符号
        /// <summary>移动模式：自由游走（环形范围）或跟随（保持距离带）。</summary>
        public enum RoamMode { FreeRoam, Follow }

        private RoamMode _mode = RoamMode.FreeRoam;

        /// <summary>模式切换钩子：自由模式 ⇄ 跟随模式切换时触发（参数 = 新模式）。外部系统可订阅。</summary>
        public event Action<RoamMode>? RoamModeChanged;

        /// <summary>当前是否处于跟随模式（Walk 状态据此切换 move/walk 动画）。</summary>
        public bool IsFollowingMode => _mode == RoamMode.Follow;

        /// <summary>
        /// 统一的模式切换入口：触发 RoamModeChanged 钩子 + 可选对话气泡。
        /// notifyDialogue = true 时弹模式切换台词（越界跟随/超时恢复自由等"自然切换"）；
        /// 动作流程内的切换（TriggerAction/TryFirePendingAction）传 false——已有动作气泡，避免三连气泡。
        /// </summary>
        private void ChangeRoamMode(RoamMode newMode, bool notifyDialogue)
        {
            if (_mode == newMode) return;
            _mode = newMode;
            RoamModeChanged?.Invoke(newMode);

            if (notifyDialogue && _dialogue != null)
            {
                _dialogue.Speak(newMode == RoamMode.Follow
                    ? P2DialogueEvent.FollowStarted
                    : P2DialogueEvent.FreeRoamStarted);
            }
        }

        /// <summary>玩家当前位置（供外部系统做距离判定）。</summary>
        public Vector2 PlayerPosition => _player?.GlobalPosition ?? GlobalPosition;

        /// <summary>玩家瞬移/换房间后：把 P2 直接放到跟随点并清掉旧目标缓存。
        /// 否则远距离瞬移会让 P2 切到 Follow 模式，以 FollowSpeed 从旧位置一路飞过去。</summary>
        public void SnapToPlayer()
        {
            if (_player == null || !GodotObject.IsInstanceValid(_player)) return;

            GlobalPosition = ComputeFollowPosition(_player.GlobalPosition);
            _smoothTarget = null;
            _moveTarget = null;
            _pendingAction = false;
            _wanderTimer = 0f;
            _followElapsed = 0f;
            Velocity = Vector2.Zero;

            UpdateMotionState();
            UpdateVisualFacing(GlobalPosition);
            UpdateDynamicLayering();
        }

        /// <summary>拾取/拖回武器期间忽略移动范围约束（由 P2WeaponCarrier 设置），
        /// 防止玩家移动导致 P2 被持续拉回打断拾取流程。</summary>
        public bool IgnoreMoveRange { get; set; }
        /// <summary>移动速度覆盖（px/秒）：任务（如武器搬运）设置后优先于模式速度（FollowSpeed/FreeRoamSpeed）；null 按模式取。</summary>
        public float? MoveSpeedOverride { get; set; }
        private Vector2? _moveTarget;              // 决策/游走指定的移动目标（世界坐标），null = 跟随
        private Vector2? _smoothTarget;            // 目标平滑缓冲（对移动目标做指数平滑，消除方向突变）
        private float _wanderTimer;                // 空闲游走计时
        private float _hitInvincibilityRemaining;  // 受击免疫剩余时间
        private bool _pendingAction;               // 等待接近玩家后触发的 action（决策动作两阶段）
        private float _followElapsed;              // 跟随模式已持续时长（超 FollowMaxDuration 后强制退出）

        private P2DialogueController? _dialogue;   // 对话控制器（气泡逻辑独立组件）

        public override void _Ready()
        {
            AddToGroup("companions"); // 供 GameStateProvider 组回退识别

            _dialogue = GetNodeOrNull<P2DialogueController>(DialogueControllerPath);

            ResolveReferences();

            if (_player != null)
            {
                // 初始放到跟随点、同步朝向与层级，并播 "ready" 气泡（经对话控制器）
                GlobalPosition = ComputeFollowPosition(_player.GlobalPosition);
                UpdateVisualFacing(GlobalPosition); // 初始无移动目标 → 保持默认朝向
                UpdateDynamicLayering();
                _dialogue?.Speak(P2DialogueEvent.Ready);
            }

            // 初始化状态机（P2.tscn 的 StateMachine 节点，状态见 scripts/companions/states/）
            GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine")?.Initialize(this);
        }

        /// <summary>播放 Spine 动画：主 SpineSprite + OutlineLayer 下全部描边精灵同步播放（仿 MainCharacter）。
        /// 主精灵未挂 SpineController 时跳过（由 P2.tscn 配置），outline 始终生效。</summary>
        public void PlaySpineAnimation(string animationName, bool loop = true)
        {
            if (string.IsNullOrWhiteSpace(animationName)) return;

            var spine = GetNodeOrNull<Node>("SpineSprite");
            if (spine != null && spine.HasMethod("play"))
                spine.Call("play", animationName, loop);

            var outlineLayer = GetNodeOrNull<Node>("OutlineLayer");
            if (outlineLayer == null) return;
            foreach (Node child in outlineLayer.GetChildren())
            {
                if (child.HasMethod("play"))
                    child.Call("play", animationName, loop);
            }
        }

        public override void _Notification(int what)
        {
            // P2 被隐藏时（过场 HideNodePaths 触发），立即取消正在显示的 hint 气泡
            // （子节点收不到父级 VisibilityChanged 通知，故由根节点转发给对话控制器）
            if (what == NotificationVisibilityChanged && !Visible)
                _dialogue?.CancelActiveHint();
        }

        public override void _PhysicsProcess(double delta)
        {
            ResolveReferences();
            if (_player == null)
            {
                return;
            }

            _hoverClock += (float)delta;

            // 受击免疫计时
            if (_hitInvincibilityRemaining > 0f)
                _hitInvincibilityRemaining -= (float)delta;

            // 计算移动目标（双模式：自由游走/跟随）
            Vector2 rawTarget = ComputeMovementTarget((float)delta);

            // 目标平滑：对移动目标做指数平滑，消除目标突变（跟随↔游走↔搬运切换）造成的方向跳变；
            // 位置移动仍由下方 FollowSmoothing + 速度钳制执行（近距离减速 + 匀速上限）
            if (!_smoothTarget.HasValue)
                _smoothTarget = rawTarget;
            else
                _smoothTarget = _smoothTarget.Value.Lerp(rawTarget, 1f - Mathf.Exp(-Mathf.Max(0.1f, TargetSmoothing) * (float)delta));
            Vector2 target = _smoothTarget.Value;

            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, FollowSmoothing) * (float)delta);
            Vector2 next = GlobalPosition.Lerp(target, blend);

            // 速度上限：任务覆盖（如武器搬运 CarrySpeed）优先，否则按模式区分（跟随 > 自由游走，保证跟上玩家）
            float speedLimit = MoveSpeedOverride ?? (_mode == RoamMode.Follow ? FollowSpeed : FreeRoamSpeed);
            float maxStep = Mathf.Max(10f, speedLimit) * (float)delta;
            Vector2 step = next - GlobalPosition;
            if (step.Length() > maxStep)
            {
                next = GlobalPosition + step.Normalized() * maxStep;
            }

            // 射线碰撞：自由游走时向移动方向检测空气墙，命中则贴墙（赋值前检测）
            next = ApplyRayCollision(GlobalPosition, next);
            GlobalPosition = next;

            UpdateMotionState(); // 状态机：有目标 → Walk，否则 Idle（位移唯一由本方法驱动）
            UpdateVisualFacing(target);
            UpdateDynamicLayering();

            if (EnableDebugHintHotkey && Input.IsKeyPressed(DebugHintKey))
            {
                _dialogue?.Speak(P2DialogueEvent.Combat);
            }
        }

        /// <summary>
        /// 移动目标解析（P2 位移的唯一驱动者，状态机不参与位置计算）。
        /// 双模式：
        /// - 自由模式（FreeRoam）：在 MoveRangeMin~Max 环形区域内游走；超出 MoveRangeMax 切换到跟随模式；
        /// - 跟随模式（Follow）：接近玩家直到进入 FollowRangeMin~Max 区间，然后恢复自由模式。
        /// 自由模式下：显式目标（决策）→ 移向目标；无目标 → 定时生成随机点；等待期间停在原地。
        /// </summary>
        private Vector2 ComputeMovementTarget(float delta)
        {
            // 玩家死亡：守尸——唯一移动目标 = 玩家位置（走向玩家停在身边），忽略游走/距离带/范围约束
            if (_player != null && (_player.IsDeathSequenceActive || _player.IsDead))
            {
                return _player.GlobalPosition;
            }

            Vector2 anchor = _companionAnchor?.GlobalPosition ?? _player!.GlobalPosition;
            float distToPlayer = GlobalPosition.DistanceTo(anchor);

            // ── 模式切换：唯一出口 = FollowMaxDuration 超时（非挂起 action 时计时）──
            if (!IgnoreMoveRange && _mode == RoamMode.FreeRoam && distToPlayer > MoveRangeMax)
            {
                // 超出自由范围 → 切跟随模式（清空目标，开始计时）；自然切换 → 弹"跟随开始"对话
                ChangeRoamMode(RoamMode.Follow, notifyDialogue: true);
                _moveTarget = null;
                _followElapsed = 0f;
            }
            else if (_mode == RoamMode.Follow && !_pendingAction)
            {
                // 跟随持续计时：超过 FollowMaxDuration 后即使距离未达标也恢复自由，避免无限跟随
                _followElapsed += delta;
                if (_followElapsed >= FollowMaxDuration)
                {
                    ChangeRoamMode(RoamMode.FreeRoam, notifyDialogue: true); // 超时恢复自由 → 弹"自由游走"对话
                    _moveTarget = null;
                }
            }

            // ── 跟随模式：保持 [FollowRangeMin, FollowRangeMax] 距离带（近了后退、远了靠近、带内原地）──
            // 有显式目标（如 WeaponCarrier 搬运）时让位于它（方案 C）
            if (_mode == RoamMode.Follow)
            {
                TryFirePendingAction(distToPlayer); // 进入动作范围（≤FollowRangeMax）即触发，不贴脸
                if (_moveTarget.HasValue)
                    return _moveTarget.Value;

                if (distToPlayer > FollowRangeMax)
                    return anchor; // 太远：靠近玩家

                if (distToPlayer < FollowRangeMin)
                {
                    // 太近：沿远离方向后退到带内（防玩家重合时方向不稳，退向最小边距点）
                    Vector2 away = GlobalPosition - anchor;
                    Vector2 dir = away.LengthSquared() > 0.01f ? away.Normalized() : Vector2.Right;
                    return anchor + dir * FollowRangeMin;
                }

                return GlobalPosition; // 带内：原地保持
            }

            // ── 自由模式 ──────────────────────────────────────────
            // 1. 显式移动目标优先
            if (_moveTarget.HasValue)
            {
                if (GlobalPosition.DistanceTo(_moveTarget.Value) < ArriveDistance)
                {
                    // 到达：清空目标并重置计时，等 WanderInterval 后再生成下一个，
                    // 避免目标过近时连续快速切换导致来回频率过高
                    _moveTarget = null;
                    _wanderTimer = WanderInterval;
                }
                else
                {
                    return _moveTarget.Value;
                }
            }

            // 2. 常态游走：无目标时定时生成环形内随机目标点
            if (EnableFreeRoam && !_moveTarget.HasValue)
            {
                _wanderTimer -= delta;
                if (_wanderTimer <= 0f)
                {
                    _wanderTimer = WanderInterval;
                    _moveTarget = GenerateWanderTarget(anchor);
                }
                else
                {
                    // 等待下一个随机点期间：停在原地（不回跟随点，避免往返玩家身边）
                    return GlobalPosition;
                }
            }

            // 3. 默认跟随玩家偏移点（纯跟随模式 EnableFreeRoam=false 时）
            return ComputeFollowPosition(anchor);
        }

        /// <summary>原跟随逻辑：玩家偏移点 + 朝向反侧 + 正弦浮动。</summary>
        private Vector2 ComputeFollowPosition(Vector2 anchor)
        {
            float sideSign;
            if (AlwaysFollowBehindPlayer)
            {
                // 跟随玩家背后：偏移取朝向反侧
                sideSign = _player!.FacingRight ? -1f : 1f;
            }
            else
            {
                sideSign = _player.FacingRight ? 1f : -1f;
                if (!KeepCompanionOnFacingSide)
                {
                    // 保持世界空间固定侧（转身不穿越玩家）
                    sideSign = GlobalPosition.X >= anchor.X ? 1f : -1f;
                }
            }

            // 正弦浮动
            float hover = Mathf.Sin(_hoverClock * Mathf.Tau * FloatFrequency) * FloatAmplitude;
            return anchor + new Vector2(FollowOffset.X * sideSign, FollowOffset.Y + hover);
        }

        /// <summary>射线碰撞（类似 ECore）：自由游走时从当前位置向移动目标发射线，
        /// 命中墙体则移动到命中点贴墙停住。仅"自由游走"状态检测：
        /// - 拾取/拖拽武器（IgnoreMoveRange）不检测（可穿墙执行任务）
        /// - 跟随模式、action 两阶段（接近/播放）不检测</summary>
        private Vector2 ApplyRayCollision(Vector2 from, Vector2 to)
        {
            if (!ClampToBoundary) return to;
            if (IgnoreMoveRange) return to;                       // 拾取/拖拽流程中
            if (_mode != RoamMode.FreeRoam) return to;            // 非自由模式（含跟随模式）
            if (_pendingAction) return to;                        // action 接近阶段
            var sm = GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine");
            if (sm?.CurrentState?.Name == "Action") return to;    // action 播放阶段

            Vector2 dir = to - from;
            if (dir.LengthSquared() < 0.0001f) return to;

            var query = new PhysicsRayQueryParameters2D
            {
                From = from,
                To = to,
                CollisionMask = BoundaryCollisionMask,
                Exclude = new Godot.Collections.Array<Rid> { GetRid() }, // 排除自身碰撞体
                CollideWithAreas = false,
                CollideWithBodies = true,
            };
            var result = GetWorld2D().DirectSpaceState.IntersectRay(query);
            if (result.Count == 0) return to;

            // 命中：移动到命中点并略微回退（贴墙停住，避免贴边抖动）
            Vector2 hitPos = result["position"].AsVector2();
            return hitPos - dir.Normalized() * 2f;
        }

        /// <summary>生成环形区域（Min~Max）内的随机游走目标点。</summary>
        private Vector2 GenerateWanderTarget(Vector2 anchor)
        {
            float angle = GD.Randf() * Mathf.Tau;
            float radius = Mathf.Lerp(MoveRangeMin, MoveRangeMax, GD.Randf());
            return anchor + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>设置移动目标（世界坐标）。决策/游走调用；null 恢复跟随。
        /// 新任务重置目标平滑缓冲，避免从旧轨迹过渡（如武器搬运切换目标）。</summary>
        public void SetMoveTarget(Vector2? target)
        {
            _moveTarget = target;
            _smoothTarget = null;
        }

        /// <summary>停止当前移动目标（受击硬直时由 Hit 状态调用）。</summary>
        public void StopMoving()
        {
            _moveTarget = null;
        }

        /// <summary>
        /// 执行决策动作（治疗/护盾等）：两阶段——先进入跟随模式接近玩家，
        /// 到达 FollowRangeMin 内（TryFirePendingAction）才切 Action 状态播 action 动画。
        /// </summary>
        public void TriggerAction()
        {
            ChangeRoamMode(RoamMode.Follow, notifyDialogue: false); // 动作流程内切换：不发模式对话（已有动作气泡）
            _moveTarget = null;
            _pendingAction = true;
        }

        /// <summary>
        /// 在玩家身上生成动作特效（如护盾/治疗/装备加成），返回生成的实例（供调用方按生命周期销毁）。
        /// effectIndex 对应 ActionEffectScenes 数组约定索引：[0] 护盾、[1] 治疗、[2] 装备加成恢复；
        /// 越界/空场景/玩家缺失时返回 null。特效挂玩家视觉挂点容器下（跟随移动）：
        /// 锚点偏移由内部子节点承载（VisualAnchorAttach），根保持原点以正确参与 y-sort
        /// （画在角色之上,而不是被角色遮挡）——与 A_009 护盾同一体例。
        /// 特效需自带自动销毁（EffectAutoDestroy/FadeInOutDestroy）。
        /// </summary>
        /// <summary>取动作特效场景(不实例化;供共享控制器如 ChargeShieldController 自管理视觉时使用)。</summary>
        public PackedScene? GetActionEffectScene(int effectIndex)
            => ActionEffectScenes != null && effectIndex >= 0 && effectIndex < ActionEffectScenes.Count
                ? ActionEffectScenes[effectIndex]
                : null;

        public Node? SpawnActionEffect(int effectIndex)
        {
            if (_player == null || ActionEffectScenes == null) return null;
            if (effectIndex < 0 || effectIndex >= ActionEffectScenes.Count) return null;

            var scene = ActionEffectScenes[effectIndex];
            if (scene == null) return null;

            var instance = scene.Instantiate<Node>();
            if (instance is Node2D fxNode)
            {
                Kuros.Fx.VisualAnchorAttach.Attach(fxNode, _player);
            }
            else
            {
                _player.AddChild(instance);
            }

            return instance;
        }

        /// <summary>跟随模式进入动作范围（≤ FollowRangeMax，即带内）时触发挂起的 action 动画——不贴脸。
        /// 触发即恢复自由模式；保留 _moveTarget（方案 C：不破坏 WeaponCarrier 搬运目标）。</summary>
        private void TryFirePendingAction(float distToPlayer)
        {
            if (!_pendingAction) return;
            if (distToPlayer > FollowRangeMax) return; // 还没进入动作范围
            _pendingAction = false;
            ChangeRoamMode(RoamMode.FreeRoam, notifyDialogue: false); // 动作流程内切换：不发模式对话
            GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine")?.ChangeState("Action");
        }

        /// <summary>状态机协作：自由模式有移动目标 → Walk；跟随模式（移向玩家，目标已被清空）→ Walk；否则 Idle。
        /// 跟随模式必须算作移动，否则状态停在 Idle 播 walk 循环，move 动画永远无法播放。
        /// 只在 Walk/Idle 之间切换：当前处于 Action/Hit/Stun 等动作状态时不打断，
        /// 否则每帧的 ChangeState 会在下一帧把 action 动画顶掉。</summary>
        private void UpdateMotionState()
        {
            var sm = GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine");
            if (sm == null) return;

            // 动作/受击/眩晕等状态不打断（播完自行回 Idle 后再接管）
            string current = sm.CurrentState?.Name ?? string.Empty;
            if (current != "Walk" && current != "Idle") return;

            string targetState = _moveTarget.HasValue || _mode == RoamMode.Follow ? "Walk" : "Idle";
            if (current != targetState)
                sm.ChangeState(targetState);
        }

        /// <summary>
        /// 受击入口（DamageDispatcher 通过 HasMethod("TakeDamage") 命中）。
        /// 扣 HP + 免疫窗口 + 切 Hit 状态（播 hit 动画）。
        /// </summary>
        public void TakeDamage(int damage, Vector2? attackOrigin = null, GameActor? attacker = null,
            DamageSource damageSource = DamageSource.DirectAttack)
        {
            if (damage <= 0) return;
            if (_hitInvincibilityRemaining > 0f) return; // 免疫窗口内忽略
            if (ReportedCurrentHp <= 0) return;

            ReportedCurrentHp = Mathf.Max(0, ReportedCurrentHp - damage);
            _hitInvincibilityRemaining = HitInvincibilityDuration;

            GetNodeOrNull<Kuros.Systems.FSM.StateMachine>("StateMachine")?.ChangeState("Hit");
        }

        /// <summary>解析玩家/精灵/锚点引用（三级回退：相对路径 → ../归一化 → 组回退）。</summary>
        private void ResolveReferences()
        {
            if (_player == null || !IsInstanceValid(_player) || !_player.IsInsideTree())
            {
                _player = GetNodeOrNull<MainCharacter>(PlayerPath)
                    ?? GetNodeOrNull<MainCharacter>(NormalizeRelativePath(PlayerPath))
                    ?? GetTree().GetFirstNodeInGroup("player") as MainCharacter;
            }

            if (_player == null)
            {
                _companionAnchor = null;
                return;
            }

            _sprite ??= GetNodeOrNull<Node2D>(SpritePath);

            if (_companionAnchor == null || !IsInstanceValid(_companionAnchor) || !_companionAnchor.IsInsideTree())
            {
                _companionAnchor = _player.GetNodeOrNull<Node2D>(CompanionAnchorPath)
                    ?? _player.FindChild(CompanionAnchorPath.ToString(), recursive: true, owned: false) as Node2D;
            }
        }

        /// <summary>动态渲染层级：按 Y 差（或朝向）切换 ZIndex，实现前后遮挡。</summary>
        private void UpdateDynamicLayering()
        {
            if (!EnableDynamicLayering || _player == null)
            {
                return;
            }

            if (AlwaysFollowBehindPlayer)
            {
                // 跟随背后：固定在后层
                ZIndex = _player.ZIndex + BackLayerDelta;
                return;
            }

            if (LayerByFacingDirection)
            {
                // 按朝向判断前后：与玩家朝向同侧在前
                float xDiff = GlobalPosition.X - _player.GlobalPosition.X;
                if (Mathf.Abs(xDiff) > Mathf.Max(0f, LayerSwitchDeadZone))
                {
                    bool sameSideAsFacing = _player.FacingRight ? xDiff >= 0f : xDiff <= 0f;
                    _layerSign = sameSideAsFacing ? 1 : -1;
                }
            }
            else
            {
                // 按 Y 差判断前后：Y 更大（下方）在前
                float yDiff = GlobalPosition.Y - _player.GlobalPosition.Y;
                if (Mathf.Abs(yDiff) > Mathf.Max(0f, LayerSwitchDeadZone))
                {
                    _layerSign = yDiff >= 0f ? 1 : -1;
                }
            }

            int delta = _layerSign >= 0 ? FrontLayerDelta : BackLayerDelta;
            ZIndex = _player.ZIndex + delta;
        }

        /// <summary>
        /// 朝向同步：统一不跟随玩家朝向，只跟随移动 X 轴方向（垂直移动/站定时保持当前朝向）。
        /// Spine 角色翻转用 Scale.X（同 GameActor 的 spine 翻转方式），保留原缩放绝对值（P2 根 Scale 0.33）。
        /// </summary>
        private void UpdateVisualFacing(Vector2 target)
        {
            if (_player == null)
            {
                return;
            }

            // 按移动 X 轴方向朝向（垂直移动/站定保持当前朝向，不受玩家朝向影响）
            float dx = target.X - GlobalPosition.X;
            if (Mathf.Abs(dx) < 0.1f) return;
            float sign = dx > 0f ? 1f : -1f;

            // 主精灵
            if (_sprite != null)
                _sprite.Scale = new Vector2(Mathf.Abs(_sprite.Scale.X) * sign, _sprite.Scale.Y);

            // OutlineLayer 下的描边精灵同步翻转
            var outlineLayer = GetNodeOrNull<Node2D>("OutlineLayer");
            if (outlineLayer == null) return;
            foreach (Node child in outlineLayer.GetChildren())
            {
                if (child is Node2D outline)
                    outline.Scale = new Vector2(Mathf.Abs(outline.Scale.X) * sign, outline.Scale.Y);
            }
        }

        /// <summary>相对路径归一化：无 ../ 前缀时补上（相对本节点的路径统一为 ../ 形式）。</summary>
        private static NodePath NormalizeRelativePath(NodePath path)
        {
            string text = path.ToString();
            if (string.IsNullOrWhiteSpace(text) || text.StartsWith("../", System.StringComparison.Ordinal))
            {
                return path;
            }

            return new NodePath($"../{text}");
        }
    }
}
