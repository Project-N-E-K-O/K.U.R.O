using Godot;
using System;
using Kuros.Systems.FSM;
using Kuros.Core;
using Kuros.Managers;

namespace Kuros.Actors.Heroes.States
{
	/// <summary>
	/// MainCharacter 专用的状态基类
	/// 使用 Spine 动画而不是 AnimationPlayer
	/// PlayerState 的 PlayAnimation 方法会自动检测 MainCharacter 并使用 Spine 动画
	/// </summary>
	public abstract partial class MainCharacterState : PlayerState
	{
		protected MainCharacter MainCharacter => (MainCharacter)Player;
		
		// 注意：不需要重写 PlayAnimation，因为 PlayerState.PlayAnimation 已经会自动检测 MainCharacter
	}
}
