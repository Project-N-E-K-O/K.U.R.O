using Godot;
using System;
using System.Threading.Tasks;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Environments;

namespace Kuros.Tests;

/// <summary>运行 PlayerFallRuntimeCheck.tscn；验证真实玩家状态机与区域物理查询。</summary>
public partial class PlayerFallRuntimeCheck : Node2D
{
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        GD.Print($"[FallCheck] PASS {name}");
    }
    private async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
    public override async void _Ready()
    {
        try
        {
            var player = GetNode<MainCharacter>("MainCharacter");
            var controller = player.GetNode<PlayerFallController>("FallController");
            var region = GetNode<FallRegion>("FallRegion");
            region.FallDuration = 0.2f;
            await Frames();
            Check(controller.LastSafePosition.HasValue, "进入黄区立即记录固定岸边");
            Vector2 left = controller.LastSafePosition!.Value;
            Check(!controller.IsSafe(region, new Vector2(0, 350)), "红区不能作为安全点");
            int initialHealth = player.CurrentHealth;
            player.ActiveImmunities |= ImmunityFlags.Stun;
            player.StartHitInvincibility(10);
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            Check(controller.IsFalling && !player.Visible, "眩晕免疫和伤害无敌不阻止 Fall，玩家隐藏");
            Check(!player.StateMachine.ChangeState("Hit"), "Fall 拒绝 Hit 打断");
            Check(!player.TakeDamage(10), "隐藏期间拒绝普通伤害");
            Check(player.GetControlledMovementInput() == Vector2.Zero, "Fall 禁止角色移动输入");
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(!controller.IsFalling && player.Visible, "到期恢复控制和可见性");
            Check(player.GlobalPosition.DistanceTo(left) < 1, "回到左岸最近有效点");
            Check(player.CurrentHealth == initialHealth - Mathf.CeilToInt(player.MaxHealth * 0.2f), "无敌期间仍准确扣最大生命20%");

            player.ActiveImmunities |= ImmunityFlags.Fall;
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            Check(!controller.IsFalling, "独立 Fall 免疫有效");
            player.ActiveImmunities &= ~ImmunityFlags.Fall;
            await Frames();
            Check(controller.IsFalling, "留在红区时免疫失效会触发，不依赖重新进入");
            controller.ResetSafeHistory();
            player.GlobalPosition = new Vector2(1100, 0);
            await Frames();
            Check(!controller.IsFalling && player.Visible, "外部传送取消坠落并清理旧记录");
            Check(controller.LastSafePosition.HasValue && controller.LastSafePosition.Value.X > 700, "右岸可更新为新的安全点");

            // 停住平台后，验证绿色优先和不保存平台坐标。
            var platform = GetNode<FallMovingPlatform>("FallRegion/MovingPlatform");
            platform.Speed = 0;
            platform.Position = Vector2.Zero;
            await Frames();
            player.GlobalPosition = Vector2.Zero;
            await Frames();
            Check(!controller.IsFalling, "红绿重叠有承托，不误坠落");
            Check(!controller.IsSafe(region, Vector2.Zero), "平台不产生回退记录");
            Vector2 beforeCarry = player.GlobalPosition;
            platform.Speed = 120;
            await Frames(6);
            Check(player.GlobalPosition.DistanceTo(beforeCarry) > 1 && !controller.IsFalling, "平台实际传递位移，站立玩家随平台移动");
            platform.Speed = 0;
            platform.GetNode<FallSupportArea>("SupportArea").SupportEnabled = false;
            await Frames();
            Check(controller.IsFalling, "平台失去承托时在原地触发坠落");
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(player.GlobalPosition.X > 700, "平台中途掉落回最近右岸");

            // 没有采样记录时，仍可依靠经过相同验证的兜底点恢复。
            player.RestoreHealth(player.MaxHealth);
            controller.ResetSafeHistory();
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            Check(controller.IsFalling, "直接进入红区且没有记录时可使用固定兜底");
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(!controller.IsFalling && Mathf.Abs(player.GlobalPosition.X) > 700, "无历史兜底恢复到固定岸边");

            // Frozen 的退出守卫不能压过已验证的 Fall。
            player.ActiveImmunities &= ~ImmunityFlags.Stun;
            Check(player.StateMachine.ChangeState("Frozen"), "进入真实 Frozen 状态");
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            Check(controller.IsFalling, "Fall 可中断 Frozen 退出守卫");
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);

            player.GlobalPosition = new Vector2(0, 350);
            Check(player.StateMachine.ChangeState("Dash"), "进入真实 Dash 状态");
            await Frames();
            Check(!controller.IsFalling, "真实 Dash 期间免坠落");
            await ToSignal(GetTree().CreateTimer(0.72), SceneTreeTimer.SignalName.Timeout);
            Check(controller.IsFalling || Mathf.Abs(player.GlobalPosition.X) > 700, "Dash 结束后重新判定或已完成回岸");
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);

            // 使用独立的新玩家血量，验证致死伤害不会被 Idle 恢复覆盖。
            player.RestoreHealth(1);
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(player.IsDeathSequenceActive && !controller.IsFalling, "坠落致死进入正常死亡链并清理 Fall");
            Check(player.StateMachine.CurrentState.Name != "Idle", "致死后不会被恢复逻辑切回 Idle");
            GD.Print("[FallCheck] ALL PASSED");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"[FallCheck] FAILED {exception}");
            GetTree().Quit(1);
        }
    }
}
