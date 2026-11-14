using Godot;
using System.Collections.Generic;
using System.Linq;
using Kuros.Systems.Stage;
using Kuros.Utils;

namespace Kuros.Managers
{
    /// <summary>
    /// 关卡生成器：根据配置随机拼接房间场景，生成无缝横向关卡。
    ///
    /// 拼接规则：
    ///   每个房间场景根节点下必须有 AreaSize/Area2D → CollisionShape2D(RectangleShape2D)
    ///   生成器从该矩形读取本地左右边界，依次将下一间房间的左边缘对齐上一间的右边缘。
    ///
    /// 注意：
    ///   若房间内有 top_level = true 的子节点（如 BattleArena、EnemySpawnManager），
    ///   它们使用世界坐标，不随父节点平移。生成器会自动为这些节点补偿偏移量。
    /// </summary>
    [GlobalClass]
    public partial class StageGeneratorManager : Node
    {
        [ExportCategory("Generation")]
        /// <summary>随机种子，0 = 每次随机。</summary>
        [Export] public int RandomSeed { get; set; } = 0;
        /// <summary>独立场景启动路线。酒店由 StageSession 提交路线。</summary>
        [Export] public StageConfig? StandaloneStage { get; set; }

        [ExportCategory("Camera Limits")]
        [Export] public int CameraLimitTop { get; set; } = -1500;
        [Export] public int CameraLimitBottom { get; set; } = 1500;

        [ExportCategory("Node Paths")]
        [Export] public NodePath WorldNodePath { get; set; } = new NodePath("../World");

        /// <summary>关卡生成完毕后触发。</summary>
        [Signal] public delegate void StageGeneratedEventHandler();
        [Signal] public delegate void StageGenerationFailedEventHandler();

        [Export] public RoomCatalog? Catalog { get; set; }
        private IReadOnlyList<PackedScene>? _loadedRooms;
        private IReadOnlyList<RoomType>? _roomTypes;
        /// <summary>显式玩家落点（隐藏入口/密道出口用）；null = 新关 PlayerSpawnPoint。</summary>
        private Vector2? _landingPosition;
        private readonly List<Node> _spawnedRooms = new();

        public override void _Ready()
        {
            if (StandaloneStage != null)
                CallDeferred(MethodName.GenerateStage);
        }

        /// <summary>
        /// 按已确定的房间顺序重新生成关卡（先清空上次生成的房间）。
        /// landingPosition 指定玩家落点（世界坐标，隐藏入口/密道出口用）；null = 新关 PlayerSpawnPoint。
        /// </summary>
        public void Regenerate(StagePlan plan, IReadOnlyList<PackedScene> rooms, Vector2? landingPosition = null)
        {
            _loadedRooms = rooms;
            _roomTypes = plan.RoomTypes;
            _landingPosition = landingPosition;
            ClearSpawnedRooms();
            CallDeferred(MethodName.GenerateStage);
        }

        private void ClearSpawnedRooms()
        {
            foreach (var room in _spawnedRooms)
            {
                if (GodotObject.IsInstanceValid(room))
                    room.Free();
            }
            _spawnedRooms.Clear();
        }

        /// <summary>
        /// 换关整场清残留（ApplyStage 在 Regenerate 前调用）:World 直属子节点按白名单清场——
        /// 排除 房间(由 Regenerate 释放)、玩家/角色/相机(持久),其余一律 QueueFree。
        /// 覆盖所有直连生成、未入 stage_world_items 组的残留(OnThrowDestroy 效果物 HealItemA、
        /// 家具被打碎产物、敌人死亡生成的家具体、投掷副本等),不依赖逐类追踪。
        /// 注意:将来若往 World 添加其它持久物,须在此加入白名单。
        /// </summary>
        public void ClearWorldRemnants()
        {
            var world = GetNodeOrNull<Node>(WorldNodePath);
            if (world == null) return;

            foreach (var child in world.GetChildren())
            {
                if (_spawnedRooms.Contains(child)) continue;
                if (child is Kuros.Actors.Heroes.MainCharacter) continue;
                if (child is Camera2D) continue;
                if (child.Name == "P2") continue; // 第二角色(持久场景实例)
                child.QueueFree();
            }
        }

        private void GenerateStage()
        {
            var world = GetNodeOrNull<Node2D>(WorldNodePath);
            if (world == null)
            {
                GD.PushError($"[StageGeneratorManager] 未找到 World 节点，路径：{WorldNodePath}");
                EmitSignal(SignalName.StageGenerationFailed);
                return;
            }

            var roomScenes = _loadedRooms ?? (StandaloneStage is { } config ? CreatePlan(config)?.RoomPaths
                .Select(path => ResourceLoader.Load<PackedScene>(path)).ToArray() : null);
            if (roomScenes == null)
            {
                EmitSignal(SignalName.StageGenerationFailed);
                return;
            }
            if (roomScenes.Count == 0 || roomScenes.Any(room => room == null || !room.CanInstantiate()))
            {
                GD.PushWarning("[StageGeneratorManager] 没有配置任何房间场景，跳过生成。");
                EmitSignal(SignalName.StageGenerationFailed);
                return;
            }

            GameLogger.Info(nameof(StageGeneratorManager),
                $"开始生成关卡：{roomScenes.Count} 个房间（Begin + {roomScenes.Count - 2} 中间 + End）");

            // 临时验证日志：记录生成耗时（分帧后房间日志应跨帧、耗时递增）
            ulong genStartMs = Time.GetTicksMsec();

            float currentRightEdge = 0f;
            float stageLeft = float.MaxValue;
            float stageRight = float.MinValue;
            Node2D? playerSpawn = null;

            for (int index = 0; index < roomScenes.Count; index++)
            {
                var room = roomScenes[index].Instantiate<Node2D>();
                // 侧翼过场用固定角色路径访问本次抽中的场景实例。
                if (_roomTypes?[index] == RoomType.BossWingA) room.Name = "F_boss_left";
                if (_roomTypes?[index] == RoomType.BossWingC) room.Name = "F_boss_right";
                // 从 AreaSize 读取本地左右边界
                var (localLeft, localRight) = GetRoomLocalBounds(room);

                // 将本房间左边缘对齐上一房间右边缘
                float offsetX = currentRightEdge - localLeft;
                room.Position = new Vector2(offsetX, 0f);
                world.AddChild(room);
                _spawnedRooms.Add(room);

                // top_level=true 的直接子节点不随父节点平移，需手动补偿
                OffsetTopLevelChildren(room, offsetX);

                // 取第一个 PlayerSpawnPoint 作为玩家起始点（仅 B_begin 应有）
                playerSpawn ??= room.GetNodeOrNull<Node2D>("PlayerSpawnPoint");

                float worldLeft  = offsetX + localLeft;
                float worldRight = offsetX + localRight;
                stageLeft  = Mathf.Min(stageLeft,  worldLeft);
                stageRight = Mathf.Max(stageRight, worldRight);
                currentRightEdge = worldRight;

                GameLogger.Info(nameof(StageGeneratorManager),
                    $"  {room.Name}: offsetX={offsetX:F0}，世界范围 [{worldLeft:F0}, {worldRight:F0}]（生成进度 {(Time.GetTicksMsec() - genStartMs)}ms）");

                // 分帧实例化：每帧 1 间房间，摊平数百节点一帧建树的卡顿
                //await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }

            // 更新相机全局边界
            var camMgr = GetParent()?.GetNodeOrNull<CameraZoneManager>("CameraZoneManager");
            if (camMgr != null)
            {
                camMgr.SetGlobalBounds((int)stageLeft, CameraLimitTop, (int)stageRight, CameraLimitBottom);
            }
            else
            {
                GD.PushWarning("[StageGeneratorManager] 未找到 CameraZoneManager，相机边界未更新。");
            }

            // 重定位玩家和同伴（相机改动已撤回——保持分帧前的循环后重定位 + 平滑跟随行为）
            RepositionActors(world, playerSpawn, stageLeft, _landingPosition);

            // 强制 NavigationServer 立即同步所有房间的导航网格
            // （各房间 AddChild + 设置 Position 后，NavigationServer 在下一物理帧才处理）
            // 调用此方法确保生成信号发出前导航数据已就绪
            NavigationServer2D.MapForceUpdate(GetViewport().FindWorld2D().NavigationMap);

            _loadedRooms = null;
            _roomTypes = null;
            EmitSignal(SignalName.StageGenerated);

            GameLogger.Info(nameof(StageGeneratorManager),
                $"关卡生成完成：总宽度 {stageRight - stageLeft:F0}，X[{(int)stageLeft}, {(int)stageRight}]");
        }

        public StagePlan? CreatePlan(StageConfig config)
        {
            var rng = new RandomNumberGenerator();
            if (RandomSeed == 0) rng.Randomize();
            else rng.Seed = (ulong)RandomSeed;

            var types = new List<RoomType?>();
            var candidates = new List<string[]>();
            void AddSlot(RoomType? type, IEnumerable<string> rooms)
            {
                types.Add(type);
                candidates.Add(rooms.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct().ToArray());
            }
            var roomsForTheme = Catalog?.Rooms.Where(room => room != null
                && room.FloorTheme == config.FloorTheme).ToArray() ?? System.Array.Empty<RoomDefinition>();
            var middleTypes = new Dictionary<string, RoomType>();
            if (config.RouteMode == StageRouteMode.RandomMiddleRooms)
            {
                foreach (var room in roomsForTheme)
                    if (room.RoomType is (RoomType.Combat or RoomType.AdvancedCombat
                            or RoomType.EliteCombat or RoomType.Puzzle)
                        && (string.IsNullOrWhiteSpace(config.MiddleRoomRouteTag)
                            || room.RouteTags.Contains(config.MiddleRoomRouteTag))
                        && !string.IsNullOrWhiteSpace(room.ScenePath)
                        && room.ScenePath != config.BeginScenePath
                        && room.ScenePath != config.EndScenePath)
                        middleTypes.TryAdd(room.ScenePath, room.RoomType);

                AddSlot(RoomType.Begin, new[] { config.BeginScenePath });
                for (int i = 0; i < config.MiddleRoomCount; i++)
                    AddSlot(null, middleTypes.Keys);
                AddSlot(RoomType.Boss, new[] { config.EndScenePath });
            }
            else
            {
                foreach (var type in config.RoomSequence)
                    AddSlot(type, roomsForTheme.Where(room => room.RoomType == type).Select(room => room.ScenePath));
            }

            if (types.Count == 0) return null;
            foreach (var pool in candidates)
                for (int i = pool.Length - 1; i > 0; i--)
                {
                    int j = rng.RandiRange(0, i);
                    (pool[i], pool[j]) = (pool[j], pool[i]);
                }

            var selected = new string[types.Count];
            var assigned = new System.Collections.Generic.Dictionary<string, int>();
            bool Assign(int slot, HashSet<string> visited)
            {
                foreach (var key in candidates[slot])
                {
                    if (!visited.Add(key)) continue;
                    if (assigned.TryGetValue(key, out int previousSlot) && !Assign(previousSlot, visited))
                        continue;
                    assigned[key] = slot;
                    selected[slot] = key;
                    return true;
                }
                return false;
            }

            for (int i = 0; i < types.Count; i++)
                if (!Assign(i, new HashSet<string>()))
                {
                    GD.PushWarning($"[StageGeneratorManager] {config.StageId} 无法生成不重复的路线，位置 {i + 1}：{config.FloorTheme}/{types[i]}");
                    return null;
                }

            var resolvedTypes = types.Select((type, index) => type ?? middleTypes[selected[index]]).ToArray();
            var icons = selected.Select((path, index) =>
                roomsForTheme.FirstOrDefault(room => room.ScenePath == path && room.RoomType == resolvedTypes[index])?.ElevatorPreviewIcon
                ?? roomsForTheme.FirstOrDefault(room => room.ScenePath == path)?.ElevatorPreviewIcon).ToArray();
            return new StagePlan(config, System.Array.AsReadOnly(resolvedTypes),
                System.Array.AsReadOnly(selected), System.Array.AsReadOnly(icons));
        }

        /// <summary>
        /// 从房间根节点的 AreaSize/CollisionShape2D 获取本地左右边界（相对于房间根节点）。
        /// </summary>
        private static (float left, float right) GetRoomLocalBounds(Node2D room)
        {
            var areaSize = room.GetNodeOrNull<Area2D>("AreaSize");
            if (areaSize != null)
            {
                var shape = areaSize.GetNodeOrNull<CollisionShape2D>("CollisionShape2D");
                if (shape?.Shape is RectangleShape2D rect)
                {
                    float left  = shape.Position.X - rect.Size.X / 2f;
                    float right = shape.Position.X + rect.Size.X / 2f;
                    return (left, right);
                }
            }

            GD.PushWarning($"[StageGeneratorManager] 房间 {room.Name} 缺少 AreaSize/CollisionShape2D(RectangleShape2D)，使用默认宽度 5000。");
            return (-2500f, 2500f);
        }

        /// <summary>
        /// 递归查找所有 TopLevel=true 的节点并手动追加世界偏移。
        /// TopLevel 节点不随父节点移动，父节点 Position 改变后需手动补偿。
        /// 注意：需要递归处理，因为 TopLevel 节点可能嵌套在普通子节点内（如 WaveSpawnManager/TriggerArea）。
        /// </summary>
        private static void OffsetTopLevelChildren(Node room, float offsetX)
        {
            if (Mathf.IsZeroApprox(offsetX)) return;

            OffsetTopLevelRecursive(room, offsetX);
        }

        private static void OffsetTopLevelRecursive(Node parent, float offsetX)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is Node2D node2D && node2D.TopLevel)
                {
                    // TopLevel 节点使用世界坐标，直接补偿偏移
                    node2D.GlobalPosition += new Vector2(offsetX, 0f);
                    // TopLevel 节点的子树跟随自身移动（除非子节点也是 TopLevel），继续向下递归
                }
                // 不管当前节点是否 TopLevel，都继续向下找嵌套的 TopLevel 节点
                OffsetTopLevelRecursive(child, offsetX);
            }
        }

        private void RepositionActors(Node2D world, Node2D? spawnPoint, float stageLeft, Vector2? explicitTarget = null)
        {
            // 若场景中没有 PlayerSpawnPoint，默认放在关卡左边缘右侧 1500 处；explicitTarget（隐藏入口出口）优先
            var target = explicitTarget ?? spawnPoint?.GlobalPosition ?? new Vector2(stageLeft + 1500f, 200f);

            var player = world.GetNodeOrNull<Node2D>("MainCharacter");
            if (player != null)
            {
                player.GetNodeOrNull<Kuros.Actors.Heroes.PlayerFallController>("FallController")?.ResetSafeHistory();
                player.GlobalPosition = target;
                // 复用玩家节点时立即同步碰撞体位置，避免下一次物理检测仍使用出舱前的坐标。
                player.ForceUpdateTransform();
                GameLogger.Info(nameof(StageGeneratorManager), $"玩家重定位 → {target}");
            }

            // P2 同伴稍微偏右
            var p2 = world.GetNodeOrNull<Node2D>("P2");
            if (p2 != null)
                p2.GlobalPosition = target + new Vector2(200f, 0f);
        }
    }
}
