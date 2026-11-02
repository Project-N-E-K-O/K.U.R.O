using Godot;
using Kuros.Actors.Heroes;
using System.Collections.Generic;

namespace Kuros.Environments;

/// <summary>
/// 简单直线往返平台：在「起点（场景摆放位置）」与「起点 + TravelOffset」之间匀速往返。
/// 先由平台承载玩家移动（物理优先级更早），再由玩家状态机处理主动移动。
/// </summary>
[GlobalClass]
public partial class FallMovingPlatform : Node2D
{
    /// <summary>终点相对起点的偏移（不是绝对坐标）：默认向右 900px；可做垂直/斜向移动。</summary>
    [Export] public Vector2 TravelOffset { get; set; } = new(0, 0);
    /// <summary>移动速度（px/秒）。0 = 原地不动；无加减速，到达端点立刻掉头。</summary>
    [Export(PropertyHint.Range, "1,2000,1,or_greater")] public float Speed { get; set; } = 200;
    /// <summary>承载判定区：只有站在该 FallSupportArea 上（且 SupportEnabled）的玩家会被平台带着走。</summary>
    [Export] public NodePath SupportPath { get; set; } = new("SupportArea");
    /// <summary>起点（_Ready 时的 Position）；每次往返都回到这里。</summary>
    private Vector2 _start;
    /// <summary>当前方向：true = 驶向 TravelOffset 终点；false = 驶回起点。</summary>
    private bool _outward = true;
    private FallSupportArea? _support;

    public override void _Ready()
    {
        _start = Position;
        _support = GetNodeOrNull<FallSupportArea>(SupportPath);
        // 比玩家（默认优先级 0）更早执行：先移动平台并带动乘客，再让玩家处理自己的移动/碰撞。
        ProcessPhysicsPriority = -100;
    }

    public override void _PhysicsProcess(double delta)
    {
        // 两端点之间直线插值，MoveToward 保证恒速且不会越过目标。
        Vector2 target = _start + (_outward ? TravelOffset : Vector2.Zero);
        Vector2 next = Position.MoveToward(target, Mathf.Max(0, Speed) * (float)delta);

        // 本节点 Position 是父级局部坐标；乘客在世界坐标移动，需要把位移换算到世界空间
        // （父节点带缩放/旋转时直接加 (next - Position) 会错位）。
        Vector2 displacement = GetParent() is Node2D parent
            ? parent.GlobalTransform.BasisXform(next - Position) : next - Position;

        // 收集本帧站在平台上的玩家。用点查询而不是 Area 重叠：脚下 GroundProbe 命中
        // 本平台的 FallSupportArea 才算站稳（跳起/离台瞬间不再被带走）。
        var riders = new List<MainCharacter>();
        if (_support != null && _support.SupportEnabled)
        {
            foreach (Node node in GetTree().GetNodesInGroup("player"))
            {
                if (node is not MainCharacter player || player.IsFalling || player.IsDead || player.IsDeathSequenceActive || !player.CanProcess()) continue;
                Vector2 foot = player.GetNodeOrNull<Node2D>("GroundProbe")?.GlobalPosition ?? player.GlobalPosition;
                var hits = GetWorld2D().DirectSpaceState.IntersectPoint(new PhysicsPointQueryParameters2D
                { Position = foot, CollisionMask = FallSupportArea.Layer, CollideWithAreas = true, CollideWithBodies = false }, 128);
                foreach (var hit in hits)
                    if (hit["collider"].AsGodotObject() == _support) { riders.Add(player); break; }
            }
        }

        // 先移动平台，再把同样的位移施加给乘客（MoveAndCollide：撞墙会被挡，不会被硬推进墙里）。
        Position = next;
        foreach (var player in riders) player.MoveAndCollide(displacement);

        // 到端点后掉头（MoveToward 保证不会越过，所以近似相等即可）。
        if (Position.IsEqualApprox(target)) _outward = !_outward;
    }
}
