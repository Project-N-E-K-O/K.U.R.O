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
        /// 如果对话正在进行，则返回false，阻止移动和攻击输入
        /// 但保留ESC和Space等对话功能键
        /// </summary>
        protected bool ShouldProcessPlayerInput()
        {
            // 如果对话管理器存在且对话正在进行，则阻止玩家输入
            if (DialogueManager.Instance != null && DialogueManager.Instance.IsDialogueActive)
            {
                return false;
            }
            return true;
        }
    }
}

