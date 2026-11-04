using Godot;
using Kuros.Actors.Heroes;
using Kuros.Core;
using System.Collections.Generic;
using System;

namespace Kuros.Environments;

/// <summary>
/// 简单直线往返平台：在「起点（场景摆放位置）」与「起点 + TravelOffset」之间匀速往返。
/// 按 player / enemies / world_items 组筛选乘客，再验证身体碰撞或物件生命周期。
/// 平台先承载，角色状态机随后处理主动移动；重叠平台每帧只承载同一对象一次。
/// </summary>
[GlobalClass]
public partial class FallMovingPlatform : Node2D
{
    /// <summary>终点相对起点的局部偏移，0 表示原地；可做水平、垂直或斜向移动。</summary>
    [Export] public Vector2 TravelOffset { get; set; } = new(0, 0);
    /// <summary>移动速度（px/秒）。0 = 原地不动；无加减速，到达端点立刻掉头。</summary>
    [Export(PropertyHint.Range, "1,2000,1,or_greater")] public float Speed { get; set; } = 200;
    /// <summary>承载判定区：合格乘客的脚底必须位于本 FallSupportArea，且 SupportEnabled。</summary>
    [Export] public NodePath SupportPath { get; set; } = new("SupportArea");
    [ExportGroup("Passengers")]
    [Export] public bool CarryPlayers { get; set; } = true;
    [Export] public bool CarryEnemies { get; set; } = true;
    [Export] public bool CarryWorldItems { get; set; } = true;
    /// <summary>该组内对象始终不承载；身体形状全部禁用的角色也自动排除。</summary>
    [Export] public string ExcludedGroup { get; set; } = "platform_carry_excluded";
    /// <summary>家具位移扫掠阻挡层，默认 Environment + Enemy + Player。</summary>
    [Export(PropertyHint.Layers2DPhysics)] public uint FurnitureObstacleMask { get; set; } = 7;
    /// <summary>起点（_Ready 时的 Position）；每次往返都回到这里。</summary>
    private Vector2 _start;
    /// <summary>当前方向：true = 驶向 TravelOffset 终点；false = 驶回起点。</summary>
    private bool _outward = true;
    private FallSupportArea? _support;
    private readonly HashSet<IPlatformPassenger> _attached = new();
    public void RegisterAttachedPassenger(IPlatformPassenger passenger) => _attached.Add(passenger);
    public void UnregisterAttachedPassenger(IPlatformPassenger passenger) => _attached.Remove(passenger);
    /// <summary>平台本帧完成平移后的实际世界位移，供显式附着的地面效果使用。</summary>
    public event Action<Vector2>? WorldDisplaced;
    public FallSupportArea? SupportArea => _support;
    public bool CanAttachEffects => IsInsideTree() && !IsQueuedForDeletion() && CanProcess()
        && PlatformCarryPhysics.IsSupportEnabled(_support);

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

        var riders = new List<Node>();
        if (PlatformCarryPhysics.IsSupportEnabled(_support) && !displacement.IsZeroApprox())
        {
            var seen = new HashSet<ulong>();
            if (CarryPlayers) Collect("player", riders, seen);
            if (CarryEnemies) Collect("enemies", riders, seen);
            if (CarryWorldItems) Collect("world_items", riders, seen);
        }

        // 先移动平台，再把同样的位移施加给乘客（MoveAndCollide：撞墙会被挡，不会被硬推进墙里）。
        Vector2 previousWorldPosition = GlobalPosition;
        Position = next;
        foreach (var rider in riders)
        {
            if (!IsInstanceValid(rider) || rider.IsQueuedForDeletion()) continue;
            ApplyMotion(rider as IPlatformPassenger, rider as Node2D, displacement);
        }
        Vector2 actualDisplacement = GlobalPosition - previousWorldPosition;
        if (CanAttachEffects && !actualDisplacement.IsZeroApprox())
            foreach (var attached in new List<IPlatformPassenger>(_attached))
            {
                if (attached is GodotObject obj && !IsInstanceValid(obj)) { _attached.Remove(attached); continue; }
                ApplyMotion(attached, attached.PlatformTarget, actualDisplacement);
            }
        if (!actualDisplacement.IsZeroApprox()) WorldDisplaced?.Invoke(actualDisplacement);

        // 到端点后掉头（MoveToward 保证不会越过，所以近似相等即可）。
        if (Position.IsEqualApprox(target)) _outward = !_outward;
    }

    private void Collect(string group, List<Node> riders, HashSet<ulong> seen)
    {
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (!seen.Add(node.GetInstanceId()) || node.IsQueuedForDeletion() || !node.CanProcess()
                || (!string.IsNullOrEmpty(ExcludedGroup) && node.IsInGroup(ExcludedGroup))) continue;
            Vector2 foot;
            var sensor = GroundContactSensor.For(node);
            if (node is IPlatformPassenger passenger)
            {
                if (!passenger.CanRidePlatform) continue;
                var controller = sensor?.GetParent() as GroundObjectController;
                if (controller != null && (!controller.FollowPlatformEnabled || !controller.Grounded || controller.IsFalling)) continue;
                foot = passenger.PlatformGroundPosition;
            }
            else if (node is GameActor actor)
            {
                if (actor.IsDead || actor.IsDeathSequenceActive
                    || (actor is SamplePlayer player && player.IsFalling)
                    || !PlatformCarryPhysics.HasEnabledBodyShape(actor)) continue;
                foot = PlatformCarryPhysics.CharacterGroundPosition(actor);
            }
            else continue; // 不对未知 Node2D / 正在自由模拟的刚体强行修改坐标。

            bool supported = sensor != null ? sensor.SampleNow().Platform == this
                : PlatformCarryPhysics.IsOnPlatform(GetWorld2D(), foot, this);
            if (supported)
                riders.Add(node);
        }
    }

    private void ApplyMotion(IPlatformPassenger? passenger, Node2D? target, Vector2 motion)
    {
        if (!IsInstanceValid(target) || !target!.IsInsideTree() || target.IsQueuedForDeletion() || !target.CanProcess()
            || !string.IsNullOrEmpty(ExcludedGroup) && target.IsInGroup(ExcludedGroup)
            || target.IsInGroup("world_items") && !CarryWorldItems
            || target.IsInGroup("player") && !CarryPlayers || target.IsInGroup("enemies") && !CarryEnemies) return;
        if (passenger != null && !passenger.CanRidePlatform) return;
        if (!PlatformCarryPhysics.TryClaim(target)) return;
        if (passenger != null) passenger.ApplyPlatformDisplacement(motion, FurnitureObstacleMask);
        else if (target is CharacterBody2D character) character.MoveAndCollide(motion);
    }
}
