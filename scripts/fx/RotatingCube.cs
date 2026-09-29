using Godot;
using Kuros.Core;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 伪 3D 旋转线框立方体弹幕。
    /// 三层 Sprite2D 叠加渲染：FaceFill（面+二进制雨）+ Wireframe（发光线框）+ BuildMask（构建扫描线）。
    /// 生命周期：spawn 构建动画 → 直线飞行 → 超时或命中后 despawn 消解动画。
    /// </summary>
    public partial class RotatingCube : Node2D, IFacingDirectional, IAttackerProvider
    {
        [ExportCategory("Movement")]
        [Export] public bool FacingRight { get; set; } = true;
        /// <summary>飞行速度（像素/秒）。外部（如 CubeDataBoom）可设为 0 接管移动。</summary>
        [Export(PropertyHint.Range, "50,6000,10")] public float Speed = 600f;
        /// <summary>朝玩家瞄准时的最大垂直偏移角度（度）。0 = 纯水平飞行。</summary>
        [Export(PropertyHint.Range, "0,360,0.5")] public float MaxVerticalTiltDegrees = 30f;
        /// <summary>
        /// 外部指定的固定飞行方向（如上下发射的子立方体）。非 null 时跳过瞄准，直接朝该方向直飞。
        /// 需在 AddChild 之前设置：_Ready 中的 spawn 动画回调结束时读取。
        /// </summary>
        public Vector2? FixedDirection { get; set; }

        /// <summary>当前飞行速度向量（未飞行时为 Zero）。子类可用其正交方向实现"垂直发射"。</summary>
        public Vector2 Velocity => _velocity;

        [ExportCategory("Timing")]
        /// <summary>飞行阶段时长。到期后进入 despawn。</summary>
        [Export(PropertyHint.Range, "0.01,30,0.01")] public float Duration = 8.0f;

        [ExportCategory("Lifecycle")]
        /// <summary>spawn 构建动画时长（build_progress 0→1 + scale pop）。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float BuildDuration = 0.3f;
        /// <summary>despawn 消解动画时长（build_progress 1→0）。</summary>
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float DespawnDuration = 0.5f;
        /// <summary>销毁时在当前位置生成的额外特效（如爆炸粒子）。</summary>
        [Export] public PackedScene? DestroyEffect { get; set; }
        /// <summary>spawn 动画结束后的目标缩放。外部（如 CubeDataBoom）可设置。</summary>
        public float BaseScale { get; set; } = 1f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Player | TargetableFactions.WorldItem;
        [Export] public bool AllowSelfDamage { get; set; } = false;
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 25;

        [ExportCategory("Collision Layers")]
        /// <summary>物理查询碰撞掩码（层 1 = HitArea/DestructibleObject，层 2 = 敌人 body，层 3 = 玩家 body）。阵营过滤由 TargetableFactions 负责。</summary>
        [Export(PropertyHint.Layers2DPhysics)] public uint TargetCollisionMask = 7u;

        [ExportCategory("Knockback")]
        /// <summary>击退位移距离（像素），0 不击退。位移在 KnockbackDuration 内匀减速滑完。</summary>
        [Export(PropertyHint.Range, "0,2000,1")] public float KnockbackDistance = 80f; // 原速度400×0.2s折算
        [Export(PropertyHint.Range, "0.01,2,0.01")] public float KnockbackDuration = 0.18f;

        // 三层渲染 Sprite
        private Area2D? _attackArea;
        private Sprite2D? _wireframeSprite;
        private Sprite2D? _buildSprite;
        private Sprite2D? _faceSprite;
        // 每个 Sprite 持有独立的 Material 副本，避免多个 cube 共享材质参数
        private ShaderMaterial? _wireframeMaterial;
        private ShaderMaterial? _buildMaterial;
        private ShaderMaterial? _faceMaterial;
        // 飞行向量（由 PlaySpawnAnimation 在 spawn 完成后计算）
        private Vector2 _velocity;
        // 飞行计时器，归零时触发 Destroy
        private float _timer;
        private bool _spawning;
        private bool _despawning;
        private float _despawnTimer;
        // 命中后不再造成伤害，直接销毁
        private bool _hit;
        // 攻击来源（父节点下的敌人），用于同阵营伤害过滤
        private GameActor? _attacker;

        public override void _Ready()
        {
            _timer = Duration;
            _spawning = true;
            _despawning = false;
            _hit = false;

            ResolveSprites();
            ResolveMaterials();

            _attackArea = GetNodeOrNull<Area2D>("AttackArea");
            if (_attackArea != null)
            {
                ApplyCollisionMaskOverride();
                _attackArea.BodyEntered += OnAttackAreaBodyEntered;
                _attackArea.AreaEntered += OnAttackAreaAreaEntered;
            }

            ResolveAttacker();
            PlaySpawnAnimation();
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            // ———— 消解阶段 ————
            if (_despawning)
            {
                _despawnTimer -= dt;
                if (_despawnTimer <= 0f)
                {
                    SpawnDestroyEffect();
                    QueueFree();
                    return;
                }
            }

            // spawn 中或已命中 → 暂停更新
            if (_spawning || _hit) return;

            // 匀速直线飞行
            GlobalPosition += _velocity * dt;

            // 飞行计时（仅非 despawn 期间，避免重复触发 Destroy）
            if (!_despawning)
            {
                _timer -= dt;
                if (_timer <= 0f)
                    Destroy();
            }

            // despawn 期间驱动 build_progress 从 1→0（消解扫描线从上往下），各shader的 alpha从1-0
            if (_despawning)
            {
                float t = Mathf.Max(0f, _despawnTimer / DespawnDuration);
                _wireframeMaterial?.SetShaderParameter("alpha", t);
                _faceMaterial?.SetShaderParameter("face_alpha", t);
                _buildMaterial?.SetShaderParameter("build_progress", t);
            }
        }

        public override void _ExitTree()
        {
            // 断开信号，防止悬空引用
            if (_attackArea != null)
            {
                _attackArea.BodyEntered -= OnAttackAreaBodyEntered;
                _attackArea.AreaEntered -= OnAttackAreaAreaEntered;
            }
            base._ExitTree();
        }

        /// <summary>
        /// spawn 构建动画：build_progress 0→1 + scale 从 0.1 pop 到 BaseScale。
        /// 动画完成后计算朝向玩家的飞行向量。
        /// </summary>
        private void PlaySpawnAnimation()
        {
            // 初始状态：所有层不可见 + 最小缩放
            _wireframeMaterial?.SetShaderParameter("alpha", 0f);
            _buildMaterial?.SetShaderParameter("build_progress", 0f);
            _faceMaterial?.SetShaderParameter("face_alpha", 0f);
            if (_buildSprite != null) _buildSprite.Visible = true;
            Scale = new Vector2(0.1f, 0.1f);

            var tree = GetTree();
            if (tree == null) return;

            var tween = tree.CreateTween();
            tween.SetParallel(true);

            // 构建进度 0→1：扫描线从下往上 reveal 线框 + 面，同时 alpha 淡入
            tween.TweenMethod(Callable.From<float>(p =>
            {
                _buildMaterial?.SetShaderParameter("build_progress", p);
                _wireframeMaterial?.SetShaderParameter("alpha", p);
                _faceMaterial?.SetShaderParameter("face_alpha", p);
            }), 0f, 1f, BuildDuration);
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Cubic);

            // Scale pop 带 overshoot 回弹
            tween.TweenProperty(this, "scale", new Vector2(BaseScale, BaseScale), BuildDuration);
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Back);

            // 动画完成后：隐藏构建遮罩、计算飞行方向
            tween.Chain().TweenCallback(Callable.From(() =>
            {
                _spawning = false;
                if (_buildSprite != null) _buildSprite.Visible = false;

                // 外部指定固定方向时跳过瞄准（如上下发射的子立方体），直接朝该方向直飞
                if (FixedDirection.HasValue)
                {
                    _velocity = FixedDirection.Value.Normalized() * Speed;
                    return;
                }

                // 基准方向：面朝右 = 右飞，面朝左 = 左飞
                float baseAngle = FacingRight ? 0f : Mathf.Pi;

                // 按 TargetableFactions 选择瞄准目标（玩家/最近敌人/最近者），无目标则纯水平飞行
                var target = ResolveAimTarget();
                if (target != null)
                {
                    Vector2 toTarget = GetAimCenter(target) - GlobalPosition;
                    if (toTarget != Vector2.Zero)
                    {
                        // 全角度追踪：X 不取绝对值，目标在后方时也朝目标方向飞行
                        // MaxVerticalTiltDegrees = 180+ 时可达 360 度无死角
                        float maxTilt = Mathf.DegToRad(MaxVerticalTiltDegrees);
                        float dySign = FacingRight ? 1f : -1f;
                        float tiltAngle = Mathf.Atan2(toTarget.Y * dySign, toTarget.X * dySign);
                        tiltAngle = Mathf.Clamp(tiltAngle, -maxTilt, maxTilt);
                        baseAngle += tiltAngle;
                    }
                }
                _velocity = new Vector2(Mathf.Cos(baseAngle), Mathf.Sin(baseAngle)) * Speed;
            }));
        }

        /// <summary>
        /// 进入 despawn 阶段：显示 BuildMask，build_progress 将在 _Process 中 1→0 消解。
        /// </summary>
        private void Destroy()
        {
            if (_despawning) return;
            _despawning = true;
            _despawnTimer = DespawnDuration;
            // 重新显示 build 遮罩，初始进度 1（全部可见）
            if (_buildSprite != null) _buildSprite.Visible = true;
            _buildMaterial?.SetShaderParameter("build_progress", 1f);
            // 停止伤害检测
            if (_attackArea != null)
                _attackArea.SetDeferred(Area2D.PropertyName.Monitoring, false);
        }

        /// <summary>
        /// 在当前位置实例化 DestroyEffect 并挂到父节点下。
        /// 若为 ParticleBurst 类型则传入飞行方向，使爆散朝向与 cube 移动方向一致。
        /// </summary>
        private void SpawnDestroyEffect()
        {
            if (DestroyEffect == null) return;

            var instance = DestroyEffect.Instantiate();
            if (instance is Node2D node2D)
            {
                GetParent()?.AddChild(node2D);
                node2D.GlobalPosition = GlobalPosition;

                // 将 cube 的飞行方向传递给 ParticleBurst，使其爆散方向与飞行方向一致
                if (node2D is ParticleBurst burst && _velocity.LengthSquared() > 0.01f)
                {
                    burst.BaseDirection = _velocity.Normalized();
                }
            }
            else
            {
                instance.QueueFree();
            }
        }

        /// <summary>
        /// 按名称查找三层 Sprite：Wireframe / BuildMask / FaceFill。
        /// </summary>
        private void ResolveSprites()
        {
            _wireframeSprite = GetNodeOrNull<Sprite2D>("Visual/Wireframe");
            _buildSprite = GetNodeOrNull<Sprite2D>("Visual/BuildMask");
            _faceSprite = GetNodeOrNull<Sprite2D>("Visual/FaceFill");
        }

        /// <summary>
        /// 为每个 Sprite 的 ShaderMaterial 创建独立副本。
        /// 必须复制：场景中的 material 是所有实例共享的，直接修改会污染其他 cube。
        /// </summary>
        private void ResolveMaterials()
        {
            if (_wireframeSprite?.Material is ShaderMaterial sm)
            {
                _wireframeMaterial = (ShaderMaterial)sm.Duplicate();
                _wireframeSprite.Material = _wireframeMaterial;
            }
            if (_buildSprite?.Material is ShaderMaterial smb)
            {
                _buildMaterial = (ShaderMaterial)smb.Duplicate();
                _buildSprite.Material = _buildMaterial;
            }
            if (_faceSprite?.Material is ShaderMaterial smf)
            {
                _faceMaterial = (ShaderMaterial)smf.Duplicate();
                _faceSprite.Material = _faceMaterial;
            }
        }

        private void ApplyCollisionMaskOverride()
        {
            if (_attackArea == null) return;
            if (TargetCollisionMask == 0) return;
            _attackArea.CollisionMask |= TargetCollisionMask;
        }

        /// <summary>
        /// 物理体进入攻击区域：判定阵营 → 造成伤害 → 击退 → 自毁。
        /// 命中即消失（单次伤害），由 _hit 标记防止重复触发。
        /// </summary>
        private void OnAttackAreaBodyEntered(Node body)
        {
            if (_hit || _spawning) return;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;

            // 传 null 跳过 IsHitByArea 二次重叠检测：信号已确认碰撞，
            // 高速飞行时二次查询可能与物理状态错开导致漏伤害
            bool dealt = DamageDispatcher.DealDamage(body, Damage, GlobalPosition, _attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, null, _velocity);
            if (!dealt)
            {
                // 仅 AirWall（空气墙）拦截销毁，其他物理体（地面/障碍/投掷物）不拦截
                if (body is not GameActor
                    && body is Node node
                    && string.Equals((string)node.Name, "AirWall", System.StringComparison.OrdinalIgnoreCase))
                {
                    SpawnDestroyEffect();
                    QueueFree();
                }
                return;
            }

            if (body is GameActor hitActor)
                ApplyKnockback(hitActor);

            _hit = true;
            SpawnDestroyEffect();
            QueueFree();
        }

        /// <summary>
        /// Area2D 进入攻击区域：逻辑同 BodyEntered，处理 Area 类型的目标。
        /// </summary>
        private void OnAttackAreaAreaEntered(Area2D area)
        {
            if (_hit || _spawning) return;
            // 仅接受目标的 HitArea，避免敌人的攻击判定区等误触发
            if ((string)area.Name != "HitArea") return;
            var target = area.Owner ?? area;
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(target, _attacker)) return;

            bool dealt = DamageDispatcher.DealDamage(target, Damage, GlobalPosition, _attacker,
                DamageSource.DirectAttack, TargetableFactions, AllowSelfDamage, null, _velocity);
            if (!dealt) return;

            if (area.Owner is GameActor hitActor)
                ApplyKnockback(hitActor);

            _hit = true;
            SpawnDestroyEffect();
            QueueFree();
        }

        /// <summary>
        /// 沿飞行方向施加击退速度。
        /// </summary>
        private void ApplyKnockback(GameActor actor)
        {
            if (KnockbackDistance > 0f && _velocity.LengthSquared() > 0.01f)
                actor.ApplyKnockbackDisplacement(_velocity.Normalized(), KnockbackDistance, KnockbackDuration);
        }

        /// <summary>
        /// 显式设置的攻击来源（由生成方传入，如玩家投掷的弹幕 → 玩家）。
        /// 直接映射 _attacker：生成方在 AddChild 之后赋值也能立即生效（_Ready 已解析过）。
        /// </summary>
        public GameActor? Attacker
        {
            get => _attacker;
            set => _attacker = value;
        }

        /// <summary>
        /// 在父节点的子节点中查找第一个 "enemies" 组 GameActor 作为攻击来源（无显式设置时）。
        /// </summary>
        private void ResolveAttacker()
        {
            if (_attacker != null && GodotObject.IsInstanceValid(_attacker))
                return;

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

        /// <summary>
        /// 按 TargetableFactions 选择瞄准目标：玩家、最近敌人，或两者中的最近者。
        /// 跳过**常态免疫**目标（<see cref="GameActor.CanBeAffected"/> 为 false 的敌人，如 rogueAI 本体 /
        /// 磁铁臂 / netAdmin）——朝它们飞只会白跑一趟；注意只认这一道闸，无敌帧、伤害拦截（延迟损伤/护盾）
        /// 这类"伤害仍然到达"的状态**不能**当作筛选条件。
        /// 无可用目标时返回 null（纯水平飞行）。
        /// </summary>
        private Node2D? ResolveAimTarget()
        {
            float bestDistSq = float.MaxValue;
            Node2D? best = null;

            if (TargetableFactions.HasFlag(TargetableFactions.Player))
            {
                var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
                if (player != null && GodotObject.IsInstanceValid(player))
                {
                    float d = player.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                    if (d < bestDistSq) { bestDistSq = d; best = player; }
                }
            }

            if (TargetableFactions.HasFlag(TargetableFactions.Enemy))
            {
                foreach (Node node in GetTree().GetNodesInGroup("enemies"))
                {
                    if (node is not GameActor enemy || !GodotObject.IsInstanceValid(enemy)) continue;
                    if (enemy.IsDeathSequenceActive || enemy.IsDead) continue;
                    if (!enemy.CanBeAffected(null)) continue; // 常态免疫目标不参与瞄准
                    float d = enemy.GlobalPosition.DistanceSquaredTo(GlobalPosition);
                    if (d < bestDistSq) { bestDistSq = d; best = enemy; }
                }
            }

            return best;
        }

        /// <summary>
        /// 获取目标的受击判定中心（HitArea 的 CollisionShape2D 全局位置），
        /// 用于计算飞行方向时的垂直偏移。
        /// </summary>
        private static Vector2 GetAimCenter(Node2D target)
        {
            var hitArea = target.GetNodeOrNull<Area2D>("HitArea")
                ?? target.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? target.GlobalPosition;
        }
    }
}
