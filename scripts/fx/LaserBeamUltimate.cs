using Godot;
using Kuros.Core;
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
    public partial class LaserBeamUltimate : Node2D
    {
        // ── 导出参数 ──────────────────────────────────────────────

        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "50,12000,10")]  public float Speed             = 600f;
        [Export(PropertyHint.Range, "0.01,1,0.01")] public float DragFactor        = 0.08f; 
        /// <summary>初始速度偏转角（度）。0 = 直接朝玩家；±90 = 侧向出发形成大弧。</summary>
        [Export(PropertyHint.Range, "-180,180,1")]  public float InitialAngleOffset = 0f;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.5,30,0.1")] public float Duration = 6.0f;

        [ExportCategory("Trail")]
        /// <summary>拖尾保留的历史点数量；点越多拖尾越长。</summary>
        [Export(PropertyHint.Range, "2,60,1")] public int   TrailPoints = 20;
        [Export] public Color BeamColor  = new Color(1f, 0.85f, 0.2f, 1f);
        [Export] public Color GlowColor  = new Color(1f, 0.23f, 0f, 0.52f);
        [Export(PropertyHint.Range, "1,50,1")]  public float BeamWidth  = 8f;
        [Export(PropertyHint.Range, "1,100,1")] public float GlowWidth  = 24f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 30;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,3000,1")]  public float KnockbackSpeed    = 600f;
        [Export(PropertyHint.Range, "0,2000,1")]  public float KnockbackDistance = 0f;
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        // ── 子节点引用 ────────────────────────────────────────────

        private Line2D? _beamLine;
        private Line2D? _glowLine;
        private Area2D? _attackArea;

        // ── 运行时状态 ────────────────────────────────────────────

        private Vector2 _currentVelocity;
        private float   _timer;
        private bool    _initialized;
        private bool    _hit;
        private Node2D? _player;

        /// <summary>拖尾历史世界坐标（队列头 = 最旧）。</summary>
        private readonly Queue<Vector2> _trail = new();

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _timer       = Duration;
            _initialized = false;
            _hit         = false;

            _beamLine   = GetNodeOrNull<Line2D>("BeamLine");
            _glowLine   = GetNodeOrNull<Line2D>("GlowLine");
            _attackArea = GetNodeOrNull<Area2D>("AttackArea");

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
            // 伤害通过 _Process 每帧轮询 IsHitByArea 检测，无需 BodyEntered 信号
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

            // ─ 旋转朝向速度方向 ────────────────────────────────────
            if (_currentVelocity.LengthSquared() > 0.1f)
                Rotation = _currentVelocity.Angle();

            // ─ 更新拖尾 ────────────────────────────────────────────
            _trail.Enqueue(GlobalPosition);
            while (_trail.Count > TrailPoints)
                _trail.Dequeue();
            UpdateTrail();

            // ─ 命中检测（每帧轮询，与项目其他攻击一致）──────────────
            if (!_hit)
                TryHitPlayer();

            // ─ 计时销毁 ─────────────────────────────────────────────
            _timer -= dt;
            if (_timer <= 0f)
                QueueFree();
        }

        // ── 私有方法 ──────────────────────────────────────────────

        /// <summary>
        /// 将历史世界坐标转换到本节点局部空间后赋给 Line2D。<br/>
        /// 结果：从最老位置（拖尾尾部）到最新位置（飞弹头部，接近原点）的连线。
        /// </summary>
        private void UpdateTrail()
        {
            if (_beamLine == null && _glowLine == null) return;

            var pts = new Vector2[_trail.Count];
            int i = 0;
            foreach (var p in _trail)
                pts[i++] = ToLocal(p);   // 全局坐标 → 本节点局部坐标

            if (_beamLine != null) _beamLine.Points = pts;
            if (_glowLine != null) _glowLine.Points = pts;
        }

        /// <summary>
        /// 每帧轮询：用 IsHitByArea 检测 AttackArea 与玩家 HitArea 的重叠，
        /// 与项目其他攻击（PerformAttack / IsHitByArea）保持一致。
        /// </summary>
        private void TryHitPlayer()
        {
            if (_attackArea == null || !_attackArea.IsInsideTree()) return;
            if (_player is not GameActor actor) return;
            if (!actor.IsHitByArea(_attackArea)) return;

            _hit = true;

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
                if (knockSpeed > 0f && _currentVelocity.LengthSquared() > 0.01f)
                    actor.Velocity = _currentVelocity.Normalized() * knockSpeed;
            }

            QueueFree();
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
