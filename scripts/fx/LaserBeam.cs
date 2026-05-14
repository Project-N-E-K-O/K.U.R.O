using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 激光束视觉特效节点。
    /// 实例化后自动播放并在 <see cref="Lifetime"/> 到期后销毁自身。
    /// 使用两条 Line2D（外层光晕 + 内层光束）呈现激光效果；
    /// RayCast2D 用于检测碰撞并裁切光束长度。
    ///
    /// 朝向规则（当 <see cref="AutoAimAtPlayer"/> 为 true）：
    ///   激光基础方向为水平（左/右取决于玩家水平位置），
    ///   垂直方向仅在 ±<see cref="MaxVerticalTiltDegrees"/> 范围内跟随玩家高度微调。
    /// 也可调用 <see cref="LookAtGlobal"/> 手动指定精确朝向。
    /// </summary>
    public partial class LaserBeam : Node2D
    {
        // ── 导出参数 ──────────────────────────────────────────────

        /// <summary>光束最大长度（像素，局部坐标 +X 方向）。</summary>
        [ExportCategory("Beam")]
        [Export] public float MaxLength = 3000f;

        /// <summary>内层光束颜色。</summary>
        [Export] public Color BeamColor = new Color(1f, 0.85f, 0.2f, 1f);

        /// <summary>外层光晕颜色。</summary>
        [Export] public Color GlowColor = new Color(1f, 0.6f, 0.05f, 0.35f);

        /// <summary>内层光束宽度（像素）。</summary>
        [Export] public float BeamWidth = 8f;

        /// <summary>外层光晕宽度（像素）。</summary>
        [Export] public float GlowWidth = 32f;

        /// <summary>特效存在总时长（秒）。</summary>
        [ExportCategory("Timing")]
        [Export] public float Lifetime = 0.45f;

        /// <summary>淡出时长（秒），从 Lifetime 末尾开始淡出。</summary>
        [Export] public float FadeDuration = 0.15f;

        [ExportCategory("Damage")]
        /// <summary>激光命中玩家造成的伤害（0 = 不造成伤害）。</summary>
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 0;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;

        /// <summary>击退持续时间（秒）。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        /// <summary>命中时施加的击退速度（像素/秒，0 = 不击退）。</summary>
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 0f;
        /// <summary>
        /// 若为 true，首帧自动查找玩家并计算朝向：
        /// 水平方向由玩家相对位置决定（左/右），
        /// 垂直方向仅在 ±<see cref="MaxVerticalTiltDegrees"/> 范围内微调。
        /// </summary>
        [ExportCategory("Targeting")]
        [Export] public bool AutoAimAtPlayer = true;   

        /// <summary>垂直倾斜最大角度（度）。激光基础方向水平，此值限制上下偏转幅度。</summary>
        [Export(PropertyHint.Range, "0,180,0.5")] public float MaxVerticalTiltDegrees = 5f;

        /// <summary>
        /// 激光水平朝向：true = 向右，false = 向左。<br/>
        /// 通过 EnemyAttackTemplate.SpawnEffectAtEnemy 生成时自动由敌人朝向设置，
        /// 无需手动配置。
        /// </summary>
        [Export] public bool FacingRight = true;

        // ── 子节点引用 ────────────────────────────────────────────

        private RayCast2D? _ray;
        private Line2D? _glowLine;
        private Line2D? _beamLine;

        // ── 运行时状态 ────────────────────────────────────────────

        private float _timer;
        private Color _initBeamColor;
        private Color _initGlowColor;
        /// <summary>是否还没执行过首帧自动对准。</summary>
        private bool _pendingAutoAim;
        /// <summary>是否已经在本次激活中造成过伤害（每次实例化只触发一次）。</summary>
        private bool _hasDamaged;
        /// <summary>autoAim 时缓存的玩家节点，供 TryDamagePlayer 直接使用，避免射线被敌人自身 Area2D 阻挡。</summary>
        private Node2D? _cachedPlayer;

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _ray      = GetNodeOrNull<RayCast2D>("RayCast2D");
            _glowLine = GetNodeOrNull<Line2D>("GlowLine");
            _beamLine = GetNodeOrNull<Line2D>("BeamLine");

            if (_ray == null || _glowLine == null || _beamLine == null)
            {
                GD.PushWarning("[LaserBeam] 缺少子节点，请检查场景结构。");
                QueueFree();
                return;
            }

            // 应用导出颜色 / 宽度（支持在场景或代码中覆盖）
            _beamLine.Width        = BeamWidth;
            _beamLine.DefaultColor = BeamColor;
            _glowLine.Width        = GlowWidth;
            _glowLine.DefaultColor = GlowColor;

            // 缓存初始颜色用于淡出计算
            _initBeamColor = BeamColor;
            _initGlowColor = GlowColor;

            _ray.TargetPosition = new Vector2(MaxLength, 0f);
            _ray.Enabled        = true;

            _timer = Lifetime;

            // 自动对准延迟到 _Process 第一帧执行：
            // AddChild 触发 _Ready 时 GlobalPosition 尚未由 SpawnEffectAtEnemy 设置，
            // 需等到下一帧位置就位后再计算朝向。
            _pendingAutoAim = AutoAimAtPlayer;

            // 去掉这行，等_Process第一帧位置和朝向都就位后再画
            //UpdateBeam();
        }

        public override void _Process(double delta)
        {
            // 首帧：GlobalPosition 已由 SpawnEffectAtEnemy 设置完毕，此时再对准才准确
            if (_pendingAutoAim)
            {
                _pendingAutoAim = false;
                var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (player != null)
                {
                    _cachedPlayer = player;
                    // 瞄准 HitArea 的 CollisionShape2D 中心（即受击体积的实际中心）
                    // HitArea 节点本身无位置偏移，偏移在其子节点 CollisionShape2D 上
                    var hitArea = player.GetNodeOrNull<Area2D>("HitArea")
                        ?? player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
                    var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                    Vector2 aimTarget = hitShape?.GlobalPosition
                        ?? hitArea?.GlobalPosition
                        ?? player.GlobalPosition;
                    AimHorizontalWithVerticalTilt(aimTarget);
                }
                UpdateBeam();
                TryDamagePlayer();
            }

            _timer -= (float)delta;

            if (_timer <= 0f)
            {
                QueueFree();
                return;
            }

            // 淡出
            if (_timer < FadeDuration && FadeDuration > 0f)
            {
                float t = _timer / FadeDuration;
                if (_beamLine != null)
                    _beamLine.DefaultColor = new Color(
                        _initBeamColor.R, _initBeamColor.G, _initBeamColor.B,
                        _initBeamColor.A * t);
                if (_glowLine != null)
                    _glowLine.DefaultColor = new Color(
                        _initGlowColor.R, _initGlowColor.G, _initGlowColor.B,
                        _initGlowColor.A * t);
            }

            UpdateBeam();
        }

        // ── 公开 API ──────────────────────────────────────────────

        /// <summary>
        /// 水平朝向 + 垂直小角度倾斜：<br/>
        /// 水平基础方向由 <paramref name="globalTarget"/> 的 X 相对位置决定（左/右），<br/>
        /// 垂直偏转角由玩家高度差决定，但限制在 ±<see cref="MaxVerticalTiltDegrees"/> 以内。
        /// </summary>
        public void AimHorizontalWithVerticalTilt(Vector2 globalTarget)
        {
            Vector2 toTarget = globalTarget - GlobalPosition;

            // 水平基础角由 FacingRight（敌人朝向）决定，与玩家水平位置无关
            float baseAngle = FacingRight ? 0f : Mathf.Pi;

            // 仅当玩家在敌人正面时才计算垂直倾斜；玩家在背后则保持水平，不翻转
            bool playerInFront = FacingRight ? toTarget.X >= 0f : toTarget.X <= 0f;
            float tiltAngle = 0f;
            if (playerInFront && toTarget != Vector2.Zero)
            {
                float maxTiltRad = Mathf.DegToRad(MaxVerticalTiltDegrees);
                // 向左时倾斜符号需反转：Rotation=π 时顺时针(+)会使 Y 分量变负(向上)，需取反才能向下倾
                float dySign = FacingRight ? 1f : -1f;
                tiltAngle = Mathf.Atan2(toTarget.Y * dySign, Mathf.Abs(toTarget.X));
                tiltAngle = Mathf.Clamp(tiltAngle, -maxTiltRad, maxTiltRad);
            }

            Rotation = baseAngle + tiltAngle;
        }

        /// <summary>
        /// 将光束精确旋转朝向 <paramref name="globalTarget"/>（不限角度）。
        /// </summary>
        public void LookAtGlobal(Vector2 globalTarget)
        {
            Vector2 dir = (globalTarget - GlobalPosition).Normalized();
            if (dir != Vector2.Zero)
            {
                Rotation = dir.Angle();
            }
        }

        // ── 私有方法 ──────────────────────────────────────────────

        /// <summary>
        /// 对缓存的玩家节点做几何距离检测，命中 HitArea 后触发一次伤害和击退。
        /// 不使用射线查询，避免被激光起点附近的敌人自身 Area2D 阻挡。
        /// </summary>
        private void TryDamagePlayer()
        {
            if (_hasDamaged) return;
            if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;
            if (_cachedPlayer == null) return;

            // 取 HitArea 的 CollisionShape2D 世界坐标作为检测中心
            var hitArea = _cachedPlayer.GetNodeOrNull<Area2D>("HitArea")
                ?? _cachedPlayer.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            Vector2 targetCenter = hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? _cachedPlayer.GlobalPosition;

            // 求 targetCenter 到激光线段的距离
            Vector2 beamDir = new Vector2(Mathf.Cos(Rotation), Mathf.Sin(Rotation));
            Vector2 toTarget = targetCenter - GlobalPosition;

            // 沿激光方向的投影（必须在 [0, MaxLength] 范围内才算命中）
            float along = toTarget.Dot(beamDir);
            if (along < 0f || along > MaxLength) return;

            // 垂直激光方向的距离
            float perp = Mathf.Abs(toTarget.X * beamDir.Y - toTarget.Y * beamDir.X);

            // 用 HitArea 胶囊的世界半径作为判定宽度（含父节点缩放）
            float detectionRadius = 150f; // 默认回退值
            if (hitShape?.Shape is CapsuleShape2D cap)
            {
                // GlobalTransform.Scale 已包含所有父节点累乘缩放
                float worldScale = Mathf.Abs(hitShape.GlobalTransform.Scale.X);
                detectionRadius = cap.Radius * worldScale;
            }

            if (perp > detectionRadius) return;

            // 命中
            if (_cachedPlayer is not GameActor actor) return;

            // 在伤害判定前记录玩家当前无敌状态：
            // TakeDamage 调用后第一击会触发无敌帧，后续同帧命中不应再覆写速度。
            bool alreadyInvincible = actor is Kuros.Actors.Heroes.MainCharacter mc && mc.IsHitInvincible;

            _hasDamaged = true;

            if (Damage > 0)
                actor.TakeDamage(Damage, GlobalPosition);

            // 仅在命中前玩家尚未处于无敌帧时才施加击退，避免覆盖已有的击退速度。
            // 速度优先：KnockbackSpeed > 0 直接使用；否则由 KnockbackDistance / KnockbackDuration 推算（与 EnemyAttackTemplate 一致）。
            if (!alreadyInvincible)
            {
                float knockSpeed = KnockbackSpeed > 0f
                    ? KnockbackSpeed
                    : (KnockbackDistance > 0f ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f) : 0f);
                if (knockSpeed > 0f)
                    actor.Velocity = beamDir * knockSpeed;
            }
        }

        private void UpdateBeam()
        {
            if (_ray == null || _beamLine == null || _glowLine == null) return;

            _ray.ForceRaycastUpdate();

            Vector2 endPt = _ray.IsColliding()
                ? ToLocal(_ray.GetCollisionPoint())
                : new Vector2(MaxLength, 0f);

            var pts = new[] { Vector2.Zero, endPt };
            _beamLine.Points = pts;
            _glowLine.Points = pts;
        }
    }
}
