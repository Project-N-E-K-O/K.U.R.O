using Godot;
using System;
using System.Collections.Generic;

namespace Kuros.Actors.Enemies.Attacks
{
    public partial class EnemyC1WaiterBAttackController : EnemyAttackController
    {
        [Export] public string Skill1AttackName { get; set; } = "DashSlashAttack";
        [Export] public string MeleeAttackName { get; set; } = "SimpleMeleeAttack";
        [Export] public string UltimateAttackName { get; set; } = "UltimateBeamAttack";
        [Export(PropertyHint.Range, "1,10,1")] public int MeleeCountBeforeCharge { get; set; } = 2;

        [ExportCategory("Ultimate Attack")]
        /// <summary>
        /// 触发终极技的血量百分比阈值列表（0~1）。
        /// 每个阈值只触发一次，按从高到低顺序依次检测，参考 EnemySpawnState 逻辑。
        /// </summary>
        [Export] public float[] UltimateHealthThresholds = new float[] { 0.5f };

        public string CurrentAttackName { get; private set; } = string.Empty;
        /// <summary>每次子攻击启动时自增，供动画控制器区分连续同类攻击的不同执行次。</summary>
        public int AttackRunId { get; private set; } = 0;

        private int _meleeCountSinceCharge;
        private float[] _sortedUltimateThresholds = Array.Empty<float>();
        private int _triggeredUltimateIndex;

        public override void Initialize(SampleEnemy enemy)
        {
            base.Initialize(enemy);
            _meleeCountSinceCharge = 0;
            _triggeredUltimateIndex = 0;
            RefreshThresholdCache();
            ConfigureNextAttack(forceCharge: false);
        }

        protected override void OnChildAttackStarted(EnemyAttackTemplate attack)
        {
            base.OnChildAttackStarted(attack);
            AttackRunId++;
            CurrentAttackName = attack.Name;

            if (IsAttack(attack.Name, UltimateAttackName))
            {
                // 终极技真正开始时才消耗触发器
                ConsumeUltimateTrigger();
                // 终极技结束后重置连招计数，恢复普通循环
                _meleeCountSinceCharge = 0;
                ConfigureNextAttack(forceCharge: false);
                return;
            }

            if (IsAttack(attack.Name, MeleeAttackName))
            {
                _meleeCountSinceCharge++;
                int threshold = Mathf.Max(1, MeleeCountBeforeCharge);
                ConfigureNextAttack(forceCharge: _meleeCountSinceCharge >= threshold);
                return;
            }

            if (IsAttack(attack.Name, Skill1AttackName))
            {
                _meleeCountSinceCharge = 0;
                ConfigureNextAttack(forceCharge: false);
            }
        }

        protected override void OnAttackFinished()
        {
            // 在 base 调用 QueueNextAttack 之前重新检查血量，
            // 捕获本次攻击期间发生的血量变化，确保及时触发终极技。
            bool forceCharge = _meleeCountSinceCharge >= Mathf.Max(1, MeleeCountBeforeCharge);
            ConfigureNextAttack(forceCharge);
            base.OnAttackFinished();
        }

        private void ConfigureNextAttack(bool forceCharge)
        {
            // 血量阈值优先级最高：满足条件时强制排队终极技（不消耗触发器，等 Ultimate 真正启动时再消耗）
            if (ShouldTriggerUltimate())
            {
                TrySetAttackWeight(UltimateAttackName, 1f);
                TrySetAttackWeight(Skill1AttackName, 0f);
                TrySetAttackWeight(MeleeAttackName, 0f);
                return;
            }

            // 普通循环：重置终极技权重后按近战/冲刺切换
            TrySetAttackWeight(UltimateAttackName, 0f);

            if (forceCharge)
            {
                TrySetAttackWeight(Skill1AttackName, 1f);
                TrySetAttackWeight(MeleeAttackName, 0f);
                return;
            }

            TrySetAttackWeight(Skill1AttackName, 0f);
            TrySetAttackWeight(MeleeAttackName, 1f);
        }

        private bool ShouldTriggerUltimate()
        {
            if (_sortedUltimateThresholds.Length == 0) return false;
            if (_triggeredUltimateIndex >= _sortedUltimateThresholds.Length) return false;
            if (Enemy == null || Enemy.MaxHealth <= 0) return false;

            float healthRatio = (float)Enemy.CurrentHealth / Enemy.MaxHealth;
            return healthRatio <= _sortedUltimateThresholds[_triggeredUltimateIndex];
        }

        private void ConsumeUltimateTrigger()
        {
            _triggeredUltimateIndex++;
        }

        private void RefreshThresholdCache()
        {
            var list = new List<float>();
            if (UltimateHealthThresholds != null)
            {
                foreach (float t in UltimateHealthThresholds)
                {
                    float clamped = Mathf.Clamp(t, 0.01f, 1.0f);
                    if (!list.Contains(clamped))
                        list.Add(clamped);
                }
            }
            // 降序排列，从最高阈值开始依次触发
            list.Sort((a, b) => b.CompareTo(a));
            _sortedUltimateThresholds = list.ToArray();
        }

        private static bool IsAttack(string attackName, string expectedName)
        {
            return attackName.Equals(expectedName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 终极激光束攻击进行中不允许玩家跳出检测区域而中断攻击。
        /// </summary>
        protected override bool ShouldInterruptOnPlayerExit()
        {
            return !IsAttack(CurrentAttackName, UltimateAttackName);
        }
    }
}


