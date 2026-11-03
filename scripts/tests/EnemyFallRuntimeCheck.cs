using Godot;
using System;
using System.Threading.Tasks;
using Kuros.Actors.Enemies;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Environments;
using Kuros.Actors.Enemies.Attacks;
using Kuros.Items;
using Kuros.Systems.Loot;

namespace Kuros.Tests;

public partial class EnemyFallRuntimeCheck : Node2D
{
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        GD.Print($"[EnemyFallCheck] PASS {name}");
    }
    private async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    private EnemyNormalGuard1 Guard(Vector2 position, bool onePunchOnly = false)
    {
        var guard = GD.Load<PackedScene>("res://scenes/actors/characters/Enemy_Normal_guard1.tscn").Instantiate<EnemyNormalGuard1>();
        if (onePunchOnly) guard.GetNode("StateMachine/Attack/AttackController/SimpleMeleeAttack").Free();
        guard.Position = position;
        guard.ProcessMode = ProcessModeEnum.Disabled; // 测试手动推进坠落，避免 AI 自主走动干扰断言。
        KeepPhysicsActive(guard);
        guard.LootTable = null;
        AddChild(guard);
        return guard;
    }
    private static void KeepPhysicsActive(Node node)
    {
        if (node is CollisionObject2D body) body.DisableMode = CollisionObject2D.DisableModeEnum.KeepActive;
        foreach (Node child in node.GetChildren()) KeepPhysicsActive(child);
    }
    private Node2D? Visual()
    {
        foreach (Node child in GetChildren())
            if (child is Node2D node && node.SceneFilePath == "res://scenes/objects/Animation_character.tscn" && !node.IsQueuedForDeletion()) return node;
        return null;
    }

    public override async void _Ready()
    {
        try
        {
            var region = GD.Load<PackedScene>("res://scenes/environments/FallRegion.tscn").Instantiate<FallRegion>();
            region.FallDuration = 0.3f;
            AddChild(region);
            var platform = GD.Load<PackedScene>("res://scenes/environments/FallMovingPlatform.tscn").Instantiate<FallMovingPlatform>();
            platform.Speed = 0;
            AddChild(platform);
            var player = GD.Load<PackedScene>("res://scenes/actors/characters/main_character.tscn").Instantiate<MainCharacter>();
            player.Position = new Vector2(1100, 400);
            player.ProcessMode = ProcessModeEnum.Disabled;
            KeepPhysicsActive(player);
            AddChild(player);
            var progress = new KillProgressAscendController
            {
                DriveAscendVisual = false,
                Phases = new() { new KillProgressPhase { TotalKillsForFull = 100 } }
            };
            AddChild(progress);
            var guard = Guard(Vector2.Zero);
            var controller = guard.GetNode<EnemyFallController>("FallController");
            await Frames();
            Check(controller.FallEnabled && guard.StateMachine.HasState("Fall"), "guard1 场景已配置通用控制器及 Fall 状态");
            Check(!guard.StateMachine.ChangeState("Fall"), "未验证请求不能直接进入 Fall");
            controller._PhysicsProcess(0.016);
            Check(!guard.IsDead, "红绿重叠时平台承托优先");
            guard.GlobalPosition = new Vector2(0, 350);
            controller.FallEnabled = false;
            await Frames();
            controller._PhysicsProcess(0.016);
            Check(!guard.IsDead, "关闭 FallEnabled 不触发坠落");
            controller.FallEnabled = true;
            controller.ImmuneStates = new[] { guard.StateMachine.CurrentState.Name.ToString() };
            controller._PhysicsProcess(0.016);
            Check(!guard.IsDead, "每个敌人可独立配置状态免坠落白名单");
            controller.ImmuneStates = Array.Empty<string>();
            guard.ActiveImmunities |= ImmunityFlags.Fall;
            controller._PhysicsProcess(0.016);
            Check(!guard.IsDead, "独立 Fall 免疫有效");
            guard.ActiveImmunities &= ~ImmunityFlags.Fall;
            guard.ActiveImmunities |= ImmunityFlags.Stun;
            Check(guard.StateMachine.ChangeState("Frozen"), "可先进入真实眩晕状态");
            int deaths = 0, damageEvents = 0, scoreBefore = player.Score;
            GameActor.DeathFinalized += OnDeath;
            guard.DamageTaken += _ => damageEvents++;
            void OnDeath(GameActor actor) { if (actor == guard) deaths++; }
            controller.FallEffectOffset = new Vector2(20, -10);
            controller._PhysicsProcess(0.016);
            Check(guard.IsDead && guard.CurrentHealth == 0 && controller.IsFalling, "眩晕免疫不阻止坠落，立即归零并结算死亡");
            Check(guard.StateMachine.CurrentState.Name == "Fall", "强制中断 Frozen，跳过普通 Dying/Dead 动画");
            Check(!guard.Visible && guard.CollisionLayer == 0 && guard.CollisionMask == 0, "本体隐藏并关闭碰撞");
            Check(deaths == 1 && progress.KillCount == 1, "正常死亡事件及击杀进度恰好结算一次");
            Check(player.Score == scoreBefore + guard.ScoreValue, "保留正常死亡分数奖励");
            Check(damageEvents == 0, "坠落死亡不伪造普通伤害事件和飘字");
            var visual = Visual();
            Check(visual != null && visual.Visible && visual.GlobalPosition.IsEqualApprox(new Vector2(20, 340)), "玩家临时替身在敌人踩空位置加世界偏移生成");
            controller._PhysicsProcess(0.016);
            guard.FinalizeFallDeath(null);
            Check(deaths == 1, "重复判定或结算不会重复死亡");
            GameActor.DeathFinalized -= OnDeath;
            await Frames();
            Check(!IsInstanceValid(guard) && IsInstanceValid(visual), "敌人已释放，独立替身继续播放");
            await ToSignal(GetTree().CreateTimer(0.4), SceneTreeTimer.SignalName.Timeout);
            Check(!IsInstanceValid(visual), "替身按区域时长独立清理");

            // 攻击状态的退出必须取消真实模板，避免坠落后继续出拳。
            player.GlobalPosition = new Vector2(300, 350);
            var attacking = Guard(new Vector2(0, 350), onePunchOnly: true);
            await Frames();
            var attack = attacking.GetNode<EnemyAttackController>("StateMachine/Attack/AttackController");
            attack._PhysicsProcess(0.016); // Ready 时物理重叠尚未更新，推进控制器以排队可用攻击。
            Check(attacking.StateMachine.ChangeState("Attack"), "进入真实 Attack 状态");
            Check(attack.IsRunning, "坠落前真实攻击模板正在运行");
            attacking.GetNode<EnemyFallController>("FallController")._PhysicsProcess(0.016);
            Check(attacking.IsDead && !attack.IsRunning, "Fall 中断并取消真实攻击模板");
            await Frames();

            var phaseGuard = Guard(new Vector2(0, 350), onePunchOnly: true);
            var phaseController = phaseGuard.GetNode<EnemyFallController>("FallController");
            var phaseAttack = phaseGuard.GetNode<EnemyOnePunchAttack>("StateMachine/Attack/AttackController/OnePunchAttack");
            phaseAttack.SnapshotDelaySeconds = 0;
            phaseAttack.WarmupDuration = 0.05f;
            await Frames();
            const ImmunityFlags fallPhases = ImmunityFlags.WarmupFallImmunity | ImmunityFlags.ActiveFallImmunity | ImmunityFlags.RecoveryFallImmunity;
            Check((phaseAttack.GrantedImmunities & fallPhases) == ImmunityFlags.ActiveFallImmunity && !phaseAttack.ProvidesFallImmunity, "GrantedImmunities 已迁移为仅 Active 阶段坠落免疫");
            var phaseAttackController = phaseGuard.GetNode<EnemyAttackController>("StateMachine/Attack/AttackController");
            phaseAttackController._PhysicsProcess(0.016);
            phaseGuard.StateMachine.ChangeState("Attack");
            Check(phaseAttack.IsRunning && !phaseAttack.ProvidesFallImmunity, "真实蓄力阶段不因霸体而获得坠落免疫");
            Check((phaseGuard.ActiveImmunities & fallPhases) == 0, "阶段标记不会被攻击起手写入角色免疫集合");
            phaseAttack.GrantedImmunities = (phaseAttack.GrantedImmunities & ~fallPhases) | ImmunityFlags.WarmupFallImmunity;
            Check(phaseAttack.ProvidesFallImmunity, "可独立配置 Warmup 免疫");
            phaseAttack.GrantedImmunities = (phaseAttack.GrantedImmunities & ~fallPhases) | ImmunityFlags.ActiveFallImmunity;
            phaseAttack.Tick(0.1);
            Check(phaseAttack.CurrentPhase == EnemyAttackTemplate.AttackPhase.Active && phaseAttack.ProvidesFallImmunity, "真实 Active 阶段提供免疫");
            phaseController._PhysicsProcess(0.016);
            Check(!phaseGuard.IsDead, "当前运行模组的阶段免疫阻止红区坠落");
            Check((phaseGuard.ActiveImmunities & ImmunityFlags.Fall) == 0, "阶段查询不写入全局 Fall 免疫位");
            phaseAttack.Tick(10);
            Check(phaseAttack.CurrentPhase == EnemyAttackTemplate.AttackPhase.Recovery && !phaseAttack.ProvidesFallImmunity, "进入 Recovery 自动结束 Active 免疫");
            phaseAttack.GrantedImmunities = (phaseAttack.GrantedImmunities & ~fallPhases) | ImmunityFlags.RecoveryFallImmunity;
            phaseController._PhysicsProcess(0.016);
            Check(phaseAttack.ProvidesFallImmunity && !phaseGuard.IsDead, "可独立配置 Recovery 免疫");
            phaseAttack.GrantedImmunities = (phaseAttack.GrantedImmunities & ~fallPhases) | ImmunityFlags.ActiveFallImmunity;
            phaseController._PhysicsProcess(0.016);
            Check(phaseGuard.IsDead && !phaseAttack.IsRunning && !phaseAttack.ProvidesFallImmunity, "仍在坑内时免疫结束立即坠落，取消后不残留阶段免疫");
            await Frames();

            // 确定性掉落表：无散落和额外偏移，验证安全落点。
            var looting = Guard(new Vector2(0, 350));
            looting.LootTable = new LootDropTable
            {
                ScatterRadius = 0,
                Entries = new[] { new LootDropEntry { Item = GD.Load<ItemDefinition>("res://resources/items/Weapon_Brawl_DiscoBall.tres") } }
            };
            await Frames();
            looting.GetNode<EnemyFallController>("FallController")._PhysicsProcess(0.016);
            bool safeLoot = false;
            foreach (Node item in GetTree().GetNodesInGroup("world_items"))
                if (item is Node2D node && Mathf.Abs(node.GlobalPosition.X) > 700) safeLoot = true;
            Check(safeLoot, "坠落掉落物使用通过检查的固定岸边兜底点");
            await Frames();
            var noVisual = Guard(new Vector2(0, 350));
            noVisual.GetNode<EnemyFallController>("FallController").FallEffectScene = null;
            region.FallbackMarkerPaths.Clear();
            await Frames();
            noVisual.GetNode<EnemyFallController>("FallController")._PhysicsProcess(0.016);
            Check(noVisual.IsDead, "没有替身或兜底点仍能完成坠落死亡");
            GD.Print("[EnemyFallCheck] ALL PASSED");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"[EnemyFallCheck] FAILED {exception}");
            GetTree().Quit(1);
        }
    }
}
