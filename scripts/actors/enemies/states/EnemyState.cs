using Godot;
using Kuros.Core.Effects;
using Kuros.Systems.FSM;

namespace Kuros.Actors.Enemies.States
{
    public partial class EnemyState : State
    {
        protected SampleEnemy Enemy => (SampleEnemy)Actor;
        protected SamplePlayer? Player => Enemy.PlayerTarget;
        
        protected bool HasPlayer => Player != null;

        /// <summary>在世界层（敌人父级下）生成一次性特效：定位到敌人当前位置 + <paramref name="offset"/>、
        /// 按朝向镜像。偏移按**未镜像（朝右）姿态**摆，朝左自动翻 X——与锚点偏移的习惯一致。
        /// 朝向处理与攻击生成路径同款：**实现了 <see cref="Kuros.Fx.IFacingDirectional"/> 的特效注入朝向**
        /// （AddChild 之前写入，_Ready 里才用得上——特效自己决定怎么镜像）；其余特效由这里外部翻 X
        /// （乘号保留特效自带的缩放量级）。挂父级而不是敌人子树——敌人随后的销毁（FinalizeDeath）
        /// 不会把特效一起带走。</summary>
        protected void SpawnEffectAtSelf(PackedScene? scene, Vector2 offset)
        {
            if (scene == null) return;
            var effect = scene.Instantiate<Node2D>();
            if (effect is Kuros.Fx.IFacingDirectional facing)
                facing.FacingRight = Enemy.FacingRight;
            else if (!Enemy.FacingRight)
                effect.Scale = new Vector2(-effect.Scale.X, effect.Scale.Y);
            Enemy.GetParent()?.AddChild(effect);
            Vector2 mirrored = Enemy.FacingRight ? offset : new Vector2(-offset.X, offset.Y);
            effect.GlobalPosition = Enemy.GlobalPosition + mirrored;
        }

        /// <summary>多锚点轮换下标（<see cref="SpawnEffectEntry"/> 用；语义同攻击模板：每次生成取下一个锚点，
        /// 取模轮换）。由消费状态在合适的时点清零（如 Dying.Enter）——一次"阶段"内多次生成依次取不同锚点。</summary>
        protected int _spawnMarkerIndex;

        /// <summary>按 <see cref="EffectSpawnConfig"/> 生成一次性特效（状态侧专用，语义与攻击生成对齐）：
        /// PropertyOverrides / 锚点 <c>SpawnMarkerPaths</c>（**先按本状态节点解析、再按敌人根兜底**——与攻击模板
        /// 的"模板节点优先"同款，两种路径写法都吃；含骨骼 marker；**多锚点按顺序轮换**）/
        /// <c>EffectOffset</c> 与偏移镜像（OffsetFlip，默认随朝向翻；**骨骼自镜像锚点——如 SpineBoneAnchor——
        /// 把 OffsetFlip 设为 NoFlip，否则位置会被翻两次落回未镜像的一侧**）/ 条目翻转（FlipMode）/
        /// <see cref="Kuros.Fx.IFacingDirectional"/> 朝向注入。挂敌人父级（世界层）。</summary>
        protected void SpawnEffectEntry(EffectSpawnConfig? entry)
        {
            if (entry?.Scene == null) return;
            var effect = entry.InstantiateNode();
            if (effect == null) return;

            if (effect is not Node2D node2D)
            {
                Enemy.GetParent()?.AddChild(effect);
                return;
            }

            if (node2D is Kuros.Fx.IFacingDirectional facing)
                facing.FacingRight = Enemy.FacingRight;

            bool offsetFlip = entry.ResolveOffsetFlip(true);

            Vector2 basePos = Enemy.GlobalPosition;
            var markers = entry.ResolveMarkers(this, Enemy, System.Array.Empty<Marker2D>());
            if (markers.Length > 0)
            {
                int index = entry.PickMarkerIndex(markers, _spawnMarkerIndex);
                var marker = index >= 0 ? markers[index] : null;
                _spawnMarkerIndex++;
                if (marker != null && GodotObject.IsInstanceValid(marker))
                {
                    Vector2 rel = marker.GlobalPosition - Enemy.GlobalPosition;
                    if (!Enemy.FacingRight && offsetFlip) rel.X = -rel.X;
                    basePos += rel;
                }
            }

            Vector2 offset = entry.ResolveOffset(Vector2.Zero);
            if (!Enemy.FacingRight && offsetFlip) offset.X = -offset.X;

            if (entry.ResolveFlip(false) && !Enemy.FacingRight)
                node2D.Scale = new Vector2(-node2D.Scale.X, node2D.Scale.Y);

            Enemy.GetParent()?.AddChild(node2D);
            node2D.GlobalPosition = basePos + offset;
        }
    }
}

