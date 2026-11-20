using Godot;
using System;
using System.IO;
using System.Collections.Generic;
using Kuros.Scenes;
using Kuros.Systems;
using Kuros.Systems.Inventory;
using Kuros.Actors.Heroes;
using Kuros.Core;
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
        private const int SAVE_FORMAT_VERSION = 2;
        private const int SaveSlotsCount = 3;
        private string _saveDirectory = "";

        // 游戏时间追踪
        private int _totalPlayTimeSeconds = 0;
        private double _accumulatedDelta = 0.0;
        private RunSession? _run;
        private double CurrentPlayTime => _totalPlayTimeSeconds + _accumulatedDelta;

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
        }
        
        public override void _Process(double delta)
        {
            if (GetTree().CurrentScene?.IsInGroup("exclude_play_time") == true) return;

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
            if (slotIndex < 0 || slotIndex >= SaveSlotsCount) return false;
            string filePath = GetSaveFilePath(slotIndex);
            return Godot.FileAccess.FileExists(filePath);
        }

        /// <summary>
        /// 保存游戏数据
        /// </summary>
        public bool SaveGame(int slotIndex, GameSaveData data)
        {
            if (slotIndex < 0 || slotIndex >= SaveSlotsCount)
            {
                GD.PrintErr($"SaveManager: 无效的存档槽位: {slotIndex}");
                return false;
            }

            string filePath = GetSaveFilePath(slotIndex);

            // 确保存档数据包含正确的槽位索引
            data.SlotIndex = slotIndex;

            // 保存数据为JSON格式，包含版本号以支持未来迁移
            var savePayload = new Godot.Collections.Dictionary<string, Variant>
            {
                { "version", SAVE_FORMAT_VERSION },
                { "data", data.ToDictionary() }
            };
            var json = Json.Stringify(savePayload);
            var temporaryPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporaryPath, json);
                File.Move(temporaryPath, filePath, overwrite: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                GD.PushError($"SaveManager: 无法保存槽位 {slotIndex}: {exception.Message}");
                try { File.Delete(temporaryPath); }
                catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
                { GD.PushWarning($"SaveManager: 无法清理临时存档: {cleanup.Message}"); }
                return false;
            }

            GD.Print($"SaveManager: 成功保存到槽位 {slotIndex}: {filePath}");
            return true;
        }

        /// <summary>
        /// 加载游戏数据
        /// </summary>
        public GameSaveData? LoadGame(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SaveSlotsCount)
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
                ClearCount = gameData.ClearCount,
                CycleCount = gameData.CycleCount,
                MaxStageReached = gameData.MaxStageReached
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

        /// <summary>
        /// 自动存档：刷新当前存档的元数据（保存时间/累计游玩时长）并写回当前槽位。
        /// 供电梯加载、玩家死亡等自动保存点调用；无当前游戏数据时不写盘。
        /// </summary>
        public bool AutosaveCurrentSlot()
        {
            if (CurrentGameData == null)
            {
                GD.PushWarning("SaveManager: 无当前游戏数据，自动存档已跳过");
                return false;
            }

            if (_run?.FinalRecord is { } record && !CurrentGameData.HasRunRecord(record.Id))
                return FinishRun(record.Outcome, _run.CountDeath);

            CurrentGameData.SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            CurrentGameData.PlayTimeSeconds = _totalPlayTimeSeconds;
            return SaveGame(CurrentGameData.SlotIndex, CurrentGameData);
        }

        internal void BeginRun()
        {
            if (CurrentGameData == null) return;
            if (_run?.FinalRecord is { } pending && !FinishRun(pending.Outcome, _run.CountDeath)) return;
            _run = new RunSession(CurrentPlayTime);
        }

        internal void RecordRunGold(int amount) => _run?.AddGold(amount);

        internal void RecordRunItem(ItemDefinition item, int quantity) => _run?.AddItem(item, quantity);

        internal bool HasActiveRun => _run is { FinalRecord: null };

        internal bool HasRunToFinish => _run != null;

        internal void ResetRunState()
        {
            PendingInventoryTransit = null;
            PendingNextStagePath = "";
            // 死亡结算写盘失败时保留已冻结的记录，以便下次自动存档重试。
            if (CurrentGameData == null || _run?.FinalRecord is not { } record || CurrentGameData.HasRunRecord(record.Id))
                _run = null;
        }

        internal bool FinishRun(RunOutcome outcome, bool playerDied = false)
        {
            if (outcome == RunOutcome.None || CurrentGameData is not { } current || _run == null) return false;
            var record = _run.Finish(outcome, playerDied, CurrentPlayTime);
            if (current.HasRunRecord(record.Id)) return true;

            var updated = GameSaveData.FromDictionary(current.ToDictionary());
            updated.AppendRunRecord(record);
            if (record.Outcome == RunOutcome.Cleared) updated.ClearCount++;
            if (_run.CountDeath) updated.CycleCount++;
            updated.SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            updated.PlayTimeSeconds = _totalPlayTimeSeconds;
            if (!SaveGame(updated.SlotIndex, updated)) return false;
            CurrentGameData = updated;
            return true;
        }

        /// <summary>新游戏：在空槽位写入初始永久进度数据，并设为当前存档（死亡/电梯自动存档依赖它）。</summary>
        public bool NewGame(int slotIndex)
        {
            var data = new GameSaveData
            {
                SlotIndex = slotIndex,
                SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                PlayTimeSeconds = 0,
                HighScore = 0,
                ClearCount = 0,
                CycleCount = 0,
                MaxStageReached = 1,
            };
            if (!SaveGame(slotIndex, data)) return false;
            SetCurrentGameData(data);
            return true;
        }

        /// <summary>删除指定槽位的存档。</summary>
        public bool DeleteSave(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= SaveSlotsCount) return false;
            string filePath = GetSaveFilePath(slotIndex);
            if (!Godot.FileAccess.FileExists(filePath)) return false;
            if (DirAccess.RemoveAbsolute(filePath) != Error.Ok) return false;
            if (CurrentGameData?.SlotIndex == slotIndex)
                ClearCurrentGameData();
            GD.Print($"SaveManager: 已删除存档槽位 {slotIndex}");
            return true;
        }

        /// <summary>
        /// 设置当前游戏数据（从存档加载后调用）
        /// </summary>
        public void SetCurrentGameData(GameSaveData? data)
        {
            _run = null;
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
            _run = null;
            CurrentGameData = null;
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
            if (inv.Backpack != null)
            {
                foreach (var stack in inv.Backpack.Slots)
                {
                    if (stack == null || stack.IsEmpty || string.IsNullOrEmpty(stack.Item.ResourcePath))
                        data.BackpackSlots.Add(null);
                    else
                        data.BackpackSlots.Add(new SlotEntry(stack.Item.ResourcePath, stack.Quantity));
                }
            }

            // 家具槽
            var fs = inv.FurnitureSlotStack;
            if (fs != null && !fs.IsEmpty && !string.IsNullOrEmpty(fs.Item.ResourcePath))
                data.FurnitureSlot = new SlotEntry(fs.Item.ResourcePath, fs.Quantity);

            return data;
        }

        /// <summary>将快照还原到玩家背包组件。调用方应在 _Ready 完成后调用。
        /// owner 为玩家时用于恢复家具 OnEquip 效果（与运行时拾取行为一致）。</summary>
        public void RestoreTo(PlayerInventoryComponent inv, GameActor? owner = null)
        {
            // ── 快捷栏 ──────────────────────────────────────────
            if (inv.QuickBar != null && QuickBarSlots.Count > 0)
            {
                for (int i = 0; i < QuickBarSlots.Count && i < inv.QuickBar.Slots.Count; i++)
                {
                    var entry = QuickBarSlots[i];
                    // 先清空槽位，避免已有默认物品导致 TryAddItemToSlot 覆盖失败
                    inv.QuickBar.SetStack(i, null);
                    if (entry == null) continue;
                    var item = ResourceLoader.Load<ItemDefinition>(entry.ItemPath);
                    if (item == null)
                    {
                        GD.PushWarning($"[InventoryTransitData] 快捷栏槽位 {i} 资源加载失败：{entry.ItemPath}");
                        continue;
                    }
                    inv.QuickBar.TryAddItemToSlot(item, entry.Quantity, i);
                }
            }

            // ── 背包 ────────────────────────────────────────────
            if (inv.Backpack != null && BackpackSlots.Count > 0)
            {
                for (int i = 0; i < BackpackSlots.Count && i < inv.Backpack.Slots.Count; i++)
                {
                    var entry = BackpackSlots[i];
                    // 先清空槽位，避免已有物品导致合并或失败
                    inv.Backpack.SetStack(i, null);
                    if (entry == null) continue;
                    var item = ResourceLoader.Load<ItemDefinition>(entry.ItemPath);
                    if (item == null)
                    {
                        GD.PushWarning($"[InventoryTransitData] 背包槽位 {i} 资源加载失败：{entry.ItemPath}");
                        continue;
                    }
                    inv.Backpack.TryAddItemToSlot(item, entry.Quantity, i);
                }
            }

            // ── 家具槽 ──────────────────────────────────────────
            if (FurnitureSlot != null)
            {
                var item = ResourceLoader.Load<ItemDefinition>(FurnitureSlot.ItemPath);
                if (item == null)
                {
                    GD.PushWarning($"[InventoryTransitData] 家具槽资源加载失败：{FurnitureSlot.ItemPath}");
                }
                else
                {
                    // 先清空家具槽，确保 AddFurnitureItem 不会因槽已占用而失败
                    inv.ClearFurnitureSlot();
                    inv.AddItemSmart(item, FurnitureSlot.Quantity, owner, recordAcquisition: false);
                }
            }

            // ── 恢复选中槽位 ────────────────────────────────────
            inv.SelectedQuickBarSlot = SelectedQuickBarSlot;
        }
    }

    /// <summary>
    /// 游戏存档数据（v2：纯元进度，不含局内状态如 HP/武器/背包）。
    /// 底层为 Dictionary 驱动，序列化时直接序列化字典，新增字段无需改序列化逻辑。
    /// </summary>
    public class GameSaveData
    {
        private readonly Godot.Collections.Dictionary<string, Variant> _data = new();

        // 元数据
        public int SlotIndex       { get => Get<int>("SlotIndex"); set => Set("SlotIndex", value); }
        public string SaveTime     { get => Get<string>("SaveTime") ?? ""; set => Set("SaveTime", value); }
        public int PlayTimeSeconds { get => Get<int>("PlayTimeSeconds"); set => Set("PlayTimeSeconds", value); }

        // 永久进度
        public int HighScore       { get => Get<int>("HighScore"); set => Set("HighScore", value); }
        public int ClearCount      { get => Get<int>("ClearCount"); set => Set("ClearCount", value); }
        public int CycleCount      { get => Get<int>("CycleCount"); set => Set("CycleCount", value); }

        internal IEnumerable<RunRecord> ReadRunHistory()
        {
            foreach (var value in ReadRunEntries())
                if (value.VariantType == Variant.Type.Dictionary && RunRecord.Decode(value.AsGodotDictionary()) is { } record)
                    yield return record;
        }

        internal bool HasRunRecord(string id)
        {
            foreach (var value in ReadRunEntries())
                if (value.VariantType == Variant.Type.Dictionary && value.AsGodotDictionary().TryGetValue("id", out var savedId)
                    && savedId.AsString() == id) return true;
            return false;
        }

        internal void AppendRunRecord(RunRecord record)
        {
            var entries = ReadRunEntries().Duplicate();
            entries.Add(record.Encode());
            _data["RunHistory"] = entries;
        }

        private Godot.Collections.Array ReadRunEntries() =>
            _data.TryGetValue("RunHistory", out var entries) && entries.VariantType == Variant.Type.Array
                ? entries.AsGodotArray() : new Godot.Collections.Array();
        public int MaxStageReached { get => Get<int>("MaxStageReached"); set => Set("MaxStageReached", value); }

        // 剧情
        public string LastStoryNodeId { get => Get<string>("LastStoryNodeId") ?? ""; set => Set("LastStoryNodeId", value); }
        public Godot.Collections.Array<string> StoryFlags    { get => GetArray("StoryFlags"); set => SetArray("StoryFlags", value); }
        public Godot.Collections.Array<string> CompletedStoryIds { get => GetArray("CompletedStoryIds"); set => SetArray("CompletedStoryIds", value); }

        // 记忆（图鉴/P2 共享数据源）：key = 敌人类型名 / 物品 ItemId，value = 累计次数
        public Godot.Collections.Dictionary<string, int> EnemyDefeatedCounts { get => GetIntDict("EnemyDefeatedCounts"); set => SetIntDict("EnemyDefeatedCounts", value); }
        public Godot.Collections.Dictionary<string, int> WeaponAcquiredCounts { get => GetIntDict("WeaponAcquiredCounts"); set => SetIntDict("WeaponAcquiredCounts", value); }

        // 序列化
        public Godot.Collections.Dictionary<string, Variant> ToDictionary()
        {
            var copy = new Godot.Collections.Dictionary<string, Variant>();
            foreach (var kvp in _data) copy[kvp.Key] = kvp.Value;
            return copy;
        }
        public static GameSaveData FromDictionary(Godot.Collections.Dictionary<string, Variant> dict)
        {
            var data = new GameSaveData();
            foreach (var kvp in dict) data._data[kvp.Key] = kvp.Value;
            return data;
        }

        // 辅助：从字典取值并转为目标类型。
        // 注意：不能使用 (T)(object)variant，因为 boxed Variant 无法直接 unbox 为 int/string。
        // 必须通过 Variant 的原生转换方法 (AsInt32/AsString/AsSingle/AsBool)。
        private T? Get<T>(string key)
        {
            if (_data.TryGetValue(key, out var v) && v.VariantType != Variant.Type.Nil)
            {
                if (typeof(T) == typeof(int))
                    return (T)(object)v.AsInt32();
                if (typeof(T) == typeof(string))
                    return (T)(object)v.AsString();
                if (typeof(T) == typeof(float))
                    return (T)(object)v.AsSingle();
                if (typeof(T) == typeof(bool))
                    return (T)(object)v.AsBool();
            }
            return default;
        }
        private void Set<T>(string key, T value) => _data[key] = Variant.From(value);
        private Godot.Collections.Array<string> GetArray(string key)
        {
            if (_data.TryGetValue(key, out var v) && v.VariantType == Variant.Type.Array)
            {
                var result = new Godot.Collections.Array<string>();
                foreach (var item in v.AsGodotArray()) result.Add(item.AsString());
                return result;
            }
            return new Godot.Collections.Array<string>();
        }
        private void SetArray(string key, Godot.Collections.Array<string> value) { Variant v = value; _data[key] = v; }
        private Godot.Collections.Dictionary<string, int> GetIntDict(string key)
        {
            if (_data.TryGetValue(key, out var v) && v.VariantType == Variant.Type.Dictionary)
            {
                var result = new Godot.Collections.Dictionary<string, int>();
                foreach (var kvp in v.AsGodotDictionary())
                {
                    // JSON 反序列化会把整数解析为 Float 变体（Godot JSON.parse 行为），两者都接受
                    if (kvp.Value.VariantType == Variant.Type.Int || kvp.Value.VariantType == Variant.Type.Float)
                        result[kvp.Key.AsString()] = kvp.Value.AsInt32();
                }
                return result;
            }
            return new Godot.Collections.Dictionary<string, int>();
        }
        private void SetIntDict(string key, Godot.Collections.Dictionary<string, int> value) { Variant v = value; _data[key] = v; }
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
        public int ClearCount { get; set; }
        public int CycleCount { get; set; }
        public int MaxStageReached { get; set; }
        public Texture2D? Thumbnail { get; set; }
        public Texture2D? LocationImage { get; set; }
    }
}

