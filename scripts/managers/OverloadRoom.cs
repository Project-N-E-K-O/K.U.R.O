using Godot;
using Kuros.Actors.Enemies.Attacks;

namespace Kuros.Managers;

public partial class OverloadRoom : Node2D
{
    public const string GroupName = "overload_room";
    private EnemyF1RogueAIUltimateAttack? _owner;

    public bool ContainsPoint(Vector2 worldPoint, float doorwayClearance = 0f)
    {
        if (GetNodeOrNull<CollisionShape2D>("../AreaSize/CollisionShape2D") is not { Shape: RectangleShape2D shape } bounds)
            return false;
        var local = bounds.ToLocal(worldPoint);
        var half = shape.Size / 2f;
        return Mathf.Abs(local.X) < half.X - doorwayClearance && Mathf.Abs(local.Y) < half.Y;
    }

    public bool CanLock(Node2D player)
    {
        // 挡板沿房间拼接线摆放，左右各保留足够容纳玩家身体的余量。
        return _owner == null && ContainsPoint(player.GlobalPosition, 220f);
    }

    public bool TryLock(EnemyF1RogueAIUltimateAttack owner, Node2D player)
    {
        if (!CanLock(player)) return false;
        _owner = owner;
        SetGatesClosed(true);
        return true;
    }

    public bool IsLockedBy(EnemyF1RogueAIUltimateAttack owner) => _owner == owner;

    public void Release(EnemyF1RogueAIUltimateAttack owner)
    {
        if (_owner != owner) return;
        _owner = null;
        SetGatesClosed(false);
    }

    private void SetGatesClosed(bool closed)
    {
        foreach (var child in GetChildren())
            if (child is BattleExitGate gate) gate.SetClosed(closed);
    }
}
