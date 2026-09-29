using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using System.Collections.Generic;

namespace Kuros.Fx
{
    /// <summary>
    /// 水平追踪飞弹节点。
    ///
    /// 行为：
    ///   - 水平方向锁定玩家左右，垂直方向仅在 ±MaxVerticalTiltDegrees 范围内微调（同 LaserBeam 朝向规则）。
    ///   - drag_factor 转向模型（帧率无关），在水平锁定的基础上平滑追踪。
    ///   - 飞弹旋转朝当前速度方向。
    ///   - 伤害由子节点 AttackArea（Area2D）与玩家物理体接触触发。
    ///   - BeamLine / GlowLine 实时渲染飞行拖尾（存储最近 N 个世界坐标）。
    ///   - 超过 Duration 后自动销毁。
    /// </summary>
    public partial class EnemyBullet : Node2D, IFacingDirectional
    {
        // ── 导出参数 ──────────────────────────────────────────────

        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "50,12000,10")]  public float Speed             = 12000f;
        [Export(PropertyHint.Range, "0.00,1,0.01")] public float DragFactor        = 0.01f;
        /// <summary>初始速度偏转角（度）。0 = 直接朝玩家；±90 = 侧向出发形成大弧。</summary>
        [Export(PropertyHint.Range, "-180,180,1")]  public float InitialAngleOffset = 0f;

        [ExportCategory("Targeting")]
        /// <summary>
        /// 激光水平朝向：true = 向右，false = 向左。
        /// 由 EnemyAttackTemplate.SpawnEffectAtEnemy 生成时自动由敌人朝向设置。
        /// </summary>
        [Export] public bool FacingRight { get; set; } = true;
        /// <summary>垂直倾斜最大角度（度）。水平基础方向固定，此值限制上下偏转幅度。</summary>
        [Export(PropertyHint.Range, "0,180,0.5")] public float MaxVerticalTiltDegrees = 15f;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.5,30,0.1")] public float Duration = 4.0f;

        [ExportCategory("Trail")]
        /// <summary>拖尾保留的历史点数量；点越多拖尾越长。</summary>
        [Export(PropertyHint.Range, "2,60,1")] public int   TrailPoints = 20;
        [Export] public Color BeamColor = new Color(1f, 0.85f, 0.2f, 1f);
        [Export] public Color GlowColor = new Color(1f, 0.23f, 0f, 0.52f);
        [Export(PropertyHint.Range, "1,50,1")]  public float BeamWidth = 8f;
        [Export(PropertyHint.Range, "1,100,1")] public float GlowWidth = 24f;
        /// <summary>拖尾尾部渐隐：头部（飞弹处）原色，向尾部平滑淡出到透明。</summary>
        [Export] public bool TrailFadeOut = true;
        /// <summary>拖尾淡出曲线中段位置（0~1）：越小尾部淡出越快，越大尾部保留越久。</summary>
        [Export(PropertyHint.Range, "0.1,1,0.05")] public float TrailFadeMidpoint = 0.7f;
        /// <summary>拖尾淡出中段透明度（原色的比例，0~1）。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float TrailFadeMidAlpha = 0.3f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;

        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 10;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")]  public float KnockbackDistance = 80f; // 原速度400×0.2s(受击时长)折算
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        // ── 子节点引用 ────────────────────────────────────────────

        private Node2D? _visual;
        private Line2D? _beamLine;
        private Line2D? _glowLine;
        private Area2D? _attackArea;

        // ── 运行时状态 ────────────────────────────────────────────

        private Vector2 _currentVelocity;
        private float   _timer;
        private bool    _initialized;
        private bool    _hit;
        private Node2D? _player;
        private GameActor? _attacker;

        /// <summary>拖尾历史世界坐标（队列头 = 最旧）。</summary>
        private readonly Queue<Vector2> _trail = new();

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _timer       = Duration;
            _initialized = false;
            _hit         = false;

            _visual     = GetNodeOrNull<Node2D>("Visual");
            _beamLine   = GetNodeOrNull<Line2D>("Visual/BeamLine");
            _glowLine   = GetNodeOrNull<Line2D>("Visual/GlowLine");
            _attackArea = GetNodeOrNull<Area2D>("AttackArea");

            if (_attackArea != null)
            {
                _attackArea.BodyEntered += OnAttackAreaBodyEntered;
                _attackArea.AreaEntered += OnAttackAreaAreaEntered;
            }

            if (_beamLine != null)
            {
                _beamLine.Width        = BeamWidth;
                _beamLine.DefaultColor = BeamColor;
                _beamLine.Points       = System.Array.Empty<Vector2>();
            }
            if (_glowLine != null)
            {
                _glowLine.Width        = GlowWidth;
                _glowLine.DefaultColor = GlowColor;
                _glowLine.Points       = System.Array.Empty<Vector2>();
            }

            SetupTrailGradients();

            ResolveAttacker();
        }

        /// <summary>
        /// 拖尾渐隐：Line2D 设置 Gradient 后每点颜色按点索引比例采样——
        /// 队头（最旧=尾部）透明 → 队尾（最新=飞弹处）原色，实现尾部淡出。
        /// </summary>
        private void SetupTrailGradients()
        {
            if (!TrailFadeOut) return;
            if (_beamLine != null)
                _beamLine.Gradient = BuildFadeGradient(BeamColor);
            if (_glowLine != null)
                _glowLine.Gradient = BuildFadeGradient(GlowColor);
        }

        private Gradient BuildFadeGradient(Color color)
        {
            var g = new Gradient();
            float mid = Mathf.Clamp(TrailFadeMidpoint, 0.01f, 0.99f);
            g.SetOffsets(new float[] { 0f, mid, 1f });
            g.SetColors(new Color[]
            {
                new Color(color.R, color.G, color.B, 0f),
                new Color(color.R, color.G, color.B, color.A * Mathf.Clamp(TrailFadeMidAlpha, 0f, 1f)),
                color,
            });
            return g;
        }

        private void ResolveAttacker()
        {
            var parent = GetParent();
            if (parent == null) return;
            foreach (var child in parent.GetChildren())
            {
                if (child.IsInGroup("enemies") && child is GameActor ga)
                {
                    _attacker = ga;
                    break;
                }
            }
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // ─ 查找 / 缓存玩家 ─────────────────────────────────────
            if (_player == null || !GodotObject.IsInstanceValid(_player))
                _player = GetTree().GetFirstNodeInGroup("player") as Node2D;

            // ─ 首帧初始化速度（GlobalPosition 已就位） ─────────────
            if (!_initialized)
            {
                _initialized = true;

                // 用 LaserBeam 的水平锁定朝向规则计算初始方向
                float initAngle = ResolveHorizontalTiltAngle(_player);
                if (InitialAngleOffset != 0f)
                    initAngle += Mathf.DegToRad(InitialAngleOffset);

                _currentVelocity = new Vector2(
                    Mathf.Cos(initAngle),
                    Mathf.Sin(initAngle)) * Speed;
            }

            // ─ 追踪转向（水平锁定 + 垂直微调，drag 模型，帧率无关）──
            if (_player != null)
            {
                float desiredAngle = ResolveHorizontalTiltAngle(_player);
                Vector2 desiredVel = new Vector2(
                    Mathf.Cos(desiredAngle),
                    Mathf.Sin(desiredAngle)) * Speed;

                float lerpT      = 1f - Mathf.Pow(1f - Mathf.Clamp(DragFactor, 0f, 1f), dt * 60f);
                _currentVelocity += (desiredVel - _currentVelocity) * lerpT;
            }

            // ─ 移动 ────────────────────────────────────────────────
            GlobalPosition += _currentVelocity * dt;

            // ─ 旋转朝向速度方向（只转视觉节点：判定点固定在地面投影，不随旋转偏移）──
            if (_currentVelocity.LengthSquared() > 0.1f)
            {
                if (_visual != null)
                    _visual.Rotation = _currentVelocity.Angle();
                else
                    Rotation = _currentVelocity.Angle(); // 兼容无 Visual 节点的旧结构
            }

            // ─ 更新拖尾（记录视觉轨迹：视觉节点位置而非根/判定点位置）──
            _trail.Enqueue(_visual != null ? _visual.GlobalPosition : GlobalPosition);
            while (_trail.Count > TrailPoints)
                _trail.Dequeue();
            UpdateTrail();

            // ─ 计时销毁 ─────────────────────────────────────────────
            _timer -= dt;
            if (_timer <= 0f)
                QueueFree();
        }

        public override void _ExitTree()
        {
            if (_attackArea != null)
            {
                _attackArea.BodyEntered -= OnAttackAreaBodyEntered;
                _attackArea.AreaEntered -= OnAttackAreaAreaEntered;
            }
            base._ExitTree();
        }

        // ── 命中处理 ──────────────────────────────────────────────

        private void OnAttackAreaBodyEntered(Node body)
        {
            if (_hit) return;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;

            bool dealt = DamageDispatcher.DealDamage(body, Damage, GlobalPosition, _attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _attackArea, _currentVelocity);
            if (!dealt) return;

            if (body is GameActor hitActor)
                ApplyKnockback(hitActor);

            _hit = true;
            QueueFree();
        }

        private void OnAttackAreaAreaEntered(Area2D area)
        {
            if (_hit) return;
            var target = area.Owner ?? area;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, _attacker)) return;

            bool dealt = DamageDispatcher.DealDamage(target, Damage, GlobalPosition, _attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _attackArea, _currentVelocity);
            if (!dealt) return;

            if (area.Owner is GameActor hitActor)
                ApplyKnockback(hitActor);

            _hit = true;
            QueueFree();
        }

        /// <summary>击退：位移制。玩家侧放行由 GameActor.ApplyKnockbackDisplacement 内部统一处理
        /// （EFFECT_STANDARD 第四条），这里不再自己判断无敌帧。</summary>
        private void ApplyKnockback(GameActor actor)
        {
            if (KnockbackDistance > 0f && _currentVelocity.LengthSquared() > 0.01f)
                actor.ApplyKnockbackDisplacement(_currentVelocity.Normalized(), KnockbackDistance, KnockbackDuration);
        }

        // ── 私有方法 ──────────────────────────────────────────────

        /// <summary>
        /// LaserBeam 的朝向规则：
        /// 水平基础方向由 FacingRight 决定（与玩家水平位置无关），
        /// 垂直方向仅在 ±MaxVerticalTiltDegrees 范围内跟随玩家高度微调。
        /// 玩家在背后时保持水平，不翻转。
        /// </summary>
        private float ResolveHorizontalTiltAngle(Node2D? player)
        {
            float baseAngle = FacingRight ? 0f : Mathf.Pi;

            if (player == null) return baseAngle;

            Vector2 toTarget = GetPlayerAimCenter(player) - GlobalPosition;
            bool playerInFront = FacingRight ? toTarget.X >= 0f : toTarget.X <= 0f;

            if (!playerInFront || toTarget == Vector2.Zero)
                return baseAngle;

            float maxTiltRad = Mathf.DegToRad(MaxVerticalTiltDegrees);
            // 向左时倾斜符号反转：Rotation=π 时顺时针(+)会使 Y 分量变负(向上)，需取反才能向下倾
            float dySign   = FacingRight ? 1f : -1f;
            float tiltAngle = Mathf.Atan2(toTarget.Y * dySign, Mathf.Abs(toTarget.X));
            tiltAngle = Mathf.Clamp(tiltAngle, -maxTiltRad, maxTiltRad);

            return baseAngle + tiltAngle;
        }

        /// <summary>
        /// 将历史世界坐标转换到本节点局部空间后赋给 Line2D。
        /// </summary>
        private void UpdateTrail()
        {
            if (_beamLine == null && _glowLine == null) return;

            var pts = new Vector2[_trail.Count];
            int i = 0;
            foreach (var p in _trail)
            {
                // 拖尾属于视觉层：相对 Visual 局部空间（Visual 旋转时拖尾跟随视觉）
                pts[i++] = _visual != null ? _visual.ToLocal(p) : ToLocal(p);
            }

            if (_beamLine != null) _beamLine.Points = pts;
            if (_glowLine != null) _glowLine.Points = pts;
        }

        /// <summary>取玩家 HitArea CollisionShape2D 的世界坐标作为瞄准中心。</summary>
        private Vector2 GetPlayerAimCenter(Node2D player)
        {
            var hitArea  = player.GetNodeOrNull<Area2D>("HitArea")
                ?? player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? player.GlobalPosition;
        }
    }
}