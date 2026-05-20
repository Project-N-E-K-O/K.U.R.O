using System;
using Godot;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// WaiterA 投掷物场景的控制脚本。
    ///
    /// 工作流程：
    ///   1. _Ready() 时快照玩家当前位置作为落点目标
    ///   2. 第一帧 _PhysicsProcess 记录实际生成位置（GlobalPosition 已由外部设置）
    ///   3. 后续帧按参数化抛物线公式移动，不触发任何碰撞/伤害检测
    ///   4. 到达落点后：生成 ImpactEffectScene（若已配置），然后 QueueFree 自身
    ///
    /// 抛物线公式（来自 RigidBodyWorldItemEntity）：
    ///   x = lerp(startX, targetX, t)
    ///   y = lerp(startY, targetY, t) - sin(t * π) * PeakHeight
    ///   其中 t = elapsed / Duration ∈ [0, 1]
    /// </summary>
    public partial class EnemyWaiterAThrowProjectile : Node2D
    {
        /// <summary>飞行总时长（秒）。</summary>
        [Export] public float Duration { get; set; } = 0.8f;

        /// <summary>抛物线峰值高度（像素，正值向上）。</summary>
        [Export] public float PeakHeight { get; set; } = 300f;

        /// <summary>飞行途中每秒旋转角度（度）。正值顺时针，负值逆时针，0 不旋转。</summary>
        [Export] public float RotationDegreesPerSecond { get; set; } = 360f;

        /// <summary>落地后依次生成的特效场景列表（Node2D）。为空时跳过。</summary>
        [Export] public PackedScene[] ImpactEffectScenes { get; set; } = Array.Empty<PackedScene>();

        private Vector2 _startPos;
        private Vector2 _targetPos;
        private float _elapsed;
        private bool _launched;   // 第一帧后才开始移动，确保 _startPos 已正确记录

        public override void _Ready()
        {
            // 立即快照玩家位置作为落点（此时自身 GlobalPosition 尚未由外部设置，勿用）
            var player = GetTree().GetFirstNodeInGroup("player") as Node2D;
            _targetPos = player?.GlobalPosition ?? Vector2.Zero;

            SetPhysicsProcess(true);
        }

        public override void _PhysicsProcess(double delta)
        {
            if (!_launched)
            {
                // 第一帧：GlobalPosition 已由 SpawnEffectAtEnemy 设置完毕，记录起点
                _startPos = GlobalPosition;
                _launched = true;
                return;
            }

            _elapsed += (float)delta;
            float t = Mathf.Clamp(_elapsed / Duration, 0f, 1f);

            float upDown = Mathf.Sin(t * Mathf.Pi);
            float x = Mathf.Lerp(_startPos.X, _targetPos.X, t);
            float y = Mathf.Lerp(_startPos.Y, _targetPos.Y, t) - upDown * PeakHeight;
            GlobalPosition = new Vector2(x, y);
            RotationDegrees += RotationDegreesPerSecond * (float)delta;

            if (t >= 1f)
            {
                OnArrived();
            }
        }

        private void OnArrived()
        {
            SetPhysicsProcess(false);

            foreach (var scene in ImpactEffectScenes)
            {
                if (scene == null) continue;
                var fx = scene.Instantiate<Node>();
                GetParent()?.AddChild(fx);
                // 若根节点本身是 Node2D，直接设置位置；
                // 否则（根节点为 Node）找第一个 Node2D 子节点来设置（如 SpikeAttackEffect）
                if (fx is Node2D fx2d)
                {
                    fx2d.GlobalPosition = GlobalPosition;
                }
                else
                {
                    foreach (var child in fx.GetChildren())
                    {
                        if (child is Node2D childNode2D)
                        {
                            childNode2D.GlobalPosition = GlobalPosition;
                            break;
                        }
                    }
                }
            }

            QueueFree();
        }
    }
}
