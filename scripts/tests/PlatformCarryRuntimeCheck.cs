using Godot;
using System;
using System.Threading.Tasks;
using Kuros.Core;
using Kuros.Actors.Heroes;
using Kuros.Environments;
using Kuros.Items.World;

namespace Kuros.Tests;

/// <summary>独立运行场景验证组筛选、禁用碰撞、真实家具和重叠平台。</summary>
public partial class PlatformCarryRuntimeCheck : Node2D
{
    private int _passed;
    private void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException(name);
        _passed++; GD.Print($"[CarryCheck] PASS {name}");
    }
    private async Task Frames(int count = 4)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private GameActor Actor(string name, Vector2 position, bool disabled = false)
    {
        var actor = new GameActor { Name = name, Position = position, CollisionLayer = 2, CollisionMask = 0 };
        actor.AddToGroup("enemies");
        actor.AddChild(new CollisionShape2D { Name = "CollisionShape2D", Shape = new CircleShape2D { Radius = 10 }, Disabled = disabled });
        // 禁用身体的敌人仍有攻击形状，不能被它错误放行。
        var attack = new Area2D { Name = "AttackArea", CollisionLayer = 0, CollisionMask = 0 };
        attack.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 15 } });
        actor.AddChild(attack); AddChild(actor); return actor;
    }
    private FallMovingPlatform Platform()
    {
        var platform = GD.Load<PackedScene>("res://scenes/environments/FallMovingPlatform.tscn").Instantiate<FallMovingPlatform>();
        platform.TravelOffset = new Vector2(5000, 0); platform.Speed = 120;
        platform.GetNode<CollisionShape2D>("SupportArea/CollisionShape2D").Shape = new RectangleShape2D { Size = new Vector2(1800, 1200) };
        AddChild(platform); return platform;
    }
    public override async void _Ready()
    {
        try
        {
            var platform = Platform();
            var enemy = Actor("Enemy", new Vector2(-400, -300));
            var disabled = Actor("DisabledBoss", new Vector2(-200, -300), true);
            var excluded = Actor("Excluded", new Vector2(0, -300));
            excluded.AddToGroup("platform_carry_excluded");
            var player = GD.Load<PackedScene>("res://scenes/actors/characters/main_character.tscn").Instantiate<MainCharacter>();
            player.Position = new Vector2(400, -300); AddChild(player);
            var furniture = GD.Load<PackedScene>("res://scenes/items/Furniture_D_chairOfficeC.tscn").Instantiate<RigidBodyWorldItemEntity>();
            furniture.Position = new Vector2(-400, 150); AddChild(furniture);
            var rigid = furniture.GetNode<RigidBody2D>("RigidBody2D");
            var shape = furniture.GetNode<CollisionShape2D>("RigidBody2D/StaticBody2D/CollisionShape2D");
            var weapon = GD.Load<PackedScene>("res://scenes/weapons/Weapon_Brawl_discoBall.tscn").Instantiate<WorldItemEntity>();
            weapon.Position = new Vector2(200, 150); AddChild(weapon);
            await Frames();
            Check(enemy.Position.X > -400, "enemies 组的启用身体对象随平台移动");
            Check(player.Position.X > 400, "原有 MainCharacter 玩家承载仍然有效");
            Check(disabled.Position.X == -200, "身体禁用但 AttackArea 启用的对象不承载");
            Check(excluded.Position.X == 0, "排除组对象不承载");
            Check(furniture.IsInGroup("world_items") && furniture.CanRidePlatform, "真实家具按 world_items 组与地面生命周期承载");
            Check(furniture.PlatformGroundPosition.DistanceTo(shape.GlobalPosition) < 0.1f, "家具使用实际 StaticBody 形状位置作为脚底");
            Check(furniture.Position.X > -400, "没有刚体直接身体形状的家具也能移动");
            Check(weapon.IsInGroup("world_items") && weapon.CanRidePlatform, "迪斯科球地面武器接入 world_items 承载");
            Check(!PlatformCarryPhysics.HasEnabledBodyShape(weapon) && weapon.Position.X > 200,
                "无身体碰撞形状的 WorldItemEntity 仍随平台移动");
            Vector2 weaponRoot = weapon.GlobalPosition;
            Vector2 weaponArea = weapon.GetNode<Node2D>("CollisionArea").GlobalPosition;
            await Frames();
            Check((weapon.GlobalPosition - weaponRoot).DistanceTo(
                weapon.GetNode<Node2D>("CollisionArea").GlobalPosition - weaponArea) < 0.1f,
                "地面武器与拾取区域同步移动");
            weapon.PlatformCarryEnabled = false;
            weaponRoot = weapon.GlobalPosition;
            await Frames();
            Check(weapon.GlobalPosition.DistanceTo(weaponRoot) < 0.1f, "单件地面武器可关闭承载");
            weapon.PlatformCarryEnabled = true;
            Vector2 rootBefore = furniture.GlobalPosition, rigidBefore = rigid.GlobalPosition, shapeBefore = shape.GlobalPosition;
            await Frames();
            Vector2 motion = furniture.GlobalPosition - rootBefore;
            Check(motion.Length() > 0 && (rigid.GlobalPosition - rigidBefore).DistanceTo(motion) < 0.1f
                && (shape.GlobalPosition - shapeBefore).DistanceTo(motion) < 0.1f, "家具根、冻结刚体和阻挡形状同步且不重复位移");
            Check(rigid.Freeze && !furniture.IsInThrowLifecycle, "平台保持家具冻结且不进入投掷生命周期");

            platform.CarryEnemies = false; platform.CarryWorldItems = false;
            float enemyX = enemy.Position.X, furnitureX = furniture.Position.X;
            float weaponX = weapon.Position.X;
            await Frames();
            Check(enemy.Position.X == enemyX && furniture.Position.X == furnitureX, "平台分类开关可分别禁止承载");
            Check(weapon.Position.X == weaponX, "CarryWorldItems 同时控制普通地面武器");
            platform.CarryPlayers = false;
            float playerX = player.Position.X;
            await Frames();
            Check(player.Position.X == playerX, "玩家承载开关独立生效");
            platform.CarryPlayers = true;
            platform.CarryEnemies = true; platform.CarryWorldItems = true;

            var second = Platform(); second.Position = platform.Position;
            await Frames();
            float before = enemy.Position.X, platformBefore = platform.Position.X;
            await Frames(6);
            Check(Mathf.Abs((enemy.Position.X - before) - (platform.Position.X - platformBefore)) < 0.1f,
                "两个重叠平台每帧只携带同一对象一次");
            second.QueueFree(); await Frames();

            furniture.ApplyThrowImpulse(new Vector2(500, 0));
            Check(furniture.IsInGroup("world_items"), "真实投掷后的实例仍属于 world_items");
            Check(!furniture.CanRidePlatform, "world_items 标签不会让飞行家具被平台携带");
            Vector2 thrownRoot = furniture.GlobalPosition;
            await Frames();
            Check(furniture.GlobalPosition.DistanceTo(thrownRoot) < 0.1f, "飞行期间包装根节点不被平台额外移动");
            furniture.QueueFree(); await Frames();
            Check(IsInstanceValid(platform), "移除物件后平台继续运行，无悬空引用");

            weapon.ApplyScatterImpulse(new Vector2(300, 0));
            Check(!weapon.CanRidePlatform, "散落滑行中的地面武器暂不承载");
            weaponRoot = weapon.GlobalPosition;
            weapon.ApplyPlatformDisplacement(new Vector2(100, 0), 7);
            Check(weapon.GlobalPosition == weaponRoot, "物品滑行期间承载接口不叠加位移");
            await ToSignal(GetTree().CreateTimer(0.7), SceneTreeTimer.SignalName.Timeout);
            await Frames();
            Check(weapon.CanRidePlatform, "地面武器停稳后自动恢复承载");
            weaponRoot = weapon.GlobalPosition;
            await Frames();
            Check(weapon.GlobalPosition.X > weaponRoot.X, "恢复承载后继续随平台移动");
            // 独立测试场景没有 BattleHUD；补建 HUD 平时提供的快捷栏容器。
            var quickBar = new Kuros.Systems.Inventory.InventoryContainer { SlotCount = 5 };
            AddChild(quickBar);
            player.InventoryComponent!.SetQuickBar(quickBar);
            Check(weapon.TryPickupByActor(player), "真实地面武器可正常拾取");
            Check(!weapon.CanRidePlatform && weapon.IsQueuedForDeletion(), "拾取后立即退出承载资格并排队释放");
            await Frames();
            Check(!IsInstanceValid(weapon), "拾取后世界物品本体实际释放");
            Check(IsInstanceValid(platform), "地面武器释放后平台继续运行");

            // 投掷武器同样使用刚体包装类，但没有家具的 StaticBody2D。
            foreach (string name in new[] { "briefcase", "boomerang", "smokeGrenade", "emojiBomb", "eCore", "gravityGrenade", "metalSpike" })
            {
                var throwable = GD.Load<PackedScene>($"res://scenes/weapons/Weapon_Throw_{name}.tscn").Instantiate<RigidBodyWorldItemEntity>();
                throwable.Position = platform.Position + new Vector2(0, 400);
                AddChild(throwable);
                var throwableBody = throwable.GetNode<RigidBody2D>("RigidBody2D");
                Vector2 startRoot = throwable.GlobalPosition, startBody = throwableBody.GlobalPosition;
                await Frames();
                Check(throwable.ItemDefinition?.IsThrowWeapon == true && throwable.CanRidePlatform
                    && throwable.GlobalPosition.X > startRoot.X, $"投掷武器 {name} 地面静置时可承载");
                Check((throwableBody.GlobalPosition - startBody).DistanceTo(throwable.GlobalPosition - startRoot) < 0.1f,
                    $"投掷武器 {name} 根节点与内部刚体同步");
                throwable.ApplyThrowImpulse(new Vector2(500, 0));
                Check(!throwable.CanRidePlatform, $"投掷武器 {name} 出手后立即停止承载");
                throwable.QueueFree(); await Frames();
            }

            // 单独检查静态阻挡扫掠，避免把家具直接穿过墙。
            platform.Speed = 0;
            var blockingFurniture = GD.Load<PackedScene>("res://scenes/items/Furniture_D_chairOfficeC.tscn").Instantiate<RigidBodyWorldItemEntity>();
            blockingFurniture.Position = new Vector2(0, 200); AddChild(blockingFurniture);
            var wall = new StaticBody2D { Position = new Vector2(300, 265), CollisionLayer = 1, CollisionMask = 0 };
            wall.AddChild(new CollisionShape2D { Shape = new RectangleShape2D { Size = new Vector2(40, 300) } });
            AddChild(wall); await Frames();
            float start = blockingFurniture.Position.X;
            blockingFurniture.ApplyPlatformDisplacement(new Vector2(1000, 0), 1);
            Check(blockingFurniture.Position.X > start && blockingFurniture.Position.X < 300, "家具位移遇墙被身体形状扫掠截断");
            GD.Print($"[CarryCheck] ALL PASSED ({_passed})");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"[CarryCheck] FAILED {exception}"); GetTree().Quit(1);
        }
    }
}
