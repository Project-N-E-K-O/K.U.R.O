using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using Kuros.Scenes;
using Kuros.Systems.Inventory;
using Kuros.Actors.Heroes;
using Kuros.Items;

namespace Kuros.Managers
{
    /// <summary>
    /// 存档管理器 - 负责游戏的保存和加载
    /// </summary>
    public partial class SaveManager : Node
    {
        public static SaveManager? Instance { get; private set; }

        private const string SaveDirectoryName = "saves";
        private const string SaveFilePrefix = "save_";
        private const string SaveFileExtension = ".save";
        private const int SAVE_FORMAT_VERSION = 1;
        private string _saveDirectory = "";
        
        // 游戏时间追踪
        private int _totalPlayTimeSeconds = 0;
        private double _accumulatedDelta = 0.0;

        /// <summary>
        /// 当前加载的游戏数据（从存档加载后存储，供场景使用）
        /// </summary>
        public GameSaveData? CurrentGameData { get; private set; }

        /// <summary>
        /// 是否有待应用的游戏数据（从存档加载但尚未应用到游戏状态）
        /// </summary>
        public bool HasPendingGameData => CurrentGameData != null;

        /// <summary>
        /// 下一个目标场景路径，由各 Stage 出口触发器写入，电梯场景读取后加载。
        /// 用完后请自行清空（赋为空字符串），以免误用。
        /// </summary>
        public string PendingNextStagePath { get; set; } = "";

        public override void _Ready()
        {
            Instance = this;
            // 获取项目根目录路径
            _saveDirectory = GetProjectRootPath();
            // 确保存档目录存在
            EnsureSaveDirectoryExists();
            
            // 初始化游戏时间追踪
            _accumulatedDelta = 0.0;
            
#if DEBUG
            // 创建测试存档（槽位0，所有信息都是1）- 仅在调试模式下执行
            CreateTestSave(0);
#endif
        }
        
        public override void _Process(double delta)
        {
            // 更新游戏时间（仅在游戏未暂停时）
            if (PauseManager.Instance == null || !PauseManager.Instance.IsPaused)
            {
                _accumulatedDelta += delta;
                // 当累积的 delta 达到或超过 1 秒时，增加游戏时间
                if (_accumulatedDelta >= 1.0)
                {
                    int secondsToAdd = (int)_accumulatedDelta;
                    _totalPlayTimeSeconds += secondsToAdd;
                    _accumulatedDelta -= secondsToAdd;
                }
            }
        }

        /// <summary>
        /// 获取项目根目录路径
        /// </summary>
        private string GetProjectRootPath()
        {
            // 获取用户数据路径（user://），在导出构建中可写
            string userPath = ProjectSettings.GlobalizePath("user://");
            // 移除末尾的斜杠（如果有）
            if (userPath.EndsWith("/") || userPath.EndsWith("\\"))
            {
                userPath = userPath.Substring(0, userPath.Length - 1);
            }
            // 返回用户数据目录下的 saves 目录，使用 Path.Combine 确保跨平台路径分隔符正确
            return Path.Combine(userPath, SaveDirectoryName);
        }

        /// <summary>
        /// 确保存档目录存在
        /// </summary>
        private void EnsureSaveDirectoryExists()
        {
            if (!DirAccess.DirExistsAbsolute(_saveDirectory))
            {
                DirAccess.MakeDirRecursiveAbsolute(_saveDirectory);
                GD.Print($"SaveManager: 创建存档目录: {_saveDirectory}");
            }
        }

        /// <summary>
        /// 获取存档文件路径
        /// </summary>
        private string GetSaveFilePath(int slotIndex)
        {
            string fileName = $"{SaveFilePrefix}{slotIndex}{SaveFileExtension}";
            return Path.Combine(_saveDirectory, fileName);
        }

        /// <summary>
        /// 检查存档是否存在
        /// </summary>
        public bool HasSave(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= 12) return false;
            string filePath = GetSaveFilePath(slotIndex);
            return Godot.FileAccess.FileExists(filePath);
        }

        /// <summary>
        /// 保存游戏数据
        /// </summary>
        public bool SaveGame(int slotIndex, GameSaveData data)
        {
            if (slotIndex < 0 || slotIndex >= 12)
            {
                GD.PrintErr($"SaveManager: 无效的存档槽位: {slotIndex}");
                return false;
            }

            string filePath = GetSaveFilePath(slotIndex);
            
            using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Write);
            if (file == null)
            {
                GD.PrintErr($"SaveManager: 无法创建存档文件: {filePath}");
                return false;
            }

            // 确保存档数据包含正确的槽位索引
            data.SlotIndex = slotIndex;

            // 保存数据为JSON格式，包含版本号以支持未来迁移
            var savePayload = new Godot.Collections.Dictionary<string, Variant>
            {
                { "version", SAVE_FORMAT_VERSION },
                { "data", data.ToDictionary() }
            };
            var json = Json.Stringify(savePayload);
            file.StoreString(json);
            file.Close();

            GD.Print($"SaveManager: 成功保存到槽位 {slotIndex}: {filePath}");
            return true;
        }

        /// <summary>
        /// 加载游戏数据
        /// </summary>
        public GameSaveData? LoadGame(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= 12)
            {
                GD.PrintErr($"SaveManager: 无效的存档槽位: {slotIndex}");
                return null;
            }

            string filePath = GetSaveFilePath(slotIndex);
            
            if (!Godot.FileAccess.FileExists(filePath))
            {
                GD.Print($"SaveManager: 存档文件不存在: {filePath}");
                return null;
            }

            using var file = Godot.FileAccess.Open(filePath, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                GD.PrintErr($"SaveManager: 无法打开存档文件: {filePath}");
                return null;
            }

            string json = file.GetAsText();
            file.Close();

            var jsonResult = Json.ParseString(json);
            if (jsonResult.VariantType != Variant.Type.Dictionary)
            {
                GD.PrintErr($"SaveManager: 存档文件格式错误: {filePath}");
                return null;
            }

            var dict = jsonResult.AsGodotDictionary();
            if (dict == null || dict.Count == 0)
            {
                GD.PrintErr($"SaveManager: 存档文件格式错误: {filePath}");
                return null;
            }

            var typedDict = new Godot.Collections.Dictionary<string, Variant>();
            foreach (var key in dict.Keys)
            {
                if (key.VariantType == Variant.Type.String)
                {
                    typedDict[key.AsString()] = dict[key];
                }
                else
                {
                    GD.PushWarning($"SaveManager: 存档文件包含非字串類型的鍵，已跳過。鍵值: '{key}', 類型: {key.VariantType}, 檔案: {filePath}");
                }
            }

            // 读取版本号（如果存在），支持向后兼容旧格式
            int version = 0;
            if (typedDict.ContainsKey("version"))
            {
                version = typedDict["version"].AsInt32();
            }

            // 根据版本号提取数据
            Godot.Collections.Dictionary<string, Variant> dataDict;
            if (version > 0)
            {
                // 新格式：包含版本号和data字段
                if (!typedDict.ContainsKey("data"))
                {
                    GD.PrintErr($"SaveManager: 存档文件格式错误（缺少data字段）: {filePath}");
                    return null;
                }
                var dataVariant = typedDict["data"];
                if (dataVariant.VariantType != Variant.Type.Dictionary)
                {
                    GD.PrintErr($"SaveManager: 存档文件格式错误（data字段类型错误）: {filePath}");
                    return null;
                }
                var dataDictRaw = dataVariant.AsGodotDictionary();
                dataDict = new Godot.Collections.Dictionary<string, Variant>();
                foreach (var key in dataDictRaw.Keys)
                {
                    if (key.VariantType == Variant.Type.String)
                    {
                        dataDict[key.AsString()] = dataDictRaw[key];
                    }
                    else
                    {
                        GD.PushWarning($"SaveManager: 存档 data 字段包含非字串類型的鍵，已跳過。鍵值: '{key}', 類型: {key.VariantType}, 檔案: {filePath}");
                    }
                }
            }
            else
            {
                // 旧格式：直接是GameSaveData字典（向后兼容）
                dataDict = typedDict;
            }

            var data = GameSaveData.FromDictionary(dataDict);
            GD.Print($"SaveManager: 成功加载槽位 {slotIndex} (格式版本: {version}): {filePath}");
            return data;
        }

        /// <summary>
        /// 获取存档槽位数据（用于显示）
        /// </summary>
        public SaveSlotDisplayData GetSaveSlotData(int slotIndex)
        {
            if (!HasSave(slotIndex))
            {
                return new SaveSlotDisplayData
                {
                    SlotIndex = slotIndex,
                    HasSave = false
                };
            }

            var gameData = LoadGame(slotIndex);
            if (gameData == null)
            {
                return new SaveSlotDisplayData
                {
                    SlotIndex = slotIndex,
                    HasSave = false
                };
            }

            return new SaveSlotDisplayData
            {
                SlotIndex = slotIndex,
                HasSave = true,
                SaveName = $"存档 {slotIndex + 1}",
                SaveTime = gameData.SaveTime,
                PlayTime = FormatPlayTime(gameData.PlayTimeSeconds),
                Level = gameData.Level,
                CurrentHealth = gameData.CurrentHealth,
                MaxHealth = gameData.MaxHealth,
                WeaponName = gameData.WeaponName,
                LevelProgress = gameData.LevelProgress
            };
        }

        /// <summary>
        /// 格式化游戏时间
        /// </summary>
        private string FormatPlayTime(int seconds)
        {
            int hours = seconds / 3600;
            int minutes = (seconds % 3600) / 60;
            int secs = seconds % 60;
            return $"{hours:D2}:{minutes:D2}:{secs:D2}";
        }

        /// <summary>
        /// 场景切换时的背包过渡快照。
        /// 由出口触发器在调用 ChangeScene 前写入，新场景的 BattleSceneManager 读取后清空。
        /// </summary>
        public InventoryTransitData? PendingInventoryTransit { get; set; }

        /// <summary>
        /// 从当前场景的玩家背包组件中生成过渡快照，并存入 PendingInventoryTransit。
        /// </summary>
        public void CaptureInventoryTransit(SamplePlayer player)
        {
            if (player == null) return;
            var inv = player.InventoryComponent;
            if (inv == null) return;
            var data = InventoryTransitData.CaptureFrom(inv);
            // 直接从 player 拿 HP，不依赖 GetParent()
            data.CurrentHealth = player.CurrentHealth;
            data.MaxHealth     = player.MaxHealth;
            PendingInventoryTransit = data;
        }

        private void CreateTestSave(int slotIndex)
        {
            if (HasSave(slotIndex))
            {
                // 如果已存在，不覆盖
                return;
            }

            var testData = new GameSaveData
            {
                SlotIndex = slotIndex,
                SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PlayTimeSeconds = 1,
                Level = 1,
                CurrentHealth = 1,
                MaxHealth = 1,
                WeaponName = "1",
                LevelProgress = "1"
            };

            SaveGame(slotIndex, testData);
            GD.Print($"SaveManager: 创建测试存档，槽位 {slotIndex}，所有信息都是1");
        }

        /// <summary>
        /// 设置当前游戏数据（从存档加载后调用）
        /// </summary>
        public void SetCurrentGameData(GameSaveData? data)
        {
            CurrentGameData = data;
            if (data != null)
            {
                // 恢复游戏时间
                _totalPlayTimeSeconds = data.PlayTimeSeconds;
                _accumulatedDelta = 0.0;
                GD.Print($"SaveManager: 已设置当前游戏数据，槽位: {data.SlotIndex}");
            }
            else
            {
                GD.Print("SaveManager: 已清除当前游戏数据");
            }
        }
        
        /// <summary>
        /// 重置游戏时间（新游戏开始时调用）
        /// </summary>
        public void ResetPlayTime()
        {
            _totalPlayTimeSeconds = 0;
            _accumulatedDelta = 0.0;
        }

        /// <summary>
        /// 清除当前游戏数据（场景切换或新游戏开始时调用）
        /// </summary>
        public void ClearCurrentGameData()
        {
            CurrentGameData = null;
        }

        /// <summary>
        /// 获取当前游戏数据（用于保存）
        /// </summary>
        public GameSaveData GetCurrentGameData()
        {
            // 获取玩家实例
            SamplePlayer? player = null;
            if (GetTree() != null)
            {
                player = GetTree().GetFirstNodeInGroup("player") as SamplePlayer;
            }
            
            // 获取玩家生命值
            int currentHealth = 1;
            int maxHealth = 1;
            if (player != null)
            {
                currentHealth = player.CurrentHealth;
                maxHealth = player.MaxHealth;
            }
            
            // 获取装备的武器名称
            string weaponName = "无";
            if (player != null && player.InventoryComponent != null)
            {
                var weaponSlot = player.InventoryComponent.GetSpecialSlot(SpecialInventorySlotIds.PrimaryWeapon);
                if (weaponSlot != null && !weaponSlot.IsEmpty && weaponSlot.Stack != null)
                {
                    weaponName = weaponSlot.Stack.Item.DisplayName;
                }
            }
            
            // 获取关卡名称
            string levelName = "未知关卡";
            int level = 1; // TODO: Derive actual level number from game state (e.g., from a LevelManager or scene progression system)
            string levelProgress = "未知";
            
            if (GetTree() != null)
            {
                // 尝试查找 BattleSceneManager
                BattleSceneManager? sceneManager = null;
                
                // 首先尝试从当前场景根节点查找
                if (GetTree().CurrentScene != null)
                {
                    sceneManager = GetTree().CurrentScene.GetNodeOrNull<BattleSceneManager>(".");
                }
                
                // 如果没找到，尝试在整个场景树中查找
                if (sceneManager == null)
                {
                    var allNodes = GetTree().GetNodesInGroup("scene_manager");
                    foreach (var node in allNodes)
                    {
                        if (node is BattleSceneManager bsm)
                        {
                            sceneManager = bsm;
                            break;
                        }
                    }
                }
                
                // 如果还是没找到，尝试递归查找
                if (sceneManager == null && GetTree().CurrentScene != null)
                {
                    sceneManager = GetTree().CurrentScene.GetNodeOrNull<BattleSceneManager>("BattleSceneManager");
                }
                
                if (sceneManager != null && !string.IsNullOrEmpty(sceneManager.LevelName))
                {
                    levelName = sceneManager.LevelName;
                    levelProgress = levelName;
                }
                else if (GetTree().CurrentScene != null)
                {
                    // 使用场景名称作为关卡名称
                    levelName = GetTree().CurrentScene.Name;
                    levelProgress = levelName;
                }
            }
            
            // 游戏时间已在 _Process 中持续更新，这里不需要额外处理
            
            return new GameSaveData
            {
                SlotIndex = -1, // 占位符，SaveGame() 会在序列化前设置正确的槽位索引
                SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PlayTimeSeconds = _totalPlayTimeSeconds,
                Level = level,
                CurrentHealth = currentHealth,
                MaxHealth = maxHealth,
                WeaponName = weaponName,
                LevelProgress = levelProgress
            };
        }
    }

    /// <summary>
    /// 游戏存档数据
    /// </summary>
    /// <summary>
    /// 场景切换时用于跨场景传递背包状态的内存快照（不持久化到磁盘）。
    /// 保存快捷栏、背包各槽的物品路径与数量，以及当前选中槽位。
    /// </summary>
    public class InventoryTransitData
    {
        public record SlotEntry(string ItemPath, int Quantity);

        public List<SlotEntry?> QuickBarSlots  { get; } = new();
        public List<SlotEntry?> BackpackSlots  { get; } = new();
        public SlotEntry?       FurnitureSlot  { get; set; }
        public int              SelectedQuickBarSlot { get; set; }

        /// <summary>跨场景保留血量。</summary>
        public int CurrentHealth { get; set; }
        public int MaxHealth     { get; set; }

        /// <summary>从玩家背包组件生成快照。</summary>
        public static InventoryTransitData CaptureFrom(PlayerInventoryComponent inv)
        {
            var data = new InventoryTransitData
            {
                SelectedQuickBarSlot = inv.SelectedQuickBarSlot
            };
            // 注意：HP 由 CaptureInventoryTransit 调用方直接写入，此处不获取

            // 快捷栏
            if (inv.QuickBar != null)
            {
                foreach (var stack in inv.QuickBar.Slots)
                {
                    if (stack == null || stack.IsEmpty || string.IsNullOrEmpty(stack.Item.ResourcePath))
                        data.QuickBarSlots.Add(null);
                    else
                        data.QuickBarSlots.Add(new SlotEntry(stack.Item.ResourcePath, stack.Quantity));
                }
            }

            // 背包
            foreach (var stack in inv.Backpack.Slots)
            {
                if (stack == null || stack.IsEmpty || string.IsNullOrEmpty(stack.Item.ResourcePath))
                    data.BackpackSlots.Add(null);
                else
                    data.BackpackSlots.Add(new SlotEntry(stack.Item.ResourcePath, stack.Quantity));
            }

            // 家具槽
            var fs = inv.FurnitureSlotStack;
            if (fs != null && !fs.IsEmpty && !string.IsNullOrEmpty(fs.Item.ResourcePath))
                data.FurnitureSlot = new SlotEntry(fs.Item.ResourcePath, fs.Quantity);

            return data;
        }

        /// <summary>将快照还原到玩家背包组件。调用方应在 _Ready 完成后调用。</summary>
        public void RestoreTo(PlayerInventoryComponent inv)
        {
            // ── 快捷栏 ──────────────────────────────────────────
            if (inv.QuickBar != null && QuickBarSlots.Count > 0)
            {
                for (int i = 0; i < QuickBarSlots.Count && i < inv.QuickBar.Slots.Count; i++)
                {
                    var entry = QuickBarSlots[i];
                    if (entry == null) continue;
                    var item = ResourceLoader.Load<ItemDefinition>(entry.ItemPath);
                    if (item == null) continue;
                    inv.QuickBar.TryAddItemToSlot(item, entry.Quantity, i);
                }
            }

            // ── 背包 ────────────────────────────────────────────
            for (int i = 0; i < BackpackSlots.Count && i < inv.Backpack.Slots.Count; i++)
            {
                var entry = BackpackSlots[i];
                if (entry == null) continue;
                var item = ResourceLoader.Load<ItemDefinition>(entry.ItemPath);
                if (item == null) continue;
                inv.Backpack.TryAddItemToSlot(item, entry.Quantity, i);
            }

            // ── 家具槽 ──────────────────────────────────────────
            if (FurnitureSlot != null)
            {
                var item = ResourceLoader.Load<ItemDefinition>(FurnitureSlot.ItemPath);
                if (item != null)
                    inv.AddItemSmart(item, FurnitureSlot.Quantity);
            }

            // ── 恢复选中槽位 ────────────────────────────────────
            inv.SelectedQuickBarSlot = SelectedQuickBarSlot;
        }
    }

    public class GameSaveData
    {
        public int SlotIndex { get; set; }
        public string SaveTime { get; set; } = "";
        public int PlayTimeSeconds { get; set; }
        public int Level { get; set; }
        public int CurrentHealth { get; set; }
        public int MaxHealth { get; set; }
        public string WeaponName { get; set; } = "";
        public string LevelProgress { get; set; } = "";

        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            return new Godot.Collections.Dictionary<string, Variant>
            {
                { "SlotIndex", SlotIndex },
                { "SaveTime", SaveTime },
                { "PlayTimeSeconds", PlayTimeSeconds },
                { "Level", Level },
                { "CurrentHealth", CurrentHealth },
                { "MaxHealth", MaxHealth },
                { "WeaponName", WeaponName },
                { "LevelProgress", LevelProgress }
            };
        }

        public static GameSaveData FromDictionary(Godot.Collections.Dictionary<string, Variant> dict)
        {
            return new GameSaveData
            {
                SlotIndex = dict.ContainsKey("SlotIndex") ? dict["SlotIndex"].AsInt32() : 0,
                SaveTime = dict.ContainsKey("SaveTime") ? dict["SaveTime"].AsString() : "",
                PlayTimeSeconds = dict.ContainsKey("PlayTimeSeconds") ? dict["PlayTimeSeconds"].AsInt32() : 0,
                Level = dict.ContainsKey("Level") ? dict["Level"].AsInt32() : 1,
                CurrentHealth = dict.ContainsKey("CurrentHealth") ? dict["CurrentHealth"].AsInt32() : 0,
                MaxHealth = dict.ContainsKey("MaxHealth") ? dict["MaxHealth"].AsInt32() : 0,
                WeaponName = dict.ContainsKey("WeaponName") ? dict["WeaponName"].AsString() : "",
                LevelProgress = dict.ContainsKey("LevelProgress") ? dict["LevelProgress"].AsString() : ""
            };
        }
    }

    /// <summary>
    /// 存档槽位显示数据
    /// </summary>
    public class SaveSlotDisplayData
    {
        public int SlotIndex { get; set; }
        public bool HasSave { get; set; }
        public string SaveName { get; set; } = "";
        public string SaveTime { get; set; } = "";
        public string PlayTime { get; set; } = "";
        public int Level { get; set; }
        public Texture2D? Thumbnail { get; set; }
        public Texture2D? LocationImage { get; set; }
        public int CurrentHealth { get; set; }
        public int MaxHealth { get; set; }
        public string WeaponName { get; set; } = "";
        public string LevelProgress { get; set; } = "";
    }
}

