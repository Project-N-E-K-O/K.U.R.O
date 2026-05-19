using Godot;
using System;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// C1 服务员 A 的攻击控制器。
    /// 
    /// 工作原理：
    ///   - 管理多个攻击模板（SimpleMeleeAttack、ThrowAttack）的权重和选择
    ///   - 范围检测由各个攻击模板通过 CanStart() 自身实现
    ///   - SimpleMeleeAttack 检查近战范围（Sprite2D/MeleeAttackArea）
    ///   - ThrowAttack 检查远程范围（Sprite2D/ThrowAttackArea）
    /// </summary>
    public partial class EnemyC1WaiterAAttackController : EnemyAttackController
    {
        /// <summary>近战攻击名字。</summary>
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";

        /// <summary>远程攻击名字。</summary>
        [Export] public string ThrowAttackName { get; set; } = "ThrowAttack";

        public string CurrentAttackName { get; private set; } = string.Empty;

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);
            CurrentAttackName = attack.Name;
        }

        protected override void OnAttackFinished()
        {
            base.OnAttackFinished();
            CurrentAttackName = string.Empty;
        }

        private static bool IsAttack(string attackName, string expectedName)
        {
            return attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        }
    }
}


