using Godot;
using Kuros.Managers;

namespace Kuros.Environments
{
    [GlobalClass]
    public partial class ElevatorGate : RigidBody2D
    {
        [Export] public NodePath BattleArenaPath { get; set; } = new NodePath();

        /// <summary>
        /// 战斗结束后延迟多少秒再开门。0 = 立即开门。
        /// </summary>
        [Export(PropertyHint.Range, "0,10,0.1")]
        public float OpenDelay { get; set; } = 0f;

        private AnimationPlayer? _animPlayer;
        private BattleArena? _battleArena;
        private bool _opened = false;

        public override void _Ready()
        {
            _animPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");

            if (!BattleArenaPath.IsEmpty)
            {
                _battleArena = GetNodeOrNull<BattleArena>(BattleArenaPath);
                if (_battleArena != null)
                {
                    _battleArena.BattleEnded += OnBattleEnded;
                }
                else
                {
                    GD.PushWarning($"[ElevatorGate] {Name}: BattleArenaPath '{BattleArenaPath}' 未找到 BattleArena 节点。");
                }
            }
        }

        public override void _ExitTree()
        {
            if (_battleArena != null && GodotObject.IsInstanceValid(_battleArena))
                _battleArena.BattleEnded -= OnBattleEnded;
        }

        /// <summary>播放开门动画（只播放一次），支持延时。</summary>
        public async void Open()
        {
            if (_opened) return;
            _opened = true;

            if (OpenDelay > 0f)
            {
                await ToSignal(GetTree().CreateTimer(OpenDelay), SceneTreeTimer.SignalName.Timeout);
            }

            // 延时结束后节点可能已被销毁，需要验证
            if (!GodotObject.IsInstanceValid(this)) return;
            if (_animPlayer == null || !GodotObject.IsInstanceValid(_animPlayer)) return;
            if (!_animPlayer.HasAnimation("open")) return;

            _animPlayer.Play("open");
        }

        private void OnBattleEnded()
        {
            Open();
        }
    }
}