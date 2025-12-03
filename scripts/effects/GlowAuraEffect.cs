using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 在角色周围生成一个可配置的光源，用于照亮环境。
    /// </summary>
    [GlobalClass]
    public partial class GlowAuraEffect : ActorEffect
    {
        [Export] public Color LightColor { get; set; } = new Color(1f, 0.95f, 0.8f, 1f);
        [Export(PropertyHint.Range, "0,10,0.1")] public float Energy { get; set; } = 1.5f;
        [Export(PropertyHint.Range, "0.1,4,0.1")] public float TextureScale { get; set; } = 1.5f;

        private PointLight2D? _lightNode;

        protected override void OnApply()
        {
            base.OnApply();
            if (Actor == null || _lightNode != null) return;

            _lightNode = new PointLight2D
            {
                Name = "GlowAuraLight",
                Energy = Energy,
                Color = LightColor,
                TextureScale = TextureScale,
                ShadowEnabled = false
            };

            Actor.AddChild(_lightNode);
        }

        public override void OnRemoved()
        {
            if (_lightNode != null && GodotObject.IsInstanceValid(_lightNode))
            {
                _lightNode.QueueFree();
                _lightNode = null;
            }
            base.OnRemoved();
        }
    }
}


