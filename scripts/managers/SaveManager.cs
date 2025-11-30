using Godot;
using System;

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
        private string _saveDirectory = "";

        public override void _Ready()
        {
            Instance = this;
            // 获取项目根目录路径
            _saveDirectory = GetProjectRootPath();
            // 确保存档目录存在
            EnsureSaveDirectoryExists();
            
            // 创建测试存档（槽位0，所有信息都是1）
            CreateTestSave(0);
        }

        /// <summary>
        /// 获取项目根目录路径
        /// </summary>
        private string GetProjectRootPath()
        {
            // 获取项目资源路径（res://）
            string projectPath = ProjectSettings.GlobalizePath("res://");
            // 移除末尾的斜杠（如果有）
            if (projectPath.EndsWith("/") || projectPath.EndsWith("\\"))
            {
                projectPath = projectPath.Substring(0, projectPath.Length - 1);
            }
            // 返回项目根目录下的 saves 目录
            return $"{projectPath}/{SaveDirectoryName}";
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
            return $"{_saveDirectory}/{SaveFilePrefix}{slotIndex}{SaveFileExtension}";
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

            // 保存数据为JSON格式
            var json = Json.Stringify(data.ToDictionary());
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
                typedDict[key.AsString()] = dict[key];
            }

            var data = GameSaveData.FromDictionary(typedDict);
            GD.Print($"SaveManager: 成功加载槽位 {slotIndex}: {filePath}");
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
        /// 创建测试存档（所有信息都是1）
        /// </summary>
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
        /// 获取当前游戏数据（用于保存）
        /// </summary>
        public GameSaveData GetCurrentGameData()
        {
            // TODO: 从实际的游戏状态获取数据
            // 现在返回默认数据
            return new GameSaveData
            {
                SlotIndex = -1,
                SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PlayTimeSeconds = 1,
                Level = 1,
                CurrentHealth = 1,
                MaxHealth = 1,
                WeaponName = "1",
                LevelProgress = "1"
            };
        }
    }

    /// <summary>
    /// 游戏存档数据
    /// </summary>
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

