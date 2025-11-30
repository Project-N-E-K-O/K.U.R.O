using Godot;
using Kuros.Managers;
using Kuros.UI;

namespace Kuros.UI
{
	/// <summary>
	/// 加载测试管理器 - 用于测试加载页面功能
	/// </summary>
	public partial class LoadingTestManager : Node
	{
		private LoadingScreen? _loadingScreen;
		private bool _isLoading = false;
		
		/// <summary>
		/// 开始加载测试
		/// </summary>
		public void StartLoadingTest()
		{
			if (_isLoading)
			{
				GD.Print("LoadingTestManager: 正在加载中，请等待...");
				return;
			}
			
			_isLoading = true;
			
			// 显示加载屏幕
			ShowLoadingScreen();
			
			// 模拟加载过程（延迟3秒后完成）
			var timer = GetTree().CreateTimer(3.0f);
			timer.Timeout += OnLoadingComplete;
		}
		
		/// <summary>
		/// 显示加载屏幕
		/// </summary>
		private void ShowLoadingScreen()
		{
			if (UIManager.Instance == null)
			{
				GD.PrintErr("LoadingTestManager: UIManager未初始化！");
				_isLoading = false;
				return;
			}
			
			// 加载或获取加载屏幕
			_loadingScreen = UIManager.Instance.LoadLoadingScreen();
			
			if (_loadingScreen != null)
			{
				_loadingScreen.ShowLoading();
				
				// 连接完成信号
				if (!_loadingScreen.IsConnected(LoadingScreen.SignalName.LoadingComplete, new Callable(this, MethodName.OnLoadingScreenComplete)))
				{
					_loadingScreen.LoadingComplete += OnLoadingScreenComplete;
				}
			}
			else
			{
				GD.PrintErr("LoadingTestManager: 无法加载加载屏幕！");
				_isLoading = false;
			}
		}
		
		/// <summary>
		/// 加载完成回调
		/// </summary>
		private void OnLoadingComplete()
		{
			if (_loadingScreen != null)
			{
				_loadingScreen.SetLoadingComplete();
			}
		}
		
		/// <summary>
		/// 加载屏幕完成回调
		/// </summary>
		private void OnLoadingScreenComplete()
		{
			GD.Print("加载成功！");
			
			// 等待一小段时间让用户看到100%，然后隐藏加载屏幕并返回主菜单
			var timer = GetTree().CreateTimer(0.5f);
			timer.Timeout += ReturnToMainMenu;
		}
		
		/// <summary>
		/// 返回主菜单
		/// </summary>
		private void ReturnToMainMenu()
		{
			if (_loadingScreen != null)
			{
				_loadingScreen.HideLoading();
			}
			
			_isLoading = false;
			
			// 返回主菜单
			var tree = GetTree();
			if (tree != null)
			{
				tree.ChangeSceneToFile("res://scenes/MainMenu.tscn");
			}
		}
	}
}

