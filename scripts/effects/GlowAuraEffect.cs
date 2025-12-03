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
        [Export(PropertyHint.Range, "0,512,1")] public float Range { get; set; } = 128f;
        [Export] public Texture2D? LightTexture { get; set; }

        private PointLight2D? _lightNode;

        protected override void OnApply()
        {
            base.OnApply();
            if (Actor == null)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            if (_lightNode != null)
            {
                return;
            }

            _lightNode = new PointLight2D
            {
                Name = "GlowAuraLight",
                Energy = Energy,
                Color = LightColor,
                TextureScale = TextureScale,
                Range = Range,
                Texture = ResolveLightTexture(),
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

        private Texture2D ResolveLightTexture()
        {
            if (LightTexture != null)
            {
                return LightTexture;
            }

            var gradient = new Gradient
            {
                InterpolationMode = Gradient.InterpolationMode.Cubic
            };
            gradient.AddPoint(0f, new Color(LightColor, 0.8f));
            gradient.AddPoint(0.5f, new Color(LightColor, 0.4f));
            gradient.AddPoint(1f, new Color(LightColor, 0f));

            var texture = new GradientTexture2D
            {
                Gradient = gradient,
                Width = 256,
                Height = 256,
                Fill = GradientTexture2D.FillMode.Radial
            };
            return texture;
        }
    }
}


