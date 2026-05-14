using Godot;
using Kuros.Core;

namespace Kuros.Fx
{
    /// <summary>
    /// 终极激光束视觉 + 伤害节点。
    ///
    /// 行为：
    ///   - 激光射出点固定在节点 GlobalPosition。
    ///   - 激光长度在生成时立即设为到玩家的距离，此后每帧实时跟踪玩家位置更新长度，上限 <see cref="MaxLength"/>。
    ///   - <see cref="TrackPlayer"/> 为 true 时方向实时跟踪玩家；为 false 时锁定首帧方向。
    ///   - 玩家与激光线段（原点→远端）距离小于判定半径时触发伤害与击退，
    ///     每 <see cref="DamageInterval"/> 秒最多触发一次。
    ///   - 达到 <see cref="Duration"/> 后自动销毁，末尾 <see cref="FadeDuration"/> 内淡出。
    /// </summary>
    public partial class LaserBeamUltimate : Node2D
    {
        // ── 导出参数 ──────────────────────────────────────────────

        [ExportCategory("Beam")]
        /// <summary>激光最大长度（像素）。</summary>
        [Export] public float MaxLength = 2000f;

        /// <summary>内层光束颜色。</summary>
        [Export] public Color BeamColor = new Color(1f, 0f, 0f, 1f);

        /// <summary>外层光晕颜色。</summary>
        [Export] public Color GlowColor = new Color(1f, 0.23f, 0f, 0.52f);

        /// <summary>内层光束宽度（像素）。</summary>
        [Export] public float BeamWidth = 100f;

        /// <summary>外层光晕宽度（像素）。</summary>
        [Export] public float GlowWidth = 200f;

        [ExportCategory("Timing")]
        /// <summary>激光存在总时长（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,60,0.1")] public float Duration = 3.0f;

        /// <summary>淡出时长（秒），从 Duration 末尾开始淡出。</summary>
        [Export(PropertyHint.Range, "0,3,0.05")] public float FadeDuration = 0.3f;

        [ExportCategory("Tip Movement")]
        /// <summary>激光末端延伸 / 收缩的速度（像素/秒）。</summary>
        [Export(PropertyHint.Range, "10,3000,10")] public float TipSpeed = 400f;

        /// <summary>
        /// 激光末端沿垂直于光束方向能追赶玩家的最大线速度（像素/秒）。<br/>
        /// 0 = 立即对准；等效角速度 = TipLateralSpeed / 到玩家距离，距离越近追踪越激进。<br/>
        /// 典型值：200~400。玩家速度低于此值时无法横向甩开激光。
        /// </summary>
        [Export(PropertyHint.Range, "0,2000,10")] public float TipLateralSpeed = 300f;

        /// <summary>生成时激光初始长度偏移（像素）：初始长度 = 到玩家距离 - InitialOffset。</summary>
        [Export(PropertyHint.Range, "0,2000,10")] public float InitialOffset = 400f;

        /// <summary>
        /// true：激光方向追踪玩家（受 AngularSpeed 限制）；<br/>
        /// false：方向在首帧锁定，不再旋转。
        /// </summary>
        [Export] public bool TrackPlayer = true;

        [ExportCategory("Damage")]
        /// <summary>每次触发的伤害量（0 = 不造成伤害）。</summary>
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 30;

        /// <summary>伤害触发间隔（秒）。0 表示每帧检测一次。</summary>
        [Export(PropertyHint.Range, "0,5,0.05")] public float DamageInterval = 0.5f;

        [ExportCategory("Knockback")]
        /// <summary>击退距离（像素），与 KnockbackSpeed 二选一；两者同时设置时 Speed 优先。</summary>
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 0f;

        /// <summary>击退持续时间（秒），仅在用 KnockbackDistance 推算速度时生效。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        /// <summary>命中时施加的击退速度（像素/秒）。0 = 使用 KnockbackDistance 推算。</summary>
        [Export(PropertyHint.Range, "0,3000,1")] public float KnockbackSpeed = 800f;

        // ── 子节点引用 ────────────────────────────────────────────

        private Line2D? _beamLine;
        private Line2D? _glowLine;

        // ── 运行时状态 ────────────────────────────────────────────

        private float _timer;
        /// <summary>当前激光远端距原点的距离（像素）。</summary>
        private float _tipDistance;
        /// <summary>激光方向（单位向量，世界空间）。</summary>
        private Vector2 _tipDirection;
        private bool _directionInitialized;

        private Node2D? _player;
        private float _damageCooldown;

        private Color _initBeamColor;
        private Color _initGlowColor;

        // ── 内部辅助 ──────────────────────────────────────────────

        /// <summary>
        /// 取玩家 HitArea CollisionShape2D 的世界坐标（与 LaserBeam 保持一致）。
        /// 若找不到则回退到 GlobalPosition。
        /// </summary>
        private Vector2 GetPlayerAimCenter(Node2D player)
        {
            var hitArea  = player.GetNodeOrNull<Area2D>("HitArea")
                ?? player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? player.GlobalPosition;
        }

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _beamLine = GetNodeOrNull<Line2D>("BeamLine");
            _glowLine = GetNodeOrNull<Line2D>("GlowLine");

            if (_beamLine == null || _glowLine == null)
            {
                GD.PushWarning("[LaserBeamUltimate] 缺少 BeamLine 或 GlowLine 子节点，请检查场景结构。");
                QueueFree();
                return;
            }

            _beamLine.Width        = BeamWidth;
            _beamLine.DefaultColor = BeamColor;
            _glowLine.Width        = GlowWidth;
            _glowLine.DefaultColor = GlowColor;

            _initBeamColor = BeamColor;
            _initGlowColor = GlowColor;

            _timer               = Duration;
            _damageCooldown      = 0f;
            _tipDirection        = Vector2.Right;
            _directionInitialized = false;

            // 初始长度设为 0，首帧 _Process() 会用正确的 GlobalPosition 完成真正初始化。
            _tipDistance = 0f;
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // ─ 查找 / 缓存玩家 ─────────────────────────────────────
            if (_player == null || !GodotObject.IsInstanceValid(_player))
                _player = GetTree().GetFirstNodeInGroup("player") as Node2D;

            // ─ 更新激光方向（角速度限制旋转） ──────────────────────
            if (_player != null && TrackPlayer)
            {
                Vector2 toPlayer = GetPlayerAimCenter(_player) - GlobalPosition;
                float dist = toPlayer.Length();
                if (dist > 0.01f)
                {
                    if (!_directionInitialized || TipLateralSpeed <= 0f)
                    {
                        // 首帧或 TipLateralSpeed=0：直接对准（此时 GlobalPosition 已就位）
                        _tipDirection        = toPlayer.Normalized();
                        _directionInitialized = true;
                    }
                    else
                    {
                        // 等效角速度 = TipLateralSpeed / distance（距离越近追踪越快）
                        float maxRad = (TipLateralSpeed / dist) * dt;
                        _tipDirection        = _tipDirection.Rotated(
                            Mathf.Clamp(_tipDirection.AngleTo(toPlayer.Normalized()), -maxRad, maxRad));
                    }
                }
            }

            // ─ 首帧同步初始化长度（GlobalPosition 已就位）─────────────
            if (!_directionInitialized && _player != null)
            {
                float initDist = (GetPlayerAimCenter(_player) - GlobalPosition).Length();
                _tipDistance = Mathf.Clamp(initDist - InitialOffset, 0f, MaxLength);
            }

            // ─ 末端以 TipSpeed px/s 追赶玩家 HitArea 实时距离 ──────
            if (_player != null)
            {
                float targetDist = Mathf.Min((GetPlayerAimCenter(_player) - GlobalPosition).Length(), MaxLength);
                _tipDistance = Mathf.MoveToward(_tipDistance, targetDist, TipSpeed * dt);
            }

            // ─ 更新可视化 ──────────────────────────────────────────
            UpdateBeam();

            // ─ 伤害判定 ────────────────────────────────────────────
            _damageCooldown -= dt;
            if (_damageCooldown <= 0f)
                TryDamagePlayer();

            // ─ 计时 + 淡出 ─────────────────────────────────────────
            _timer -= dt;

            if (_timer < FadeDuration && FadeDuration > 0f && _timer > 0f)
            {
                float t = _timer / FadeDuration;
                _beamLine!.DefaultColor = new Color(
                    _initBeamColor.R, _initBeamColor.G, _initBeamColor.B, _initBeamColor.A * t);
                _glowLine!.DefaultColor = new Color(
                    _initGlowColor.R, _initGlowColor.G, _initGlowColor.B, _initGlowColor.A * t);
            }

            if (_timer <= 0f)
                QueueFree();
        }

        // ── 私有方法 ──────────────────────────────────────────────

        private void UpdateBeam()
        {
            if (_beamLine == null || _glowLine == null) return;

            // 激光在节点局部坐标中：原点 → 远端（_tipDirection 是世界方向，
            // 但节点 Rotation=0 时局部 +X 与世界 +X 同向，需转换到局部空间）。
            // 由于激光节点一般不旋转，直接将世界方向乘以距离作为局部偏移。
            Vector2 localTip = ToLocal(GlobalPosition + _tipDirection * _tipDistance);
            var pts = new[] { Vector2.Zero, localTip };
            _beamLine.Points = pts;
            _glowLine.Points = pts;
        }

        private void TryDamagePlayer()
        {
            if (_tipDistance < 1f) return;
            if (Damage <= 0 && KnockbackSpeed <= 0f && KnockbackDistance <= 0f) return;
            if (_player == null || !GodotObject.IsInstanceValid(_player)) return;

            // 取 HitArea 的 CollisionShape2D 世界坐标
            var hitArea = _player.GetNodeOrNull<Area2D>("HitArea")
                ?? _player.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            Vector2 targetCenter = hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? _player.GlobalPosition;

            Vector2 toTarget = targetCenter - GlobalPosition;

            // 投影到激光方向，必须在 [0, _tipDistance] 段内才算命中
            float along = toTarget.Dot(_tipDirection);
            if (along < 0f || along > _tipDistance) return;

            // 垂直激光方向的距离
            float perp = Mathf.Abs(toTarget.X * _tipDirection.Y - toTarget.Y * _tipDirection.X);

            // 判定半径：优先使用 HitArea 胶囊半径
            float detectionRadius = 80f;
            if (hitShape?.Shape is CapsuleShape2D cap)
            {
                float worldScale = Mathf.Abs(hitShape.GlobalTransform.Scale.X);
                detectionRadius = cap.Radius * worldScale;
            }

            if (perp > detectionRadius) return;

            // ─ 命中 ───────────────────────────────────────────────
            if (_player is not GameActor actor) return;

            bool alreadyInvincible = actor is Kuros.Actors.Heroes.MainCharacter mc && mc.IsHitInvincible;

            if (Damage > 0)
                actor.TakeDamage(Damage, GlobalPosition);

            if (!alreadyInvincible)
            {
                float knockSpeed = KnockbackSpeed > 0f
                    ? KnockbackSpeed
                    : (KnockbackDistance > 0f
                        ? KnockbackDistance / Mathf.Max(KnockbackDuration, 0.01f)
                        : 0f);
                if (knockSpeed > 0f)
                    actor.Velocity = _tipDirection * knockSpeed;
            }

            // 重置伤害冷却（哪怕 DamageInterval=0 也防止同帧多次触发）
            _damageCooldown = Mathf.Max(DamageInterval, (float)GetProcessDeltaTime());
        }
    }
}
