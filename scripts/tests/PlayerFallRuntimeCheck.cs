using Godot;
using System;
using System.Threading.Tasks;
using Kuros.Actors.Heroes;
using Kuros.Core;
using Kuros.Environments;
using Kuros.Managers;
using Kuros.UI;

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
    private int DamageTextCount()
    {
        int count = 0;
        foreach (Node child in GetTree().Root.GetChildren())
            if (child is FloatingDamageText && !child.IsQueuedForDeletion()) count++;
        return count;
    }
    public override async void _Ready()
    {
        try
        {
            var player = GetNode<MainCharacter>("MainCharacter");
            var controller = player.GetNode<PlayerFallController>("FallController");
            var region = GetNode<FallRegion>("FallRegion");
            Check(controller.FallEffectScene?.ResourcePath == "res://scenes/objects/Animation_character.tscn", "玩家自身控制器配置坠落替身场景");
            var damageTexts = FloatingDamageTextManager.Instance;
            if (damageTexts == null)
            {
                damageTexts = new FloatingDamageTextManager();
                AddChild(damageTexts);
            }
            region.FallDuration = 0.2f;
            await Frames();
            Check(controller.LastSafePosition.HasValue, "进入黄区立即记录固定岸边");
            Vector2 left = controller.LastSafePosition!.Value;
            Check(!controller.IsSafe(region, new Vector2(0, 350)), "红区不能作为安全点");
            int initialHealth = player.CurrentHealth;
            player.ActiveImmunities |= ImmunityFlags.Stun;
            player.StartHitInvincibility(10);
            controller.FallEffectOffset = new Vector2(37, -21);
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            Check(controller.IsFalling && !player.Visible, "眩晕免疫和伤害无敌不阻止 Fall，玩家隐藏");
            Node2D? fallVisual = null;
            foreach (Node child in GetTree().CurrentScene.GetChildren())
                if (child is Node2D visual && child.SceneFilePath == controller.FallEffectScene?.ResourcePath)
                    fallVisual = visual;
            Check(fallVisual != null && fallVisual.Visible && fallVisual.TopLevel, "从玩家配置生成独立可见坠落替身");
            Check(fallVisual != null && fallVisual.GlobalPosition.IsEqualApprox(new Vector2(37, 329)), "玩家特效偏移按世界单位作用于踩空位置，不受角色缩放影响");
            Check(player.GlobalPosition.IsEqualApprox(new Vector2(0, 350)), "特效偏移不改变隐藏本体的踩空位置");
            var fallAnimator = fallVisual?.GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
            Check(fallAnimator != null && fallAnimator.CurrentAnimation == controller.FallAnimationName
                && Mathf.IsEqualApprox(fallAnimator.SpeedScale, (float)fallAnimator.GetAnimation(controller.FallAnimationName).Length / region.FallDuration), "玩家配置的动画按区域时长同步播放");
            Check(!player.StateMachine.ChangeState("Hit"), "Fall 拒绝 Hit 打断");
            Check(!player.TakeDamage(10), "隐藏期间拒绝普通伤害");
            Check(player.GetControlledMovementInput() == Vector2.Zero, "Fall 禁止角色移动输入");
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(!controller.IsFalling && player.Visible, "到期恢复控制和可见性");
            Check(player.GlobalPosition.DistanceTo(left) < 1, "回到左岸最近有效点");
            controller.FallEffectOffset = Vector2.Zero;
            Check(player.CurrentHealth == initialHealth - Mathf.CeilToInt(player.MaxHealth * 0.2f), "无敌期间仍准确扣最大生命20%");
            Check(DamageTextCount() > 0, "存活坠落仍生成伤害飘字");

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

            region.DamageRatio = 0;
            var configuredEffect = controller.FallEffectScene;
            controller.FallEffectScene = null;
            int beforeZeroDamage = player.CurrentHealth;
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(player.Visible && player.CurrentHealth == beforeZeroDamage, "零伤害坠落正常恢复显示且不扣血");
            controller.FallEffectScene = configuredEffect;

            player.Visible = false;
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(!controller.IsFalling && !player.Visible, "原本隐藏的玩家非致死恢复也不强制显示");
            controller.RestoreVisibilityAfterRevive();
            Check(!player.Visible, "没有致死隐藏记录时恢复接口不修改可见性");
            player.Visible = true;
            region.DamageRatio = 0.2f;

            // 致死结算及同步退出 Fall 时都不能显示本体。
            damageTexts.ClearAll();
            await Frames();
            bool visibleAtDamage = false, revealedAfterDeath = false;
            player.DamageTaken += _ => visibleAtDamage |= player.Visible;
            player.VisibilityChanged += () => { if (player.CurrentHealth <= 0 && player.Visible) revealedAfterDeath = true; };
            player.RestoreHealth(1);
            player.GlobalPosition = new Vector2(0, 350);
            await Frames();
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(player.IsDeathSequenceActive && !controller.IsFalling, "坠落致死进入正常死亡链并清理 Fall");
            Check(player.StateMachine.CurrentState.Name != "Idle", "致死后不会被恢复逻辑切回 Idle");
            Check(!visibleAtDamage && !revealedAfterDeath, "致死扣血和状态退出过程中没有重新显示本体");
            Check(!player.Visible, "坠落致死后本体保持隐藏");
            Check(DamageTextCount() == 0, "坠落致死不在隐藏本体位置生成伤害飘字");
            await Frames();
            Check(!player.Visible, "Dying 后续物理帧仍保持隐藏");

            // 只验证显示恢复接口；项目尚无完整复活流程，不以加血冒充复活。
            controller.RestoreVisibilityAfterRevive();
            Check(player.Visible, "显式恢复接口还原坠落前可见性");
            player.Visible = false;
            controller.RestoreVisibilityAfterRevive();
            Check(!player.Visible, "重复调用不会再次覆盖外部可见性");

            var hiddenPlayer = GD.Load<PackedScene>("res://scenes/actors/characters/main_character.tscn").Instantiate<MainCharacter>();
            hiddenPlayer.Position = new Vector2(1100, 0);
            hiddenPlayer.Visible = false;
            AddChild(hiddenPlayer);
            await Frames();
            hiddenPlayer.RestoreHealth(1);
            var hiddenController = hiddenPlayer.GetNode<PlayerFallController>("FallController");
            hiddenPlayer.GlobalPosition = new Vector2(0, 350);
            await Frames();
            await ToSignal(GetTree().CreateTimer(0.35), SceneTreeTimer.SignalName.Timeout);
            Check(hiddenPlayer.IsDeathSequenceActive && !hiddenPlayer.Visible, "原本隐藏的玩家坠落致死仍隐藏");
            hiddenController.RestoreVisibilityAfterRevive();
            Check(!hiddenPlayer.Visible, "致死恢复接口保留原本隐藏状态");
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
