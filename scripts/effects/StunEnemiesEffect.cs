using Godot;
using System;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 眩晕/冻结玩家前方范围内的敌人。
    /// </summary>
    [GlobalClass]
    public partial class StunEnemiesEffect : ActorEffect
    {
        [Export(PropertyHint.Range, "0.1,10,0.1")] public float StunDuration = 2f;
        [Export(PropertyHint.Range, "10,1000,10")] public float Radius = 200f;
        [Export(PropertyHint.Range, "30,360,5")] public float ArcDegrees = 180f;
        [Export] public string TargetGroup = "enemies";
        [Export] public bool RequiresFacingCheck = true;

        protected override void OnApply()
        {
            base.OnApply();
            TryStunTargets();
            Controller?.RemoveEffect(this);
        }

        private void TryStunTargets()
        {
            if (Actor == null) return;
            var tree = Actor.GetTree();
            if (tree == null) return;

            var nodes = tree.GetNodesInGroup(TargetGroup);
            if (nodes == null || nodes.Count == 0) return;

            foreach (var node in nodes)
            {
                if (node is not GameActor enemy) continue;
                if (enemy == Actor) continue;

                Vector2 toEnemy = enemy.GlobalPosition - Actor.GlobalPosition;
                if (toEnemy.Length() > Radius) continue;

                if (RequiresFacingCheck && !IsWithinArc(toEnemy))
                {
                    continue;
                }

                var freeze = new FreezeEffect
                {
                    Duration = StunDuration,
                    EffectId = $"weapon_stun_{Actor.GetInstanceId()}_{enemy.GetInstanceId()}_{Time.GetUnixTimeFromSystem()}"
                };
                enemy.ApplyEffect(freeze);
            }
        }

        private bool IsWithinArc(Vector2 toEnemy)
        {
            if (Actor == null) return true;
            var forward = Actor.FacingRight ? Vector2.Right : Vector2.Left;
            var dir = toEnemy.Normalized();
            float dot = Mathf.Clamp(forward.Dot(dir), -1f, 1f);
            float angle = Mathf.RadToDeg(Mathf.Acos(dot));
            return angle <= ArcDegrees * 0.5f;
        }
    }
}


