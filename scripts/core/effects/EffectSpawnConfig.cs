using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Core.Effects
{
    /// <summary>攻击与状态共用的生成参数，不包含触发时机或攻击阶段。</summary>
    [GlobalClass]
    public partial class EffectSpawnConfig : Resource
    {
        [Export] public PackedScene? Scene { get; set; }

        // ── 每特效独立生成配置（默认"未配置"哨兵——解析时回退默认常量，模板级字段已随 entry 迁移移除）──

        /// <summary>独立生成位置偏移。(NaN, NaN) = 未配置（回退 Vector2.Zero）。</summary>
        [Export] public Vector2 EffectOffset = new(float.NaN, float.NaN);

        /// <summary>独立生成锚点路径数组（NodePath 相对生成方节点解析，轮换；如 "../../../../Node2D/Marker2D"）。
        /// 空数组 = 未配置（回退无锚点，用敌人原点）。Resource 不能 export Node 成员（GD0107），故用 NodePath 存储。</summary>
        [Export] public NodePath[] SpawnMarkerPaths = System.Array.Empty<NodePath>();

        /// <summary>多锚点选择：Sequence = 依次轮换（默认，历史行为）；Random = 每次生成在解析成功的锚点中随机。
        /// 攻击与状态生成路径均支持该模式。</summary>
        [Export] public SpawnMarkerSelectMode SpawnMarkerSelect = SpawnMarkerSelectMode.Sequence;

        /// <summary>独立朝向翻转。Inherit = 未配置（回退不翻转）。</summary>
        [Export] public FacingFlipMode FlipMode = FacingFlipMode.Inherit;

        /// <summary>独立"锚点偏移随朝向翻 X"开关。Inherit = 未配置（回退模板行为：非跟随类特效会把锚点偏移按朝向翻 X，
        /// 适配"锚点按未镜像姿态手工摆放"的常规用法）；<b>NoFlip</b> = 锚点自己已经带朝向
        /// （如挂在 SpineBoneNode 下、由 SpineBoneAnchor 按骨骼矩阵镜像的炮口 marker），模板不要再翻一次；
        /// Flip = 强制翻。</summary>
        [Export] public FacingFlipMode OffsetFlip = FacingFlipMode.Inherit;

        public Vector2 ResolveOffset(Vector2 fallback)
            => float.IsNaN(EffectOffset.X) ? fallback : EffectOffset;

        /// <summary>
        /// 解析独立锚点：先按 relativeTo（攻击模板自身/敌人根）解析，失败再按 enemyRoot（敌人根）兜底——
        /// 兼容"相对模板节点的 ../ 路径"与"相对敌人根的路径"两种配置习惯。未配置返回 fallback（模板锚点）。
        /// </summary>
        public Marker2D[] ResolveMarkers(Node relativeTo, Node? enemyRoot, Marker2D[] fallback)
        {
            if (SpawnMarkerPaths.Length == 0 || relativeTo == null) return fallback;
            var markers = new Marker2D[SpawnMarkerPaths.Length];
            for (int i = 0; i < SpawnMarkerPaths.Length; i++)
            {
                var path = SpawnMarkerPaths[i];
                markers[i] = relativeTo.GetNodeOrNull<Marker2D>(path)
                    ?? enemyRoot?.GetNodeOrNull<Marker2D>(path);
                // 解析失败原本静默回退敌人原点，路径层级写错时无从查起——警告里列出实际检查过的节点全路径
                if (markers[i] == null)
                    GD.PushWarning($"[EffectSpawnConfig] 锚点路径 \"{path}\" 解析失败：已检查相对节点 {DescribeNode(relativeTo)} 与兜底节点 {DescribeNode(enemyRoot)}，均未找到 Marker2D，特效将生成于拥有者原点。");
            }
            return markers;
        }

        private static string DescribeNode(Node? node)
            => node != null && GodotObject.IsInstanceValid(node)
                ? $"\"{node.GetPath()}\""
                : "<无效节点>";

        /// <summary>选出本次生成使用的锚点下标：Sequence = 传入的轮换下标取模（与历史行为逐字一致）；
        /// Random = 仅在解析成功的锚点里随机（全失败返回 -1，调用方回退敌人原点）。</summary>
        public int PickMarkerIndex(Marker2D[] markers, int cycleIndex)
        {
            if (markers.Length == 0) return -1;
            if (SpawnMarkerSelect != SpawnMarkerSelectMode.Random)
                return cycleIndex % markers.Length;

            int valid = 0;
            for (int i = 0; i < markers.Length; i++)
                if (IsValidMarker(markers[i])) valid++;
            if (valid == 0) return -1;

            int pick = (int)(GD.Randi() % (uint)valid);
            for (int i = 0; i < markers.Length; i++)
            {
                if (!IsValidMarker(markers[i])) continue;
                if (pick-- == 0) return i;
            }
            return -1;
        }

        private static bool IsValidMarker(Marker2D? marker)
            => marker != null && GodotObject.IsInstanceValid(marker);

        public bool ResolveFlip(bool fallback)
            => FlipMode == FacingFlipMode.Inherit ? fallback : FlipMode == FacingFlipMode.Flip;

        /// <summary>解析"锚点偏移是否随朝向翻 X"：Inherit 用模板行为（true），其余按配置。</summary>
        public bool ResolveOffsetFlip(bool fallback)
            => OffsetFlip == FacingFlipMode.Inherit ? fallback : OffsetFlip == FacingFlipMode.Flip;

        /// <summary>通用实例化入口：所有属性覆盖在入树前应用。</summary>
        public Node? InstantiateNode()
        {
            if (Scene == null) return null;
            var effect = Scene.Instantiate();
            ApplyOverrides(effect);
            return effect;
        }

        public ActorEffect? InstantiateEffect()
        {
            if (Scene == null) return null;
            var effect = Scene.Instantiate<ActorEffect>();
            if (effect != null) ApplyOverrides(effect);
            return effect;
        }

        public void ApplyOverrides(Node effect)
        {
            if (effect == null || PropertyOverrides.Count == 0) return;
            foreach (var pair in PropertyOverrides)
            {
                if (pair.Key == null) continue;
                try { effect.Set(pair.Key, pair.Value); }
                catch (System.Exception ex) { GD.PushWarning($"[EffectSpawnConfig] override '{pair.Key}' failed: {ex.Message}"); }
            }
        }

        [Export] public Godot.Collections.Dictionary<string, Variant> PropertyOverrides { get; set; } = new();
    }
}
