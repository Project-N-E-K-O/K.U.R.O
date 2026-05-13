using Godot;

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

        /// <summary>
        /// 若为 true，首帧自动查找玩家并计算朝向：
        /// 水平方向由玩家相对位置决定（左/右），
        /// 垂直方向仅在 ±<see cref="MaxVerticalTiltDegrees"/> 范围内微调。
        /// </summary>
        [ExportCategory("Targeting")]
        [Export] public bool AutoAimAtPlayer = true;

        /// <summary>垂直倾斜最大角度（度）。激光基础方向水平，此值限制上下偏转幅度。</summary>
        [Export(PropertyHint.Range, "0,45,0.5")] public float MaxVerticalTiltDegrees = 5f;

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

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _ray      = GetNodeOrNull<RayCast2D>("RayCast2D");
            _glowLine = GetNodeOrNull<Line2D>("GlowLine");
            _beamLine = GetNodeOrNull<Line2D>("BeamLine");

            if (_ray == null || _glowLine == null || _beamLine == null)
            {
                GD.PushWarning("[LaserBeam] 缺少子节点（RayCast2D / GlowLine / BeamLine），请检查场景结构。");
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

            UpdateBeam();
        }

        public override void _Process(double delta)
        {
            // 首帧：GlobalPosition 已由 SpawnEffectAtEnemy 设置完毕，此时再对准才准确
            if (_pendingAutoAim)
            {
                _pendingAutoAim = false;
                var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (player != null)
                    AimHorizontalWithVerticalTilt(player.GlobalPosition);
                UpdateBeam();
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
