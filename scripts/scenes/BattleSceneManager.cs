using Godot;
using Kuros.Core;
using Kuros.Managers;
using Kuros.UI;

namespace Kuros.Scenes
{
    /// <summary>
    /// 战斗场景管理器 - 负责管理战斗场景的UI加载和连接
    /// 可以附加到战斗场景的根节点
    /// </summary>
    public partial class BattleSceneManager : Node2D
    {
        [ExportCategory("References")]
        [Export] public GameActor Player { get; private set; } = null!;

        [ExportCategory("UI Settings")]
        [Export] public bool AutoLoadHUD = true;
        [Export] public bool AutoLoadMenu = true;

        private BattleHUD? _battleHUD;
        private BattleMenu? _battleMenu;

        public override void _Ready()
        {
            // 延迟查找Player和加载UI，确保场景树完全构建
            CallDeferred(MethodName.InitializeBattleScene);
        }

        private void InitializeBattleScene()
        {
            // 如果没有指定玩家，尝试查找
            if (Player == null)
            {
                // 尝试多种路径查找Player节点
                var foundPlayer = GetNodeOrNull<GameActor>("Player");
                
                if (foundPlayer == null)
                {
                    // 尝试从父节点查找
                    var parent = GetParent();
                    if (parent != null)
                    {
                        foundPlayer = parent.GetNodeOrNull<GameActor>("Player");
                    }
                }
                
                if (foundPlayer == null)
                {
                    // 尝试在整个场景树中查找
                    var playerInGroup = GetTree().GetFirstNodeInGroup("player");
                    if (playerInGroup != null)
                    {
                        foundPlayer = playerInGroup as GameActor;
                    }
                }
                
                if (foundPlayer == null)
                {
                    GD.Print("BattleSceneManager: 警告 - 未找到Player节点！UI将正常加载，但不会连接玩家数据。");
                    GD.Print("提示：可以在Inspector中手动指定Player节点，或确保场景中有名为'Player'的节点。");
                }
                else
                {
                    Player = foundPlayer;
                    GD.Print($"BattleSceneManager: 找到Player节点: {Player.Name}");
                }
            }

            // 加载UI
            LoadUIs();
        }

        private void LoadUIs()
        {
            // 加载UI
            if (AutoLoadHUD)
            {
                LoadHUD();
            }

            if (AutoLoadMenu)
            {
                LoadMenu();
            }
        }

        /// <summary>
        /// 加载战斗HUD
        /// </summary>
        public void LoadHUD()
        {
            if (UIManager.Instance == null)
            {
                GD.PrintErr("BattleSceneManager: UIManager未初始化！请在project.godot中将UIManager添加为autoload。");
                return;
            }

            _battleHUD = UIManager.Instance.LoadBattleHUD();
            
            if (_battleHUD != null)
            {
                // 如果找到了Player，连接它
                if (Player != null && Player is SamplePlayer samplePlayer)
                {
                    _battleHUD.SetPlayer(samplePlayer);
                    _battleHUD.ConnectToPlayer(samplePlayer);
                    
                    // 初始化显示
                    _battleHUD.UpdateStats(
                        samplePlayer.CurrentHealth,
                        samplePlayer.MaxHealth,
                        0 // 初始分数
                    );
                }
                else
                {
                    // 即使没有Player，也显示默认的HUD
                    _battleHUD.UpdateStats(100, 100, 0);
                    GD.Print("BattleSceneManager: HUD已加载，但未连接玩家数据。");
                }
            }
        }

        /// <summary>
        /// 加载战斗菜单
        /// </summary>
        public void LoadMenu()
        {
            if (UIManager.Instance == null)
            {
                GD.PrintErr("BattleSceneManager: UIManager未初始化！");
                return;
            }

            _battleMenu = UIManager.Instance.LoadBattleMenu();
            
            if (_battleMenu != null)
            {
                // 连接菜单信号
                _battleMenu.ResumeRequested += OnMenuResume;
                _battleMenu.QuitRequested += OnMenuQuit;
            }
        }

        /// <summary>
        /// 卸载所有UI
        /// </summary>
        public void UnloadAllUI()
        {
            if (UIManager.Instance == null) return;

            if (_battleHUD != null && Player is SamplePlayer samplePlayer)
            {
                _battleHUD.DisconnectFromPlayer(samplePlayer);
            }

            UIManager.Instance.UnloadBattleHUD();
            UIManager.Instance.UnloadBattleMenu();

            _battleHUD = null;
            _battleMenu = null;
        }

        private void OnMenuResume()
        {
            // 菜单关闭逻辑已在BattleMenu中处理
        }

        private void OnMenuQuit()
        {
            // 可以在这里添加返回主菜单的逻辑
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        }

        public override void _ExitTree()
        {
            // 场景退出时清理UI
            UnloadAllUI();
        }
    }
}

