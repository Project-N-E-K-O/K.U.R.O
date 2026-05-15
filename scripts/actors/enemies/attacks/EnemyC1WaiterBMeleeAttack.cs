using Godot;
using Kuros.Fx;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// WaiterB 专用近战攻击。
    /// 继承自 EnemySimpleMeleeAttack，支持两段 hit 帧分别在不同位置生成特效。
    /// 第 1 次 hit → EffectScene + EffectOffset（位置 A）
    /// 第 2 次 hit → SecondEffectScene + SecondEffectOffset（位置 B）
    /// </summary>
    public partial class EnemyC1WaiterBMeleeAttack : EnemySimpleMeleeAttack
    {
        [ExportCategory("WaiterB Second Hit Effect")]
        [Export] public PackedScene? SecondEffectScene = null;
        [Export] public Vector2 SecondEffectOffset = Vector2.Zero;

        private int _hitCount;

        protected override void OnAttackStarted()
        {
            base.OnAttackStarted();
            _hitCount = 0;
        }

        protected override void OnAnimationHit()
        {
            _hitCount++;

            if (_hitCount >= 2 && SecondEffectScene != null)
            {
                SpawnEffectAt(SecondEffectScene, SecondEffectOffset);
                PerformAttackNow();
                ApplyWaiterBKnockback();
                return;
            }

            // 第 1 次 hit 走基类逻辑（生成 EffectScene 于位置 A，执行伤害+击退）
            base.OnAnimationHit();
        }

        private void SpawnEffectAt(PackedScene scene, Vector2 offset)
        {
            if (Enemy == null) return;

            try
            {
                var effect = scene.Instantiate();

                Vector2 adjustedOffset = offset;
                if (!Enemy.FacingRight && offset.X != 0)
                    adjustedOffset.X = -offset.X;

                Vector2 spawnPos = Enemy.GlobalPosition + adjustedOffset;

                if (effect is Node2D node2D)
                {
                    if (node2D is LaserBeam laserBeam)
                        laserBeam.FacingRight = Enemy.FacingRight;

                    Enemy.GetParent()?.AddChild(node2D);
                    node2D.GlobalPosition = spawnPos;
                }
                else
                {
                    effect?.QueueFree();
                }
            }
            catch (System.Exception ex)
            {
                GD.PushWarning($"[WaiterBMeleeAttack] 无法生成第二段特效: {ex.Message}");
            }
        }

        private void ApplyWaiterBKnockback()
        {
            if (Enemy == null || Player == null) return;

            float distance = Mathf.Max(0f, KnockbackDistance);
            if (distance <= 0f && KnockbackSpeed <= 0f) return;

            float duration = Mathf.Max(KnockbackDuration, 0.01f);
            TryApplyPlayerKnockback(
                Player,
                distance,
                duration,
                KnockbackSpeed,
                Enemy.FacingRight ? Vector2.Right : Vector2.Left);
        }
    }
}
