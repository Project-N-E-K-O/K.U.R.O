using Godot;
using Kuros.Core;
using Kuros.Systems.FSM;

namespace Kuros.Environments
{
    /// <summary>
    /// 将 Gate 作为一个带血量的"伪敌人"驱动：
    ///
    /// 状态流程：
    ///   Normal  → gate_idle（循环）
    ///   受击          → gate_hit → gate_idle
    ///   HP ≤ BrokenThreshold → gate_broken → gate_broken_idle（循环）
    ///   Broken 受击   → gate_broken_hit → gate_broken_idle
    ///   HP = 0        → gate_knockback（终态，不再响应）
    ///
    /// 命中检测：检测玩家在指定 Area2D 范围内且处于 Attack 状态的上升沿。
    /// 一次性动画播放期间不接受新命中（_animLocked），避免动画被打断。
    /// 受击时广播伤害信号到 GameActor.AnyDamageTaken，触发相机抖动和击打特效。
    /// </summary>
    public partial class GateController : Node
    {
        [ExportCategory("检测范围")]
        [Export] public NodePath? DetectionAreaPath { get; set; } // 用于检测的 Area2D 路径（如 "HitArea"）

        [ExportCategory("Health")]
        [Export(PropertyHint.Range, "1,100,1")] public int MaxHealth { get; set; } = 6;
        /// <summary>HP 降至此值时切换到 Broken 状态。</summary>
        [Export(PropertyHint.Range, "0,100,1")] public int BrokenThreshold { get; set; } = 3;
        [Export(PropertyHint.Range, "1,20,1")] public int HitDamage { get; set; } = 1;

        [ExportCategory("Paths")]
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath("AnimationPlayer");

        private enum GatePhase { Normal, Broken, Dead }

        private AnimationPlayer? _animPlayer;
        private Area2D? _hitArea;
        private Node2D? _self;
        private Node2D? _player;

        private int _hp;
        private GatePhase _phase = GatePhase.Normal;
        private bool _wasAttacking;       // 上一帧玩家是否在攻击
        private bool _animLocked;         // 一次性动画播放中，禁止注册新命中
        private bool _playerInHitArea;    // 玩家当前是否在 HitArea 范围内

        public override void _Ready()
        {
            _self = GetParentOrNull<Node2D>();
            _animPlayer = GetParent().GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);
            
            // 根据 DetectionAreaPath 获取检测区域
            if (DetectionAreaPath != null && !string.IsNullOrEmpty(DetectionAreaPath.ToString()))
            {
                _hitArea = GetParent().GetNodeOrNull<Area2D>(DetectionAreaPath);
            }
            else
            {
                _hitArea = GetParent().GetNodeOrNull<Area2D>("HitArea");
            }

            if (_animPlayer == null)
            {
                GD.PushWarning($"[GateController] 未找到 AnimationPlayer，路径：{AnimationPlayerPath}");
                return;
            }

            if (_hitArea == null)
            {
                GD.PushWarning("[GateController] 未找到检测 Area2D，请设置 DetectionAreaPath 或确保存在 HitArea 子节点");
            }
            else
            {
                _hitArea.AreaEntered += OnHitAreaEntered;
                _hitArea.AreaExited += OnHitAreaExited;
            }

            _hp = MaxHealth;
            _animPlayer.AnimationFinished += OnAnimationFinished;
            PlayAnim("gate_idle");
            SetPhysicsProcess(true);
        }

        public override void _ExitTree()
        {
            if (_animPlayer != null)
                _animPlayer.AnimationFinished -= OnAnimationFinished;
            if (_hitArea != null)
            {
                _hitArea.AreaEntered -= OnHitAreaEntered;
                _hitArea.AreaExited -= OnHitAreaExited;
            }
        }

        public override void _PhysicsProcess(double delta)
        {
            if (_self == null || _animPlayer == null || _phase == GatePhase.Dead) return;

            // 懒加载玩家引用
            if (_player == null || !GodotObject.IsInstanceValid(_player))
            {
                var list = GetTree().GetNodesInGroup("player");
                _player = list.Count > 0 ? list[0] as Node2D : null;
            }
            if (_player == null) return;

            bool attacking = _playerInHitArea && IsPlayerInAttackState(_player);

            // 上升沿：玩家刚进入攻击状态
            if (attacking && !_wasAttacking)
                RegisterHit();

            _wasAttacking = attacking;
        }

        // ── 命中处理 ─────────────────────────────────────────────

        private void RegisterHit()
        {
            if (_animLocked || _phase == GatePhase.Dead) return;

            _hp = Mathf.Max(0, _hp - HitDamage);

            // 广播伤害信号给相机系统和其他监听者
            // Gate 自身虽然不是 GameActor，但我们手动发送伤害信号以触发相机抖动和击打特效
            BroadcastGateDamage();

            if (_hp <= 0)
            {
                _phase = GatePhase.Dead;
                PlayAnim("gate_knockback");
                return;
            }

            if (_phase == GatePhase.Normal && _hp <= BrokenThreshold)
            {
                _phase = GatePhase.Broken;
                _animLocked = true;
                PlayAnim("gate_broken");
                return;
            }

            _animLocked = true;
            PlayAnim(_phase == GatePhase.Broken ? "gate_broken_hit" : "gate_hit");
        }

        /// <summary>
        /// 广播伤害信号。虽然 Gate 不是 GameActor，但通过发送 GameActor.AnyDamageTaken 事件
        /// 让相机系统和其他系统感知到这次伤害（用于镜头抖动、击打特效等）。
        /// victim 设为 null 因为 Gate 不是 GameActor，attacker 是玩家。
        /// </summary>
        private void BroadcastGateDamage()
        {
            if (_player != null && _player is GameActor playerActor)
            {
                GameActor.BroadcastDamage(null, playerActor, HitDamage);
            }
        }

        // ── 动画结束回调 ──────────────────────────────────────────

        private void OnAnimationFinished(StringName animName)
        {
            _animLocked = false;
            switch (animName.ToString())
            {
                case "gate_hit":
                    PlayAnim("gate_idle");
                    break;
                case "gate_broken":
                case "gate_broken_hit":
                    PlayAnim("gate_broken_idle");
                    break;
                case "gate_knockback":
                    break;
            }
        }

        // ── 工具方法 ──────────────────────────────────────────────

        private void PlayAnim(string animName)
        {
            if (_animPlayer == null || !_animPlayer.HasAnimation(animName)) return;
            _animPlayer.Stop();
            _animPlayer.Play(animName);
        }

        private void OnHitAreaEntered(Area2D area)
        {
            if (IsPlayerHitArea(area))
                _playerInHitArea = true;
        }

        private void OnHitAreaExited(Area2D area)
        {
            if (IsPlayerHitArea(area))
                _playerInHitArea = false;
        }

        private bool IsPlayerHitArea(Area2D area)
        {
            if (_player == null) return false;
            var hitArea = _player.GetNodeOrNull<Area2D>("HitArea");
            return hitArea != null && area == hitArea;
        }

        private static bool IsPlayerInAttackState(Node2D player)
        {
            var sm = player.GetNodeOrNull<StateMachine>("StateMachine");
            return sm?.CurrentState?.Name == "Attack";
        }
    }
}
