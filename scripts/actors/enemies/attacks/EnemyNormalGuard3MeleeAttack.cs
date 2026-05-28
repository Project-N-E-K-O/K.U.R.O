using Godot;
using Kuros.Fx;

namespace Kuros.Actors.Enemies.Attacks
{
    /// <summary>
    /// NormalGuard3 专用近战攻击。
    /// 继承自 EnemySimpleMeleeAttack，支持在单次 hit 帧中生成两个特效。
    /// 一次 hit → EffectScene + EffectOffset（位置 A）+ SecondEffectScene + SecondEffectOffset（位置 B）
    /// </summary>
    public partial class EnemyNormalGuard3MeleeAttack : EnemySimpleMeleeAttack
    {
        [ExportCategory("NormalGuard3 Second Hit Effect")]
        [Export] public PackedScene? SecondEffectScene = null;
        [Export] public Vector2 SecondEffectOffset = Vector2.Zero;

        protected override void OnAnimationHit()
        {
            // 第 1 个特效走基类逻辑（生成 EffectScene 于位置 A）
            base.OnAnimationHit();

            // 第 2 个特效生成于位置 B
            if (SecondEffectScene != null)
            {
                SpawnEffectAt(SecondEffectScene, SecondEffectOffset);
            }
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
                    else if (node2D is EffectAutoDestroy effectAutoDestroy)
                        effectAutoDestroy.FacingRight = Enemy.FacingRight;

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
                GD.PushWarning($"[NormalGuard3MeleeAttack] 无法生成第二个特效: {ex.Message}");
            }
        }
    }
}
