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
		
		// 存储 Callable 实例，用于 IsConnected 检查和 Connect/Disconnect
		// 在 _Ready 中初始化，使用延迟初始化模式
		private Callable? _onLoadingScreenCompleteCallable;
		
		public override void _Ready()
		{
			// 初始化 Callable 实例，用于信号连接检查
			_onLoadingScreenCompleteCallable = new Callable(this, MethodName.OnLoadingScreenComplete);
		}
		
		/// <summary>
		/// 获取或创建 Callable 实例（延迟初始化）
		/// </summary>
		private Callable GetOnLoadingScreenCompleteCallable()
		{
			if (_onLoadingScreenCompleteCallable == null)
			{
				_onLoadingScreenCompleteCallable = new Callable(this, MethodName.OnLoadingScreenComplete);
			}
			// 使用显式转换，因为上面已经确保不为空
			return (Callable)_onLoadingScreenCompleteCallable;
		}
		
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
				
				// 连接完成信号（使用存储的 Callable 实例进行 IsConnected 检查和 Connect）
				var callable = GetOnLoadingScreenCompleteCallable();
				if (!_loadingScreen.IsConnected(LoadingScreen.SignalName.LoadingComplete, callable))
				{
					_loadingScreen.Connect(LoadingScreen.SignalName.LoadingComplete, callable);
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

