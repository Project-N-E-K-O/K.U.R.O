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

            float resolvedScale = TextureScale;
            if (Range > 0f)
            {
                const float maxRange = 512f;
                const float minScale = 0.1f;
                const float maxScale = 4f;
                float normalized = Mathf.Clamp(Range / maxRange, 0f, 1f);
                resolvedScale = minScale + normalized * (maxScale - minScale);
                resolvedScale = Mathf.Clamp(resolvedScale, minScale, maxScale);
            }

            _lightNode = new PointLight2D
            {
                Name = "GlowAuraLight",
                Energy = Energy,
                Color = LightColor,
                TextureScale = resolvedScale,
                Texture = ResolveLightTexture(),
                ShadowEnabled = false
            };
            _lightNode.Set("range", Range);

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

            var gradient = new Gradient();
            gradient.InterpolationMode = Gradient.InterpolationModeEnum.Cubic;
            gradient.AddPoint(0f, new Color(LightColor, 0.8f));
            gradient.AddPoint(0.5f, new Color(LightColor, 0.4f));
            gradient.AddPoint(1f, new Color(LightColor, 0f));

            var texture = new GradientTexture2D
            {
                Gradient = gradient,
                Width = 256,
                Height = 256
            };
            texture.Set("fill", 1); // 1 == Radial
            return texture;
        }
    }
}


