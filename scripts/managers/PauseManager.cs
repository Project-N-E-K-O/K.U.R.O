using Godot;
using System.Threading;

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
		/// 使用原子操作确保线程安全
		/// </summary>
		public void PushPause()
		{
			int newValue = Interlocked.Increment(ref _pauseCount);
			UpdatePauseState(newValue);
		}

		/// <summary>
		/// 取消暂停请求（减少暂停计数）
		/// 使用原子CAS循环确保线程安全，并防止计数器变为负数
		/// </summary>
		public void PopPause()
		{
			int currentValue;
			int newValue;
			
			// 使用CAS循环原子地执行"递减并钳制到零"操作
			do
			{
				currentValue = Volatile.Read(ref _pauseCount);
				newValue = currentValue > 0 ? currentValue - 1 : 0;
			}
			while (Interlocked.CompareExchange(ref _pauseCount, newValue, currentValue) != currentValue);
			
			UpdatePauseState(newValue);
		}

		/// <summary>
		/// 更新实际的暂停状态
		/// 使用CallDeferred确保SceneTree操作在主线程执行
		/// </summary>
		/// <param name="pauseCount">当前的暂停计数值（用于避免重复读取）</param>
		private void UpdatePauseState(int pauseCount)
		{
			// 将暂停状态更新推迟到主线程执行
			CallDeferred(nameof(SetPauseStateDeferred), pauseCount > 0);
		}

		/// <summary>
		/// 在主线程上设置暂停状态
		/// 此方法只应通过CallDeferred调用
		/// </summary>
		private void SetPauseStateDeferred(bool shouldPause)
		{
			if (_tree == null)
			{
				_tree = GetTree();
			}

			if (_tree != null)
			{
				_tree.Paused = shouldPause;
			}
		}

		/// <summary>
		/// 检查当前是否处于暂停状态
		/// 使用易失性读取确保线程安全
		/// </summary>
		public bool IsPaused => Volatile.Read(ref _pauseCount) > 0;

		/// <summary>
		/// 获取当前暂停计数（用于调试）
		/// 使用易失性读取确保线程安全
		/// </summary>
		public int PauseCount => Volatile.Read(ref _pauseCount);

		/// <summary>
		/// 强制清除所有暂停请求（用于场景切换等特殊情况）
		/// 使用原子操作确保线程安全
		/// </summary>
		public void ClearAllPauses()
		{
			Interlocked.Exchange(ref _pauseCount, 0);
			UpdatePauseState(0);
		}
	}
}

