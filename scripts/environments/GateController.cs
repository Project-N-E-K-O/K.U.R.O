using Godot;
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
    /// 命中检测：检测到玩家在范围内且处于 Attack 状态的上升沿（每次攻击只触发一次）。
    /// 一次性动画播放期间不接受新命中（_animLocked），避免动画被打断。
    /// </summary>
    public partial class GateController : Node
    {
        [ExportCategory("Detection")]
        [Export(PropertyHint.Range, "10,3000,10")] public float DetectionRange { get; set; } = 400f;

        [ExportCategory("Health")]
        [Export(PropertyHint.Range, "1,100,1")] public int MaxHealth { get; set; } = 6;
        /// <summary>HP 降至此值时切换到 Broken 状态。</summary>
        [Export(PropertyHint.Range, "0,100,1")] public int BrokenThreshold { get; set; } = 3;
        [Export(PropertyHint.Range, "1,20,1")] public int HitDamage { get; set; } = 1;

        [ExportCategory("Paths")]
        [Export] public NodePath AnimationPlayerPath { get; set; } = new NodePath("AnimationPlayer");

        private enum GatePhase { Normal, Broken, Dead }

        private AnimationPlayer? _animPlayer;
        private Node2D? _self;
        private Node2D? _player;

        private int _hp;
        private GatePhase _phase = GatePhase.Normal;
        private bool _wasAttacking;   // 上一帧玩家是否在攻击
        private bool _animLocked;     // 一次性动画播放中，禁止注册新命中

        public override void _Ready()
        {
            _self = GetParentOrNull<Node2D>();
            _animPlayer = GetParent().GetNodeOrNull<AnimationPlayer>(AnimationPlayerPath);

            if (_animPlayer == null)
            {
                GD.PushWarning($"[GateController] 未找到 AnimationPlayer，路径：{AnimationPlayerPath}");
                return;
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

            float dist = _self.GlobalPosition.DistanceTo(_player.GlobalPosition);
            bool attacking = dist <= DetectionRange && IsPlayerInAttackState(_player);

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
                    QueueFree();
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

        private static bool IsPlayerInAttackState(Node2D player)
        {
            var sm = player.GetNodeOrNull<StateMachine>("StateMachine");
            return sm?.CurrentState?.Name == "Attack";
        }
    }
}
