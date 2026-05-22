using Godot;
using System;
using System.Threading.Tasks;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// WaiterA 碟子轮盘攻击的主控制器。
    /// 负责：
    ///   1. 管理碟子的发射数量和间隔
    ///   2. 360 度均匀分布碟子方向
    ///   3. 克隆和配置碟子的生成过程
    ///
    /// 使用方式：
    ///   - 将此脚本挂载在 EnemyWaiterA02WheelAttackEffect.tscn 根节点上
    ///   - 配置 ProjectileCount（碟子数量）和 SpawnIntervalPerProjectile（发射间隔）
    ///   - 配置 ProjectilePrefab（碟子预制体）
    ///   - 碟子的具体行为参数（伤害、击退、飞行时间等）由 EnemyWaiterA02ProjectileInstance 控制
    /// </summary>
    [GlobalClass]
    public partial class EnemyWaiterA02WheelAttackController : Node2D
    {
        /// <summary>碟子副本数量。</summary>
        [Export] public int ProjectileCount { get; set; } = 8;

        /// <summary>每个碟子之间的生成间隔（秒），实现连续发射效果。设为 0 则同时生成。</summary>
        [Export(PropertyHint.Range, "0,1,0.01")] public float SpawnIntervalPerProjectile { get; set; } = 0.05f;

        /// <summary>模板碟子节点的路径（包含 Sprite2D 和 Area2D）。通常为 "Sprite2D" 或 "."。</summary>
        [Export] public NodePath TemplateNodePath = new NodePath(".");

        /// <summary>碟子预制体场景路径（若需要动态生成）。如为空则使用现有模板节点克隆。</summary>
        [Export] public PackedScene? ProjectilePrefab = null;

        private Node2D? _templateNode;
        private RandomNumberGenerator _rng = new RandomNumberGenerator();

        public override void _Ready()
        {
            _rng.Randomize();

            // 获取模板节点（用于克隆）
            if (!TemplateNodePath.IsEmpty)
            {
                _templateNode = GetNodeOrNull<Node2D>(TemplateNodePath);
            }

            // 推迟一帧生成碟子，确保特效的 GlobalPosition 已被正确设置
            CallDeferred(nameof(SpawnProjectiles));
        }

        /// <summary>
        /// 生成 N 个碟子，每个独立朝向随机方向。
        /// 如果设置了 SpawnIntervalPerProjectile，碟子会逐个生成（连续发射）。
        /// </summary>
        private async void SpawnProjectiles()
        {
            float angleStep = 360f / ProjectileCount;

            for (int i = 0; i < ProjectileCount; i++)
            {
                float angle = angleStep * i + _rng.Randf() * 5f;  // 加入少量随机扰动
                Vector2 direction = Vector2.FromAngle(Mathf.DegToRad(angle));

                SpawnSingleProjectile(direction);

                // 如果设置了发射间隔，等待后再生成下一个碟子
                if (i < ProjectileCount - 1 && SpawnIntervalPerProjectile > 0.01f)
                {
                    await Task.Delay((int)(SpawnIntervalPerProjectile * 1000));
                }
            }
        }

        /// <summary>
        /// 生成单个碟子实例。
        /// 只负责克隆和设置发射方向，碟子的具体参数由 EnemyWaiterA02ProjectileInstance 本身决定。
        /// </summary>
        private void SpawnSingleProjectile(Vector2 direction)
        {
            Node2D? projectileNode = null;

            if (ProjectilePrefab != null)
            {
                // 从预制体克隆
                projectileNode = ProjectilePrefab.Instantiate<Node2D>();
            }
            else if (_templateNode != null)
            {
                // 从现有模板克隆
                var duplicated = _templateNode.Duplicate();
                projectileNode = duplicated as Node2D;
                if (projectileNode == null)
                {
                    GD.PushWarning("[EnemyWaiterA02WheelAttackController] 模板节点克隆失败或不是 Node2D 类型");
                    return;
                }
            }
            else
            {
                GD.PushWarning("[EnemyWaiterA02WheelAttackController] 未找到模板节点或预制体");
                return;
            }

            if (projectileNode == null)
            {
                return;
            }

            // 添加到场景树
            GetParent()?.AddChild(projectileNode);
            projectileNode.GlobalPosition = GlobalPosition;

            // 配置发射方向（唯一由控制器负责的碟子参数）
            if (projectileNode is EnemyWaiterA02ProjectileInstance projectile)
            {
                projectile.SetDirectionAndDistance(direction, projectile.ProjectileDistance);
            }
            else
            {
                // 如果克隆的节点本身不是 EnemyWaiterA02ProjectileInstance，尝试找子节点
                var controller = projectileNode.GetNodeOrNull<EnemyWaiterA02ProjectileInstance>(".");
                if (controller != null)
                {
                    controller.SetDirectionAndDistance(direction, controller.ProjectileDistance);
                }
            }
        }
    }
}
