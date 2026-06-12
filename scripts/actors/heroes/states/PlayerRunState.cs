using Godot;
using System;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerRunState : PlayerState
    {
        [Export] public float RunAnimationSpeed = 1.0f;
        [Export] public PackedScene? DustEffectScene { get; set; }
        [Export] public float DustEffectYOffset = 0f;
        
        private float _originalSpeedScale = 1.0f;
        
        // 灰尘特效相关
        private PackedScene? _dustEffectScene;
        private Node? _spineController;
        private bool _effectSignalConnected = false;
        
        public override void Enter()
        {
            Player.NotifyMovementState(Name);
            
            // 初始化灰尘特效资源
            if (DustEffectScene != null)
            {
                _dustEffectScene = DustEffectScene;
            }
            // else
            // {
            //     _dustEffectScene ??= GD.Load<PackedScene>("res://shaders/smoke.tscn");
            // }
            
            // 连接 SpineController 信号（用于灰尘特效）
            if (Player is MainCharacter mainChar)
            {
                _spineController = mainChar.GetSpineControllerNode();
                if (_spineController != null && !_effectSignalConnected)
                {
                    // 连接 effect_triggered 信号到 _OnEffectTriggered 方法
                    try
                    {
                        _spineController.Connect(
                            new StringName("effect_triggered"),
                            new Callable(this, nameof(_OnEffectTriggered))
                        );
                        _effectSignalConnected = true;
                        //GD.Print("[PlayerRunState] 成功连接 effect_triggered 信号");
                    }
                    catch (Exception ex)
                    {
                        //GD.PrintErr($"[PlayerRunState] 连接信号失败: {ex.Message}");
                    }
                }
            }
            
            // 使用 PlayAnimation 方法，自动适配 MainCharacter 和 SamplePlayer
            if (Player is MainCharacter mainChar2)
            {
                // MainCharacter 使用 Spine 动画
                PlayAnimation(mainChar2.RunAnimationName, true, RunAnimationSpeed);
            }
            else
            {
                // SamplePlayer 使用 AnimationPlayer
                if (Actor.AnimPlayer != null)
                {
                    // Save original speed scale before modifying
                    _originalSpeedScale = Actor.AnimPlayer.SpeedScale;
                    
                    // 使用 PlayAnimation 方法（虽然它会再次检查，但这样可以统一接口）
                    PlayAnimation("animations/run", true, RunAnimationSpeed);
                }
            }
            // Increase speed by changing velocity calculation, not base stat
        }
        
        public override void Exit()
        {
            // 断开 SpineController 信号连接
            if (_spineController != null && _effectSignalConnected)
            {
                try
                {
                    _spineController.Disconnect(
                        new StringName("effect_triggered"),
                        new Callable(this, nameof(_OnEffectTriggered))
                    );
                    _effectSignalConnected = false;
                    //GD.Print("[PlayerRunState] 已断开 effect_triggered 信号");
                }
                catch (Exception ex)
                {
                    //GD.PrintErr($"[PlayerRunState] 断开信号失败: {ex.Message}");
                }
            }
            
            // Restore original animation speed when leaving run state
            if (Actor.AnimPlayer != null)
            {
                Actor.AnimPlayer.SpeedScale = _originalSpeedScale;
            }
        }

        public override void PhysicsUpdate(double delta)
        {
            if (HandleDialogueGating(delta)) return;
            
            // // 检查是否转换到 RunHolding（持握可投掷物品的奔跑）
            // var selectedStack = Player.GetComponent<PlayerInventoryComponent>("Inventory")?.GetSelectedQuickBarStack();
            // if (selectedStack != null && !selectedStack.IsEmpty && selectedStack.Item.IsThrowable)
            // {
            //     GD.Print($"[PlayerRunState] 检测到可投掷物品: {selectedStack.Item.ItemId}，转换到 RunHolding");
            //     ChangeState("RunHolding");
            //     return;
            // }
            
            if (IsAttackTriggered() && Actor.AttackTimer <= 0)
            {
                Player.RequestAttackFromState(Name);
                ChangeState("Attack");
                return;
            }
            
            // Stop running if shift is released
            if (!IsActionPressed("run"))
            {
                ChangeState("Walk");
                return;
            }
            
            Vector2 input = GetMovementInput();
            
            if (input == Vector2.Zero)
            {
                ChangeState("Idle");
                return;
            }
            
            // Run Logic (2x Speed)
            Vector2 velocity = Actor.Velocity;
            velocity.X = input.X * (Actor.Speed * 2.0f);
            velocity.Y = input.Y * (Actor.Speed * 2.0f);
            
            Actor.Velocity = velocity;
            
            if (input.X != 0)
            {
                Actor.FlipFacing(input.X > 0);
            }
            
            Actor.MoveAndSlide();
            Actor.ClampPositionToScreen();
        }
        
        /// <summary>
        /// 处理 Spine 特效事件回调（来自 SpineController.gd 的 effect_triggered 信号）
        /// </summary>
        private void _OnEffectTriggered(string animationName, float eventTime)
        {
            // 只在运行动画中生成灰尘
            if (Player is MainCharacter mainChar && animationName == mainChar.RunAnimationName)
            {
                SpawnDustAtFeet();
            }
        }
        
        /// <summary>
        /// 在玩家脚下生成灰尘特效
        /// </summary>
        private void SpawnDustAtFeet()
        {
            if (_dustEffectScene == null)
                return;
            
            var dust = _dustEffectScene.Instantiate<Node2D>();
            if (dust == null)
                return;
            
            // 在玩家脚下生成灰尘
            dust.GlobalPosition = Actor.GlobalPosition + Vector2.Down * DustEffectYOffset;
            
            // 获取粒子系统并设置发散方向为移动反方向
            if (dust is Node particleNode)
            {
                var processMaterial = particleNode.Get("process_material");
                if (processMaterial.VariantType == Variant.Type.Object)
                {
                    var material = (Resource)processMaterial;
                    if (material != null)
                    {
                        // 获取当前移动速度方向
                        Vector2 moveDirection = Actor.Velocity.Normalized();
                        
                        // 灰尘发散方向是移动的反方向
                        Vector2 dustDirection = -moveDirection;
                        
                        // 如果静止，默认向下后方发散
                        if (dustDirection == Vector2.Zero)
                        {
                            dustDirection = new Vector2(0, -1);
                        }
                        
                        // 转换为 Vector3（Z 轴为 0）
                        material.Set("direction", new Vector3(dustDirection.X, dustDirection.Y, 0));
                        
                        //GD.Print($"[PlayerRunState] 灰尘方向设置为: {dustDirection}");
                    }
                }
            }
            
            // 添加到玩家的父节点（世界）
            Actor.GetParent()?.AddChild(dust);
            
            //GD.Print($"[PlayerRunState] 灰尘特效生成于: {dust.GlobalPosition}");
        }
    }
}

