using Godot;
using Kuros.Core;
using Kuros.Core.Events;
using System.Collections.Generic;

namespace Kuros.Fx
{
    /// <summary>
    /// 追踪飞弹节点。
    ///
    /// 行为：
    ///   - drag_factor 转向模型（帧率无关），追踪玩家 HitArea。
    ///   - 飞弹旋转朝当前速度方向。
    ///   - 伤害由子节点 AttackArea（Area2D）与玩家物理体接触触发。
    ///   - BeamLine / GlowLine 实时渲染飞行拖尾（存储最近 N 个世界坐标）。
    ///   - 超过 Duration 后自动销毁。
    /// </summary>
    public partial class LaserBeamUltimate : Node2D, IAttackerProvider
    {
        public GameActor? Attacker { get; set; }

        // ── 导出参数 ──────────────────────────────────────────────

        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "50,12000,10")]  public float Speed             = 600f;
        [Export(PropertyHint.Range, "0.01,1,0.01")] public float DragFactor        = 0.08f; 
        /// <summary>初始速度偏转角（度）。0 = 直接朝玩家；±90 = 侧向出发形成大弧。</summary>
        [Export(PropertyHint.Range, "-180,180,1")]  public float InitialAngleOffset = 0f;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.5,30,0.1")] public float Duration = 6.0f;
        /// <summary>淡出时长（秒）：到期/命中后视觉原地 alpha 衰减此秒数再销毁。0 = 立即销毁（旧行为）。</summary>
        [Export(PropertyHint.Range, "0,2,0.05")] public float FadeOutDuration = 0.15f;

        [ExportCategory("Trail")]
        /// <summary>拖尾保留的历史点数量；点越多拖尾越长。</summary>
        [Export(PropertyHint.Range, "0,60,1")] public int   TrailPoints = 20;
        /// <summary>拖尾尾部渐隐：头部（飞弹处）原色，向尾部平滑淡出到透明（同 EnemyBullet）。</summary>
        [Export] public bool TrailFadeOut = true;
        /// <summary>拖尾淡出曲线中段位置（0~1）：越小尾部淡出越快，越大尾部保留越久。</summary>
        [Export(PropertyHint.Range, "0.1,1,0.05")] public float TrailFadeMidpoint = 0.7f;
        /// <summary>拖尾淡出中段透明度（原色的比例，0~1）。</summary>
        [Export(PropertyHint.Range, "0,1,0.05")] public float TrailFadeMidAlpha = 0.3f;

        [ExportCategory("Visual Spin")]
        /// <summary>需要自旋的视觉节点路径（相对根，如 Visual/Sprite2D；空 = 不启用）。
        /// 旋转叠加在节点自身的当前旋转上（每帧累加角速度），用于光轮/粒子类装饰自转。</summary>
        [Export] public NodePath SpinSpritePath { get; set; } = new();
        /// <summary>自旋角速度（度/秒）。正 = 顺时针。</summary>
        [Export(PropertyHint.Range, "-1440,1440,1")] public float SpinDegreesPerSecond { get; set; } = 90f;
        [Export] public Color BeamColor  = new Color(1f, 0.85f, 0.2f, 1f);
        [Export] public Color GlowColor  = new Color(1f, 0.23f, 0f, 0.52f);
        [Export(PropertyHint.Range, "1,100,1")]  public float BeamWidth  = 8f;
        [Export(PropertyHint.Range, "1,200,1")] public float GlowWidth  = 24f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;

        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 30;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")]  public float KnockbackDistance = 120f; // 原速度600×0.2s折算
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        // ── 子节点引用 ────────────────────────────────────────────

        private Line2D? _beamLine;
        private Line2D? _glowLine;
        private Area2D? _attackArea;
        private Node2D? _visual;
        private Node2D? _spinNode;   // SpinSpritePath 指定的自旋视觉节点（空 = 不启用）

        // ── 运行时状态 ────────────────────────────────────────────

        private Vector2 _currentVelocity;
        private float   _timer;
        private bool    _initialized;
        private bool    _hit;
        private Node2D? _player;
        private bool    _fading;        // 淡出中：冻结移动，仅衰减视觉 alpha
        private float   _fadeElapsed;   // 淡出已进行时长

        /// <summary>拖尾历史世界坐标（队列头 = 最旧）。</summary>
        private readonly Queue<Vector2> _trail = new();

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _timer       = Duration;
            _initialized = false;
            _hit         = false;
            _fading      = false;
            _fadeElapsed = 0f;

            _beamLine   = GetNodeOrNull<Line2D>("Visual/BeamLine");
            _glowLine   = GetNodeOrNull<Line2D>("Visual/GlowLine");
            _attackArea = GetNodeOrNull<Area2D>("AttackArea");
            _visual     = GetNodeOrNull<Node2D>("Visual");
            _spinNode   = SpinSpritePath.IsEmpty ? null : GetNodeOrNull<Node2D>(SpinSpritePath);

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

            // 伤害通过 _Process 每帧轮询 IsHitByArea 检测，无需 BodyEntered 信号
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // ─ 淡出阶段：冻结移动，视觉 alpha 原地衰减，结束后销毁 ──────
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

            // ─ 查找 / 缓存玩家 ─────────────────────────────────────
            if (_player == null || !GodotObject.IsInstanceValid(_player))
                _player = GetTree().GetFirstNodeInGroup("player") as Node2D;

            // ─ 首帧初始化速度（GlobalPosition 已就位） ─────────────
            if (!_initialized)
            {
                _initialized = true;
                Vector2 initDir = _player != null
                    ? (GetPlayerAimCenter(_player) - GlobalPosition).Normalized()
                    : Vector2.Right;
                if (InitialAngleOffset != 0f)
                    initDir = initDir.Rotated(Mathf.DegToRad(InitialAngleOffset));
                _currentVelocity = initDir * Speed;
            }

            // ─ 追踪转向（drag 模型，帧率无关）────────────────────
            if (_player != null)
            {
                Vector2 toPlayer = GetPlayerAimCenter(_player) - GlobalPosition;
                if (toPlayer.LengthSquared() > 0.01f)
                {
                    Vector2 desiredVel = toPlayer.Normalized() * Speed;
                    float   lerpT      = 1f - Mathf.Pow(1f - Mathf.Clamp(DragFactor, 0f, 1f), dt * 60f);
                    _currentVelocity  += (desiredVel - _currentVelocity) * lerpT;
                }
            }

            // ─ 移动 ────────────────────────────────────────────────
            GlobalPosition += _currentVelocity * dt;

            // ─ 旋转朝向速度方向（只转视觉层：判定点固定在地面，不随旋转偏移）──
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

            // ─ 视觉自旋（SpinSpritePath 指定节点按角速度自转，叠加在节点自身旋转上）──
            if (_spinNode != null && IsInstanceValid(_spinNode))
                _spinNode.Rotation += Mathf.DegToRad(SpinDegreesPerSecond) * dt;

            // ─ 命中检测（每帧轮询，与项目其他攻击一致）──────────────
            if (!_hit)
                TryHitPlayer();

            // ─ 计时销毁：到期进入淡出（无淡出时立即销毁）────────────
            _timer -= dt;
            if (_timer <= 0f)
            {
                StartFade();
                return;
            }
        }

        // ── 私有方法 ──────────────────────────────────────────────

        /// <summary>
        /// 将历史世界坐标转换到本节点局部空间后赋给 Line2D。<br/>
        /// 结果：从最老位置（拖尾尾部）到最新位置（飞弹头部，接近原点）的连线。
        /// </summary>
        /// <summary>
        /// 拖尾渐隐：Line2D 设置 Gradient 后每点颜色按点索引比例采样——
        /// 队头（最旧=尾部）透明 → 队尾（最新=飞弹处）原色，实现尾部淡出（同 EnemyBullet）。
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
            g.SetOffsets(new[] { 0f, mid, 1f });
            g.SetColors(new[]
            {
                new(color.R, color.G, color.B, 0f),
                new(color.R, color.G, color.B, color.A * Mathf.Clamp(TrailFadeMidAlpha, 0f, 1f)),
                color,
            });
            return g;
        }

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

        /// <summary>
        /// 每帧轮询（参考 EnemyBullet）：遍历 AttackArea 的重叠对象，对每个目标直接 DealDamage——
        /// TargetableFactions 完整过滤（玩家/敌人/WorldItem）+ 自伤保护，命中即销毁。
        /// </summary>
        private void TryHitPlayer()
        {
            if (_attackArea == null || !_attackArea.IsInsideTree()) return;

            foreach (var area in _attackArea.GetOverlappingAreas())
            {
                var target = area.Owner ?? area;
                if (target == null) continue;
                if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, Attacker)) continue;

                bool dealt = DamageDispatcher.DealDamage(target, Damage, GlobalPosition, Attacker,
                    DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _attackArea, _currentVelocity);
                if (!dealt) continue;

                if (area.Owner is GameActor hitActor)
                    ApplyKnockbackTo(hitActor);
                _hit = true;
                StartFade();
                return;
            }

            foreach (var body in _attackArea.GetOverlappingBodies())
            {
                if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, Attacker)) continue;

                bool dealt = DamageDispatcher.DealDamage(body, Damage, GlobalPosition, Attacker,
                    DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, _attackArea, _currentVelocity);
                if (!dealt) continue;

                if (body is GameActor hitActor)
                    ApplyKnockbackTo(hitActor);
                _hit = true;
                StartFade();
                return;
            }
        }

        /// <summary>击退：位移制（KnockbackDistance/Duration）。
        /// 玩家侧放行由 GameActor.ApplyKnockbackDisplacement 内部统一处理，这里**不能**再看
        /// IsHitInvincible——这一下命中本身就会开无敌帧，事后判会把刚命中的这一击也判成已无敌
        /// （EFFECT_STANDARD 第四条）。</summary>
        private void ApplyKnockbackTo(GameActor actor)
        {
            if (KnockbackDistance > 0f && _currentVelocity.LengthSquared() > 0.01f)
                actor.ApplyKnockbackDisplacement(_currentVelocity.Normalized(), KnockbackDistance, KnockbackDuration);
        }

        /// <summary>
        /// 进入淡出：冻结移动，视觉 alpha 原地衰减 FadeOutDuration 秒后销毁（到期/命中共用）。
        /// FadeOutDuration = 0 时立即销毁（旧行为）。
        /// </summary>
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
            _currentVelocity = Vector2.Zero; // 淡出期间原地衰减，不再追踪/移动
        }

        /// <summary>取玩家 HitArea CollisionShape2D 的世界坐标作为转向瞄准中心。</summary>
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
