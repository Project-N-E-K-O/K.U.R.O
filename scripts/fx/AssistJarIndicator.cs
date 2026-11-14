using Godot;

namespace Kuros.Fx;

/// <summary>绘制协助罐的屏幕边缘指引。父节点是与世界相机独立的 CanvasLayer。</summary>
public partial class AssistJarIndicator : Node2D
{
    public Node2D? Target { get; set; }
    public Texture2D? Icon { get; set; }
    private Vector2 _direction;

    public override void _Ready() => Hide();

    public override void _Process(double delta)
    {
        if (Target == null || !IsInstanceValid(Target) || !Target.IsInsideTree() || Target.IsQueuedForDeletion())
        {
            Hide();
            return;
        }

        Rect2 screen = GetViewportRect();
        Vector2 point = Target.GetGlobalTransformWithCanvas().Origin;
        if (screen.HasPoint(point) || screen.Size.X <= 96f || screen.Size.Y <= 96f)
        {
            Hide();
            return;
        }

        Vector2 ray = point - screen.GetCenter();
        Vector2 halfSafeSize = screen.Size * 0.5f - new Vector2(48f, 48f);
        float scale = 1f / Mathf.Max(Mathf.Abs(ray.X) / halfSafeSize.X, Mathf.Abs(ray.Y) / halfSafeSize.Y);
        Position = screen.GetCenter() + ray * scale;
        _direction = ray.Normalized();
        Show();
        QueueRedraw();
    }

    public override void _Draw()
    {
        var green = new Color(0.68f, 0.93f, 0.3f);
        DrawCircle(Vector2.Zero, 22f, new Color(0.07f, 0.1f, 0.08f, 0.94f));
        DrawArc(Vector2.Zero, 21f, 0f, Mathf.Tau, 24, green, 2f, true);
        if (Icon != null)
        {
            Vector2 size = Icon.GetSize();
            float longestSide = Mathf.Max(size.X, size.Y);
            if (longestSide > 0f)
            {
                size *= 28f / longestSide;
                DrawTextureRect(Icon, new Rect2(-size * 0.5f, size), false);
            }
        }

        Vector2 side = new(-_direction.Y, _direction.X);
        DrawColoredPolygon(new[]
        {
            _direction * 35f,
            _direction * 24f + side * 7f,
            _direction * 24f - side * 7f
        }, green);
    }
}
