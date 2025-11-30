using Godot;

namespace Kuros.Managers
{
	/// <summary>
	/// 暂停管理器 - 集中管理游戏暂停状态
	/// 使用计数器机制，支持多个组件同时请求暂停
	/// 需要在project.godot中配置为autoload
	/// </summary>
	public partial class PauseManager : Node
	{
		public static PauseManager Instance { get; private set; } = null!;

		private int _pauseCount = 0;
		private SceneTree? _tree;

		public override void _Ready()
		{
			if (Instance != null && Instance != this)
			{
				QueueFree();
				return;
			}

			Instance = this;
			_tree = GetTree();
		}

		/// <summary>
		/// 请求暂停游戏（增加暂停计数）
		/// </summary>
		public void PushPause()
		{
			_pauseCount++;
			UpdatePauseState();
		}

		/// <summary>
		/// 取消暂停请求（减少暂停计数）
		/// </summary>
		public void PopPause()
		{
			if (_pauseCount > 0)
			{
				_pauseCount--;
			}
			UpdatePauseState();
		}

		/// <summary>
		/// 更新实际的暂停状态
		/// </summary>
		private void UpdatePauseState()
		{
			if (_tree == null)
			{
				_tree = GetTree();
			}

			if (_tree != null)
			{
				_tree.Paused = _pauseCount > 0;
			}
		}

		/// <summary>
		/// 检查当前是否处于暂停状态
		/// </summary>
		public bool IsPaused => _pauseCount > 0;

		/// <summary>
		/// 获取当前暂停计数（用于调试）
		/// </summary>
		public int PauseCount => _pauseCount;

		/// <summary>
		/// 强制清除所有暂停请求（用于场景切换等特殊情况）
		/// </summary>
		public void ClearAllPauses()
		{
			_pauseCount = 0;
			UpdatePauseState();
		}
	}
}

