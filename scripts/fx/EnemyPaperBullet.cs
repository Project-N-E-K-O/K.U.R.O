using Godot;
using Kuros.Core;
using Kuros.Core.Events;

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
    public partial class EnemyPaperBullet : Node2D, IFacingDirectional, IAttackerProvider
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
        [Export(PropertyHint.Range, "0,45,0.5")] public float MaxVerticalTiltDegrees = 15f;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.5,30,0.1")] public float Duration = 4.0f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;

        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 10;

        [ExportCategory("Knockback")]
        [Export(PropertyHint.Range, "0,2000,1")]  public float KnockbackDistance = 80f; // 原速度400×0.2s(受击时长)折算
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        [ExportCategory("Afterimage")]
        [Export] public bool EnableAfterimage = false;
        [Export] public NodePath AfterimageControllerPath = new("AfterimageController");

        [ExportCategory("Pseudo3D")]
        /// <summary>Shader 作用的目标节点。留空则自动取第一个 Sprite2D 子节点。</summary>
        [Export] public NodePath Pseudo3DTargetPath { get; set; } = new();
        /// <summary>X 轴倾斜（度），模拟俯视角桌面平放。0=正面，50=半躺。</summary>
        [Export(PropertyHint.Range, "0,360,0.1")] public float Pseudo3DXAngle = 50f;
        /// <summary>Y 轴旋转（度），左右偏转。</summary>
        [Export(PropertyHint.Range, "0,360,0.1")] public float Pseudo3DYAngle = 0f;
        /// <summary>Z 轴自转速度（度/秒），驱动 zDegrees 持续累加。</summary>
        [Export(PropertyHint.Range, "0,3600,1")] public float Pseudo3DZSpeed = 720f;

        // ── 子节点引用 ────────────────────────────────────────────

        private Area2D? _attackArea;
        private Node2D? _visual;

        // ── 运行时状态 ────────────────────────────────────────────

        private Vector2 _currentVelocity;
        private float   _timer;
        private bool    _initialized;
        private bool    _hit;
        private Node2D? _player;
        private GameActor? _attacker;
        private ShaderMaterial? _pseudo3DMaterial;
        private Node2D? _pseudo3DTarget;
        private float _pseudo3DZAccum;
        private Node? _afterimage;

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _timer       = Duration;
            _initialized = false;
            _hit         = false;

            _attackArea = GetNodeOrNull<Area2D>("AttackArea");
            _visual     = GetNodeOrNull<Node2D>("Visual");
            if (_attackArea != null)
            {
                _attackArea.BodyEntered += OnAttackAreaBodyEntered;
                _attackArea.AreaEntered += OnAttackAreaAreaEntered;
            }

            SetupPseudo3D();

            if (EnableAfterimage && !AfterimageControllerPath.IsEmpty)
                _afterimage = GetNodeOrNull<Node>(AfterimageControllerPath);

            ResolveAttacker();
        }

        /// <summary>
        /// 显式攻击来源（由生成方传入，如 EnemyAttackTemplate 生成时设置）。
        /// 优先于父节点解析：父节点下第一个敌人不一定是发射者，解析错误会导致 AllowSelfDamage 保护失效（打自己）。
        /// </summary>
        public GameActor? Attacker
        {
            get => _attacker;
            set => _attacker = value;
        }

        private void ResolveAttacker()
        {
            if (_attacker != null) return;
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

        public override void _ExitTree()
        {
            _afterimage?.Call("stop");
            if (_attackArea != null)
            {
                _attackArea.BodyEntered -= OnAttackAreaBodyEntered;
                _attackArea.AreaEntered -= OnAttackAreaAreaEntered;
            }
            base._ExitTree();
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
                _afterimage?.Call("start");

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

            // ─ 旋转朝向速度方向（只转视觉节点：判定点固定在地面，不随旋转偏移）──
            if (_currentVelocity.LengthSquared() > 0.1f)
            {
                if (_visual != null)
                    _visual.Rotation = _currentVelocity.Angle();
                else
                    Rotation = _currentVelocity.Angle(); // 兼容无 Visual 节点的旧结构
            }

            // ─ 伪3D Z轴自转 ────────────────────────────────────────
            if (Pseudo3DZSpeed != 0f && _pseudo3DMaterial != null)
            {
                _pseudo3DZAccum += Pseudo3DZSpeed * dt;
                _pseudo3DMaterial.SetShaderParameter("zDegrees",
                    _pseudo3DZAccum % 360f);
            }

            // ─ 计时销毁 ─────────────────────────────────────────────
            _timer -= dt;
            if (_timer <= 0f)
                QueueFree();
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
            _afterimage?.Call("stop");
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
            _afterimage?.Call("stop");
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

        private void SetupPseudo3D()
        {
            Node2D? target = null;
            if (!Pseudo3DTargetPath.IsEmpty)
                target = GetNodeOrNull<Node2D>(Pseudo3DTargetPath);
            else
                target = FindChild("*", recursive: false) as Sprite2D;

            if (target == null) return;
            _pseudo3DTarget = target;

            var shader = GD.Load<Shader>("res://shaders/materials/pseudo_3d_rotate.gdshader");
            if (shader == null) return;

            _pseudo3DMaterial = new ShaderMaterial();
            _pseudo3DMaterial.Shader = shader;
            target.Material = _pseudo3DMaterial;

            _pseudo3DMaterial.SetShaderParameter("isInRadians", false);
            _pseudo3DMaterial.SetShaderParameter("xDegrees", Pseudo3DXAngle);
            _pseudo3DMaterial.SetShaderParameter("yDegrees", Pseudo3DYAngle);
            _pseudo3DMaterial.SetShaderParameter("zDegrees", 0f);
        }

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
