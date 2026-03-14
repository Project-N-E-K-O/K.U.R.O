using Godot;
using Godot.Collections;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.Attacks
{
    /// <summary>
    /// 玩家攻击通用模板。
    /// 处理输入监听、预热/生效/恢复阶段、冷却、资源校验与默认命中判定。
    /// 后续的玩家攻击类型可继承本类并覆盖关键钩子。
    /// </summary>
    public partial class PlayerAttackTemplate : Node
    {
        private enum AttackPhase
        {
            Idle,
            Warmup,
            Active,
            Recovery
        }

        [ExportCategory("Meta")]
        [Export] public string AttackId = "player_attack_default";
        [Export] public string DisplayName = "Player Attack";
        [Export(PropertyHint.MultilineText)] public string Description = "";

        [ExportCategory("Input")]
        [Export] public Array<StringName> TriggerActions { get; set; } = new();
        [Export] public bool AllowHoldInput = false;
        [Export] public bool BufferInputUntilReady = true;

        [ExportCategory("Timing (s)")]
        [Export(PropertyHint.Range, "0,5,0.01")] public float WarmupDuration = 0.15f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float ActiveDuration = 0.1f;
        [Export(PropertyHint.Range, "0,5,0.01")] public float RecoveryDuration = 0.25f;
        [Export(PropertyHint.Range, "0,10,0.01")] public float CooldownDuration = 0.6f;

        [ExportCategory("Damage")]
        [Export(PropertyHint.Range, "0,500,1")] public float DamageOverride = 25.0f;
        [Export(PropertyHint.Range, "0,1000,1")] public float AttackRange = 120.0f;
        [Export] public NodePath AttackAreaPath = new NodePath();

        [ExportCategory("Animation")]
        [Export] public string AnimationName = "animations/attack";
        [Export] public bool RestartAnimationOnLoop = true;

        [ExportCategory("Requirements")]
        [Export] public bool RequiresTargetInRange = false;
        [Export] public bool RequiresResource = false;
        [Export] public StringName ResourceId = new StringName();
        [Export] public int ResourceCost = 0;
        [Export] public string RequiredItemId = "";
        [Export] public bool ConsumeResourceOnStart = true;

        protected SamplePlayer Player { get; private set; } = null!;
        protected string TriggerSourceState { get; private set; } = string.Empty;
        protected Area2D? AttackArea { get; private set; }

        private AttackPhase _phase = AttackPhase.Idle;
        private float _phaseTimer = 0f;
        private float _cooldownTimer = 0f;
        private bool _bufferedInput = false;

        public bool IsRunning => _phase != AttackPhase.Idle;
        public bool IsOnCooldown => _cooldownTimer > 0f;

        public virtual void Initialize(SamplePlayer player)
        {
            Player = player;

            if (!string.IsNullOrEmpty(AttackAreaPath.ToString()))
            {
                AttackArea = Player.GetNodeOrNull<Area2D>(AttackAreaPath);
            }

            if (AttackArea == null)
            {
                AttackArea = Player.AttackArea;
            }

            OnInitialized();
        }

        protected virtual void OnInitialized() { }

        public void SetTriggerSourceState(string stateName)
        {
            TriggerSourceState = stateName;
        }

        public void Tick(double delta)
        {
            if (_cooldownTimer > 0f)
            {
                _cooldownTimer -= (float)delta;
            }

            if (_phase == AttackPhase.Idle) return;

            _phaseTimer -= (float)delta;
            if (_phaseTimer <= 0f)
            {
                AdvancePhase();
            }

            OnTick(delta);
        }

        protected virtual void OnTick(double delta) { }

        public bool TryStart(bool checkInput = true)
        {
            if (!CanStart(checkInput)) return false;

            _cooldownTimer = CooldownDuration;
            Player.AttackTimer = Mathf.Max(Player.AttackTimer, CooldownDuration);

            OnAttackStarted();
            SetPhase(AttackPhase.Warmup);

            if (ConsumeResourceOnStart)
            {
                ConsumeResources();
            }

            return true;
        }

        public void Cancel(bool clearCooldown = false)
        {
            if (clearCooldown)
            {
                _cooldownTimer = 0f;
                Player.AttackTimer = 0f;
            }

            if (_phase != AttackPhase.Idle)
            {
                SetPhase(AttackPhase.Idle);
            }
        }

        protected virtual bool CanStart(bool checkInput)
        {
            if (Player == null) return false;
            if (IsRunning || IsOnCooldown) return false;
            if (Player.AttackTimer > 0f) return false;

            if (checkInput && !IsInputTriggered())
            {
                return false;
            }

            if (!HasRequiredResources())
            {
                return false;
            }

            if (RequiresTargetInRange && !HasValidTarget())
            {
                return false;
            }

            return MeetsCustomConditions();
        }

        protected virtual bool HasRequiredResources()
        {
            if (!RequiresResource && string.IsNullOrEmpty(RequiredItemId)) return true;
            return EvaluateCustomRequirement();
        }

        protected virtual bool EvaluateCustomRequirement() => true;

        protected virtual bool HasValidTarget()
        {
            if (AttackArea != null)
            {
                var bodies = AttackArea.GetOverlappingBodies();
                return bodies.Count > 0;
            }

            return true;
        }

        protected virtual bool MeetsCustomConditions() => true;

        protected virtual void ConsumeResources() { }

        protected virtual bool IsInputTriggered()
        {
            if (TriggerActions.Count == 0)
            {
                return true;
            }

            foreach (var action in TriggerActions)
            {
                if (Input.IsActionJustPressed(action))
                {
                    return true;
                }

                if (AllowHoldInput && Input.IsActionPressed(action))
                {
                    return true;
                }
            }

            if (BufferInputUntilReady && _bufferedInput)
            {
                _bufferedInput = false;
                return true;
            }

            return false;
        }

        public void BufferInput()
        {
            if (BufferInputUntilReady)
            {
                _bufferedInput = true;
            }
        }

        protected virtual void OnAttackStarted()
        {
            // 如果是 MainCharacter，使用 Spine 动画
            if (Player is MainCharacter mainChar)
            {
                if (!string.IsNullOrEmpty(AnimationName))
                {
                    GD.Print($"[{GetType().Name}] 播放攻击动画 (Spine): {AnimationName}");
                    mainChar.PlaySpineAnimation(AnimationName, false);
                }
                else
                {
                    GD.PushWarning($"[{GetType().Name}] AnimationName 为空，无法播放攻击动画");
                }
            }
            // 否则使用 AnimationPlayer
            else if (!string.IsNullOrEmpty(AnimationName) && Player.AnimPlayer != null)
            {
                if (RestartAnimationOnLoop || !Player.AnimPlayer.IsPlaying())
                {
                    Player.AnimPlayer.Play(AnimationName);
                }
            }
            else
            {
                GD.PushWarning($"[{GetType().Name}] 无法播放攻击动画: AnimationName={AnimationName}, AnimPlayer={Player.AnimPlayer}");
            }
        }

        protected virtual void OnWarmupStarted()
        {
            Player.Velocity = Vector2.Zero;
        }

        protected virtual void OnActivePhase()
        {
            PerformDefaultHitDetection();
        }

        protected virtual void OnRecoveryStarted()
        {
            Player.Velocity = Player.Velocity.MoveToward(Vector2.Zero, Player.Speed);
        }

        protected virtual void OnAttackFinished() { }

        private void SetPhase(AttackPhase phase)
        {
            _phase = phase;
            switch (phase)
            {
                case AttackPhase.Idle:
                    _phaseTimer = 0f;
                    OnAttackFinished();
                    break;
                case AttackPhase.Warmup:
                    _phaseTimer = WarmupDuration;
                    OnWarmupStarted();
                    break;
                case AttackPhase.Active:
                    _phaseTimer = ActiveDuration;
                    OnActivePhase();
                    break;
                case AttackPhase.Recovery:
                    _phaseTimer = RecoveryDuration;
                    OnRecoveryStarted();
                    break;
            }

            if (_phase != AttackPhase.Idle && _phaseTimer <= 0f)
            {
                AdvancePhase();
            }
        }

        private void AdvancePhase()
        {
            switch (_phase)
            {
                case AttackPhase.Warmup:
                    SetPhase(AttackPhase.Active);
                    break;
                case AttackPhase.Active:
                    SetPhase(AttackPhase.Recovery);
                    break;
                case AttackPhase.Recovery:
                    SetPhase(AttackPhase.Idle);
                    break;
            }
        }

        protected virtual void PerformDefaultHitDetection()
        {
            if (Player == null) return;

            float originalDamage = Player.AttackDamage;
            Player.AttackDamage = DamageOverride;

            Player.PerformAttackCheck();

            Player.AttackDamage = originalDamage;
        }
    }
}

