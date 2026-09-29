using System.Collections.Generic;
using Godot;
using Kuros.Core;
using Kuros.Core.Effects;
using Kuros.Core.Events;

namespace Kuros.Fx
{
    /// <summary>
    /// 回旋镖攻击效果。
    ///
    /// 行为：
    ///   - 飞行逻辑：水平投掷，X 轴速度逐渐减慢，经过 ReturnTime 秒后归零（最远点），
    ///     之后反向加速返回，同时追踪玩家 Y 轴方向，轨迹近乎椭圆形。
    ///   - 飞行途中接触玩家（投掷者）后自动销毁。
    ///   - 伤害逻辑：AttackArea 范围内每 DamageInterval 秒造成一次伤害并使目标速度归零。
    ///   - 基础逻辑和规范严格按照 EFFECT_STANDARD.md
    /// </summary>
    public partial class BoomerangAttackEffect : Node2D, IFacingDirectional, IAttackerProvider, IThrowFlightDistance
    {
        [ExportCategory("Movement")]
        [Export(PropertyHint.Range, "50,5000,10")] public float Speed = 1800f;
        [Export(PropertyHint.Range, "0.2,5,0.1")] public float ReturnTime = 1.0f;
        [Export(PropertyHint.Range, "0,5,0.1")] public float TrackStartTime = 1.2f;
        [Export(PropertyHint.Range, "0.5,20,0.5")] public float ReturnTrackLerp = 4f;
        [Export] public bool RotateWithVelocity = true;

        [ExportCategory("Direction")]
        [Export] public bool FacingRight { get; set; } = true;

        [ExportCategory("Timing")]
        [Export(PropertyHint.Range, "0.5,30,0.1")] public float Duration = 2.0f;
        [Export(PropertyHint.Range, "0,1,0.05")] public float HitPlayerDelay = 0.15f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Flags, "Player,Enemy,WorldItem")]
        public TargetableFactions TargetableFactions = TargetableFactions.Enemy;
        [Export] public bool AllowSelfDamage { get; set; } = false;
        [Export(PropertyHint.Range, "0,500,1")] public int Damage = 10;
        [Export(PropertyHint.Range, "0.1,5,0.1")] public float DamageInterval = 0.5f;

        [ExportCategory("Slow On Hit")]
        /// <summary>命中减速开关（同 SlowOnHitEffect 效果：命中时降低目标移动速度）。</summary>
        [Export] public bool EnableSlowOnHit { get; set; } = false;
        /// <summary>减速百分比（0~100），50 = 速度降低 50%。</summary>
        [Export(PropertyHint.Range, "1,100,1")] public float SlowPercent { get; set; } = 50f;
        /// <summary>减速持续时间（秒）。</summary>
        [Export(PropertyHint.Range, "0.1,30,0.1")] public float SlowDuration { get; set; } = 2f;

        [ExportCategory("Afterimage")]
        [Export] public bool EnableAfterimage = true;
        [Export] public NodePath AfterimageControllerPath = new("AfterimageController");

        [ExportCategory("Pseudo3D")]
        [Export] public NodePath Pseudo3DTargetPath { get; set; } = new();
        [Export(PropertyHint.Range, "0,360,0.1")] public float Pseudo3DXAngle = 50f;
        [Export(PropertyHint.Range, "0,360,0.1")] public float Pseudo3DYAngle = 0f;
        [Export(PropertyHint.Range, "0,3600,1")] public float Pseudo3DZSpeed = 720f;

        // ── 子节点引用 ────────────────────────────────────────────

        private Area2D? _attackArea;
        private string? _sourceWeaponItemId;

        // ── 公共属性 ──────────────────────────────────────────────

        public string? SourceWeaponItemId
        {
            get
            {
                if (_sourceWeaponItemId == null && HasMeta("source_weapon_item_id"))
                    _sourceWeaponItemId = (string)GetMeta("source_weapon_item_id");
                return _sourceWeaponItemId;
            }
            set => _sourceWeaponItemId = value;
        }

        // ── 运行时状态 ────────────────────────────────────────────

        private Vector2 _currentVelocity;
        private float _elapsed;
        private float _vx0;
        private float _ax;
        private bool _initialized;
        private bool _caught;
        private GameActor? _attacker;
        private Node2D? _visual;
        private ShaderMaterial? _pseudo3DMaterial;
        private Node2D? _pseudo3DTarget;
        private float _pseudo3DZAccum;

        /// <summary>生成侧注入的本次投掷飞行距离（px,0=未注入 → 使用 Speed 导出）。</summary>
        private float _flightDistance;

        /// <summary>生成侧注入本次投掷距离（与预览同一解析真源;见 IThrowFlightDistance）。</summary>
        public void SetThrowFlightDistance(float distance) => _flightDistance = distance;
        private Node? _afterimage;

        private readonly Dictionary<GameActor, float> _actorTimers = new();
        private readonly Dictionary<GameActor, int> _actorRefs = new();

        // ── 生命周期 ──────────────────────────────────────────────

        public override void _Ready()
        {
            _initialized = false;
            _caught = false;

            _attackArea = GetNodeOrNull<Area2D>("AttackArea");
            _visual = GetNodeOrNull<Node2D>("Visual");
            if (_attackArea != null)
            {
                _attackArea.BodyEntered += OnBodyEntered;
                _attackArea.BodyExited += OnBodyExited;
                _attackArea.AreaEntered += OnAreaEntered;
                _attackArea.AreaExited += OnAreaExited;
            }

            SetupPseudo3D();

            if (EnableAfterimage && !AfterimageControllerPath.IsEmpty)
                _afterimage = GetNodeOrNull<Node>(AfterimageControllerPath);

            ResolveAttacker();
        }

        /// <summary>
        /// 显式攻击来源（由投掷方传入，同 IAttackerProvider 项目模式）。
        /// 优先于父节点解析：父节点下第一个玩家不一定是投掷者，解析错误会导致 AllowSelfDamage 保护失效。
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
                if (child.IsInGroup("player") && child is GameActor ga)
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
                _attackArea.BodyEntered -= OnBodyEntered;
                _attackArea.BodyExited -= OnBodyExited;
                _attackArea.AreaEntered -= OnAreaEntered;
                _attackArea.AreaExited -= OnAreaExited;
            }
            _actorTimers.Clear();
            _actorRefs.Clear();
            base._ExitTree();
        }

        public override void _Process(double delta)
        {
            float dt = (float)delta;

            if (!_initialized)
            {
                _initialized = true;
                _afterimage?.Call("start");

                bool faceRight = _attacker?.FacingRight ?? FacingRight;
                // 注入距离优先：匀减速模型下最远 = v0×ReturnTime/2 → v0 = 2×距离/ReturnTime；
                // 未注入回退 Speed（行为不变）
                float v0 = _flightDistance > 0f && ReturnTime > 0.01f
                    ? 2f * _flightDistance / ReturnTime
                    : Speed;
                _vx0 = v0 * (faceRight ? 1f : -1f);
                _ax = -_vx0 / Mathf.Max(ReturnTime, 0.01f);

                _currentVelocity = new Vector2(_vx0, 0f);
            }

            _elapsed += dt;

            float vx = _vx0 + _ax * _elapsed;
            float vy = 0f;
            if (_elapsed >= TrackStartTime && _attacker != null && GodotObject.IsInstanceValid(_attacker))
            {
                float targetY = GetActorAimCenter(_attacker).Y;
                float t = 1f - Mathf.Exp(-ReturnTrackLerp * dt);
                float newY = Mathf.Lerp(GlobalPosition.Y, targetY, t);
                vy = (newY - GlobalPosition.Y) / dt;
            }
            _currentVelocity = new Vector2(vx, vy);
            GlobalPosition += _currentVelocity * dt;

            // 旋转只作用于视觉节点：攻击判定（AttackArea）固定不随旋转偏移
            float targetAngle = RotateWithVelocity
                ? (_currentVelocity.LengthSquared() > 0.1f ? _currentVelocity.Angle() : Rotation)
                : (_currentVelocity.X > 0.1f ? 0f : (_currentVelocity.X < -0.1f ? Mathf.Pi : Rotation));
            if (_visual != null)
                _visual.Rotation = targetAngle;
            else
                Rotation = targetAngle; // 兼容无 Visual 节点的旧结构

            if (Pseudo3DZSpeed != 0f && _pseudo3DMaterial != null)
            {
                _pseudo3DZAccum += Pseudo3DZSpeed * dt;
                _pseudo3DMaterial.SetShaderParameter("zDegrees", _pseudo3DZAccum % 360f);
            }

            TickDamage(dt);

            if (_elapsed >= Duration)
                Destroy();
        }

        // ── 区域计时伤害 ──────────────────────────────────────────

        private void TickDamage(float dt)
        {
            if (_actorTimers.Count == 0) return;

            var dead = new List<GameActor>();
            foreach (var (actor, timer) in _actorTimers)
            {
                if (!GodotObject.IsInstanceValid(actor) || actor.IsDeadOrDying)
                {
                    dead.Add(actor);
                    continue;
                }

                float accumulated = timer + dt;
                if (accumulated >= DamageInterval)
                {
                    _actorTimers[actor] = 0f;
                    DealDamageToActor(actor);
                }
                else
                {
                    _actorTimers[actor] = accumulated;
                }
            }

            foreach (var a in dead)
            {
                _actorTimers.Remove(a);
                _actorRefs.Remove(a);
            }
        }

        private void DealDamageToActor(GameActor actor)
        {
            // 用 ThrowImpact 而非 DirectAttack：回旋镖是投掷武器的延迟命中，若标记为直接攻击，
            // 换装后命中会误触当前武器的 on-hit 效果（与 ECoreAttackEffect 同类跨武器串效果漏洞）。
            bool dealt = DamageDispatcher.DealDamage(actor, Damage, GlobalPosition, _attacker,
                DamageSource.ThrowImpact, TargetableFactions, AllowSelfDamage, null);
            if (dealt)
            {
                actor.Velocity = Vector2.Zero;
                if (EnableSlowOnHit && actor.EffectController != null)
                {
                    actor.ApplyEffect(new BoomerangSlowDebuff
                    {
                        SlowPercent = SlowPercent,
                        Duration = SlowDuration
                    });
                }
            }
        }

        // ─── 内部减速 Debuff：命中时降低目标速度，到期/移除时恢复（同 SlowOnHitEffect 逻辑）───

        private sealed partial class BoomerangSlowDebuff : ActorEffect
        {
            public float SlowPercent { get; set; }

            /// <summary>登记到共享管理器的乘数（≤1 表示已登记）。减速统一走 SharedSpeedSlowManager，
            /// 不再自己快照 Actor.Speed——否则和区域减速/负重重叠时会互相覆盖快照、丢原始速度。</summary>
            private float _registeredMultiplier = 1f;

            public BoomerangSlowDebuff()
            {
                EffectId = "boomerang_speed_slow_debuff";
                DisplayName = "减速";
                Description = "移动速度降低。";
                IsBuff = false;
                MaxStacks = 1;
            }

            protected override void OnApply()
            {
                base.OnApply();
                _registeredMultiplier = 1f - Mathf.Clamp(SlowPercent / 100f, 0f, 1f);
                SharedSpeedSlowManager.Apply(Actor, _registeredMultiplier);
            }

            public override void OnRemoved()
            {
                // 只撤自己那一档：别的减速源还在时继续生效，最后一个撤掉时才由管理器还原原始速度
                if (Actor != null && _registeredMultiplier < 1f)
                    SharedSpeedSlowManager.Remove(Actor, _registeredMultiplier);
                _registeredMultiplier = 1f;
                base.OnRemoved();
            }
        }

        // ── 碰撞回调 ──────────────────────────────────────────────

        private void OnBodyEntered(Node body)
        {
            if (_caught) return;

            if (body is GameActor playerActor && playerActor.IsInGroup("player"))
            {
                TryCatch();
                return;
            }

            if (body is not GameActor actor)
            {
                // WorldItem（DestructibleObject 等 TakeDamage 节点）：一次性结算（不计时、无速度概念）
                if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;
                DamageDispatcher.DealDamage(body, Damage, GlobalPosition, _attacker,
                    DamageSource.ThrowImpact, TargetableFactions, AllowSelfDamage, null, _currentVelocity);
                return;
            }
            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(body, _attacker)) return;
            AddActorRef(actor);
        }

        private void OnBodyExited(Node body)
        {
            if (body is GameActor actor)
                RemoveActorRef(actor);
        }

        private void OnAreaEntered(Area2D area)
        {
            if (_caught) return;
            if ((string)area.Name != "HitArea") return;

            var actor = area.Owner as GameActor
                ?? area.GetParent() as GameActor
                ?? area.GetParent()?.GetParent() as GameActor;
            if (actor == null) return;

            if (actor.IsInGroup("player"))
            {
                TryCatch();
                return;
            }

            if (!AllowSelfDamage && DamageDispatcher.BelongsToActor(actor, _attacker)) return;
            AddActorRef(actor);
        }

        private void OnAreaExited(Area2D area)
        {
            if ((string)area.Name != "HitArea") return;

            var actor = area.Owner as GameActor
                ?? area.GetParent() as GameActor
                ?? area.GetParent()?.GetParent() as GameActor;
            if (actor != null)
                RemoveActorRef(actor);
        }

        private void AddActorRef(GameActor actor)
        {
            if (_actorRefs.TryGetValue(actor, out int count))
            {
                _actorRefs[actor] = count + 1;
                return;
            }

            _actorRefs[actor] = 1;
            _actorTimers[actor] = 0f;
            DealDamageToActor(actor);
        }

        private void RemoveActorRef(GameActor actor)
        {
            if (!_actorRefs.TryGetValue(actor, out int count)) return;
            if (count > 1)
            {
                _actorRefs[actor] = count - 1;
                return;
            }

            _actorRefs.Remove(actor);
            _actorTimers.Remove(actor);
        }

        private void TryCatch()
        {
            if (_elapsed < HitPlayerDelay) return;
            _caught = true;
            Destroy();
        }

        private void Destroy()
        {
            ResetThrowCooldown();
            _afterimage?.Call("stop");
            QueueFree();
        }

        private void ResetThrowCooldown()
        {
            if (_attacker is not SamplePlayer player) return;
            var quickBar = player.InventoryComponent?.QuickBar;
            if (quickBar == null) return;

            var weaponId = SourceWeaponItemId;
            if (!string.IsNullOrEmpty(weaponId))
            {
                for (int i = 0; i < quickBar.SlotCount; i++)
                {
                    var stack = quickBar.GetStack(i);
                    if (stack != null && stack.Item.ItemId == weaponId)
                    {
                        stack.ThrowCooldownRemaining = 0f;
                        player.InventoryComponent?.NotifyCombatWeaponResolutionChanged();
                        return;
                    }
                }
            }

            var selectedStack = player.InventoryComponent?.GetSelectedQuickBarStack();
            if (selectedStack != null)
            {
                selectedStack.ThrowCooldownRemaining = 0f;
                player.InventoryComponent?.NotifyCombatWeaponResolutionChanged();
            }
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

        private static Vector2 GetActorAimCenter(GameActor actor)
        {
            var hitArea = actor.GetNodeOrNull<Area2D>("HitArea")
                ?? actor.FindChild("HitArea", recursive: true, owned: false) as Area2D;
            var hitShape = hitArea?.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
            return hitShape?.GlobalPosition
                ?? hitArea?.GlobalPosition
                ?? actor.GlobalPosition;
        }
    }
}
