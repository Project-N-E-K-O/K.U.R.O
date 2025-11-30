using Godot;
using System;
using Kuros.Systems.FSM;
using Kuros.Core;
using Kuros.Managers;

namespace Kuros.Actors.Heroes.States
{
    public partial class PlayerState : State
    {
        protected SamplePlayer Player => (SamplePlayer)Actor;
        
        protected Vector2 GetMovementInput()
        {
            return Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        }
        
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

