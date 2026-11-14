using Godot;

namespace Kuros.Managers;

public partial class BattleExitGate : StaticBody2D
{
    public const uint PlayerBarrierLayer = 1u << 20;
    private Tween? _flashTween;

    public void SetClosed(bool closed)
    {
        CollisionLayer = closed ? PlayerBarrierLayer : 0u;
        var sprite = GetNodeOrNull<Sprite2D>("GateSprite");
        if (sprite == null) return;

        _flashTween?.Kill();
        _flashTween = null;
        sprite.Visible = closed;
        var color = sprite.SelfModulate;
        color.A = 1f;
        sprite.SelfModulate = color;
        if (!closed) return;

        _flashTween = CreateTween().SetLoops().SetIgnoreTimeScale(true);
        _flashTween.TweenProperty(sprite, "self_modulate:a", 0.25f, 0.25f);
        _flashTween.TweenProperty(sprite, "self_modulate:a", 1f, 0.25f);
    }
}
