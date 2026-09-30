using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using Kuros.Fx;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// 敌人能量弹：**固定发射角 + 弱追踪 + 定时自毁**，视觉与伤害分离。
    ///
    /// 与另外两者的分工：
    ///   · <see cref="EnergyOrbShot"/>：出手瞬间快照玩家位置、直线飞向**落点**，到点生成落地特效——不做追踪；
    ///   · <c>LaserBeamUltimate</c>：开局即朝玩家、drag 模型强追踪、带拖尾，到点/命中销毁；
    ///   · **本弹**：发射角是**固定值**（<see cref="LaunchAngleDegrees"/>，相对射手朝向，0 = 正前方），
    ///     出手**不会** 360° 瞬瞄玩家；之后只有很弱的追踪（<see cref="DragFactor"/> 越小越直），
    ///     <see cref="Duration"/> 到点自行淡出销毁。齐射想散开就开 <see cref="AimJitterPixels"/>。
    ///
    /// 视觉与伤害分离（沿用两个参考实现的做法）：
    ///   · 视觉：只有 <c>Visual</c> 子节点跟着速度方向转（判定区不转，形状不随朝向变化）；
    ///   · 伤害：独立挂在子节点 <c>AttackArea</c> 上，靠 AreaEntered/BodyEntered 触发，
    ///     与移动/旋转完全无关——换视觉不用碰伤害参数，反之亦然。
    ///
    /// 实现 <see cref="IFacingDirectional"/>：生成链会把射手的朝向注入进来（朝左时发射角自动镜像）。
    /// </summary>
    public partial class EnergyBullet : Node2D, IAttackerProvider, IFacingDirectional
    {
        public GameActor? Attacker { get; set; }

        /// <summary>射手朝向（生成链注入）：发射角相对它取，朝左镜像。</summary>
        [Export] public bool FacingRight { get; set; } = true;

        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "50,4000,10")] public float Speed { get; set; } = 900f;

        /// <summary>追踪强度（drag 模型，帧率无关）。0 = 纯直线；数值是"每帧朝目标插值的比例"，
        /// 一秒内的转向完成度 ≈ 1 − (1−值)^60：0.01 ≈ 45%/秒（弱，会绕大弧）、0.04 ≈ 91%/秒（很粘）、
        /// 0.08 以上基本一两个身位就咬死。想做"弱追踪"保持 0.01~0.02。</summary>
        [Export(PropertyHint.Range, "0,1,0.005")] public float DragFactor { get; set; } = 0.015f;

        /// <summary>发射角（度，**相对射手朝向**）：0 = 正前方，正 = 顺时针偏。出手时只按这个角给初速。</summary>
        [Export(PropertyHint.Range, "-180,180,1")] public float LaunchAngleDegrees { get; set; } = 0f;

        /// <summary>每颗弹的落点随机偏移半径（px）：齐射由此散开，不叠成一条线。0 = 全部瞄中心。</summary>
        [Export(PropertyHint.Range, "0,400,1")] public float AimJitterPixels { get; set; } = 0f;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float Duration { get; set; } = 3f;
        /// <summary>到期/命中后的淡出时长（秒）；0 = 立即销毁。</summary>
        [Export(PropertyHint.Range, "0,2,0.05")] public float FadeOutDuration { get; set; } = 0.15f;

        [ExportCategory("Visual")]
        /// <summary>只跟着**速度方向**旋转的视觉节点（判定区不转）。空 = 旋转根节点自身。</summary>
        [Export] public NodePath VisualPath { get; set; } = new("Visual");

        /// <summary>需要**自旋**的装饰节点（相对根路径；一般放在 Visual 下，这样自旋叠加在"朝向"之上）。
        /// 每帧按 <see cref="SpinDegreesPerSecond"/> 累加自身旋转。空 = 不启用。
        /// 与 <see cref="VisualPath"/> 的分工：那个是"整体朝速度方向"，本节点是"在朝向前提下自己转"。</summary>
        [Export] public NodePath SpinSpritePath { get; set; } = new();

        /// <summary>自旋角速度（度/秒；正 = 顺时针）。</summary>
        [Export(PropertyHint.Range, "-1440,1440,1")] public float SpinDegreesPerSecond { get; set; } = 90f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions { get; set; } = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;
        [Export(PropertyHint.Range, "0,500,1")] public int Damage { get; set; } = 12;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance { get; set; } = 90f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration { get; set; } = 0.18f;

        private Area2D? _attackArea;
        private Node2D? _visual;
        private Node2D? _spinNode;
        private Vector2 _velocity;
        private Vector2 _aimJitter;
        private float _timer;
        private bool _started;
        private bool _hit;
        private bool _fading;
        private float _fadeElapsed;
        private Node2D? _player;

        public override void _Ready()
        {
            // 不继承父节点变换：生成方把特效挂在敌人的父节点下，而炮位/机枪台这类敌人的父节点是
            // **滑槽的 Mount**（会随滑槽纵向滑动）——不设 TopLevel 的话弹会跟着滑槽一起漂。
            // 与 EnergyOrbShot / PlayerAttackTemplate.HitEffectForceTopLevel 同一套处理。
            TopLevel = true;

            _timer = Duration;

            _attackArea = GetNodeOrNull<Area2D>("AttackArea");
            _visual = VisualPath.IsEmpty ? null : GetNodeOrNull<Node2D>(VisualPath);
            _spinNode = SpinSpritePath.IsEmpty ? null : GetNodeOrNull<Node2D>(SpinSpritePath);

            if (_attackArea != null)
            {
                _attackArea.AreaEntered += OnAreaEntered;
                _attackArea.BodyEntered += OnBodyEntered;
            }
            else
            {
                GD.PushWarning($"{Name}: 未找到 AttackArea（伤害判定区），本弹只会飞、不会伤人");
            }

            if (AimJitterPixels > 0f)
            {
                float angle = (float)GD.RandRange(0.0, Mathf.Tau);
                float radius = (float)GD.RandRange(0.0, AimJitterPixels);
                _aimJitter = Vector2.Right.Rotated(angle) * radius;
            }
        }

        public override void _ExitTree()
        {
            if (_attackArea != null && GodotObject.IsInstanceValid(_attackArea))
            {
                _attackArea.AreaEntered -= OnAreaEntered;
                _attackArea.BodyEntered -= OnBodyEntered;
            }
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // ─ 淡出：冻结移动，只衰减视觉 alpha ─────────────────────
            if (_fading)
            {
                _fadeElapsed += dt;
                float t = FadeOutDuration > 0f ? Mathf.Clamp(_fadeElapsed / FadeOutDuration, 0f, 1f) : 1f;
                if (t >= 1f)
                {
                    QueueFree();
                    return;
                }

                CanvasItem fadeTarget = _visual is CanvasItem canvasVisual ? canvasVisual : this;
                Color modulate = fadeTarget.Modulate;
                modulate.A = 1f - t;
                fadeTarget.Modulate = modulate;
                return;
            }

            if (_player == null || !GodotObject.IsInstanceValid(_player))
                _player = GetTree().GetFirstNodeInGroup("player") as Node2D;

            // ─ 首帧：按**固定发射角**给初速（相对射手朝向）──────────
            // 不朝玩家——"出手即 360° 锁人"是这套和 LaserBeamUltimate 的核心区别。
            //
            // 朝左时是**沿 X 镜像**（dir.X 取负），不是整体转 180°：整套骨架的 d_* 姿态是美术
            // 手绘的水平镜像孪生，镜子里"下偏"还是下偏。整体转 180° 会把上下也翻过去——
            // 表现为 LaunchAngleDegrees = -45 时 attack 朝右下、d_attack 却朝左上。
            if (!_started)
            {
                _started = true;
                Vector2 dir = Vector2.Right.Rotated(Mathf.DegToRad(LaunchAngleDegrees));
                if (!FacingRight)
                    dir.X = -dir.X;
                _velocity = dir * Speed;
            }

            // ─ 弱追踪：只转**方向**、速度恒定（drag 模型，帧率无关）──────────
            // 注意不能像 LaserBeamUltimate 那样插值整个速度向量：目标在正后方时
            // 速度会穿过零点 → 弹先"急停"再掉头。这里插值的是单位方向向量，
            // 速率永远不变；两者正好相反时 Lerp 退化成零向量，那一帧保持原方向、下一帧继续转。
            if (_player != null && DragFactor > 0f && _velocity.LengthSquared() > 0.01f)
            {
                Vector2 toAim = (GetPlayerAimCenter(_player) + _aimJitter) - GlobalPosition;
                if (toAim.LengthSquared() > 0.01f)
                {
                    Vector2 dir = _velocity.Normalized();
                    Vector2 desiredDir = toAim.Normalized();
                    float lerpT = 1f - Mathf.Pow(1f - Mathf.Clamp(DragFactor, 0f, 1f), dt * 60f);
                    Vector2 turned = dir.Lerp(desiredDir, lerpT);
                    if (turned.LengthSquared() > 0.0001f)
                        _velocity = turned.Normalized() * Speed;
                }
            }

            GlobalPosition += _velocity * dt;

            // ─ 只转视觉：判定区不转，形状不随朝向变化（视觉/伤害分离）──
            if (_velocity.LengthSquared() > 0.1f)
            {
                if (_visual != null) _visual.Rotation = _velocity.Angle();
                else Rotation = _velocity.Angle();
            }

            // 装饰自旋：叠加在节点自身旋转上（一般挂在 Visual 下 = 在"朝向前方"的基础上自转）
            if (_spinNode != null && GodotObject.IsInstanceValid(_spinNode))
                _spinNode.Rotation += Mathf.DegToRad(SpinDegreesPerSecond) * dt;

            _timer -= dt;
            if (_timer <= 0f) StartFade();
        }

        // ── 伤害（独立于移动/旋转：靠 AttackArea 的接触信号）──────────────

        private void OnAreaEntered(Area2D area) => TryDealDamage(area.Owner ?? area);

        private void OnBodyEntered(Node2D body) => TryDealDamage(body);

        private void TryDealDamage(Node? target)
        {
            if (_hit || target == null || !GodotObject.IsInstanceValid(target)) return;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, Attacker)) return;

            bool dealt = DamageDispatcher.DealDamage(target, Damage, GlobalPosition, Attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _attackArea, _velocity);
            if (!dealt) return;

            if (target is GameActor hitActor) ApplyKnockbackTo(hitActor);
            _hit = true;
            StartFade();
        }

        /// <summary>击退：位移制（KnockbackDistance/Duration）。
        ///
        /// 玩家侧**不能事后判断无敌帧**：这一下命中本身就会开无敌帧，打完之后再看
        /// `IsHitInvincible` 会把"刚命中的这一击"也判成已无敌 → 表现就是"只推得动敌人、推不动玩家"。
        /// 那条规则现在由 `GameActor.ApplyKnockbackDisplacement` 内部统一把关
        /// （`MainCharacter` 覆写 `AllowsKnockback` → 读一次 `ConsumePendingHitKnockback`），
        /// 所以这里直接调用即可，不需要自己判断（见 EFFECT_STANDARD.md 第四条）。</summary>
        private void ApplyKnockbackTo(GameActor actor)
        {
            if (KnockbackDistance <= 0f || _velocity.LengthSquared() <= 0.01f) return;

            actor.ApplyKnockbackDisplacement(_velocity.Normalized(), KnockbackDistance, KnockbackDuration);
        }

        private void StartFade()
        {
            if (_fading) return;
            if (FadeOutDuration <= 0f)
            {
                QueueFree();
                return;
            }

            _fading = true;
            _fadeElapsed = 0f;
            _velocity = Vector2.Zero;   // 淡出期间原地衰减
        }

        /// <summary>转向瞄准中心 = 玩家 HitArea 的碰撞形状位置（拿不到就退回玩家原点）。</summary>
        private static Vector2 GetPlayerAimCenter(Node2D player)
        {
            var hitArea = player.GetNodeOrNull<Area2D>("HitArea")
                ?? player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? player.GlobalPosition;
        }
    }
}
