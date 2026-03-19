using Godot;
using Kuros.Items.World;

/// <summary>
/// 挂在场景中任意节点上，运行时按 F9 打印场景内所有掉落物的详细信息。
/// 调试完成后可直接删除此节点。
/// </summary>
public partial class DropItemDebugger : Node
{
    [Export] public Key TriggerKey { get; set; } = Key.F9;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == TriggerKey)
        {
            DumpAllDropItems();
        }
    }

    private void DumpAllDropItems()
    {
        GD.Print("========== [DropItemDebugger] 开始扫描场景内所有掉落物 ==========");

        int total = 0;

        // 扫描 WorldItemEntity
        foreach (var node in GetTree().GetNodesInGroup("world_items"))
        {
            if (node is WorldItemEntity w)
            {
                PrintWorldItemEntity(w);
                total++;
            }
        }

        // 扫描 RigidBodyWorldItemEntity
        foreach (var node in GetTree().GetNodesInGroup("world_items"))
        {
            if (node is RigidBodyWorldItemEntity r)
            {
                PrintRigidBodyEntity(r);
                total++;
            }
        }

        // 如果 world_items 组是空的，用场景树全局搜索兜底
        if (total == 0)
        {
            GD.Print("[DropItemDebugger] 'world_items' 组为空，尝试全局搜索...");
            ScanEntireTree(GetTree().Root, ref total);
        }

        if (total == 0)
        {
            GD.Print("[DropItemDebugger] 场景中未找到任何掉落物节点。");
        }

        GD.Print($"========== [DropItemDebugger] 扫描完毕，共找到 {total} 个掉落物 ==========");
    }

    private static void ScanEntireTree(Node node, ref int total)
    {
        if (node is WorldItemEntity w)
        {
            PrintWorldItemEntity(w);
            total++;
        }
        else if (node is RigidBodyWorldItemEntity r)
        {
            PrintRigidBodyEntity(r);
            total++;
        }

        foreach (Node child in node.GetChildren())
        {
            ScanEntireTree(child, ref total);
        }
    }

    private static void PrintWorldItemEntity(WorldItemEntity w)
    {
        GD.Print($"\n--- WorldItemEntity: {w.Name} ---");
        GD.Print($"  路径        : {w.GetPath()}");
        GD.Print($"  ItemId      : {w.ItemId}");
        GD.Print($"  Quantity    : {w.Quantity}");
        GD.Print($"  位置        : {w.GlobalPosition}");
        GD.Print($"  组          : {string.Join(", ", w.GetGroups())}");

        // Body 碰撞层（CharacterBody2D 自身）
        GD.Print($"  Body Layer  : {w.CollisionLayer}  ({LayerBits(w.CollisionLayer)})");
        GD.Print($"  Body Mask   : {w.CollisionMask}   ({LayerBits(w.CollisionMask)})");

        // TriggerArea
        if (w.TriggerArea != null)
        {
            GD.Print($"  TriggerArea 路径       : {w.TriggerArea.GetPath()}");
            GD.Print($"  TriggerArea Monitoring : {w.TriggerArea.Monitoring}");
            GD.Print($"  TriggerArea Monitorable: {w.TriggerArea.Monitorable}");
            GD.Print($"  TriggerArea Layer      : {w.TriggerArea.CollisionLayer}  ({LayerBits(w.TriggerArea.CollisionLayer)})");
            GD.Print($"  TriggerArea Mask       : {w.TriggerArea.CollisionMask}   ({LayerBits(w.TriggerArea.CollisionMask)})");
            GD.Print($"  TriggerArea 重叠Body数 : {w.TriggerArea.GetOverlappingBodies().Count}");
            GD.Print($"  TriggerArea 重叠Area数 : {w.TriggerArea.GetOverlappingAreas().Count}");
        }
        else
        {
            GD.Print("  TriggerArea : NULL !");
        }
    }

    private static void PrintRigidBodyEntity(RigidBodyWorldItemEntity r)
    {
        GD.Print($"\n--- RigidBodyWorldItemEntity: {r.Name} ---");
        GD.Print($"  路径        : {r.GetPath()}");
        GD.Print($"  位置        : {r.GlobalPosition}");
        GD.Print($"  组          : {string.Join(", ", r.GetGroups())}");

        // 找 GrabArea（按名称搜索子节点）
        var grabArea = r.GetNodeOrNull<Area2D>("GrabArea") 
                    ?? r.FindChild("GrabArea", recursive: true, owned: false) as Area2D;

        if (grabArea != null)
        {
            GD.Print($"  GrabArea 路径       : {grabArea.GetPath()}");
            GD.Print($"  GrabArea Monitoring : {grabArea.Monitoring}");
            GD.Print($"  GrabArea Monitorable: {grabArea.Monitorable}");
            GD.Print($"  GrabArea Layer      : {grabArea.CollisionLayer}  ({LayerBits(grabArea.CollisionLayer)})");
            GD.Print($"  GrabArea Mask       : {grabArea.CollisionMask}   ({LayerBits(grabArea.CollisionMask)})");
            GD.Print($"  GrabArea 重叠Body数 : {grabArea.GetOverlappingBodies().Count}");
            GD.Print($"  GrabArea 重叠Area数 : {grabArea.GetOverlappingAreas().Count}");
        }
        else
        {
            GD.Print("  GrabArea : 未找到");
        }
    }

    /// <summary>
    /// 把 uint 碰撞层转成可读的 "L1 L3 L5" 格式
    /// </summary>
    private static string LayerBits(uint mask)
    {
        if (mask == 0) return "（无）";
        var bits = new System.Text.StringBuilder();
        for (int i = 0; i < 32; i++)
        {
            if ((mask & (1u << i)) != 0)
            {
                if (bits.Length > 0) bits.Append(' ');
                bits.Append($"L{i + 1}");
            }
        }
        return bits.ToString();
    }
}