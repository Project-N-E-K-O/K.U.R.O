using Godot;
using System;
using System.Threading.Tasks;
using Kuros.Core;
using Kuros.Effects;
using Kuros.Environments;

namespace Kuros.Tests;

public partial class PlatformEffectRuntimeCheck : Node2D
{
    private int _passed;
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        GD.Print($"[EffectCheck] PASS {name}"); _passed++;
    }
    private async Task Frames(int count = 4)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private FallMovingPlatform Platform(Vector2 position)
    {
        var platform = GD.Load<PackedScene>("res://scenes/environments/FallMovingPlatform.tscn").Instantiate<FallMovingPlatform>();
        platform.Position = position; platform.TravelOffset = new Vector2(5000, 0); platform.Speed = 120;
        AddChild(platform); return platform;
    }
    private Node2D Effect(Node parent, Vector2 position, bool destroyOnLost = false)
    {
        var effect = new Node2D();
        effect.AddChild(new PlatformEffectFollower { Name = "Follower", DestroyOnPlatformLost = destroyOnLost });
        parent.AddChild(effect); effect.GlobalPosition = position; return effect;
    }
    public override async void _Ready()
    {
        try
        {
            var platform = Platform(new Vector2(1000, 0));
            var spike = GD.Load<PackedScene>("res://resources/effects/SpikeAttackEffect.tscn").Instantiate<SpikeAttackEffect>();
            var source = new GameActor { Position = new Vector2(-3000, 0), CollisionLayer = 0, CollisionMask = 0 };
            AddChild(source); spike.Attacker = source;
            var slow = spike.GetNode<SlowHitAreaEffect>("SlowHitArea"); slow.Duration = 0; slow.DamageInterval = 0.05f;
            AddChild(spike); // 特意先 AddChild 再定位，复现实际投掷生成顺序。
            spike.GlobalPosition = platform.GlobalPosition;
            var follower = spike.GetNode<PlatformEffectFollower>("PlatformEffectFollower");
            await Frames();
            Check(follower.BoundPlatform == platform, "首次物理帧使用生成后的最终世界坐标绑定");
            Vector2 before = spike.GlobalPosition, platformBefore = platform.GlobalPosition;
            Vector2 spriteBefore = spike.GetNode<Node2D>("Sprite2D").GlobalPosition;
            Vector2 areaBefore = slow.GetNode<Node2D>("Area2D").GlobalPosition;
            await Frames();
            Vector2 motion = platform.GlobalPosition - platformBefore;
            Check(motion.Length() > 0 && (spike.GlobalPosition - before).DistanceTo(motion) < 0.1f, "地刺根节点随平台等量移动");
            Check((spike.GetNode<Node2D>("Sprite2D").GlobalPosition - spriteBefore).DistanceTo(motion) < 0.1f
                && (slow.GetNode<Node2D>("Area2D").GlobalPosition - areaBefore).DistanceTo(motion) < 0.1f,
                "地刺视觉和实际伤害区域同步移动");
            Check(slow.Attacker == source && spike.Attacker == source, "随动不改变攻击来源");

            var victim = new GameActor { Position = spike.GlobalPosition, CollisionLayer = 2, CollisionMask = 0, MaxHealth = 500, Speed = 100 };
            victim.AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 10 } });
            AddChild(victim); int health = victim.CurrentHealth;
            await Frames(8);
            Check(victim.CurrentHealth < health, "移动中的真实地刺伤害区域仍能命中");
            victim.QueueFree(); await Frames();

            // 绑定后即使锚点已在平台边缘之外，也保持附着，不再反复点查询。
            spike.GlobalPosition += new Vector2(0, 500);
            before = spike.GlobalPosition; platformBefore = platform.GlobalPosition;
            await Frames();
            Check(follower.BoundPlatform == platform && (spike.GlobalPosition - before).DistanceTo(platform.GlobalPosition - platformBefore) < 0.1f,
                "绑定后不因特效超出平台边缘脱离");

            var ground = Effect(this, new Vector2(0, 1000));
            await Frames();
            var groundFollower = ground.GetNode<PlatformEffectFollower>("Follower");
            Check(groundFollower.BoundPlatform == null, "固定地面特效不绑定平台");
            var passingPlatform = Platform(ground.GlobalPosition);
            before = ground.GlobalPosition;
            await Frames();
            Check(groundFollower.BoundPlatform == null && ground.GlobalPosition == before, "后来经过的平台不自动吸附已有地面特效");
            Check(groundFollower.TryBindToPlatformBelow(), "显式重新绑定可用于延迟落地效果");
            await Frames();
            Check(ground.GlobalPosition.X > before.X, "显式绑定后开始随动");
            passingPlatform.SupportArea!.SupportEnabled = false;
            before = ground.GlobalPosition; await Frames();
            Check(groundFollower.BoundPlatform == null && ground.GlobalPosition == before, "承托停用后默认解除绑定保留位置");

            foreach (string sceneName in new[] { "FireWallA", "FireWallB" })
            {
                var wall = GD.Load<PackedScene>($"res://resources/effects/{sceneName}.tscn").Instantiate<Node2D>();
                var marker = wall.GetNode<Marker2D>("Marker2D");
                var wallFollower = wall.GetNode<PlatformEffectFollower>("PlatformEffectFollower");
                AddChild(wall);
                wall.GlobalPosition += platform.GlobalPosition - marker.GlobalPosition;
                await Frames();
                Check(wallFollower.GroundAnchor == marker && wallFollower.BoundPlatform == platform,
                    $"{sceneName} 正确解析 Marker 引用且根不在平台内仍能绑定");
                before = wall.GlobalPosition; platformBefore = platform.GlobalPosition;
                await Frames();
                Check((wall.GlobalPosition - before).DistanceTo(platform.GlobalPosition - platformBefore) < 0.1f,
                    $"{sceneName} 按 Marker 绑定后整体随动");
                wall.QueueFree(); await Frames();
            }

            var inherited = Effect(platform, platform.GlobalPosition);
            await Frames();
            before = inherited.GlobalPosition; platformBefore = platform.GlobalPosition;
            await Frames();
            Check((inherited.GlobalPosition - before).DistanceTo(platform.GlobalPosition - platformBefore) < 0.1f,
                "作为平台子节点的效果不重复叠加父级位移");
            var duplicate = Effect(this, platform.GlobalPosition);
            duplicate.AddChild(new PlatformEffectFollower());
            await Frames(); before = duplicate.GlobalPosition; platformBefore = platform.GlobalPosition;
            await Frames();
            Check((duplicate.GlobalPosition - before).DistanceTo(platform.GlobalPosition - platformBefore) < 0.1f,
                "重复跟随组件每帧最多移动效果一次");

            var destroyEffect = Effect(this, platform.GlobalPosition, true);
            await Frames();
            before = spike.GlobalPosition; platform.QueueFree(); await Frames();
            Check(IsInstanceValid(spike) && follower.BoundPlatform == null && spike.GlobalPosition == before,
                "平台释放后地刺默认留在当前世界位置");
            Check(!IsInstanceValid(destroyEffect), "可配置随平台丢失销毁效果");
            slow.Duration = 0.01f;
            await Frames(8);
            Check(!IsInstanceValid(spike), "原效果持续时间及容器释放仍正常");
            GD.Print($"[EffectCheck] ALL PASSED ({_passed})"); GetTree().Quit(0);
        }
        catch (Exception exception)
        { GD.PushError($"[EffectCheck] FAILED {exception}"); GetTree().Quit(1); }
    }
}
