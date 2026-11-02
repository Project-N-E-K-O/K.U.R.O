using Godot;
using System;
using System.Collections.Generic;
using Kuros.Systems.FSM;
using Kuros.Core;
using Kuros.Managers;
using Kuros.Actors.Heroes;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerState : State
    {
        [ExportCategory("Input Buffer")]
        [Export(PropertyHint.Range, "0.05,1,0.01")] public float BufferWindow = 0.3f;
        [Export] public int DashPriority = 3;
        [Export] public int AttackPriority = 2;

        private struct BufferedInput
        {
            public string Action;
            public int Priority;
            public float Remaining;
        }

        private readonly List<BufferedInput> _inputBuffer = new();
        public void ClearBufferedInputs() => _inputBuffer.Clear();

        protected SamplePlayer Player => (SamplePlayer)Actor;

        /// <summary>缓冲一个预输入，在可打断阶段自动消费。</summary>
        protected void BufferInput(string action, int priority)
        {
            for (int i = _inputBuffer.Count - 1; i >= 0; i--)
            {
                if (_inputBuffer[i].Action == action)
                    _inputBuffer.RemoveAt(i);
            }
            _inputBuffer.Add(new BufferedInput { Action = action, Priority = priority, Remaining = BufferWindow });
        }

        /// <summary>取出缓冲中优先级最高的动作，无则返回 null。</summary>
        protected string? ConsumeBufferedInput()
        {
            if (_inputBuffer.Count == 0) return null;
            int best = 0;
            for (int i = 1; i < _inputBuffer.Count; i++)
                if (_inputBuffer[i].Priority > _inputBuffer[best].Priority)
                    best = i;
            var action = _inputBuffer[best].Action;
            _inputBuffer.Clear();
            return action;
        }

        public override void Update(double delta)
        {
            for (int i = _inputBuffer.Count - 1; i >= 0; i--)
            {
                var b = _inputBuffer[i];
                b.Remaining -= (float)delta;
                if (b.Remaining <= 0f)
                    _inputBuffer.RemoveAt(i);
                else
                    _inputBuffer[i] = b;
            }
        }
        
        /// <summary>
        /// 播放动画（自动检测是使用 AnimationPlayer 还是 Spine 动画）
        /// 如果是 MainCharacter，使用 Spine 动画；否则使用 AnimationPlayer
        /// </summary>
        protected void PlayAnimation(string animName, bool loop = true, float timeScale = 1.0f)
        {
            if (Player is MainCharacter mainChar)
            {
                // 使用 Spine 动画
                mainChar.PlaySpineAnimation(animName, loop, timeScale);
            }
            else if (Actor.AnimPlayer != null)
            {
                // 使用 AnimationPlayer
                if (Actor.AnimPlayer.HasAnimation(animName))
                {
                    Actor.AnimPlayer.Play(animName);
                    Actor.AnimPlayer.SpeedScale = timeScale;
                    var anim = Actor.AnimPlayer.GetAnimation(animName);
                    if (anim != null)
                    {
                        anim.LoopMode = loop ? Animation.LoopModeEnum.Linear : Animation.LoopModeEnum.None;
                    }
                }
            }
        }
        
        protected Vector2 GetMovementInput()
        {
            return Player.GetControlledMovementInput();
        }

        protected bool IsActionPressed(string actionName)
        {
            return Player.IsControlledActionPressed(actionName);
        }

        protected bool IsActionJustPressed(string actionName)
        {
            return Player.ConsumeControlledActionJustPressed(actionName);
        }

        protected bool IsAttackTriggered()
        {
            if (IsActionJustPressed("attack"))
                return true;

            var skill = Player.WeaponSkillController?.GetPrimarySkillDefinition();
            if ((skill == null || skill.AllowHoldContinuousAttack) && IsActionPressed("attack"))
                return true;

            return false;
        }

        protected bool IsActionLongPressHeld(string actionName)
            => Player.IsActionLongPressHeld(actionName);

        protected bool WasActionLongPressTriggered(string actionName)
            => Player.WasActionLongPressTriggered(actionName);

        protected bool WasActionShortPressed(string actionName)
            => Player.WasActionShortPressed(actionName);

        protected bool WasActionJustPressed(string actionName)
            => Player.WasActionJustPressed(actionName);

        protected float GetActionHoldDuration(string actionName)
            => Player.GetActionHoldDuration(actionName);

        /// <summary>
        /// 检查是否应该处理玩家输入（移动和攻击）
        /// 如果对话正在进行或刚刚结束，则返回false，阻止移动和攻击输入
        /// 但保留ESC和Space等对话功能键
        /// </summary>
        protected bool ShouldProcessPlayerInput()
        {
            // 如果对话管理器存在，检查是否应该阻止输入
            if (DialogueManager.Instance != null)
            {
                // 检查对话是否正在进行或刚刚结束
                if (DialogueManager.Instance.ShouldBlockPlayerInput())
                {
                    return false;
                }
            }
            return true;
        }
        
        /// <summary>
        /// 处理对话门控逻辑：如果对话正在进行，减速并切换到Idle状态
        /// </summary>
        /// <param name="delta">帧时间增量</param>
        /// <returns>如果输入被阻止（对话中）返回true，否则返回false</returns>
        protected bool HandleDialogueGating(double delta)
        {
            if (!ShouldProcessPlayerInput())
            {
                // 对话中时，停止移动并切换到Idle状态
                Actor.Velocity = Actor.Velocity.MoveToward(Vector2.Zero, Actor.Speed * 2 * (float)delta);
                Actor.MoveAndSlide();
                if (Actor.Velocity.Length() < 1.0f && Name != "Idle")
                {
                    ChangeState("Idle");
                }
                return true;
            }
            return false;
        }
    }
}

