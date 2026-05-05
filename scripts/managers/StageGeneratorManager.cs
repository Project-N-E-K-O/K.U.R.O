using Godot;
using Godot.Collections;
using System.Collections.Generic;
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
        [ExportCategory("房间场景")]
        [Export] public PackedScene? BeginScene { get; set; }
        [Export] public PackedScene? EndScene { get; set; }
        [Export] public Array<PackedScene> MiddleScenePool { get; set; } = new();

        [ExportCategory("生成配置")]
        [Export(PropertyHint.Range, "0,10,1")] public int MinMiddleRooms { get; set; } = 2; // 生成中间房间数量的随机范围，实际数量在 Min 和 Max 之间随机抽取
        [Export(PropertyHint.Range, "0,10,1")] public int MaxMiddleRooms { get; set; } = 2; 
        /// <summary>随机种子，0 = 每次随机。</summary>
        [Export] public int RandomSeed { get; set; } = 0;  

        [ExportCategory("相机范围")]
        [Export] public int CameraLimitTop { get; set; } = -1500;
        [Export] public int CameraLimitBottom { get; set; } = 1500;

        [ExportCategory("节点路径")]
        [Export] public NodePath WorldNodePath { get; set; } = new NodePath("../World");

        /// <summary>关卡生成完毕后触发。</summary>
        [Signal] public delegate void StageGeneratedEventHandler();

        public override void _Ready()
        {
            CallDeferred(MethodName.GenerateStage);
        }

        private void GenerateStage()
        {
            var world = GetNodeOrNull<Node2D>(WorldNodePath);
            if (world == null)
            {
                GD.PushError($"[StageGeneratorManager] 未找到 World 节点，路径：{WorldNodePath}");
                EmitSignal(SignalName.StageGenerated);
                return;
            }

            var rng = new RandomNumberGenerator();
            rng.Seed = RandomSeed != 0 ? (ulong)RandomSeed : (ulong)Time.GetTicksMsec();

            var roomScenes = BuildRoomSequence(rng);
            if (roomScenes.Count == 0)
            {
                GD.PushWarning("[StageGeneratorManager] 没有配置任何房间场景，跳过生成。");
                EmitSignal(SignalName.StageGenerated);
                return;
            }

            GameLogger.Info(nameof(StageGeneratorManager),
                $"开始生成关卡：{roomScenes.Count} 个房间（Begin + {roomScenes.Count - 2} 中间 + End）");

            float currentRightEdge = 0f;
            float stageLeft = float.MaxValue;
            float stageRight = float.MinValue;
            Node2D? playerSpawn = null;

            foreach (var scene in roomScenes)
            {
                var room = scene.Instantiate<Node2D>();
                world.AddChild(room);

                // 从 AreaSize 读取本地左右边界
                var (localLeft, localRight) = GetRoomLocalBounds(room);

                // 将本房间左边缘对齐上一房间右边缘
                float offsetX = currentRightEdge - localLeft;
                room.Position = new Vector2(offsetX, 0f);

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
                    $"  {room.Name}: offsetX={offsetX:F0}，世界范围 [{worldLeft:F0}, {worldRight:F0}]");
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

            // 重定位玩家和同伴
            RepositionActors(world, playerSpawn, stageLeft);

            EmitSignal(SignalName.StageGenerated);

            GameLogger.Info(nameof(StageGeneratorManager),
                $"关卡生成完成：总宽度 {stageRight - stageLeft:F0}，X[{(int)stageLeft}, {(int)stageRight}]");
        }

        private List<PackedScene> BuildRoomSequence(RandomNumberGenerator rng)
        {
            var list = new List<PackedScene>();

            if (BeginScene != null)
                list.Add(BeginScene);

            if (MiddleScenePool.Count > 0)
            {
                int count = rng.RandiRange(MinMiddleRooms, MaxMiddleRooms);
                var available = new List<PackedScene>(MiddleScenePool);

                for (int i = 0; i < count; i++)
                {
                    // 无重复抽取；池耗尽后重新放回
                    if (available.Count == 0)
                        available = new List<PackedScene>(MiddleScenePool);

                    int idx = rng.RandiRange(0, available.Count - 1);
                    list.Add(available[idx]);
                    available.RemoveAt(idx);
                }
            }

            if (EndScene != null)
                list.Add(EndScene);

            return list;
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
        /// 对直接子节点中 TopLevel=true 的节点手动追加世界偏移。
        /// TopLevel 节点不随父节点移动，父节点 Position 改变后需手动补偿。
        /// </summary>
        private static void OffsetTopLevelChildren(Node2D room, float offsetX)
        {
            if (Mathf.IsZeroApprox(offsetX)) return;

            foreach (var child in room.GetChildren())
            {
                if (child is Node2D node && node.TopLevel)
                {
                    node.GlobalPosition += new Vector2(offsetX, 0f);
                }
            }
        }

        private void RepositionActors(Node2D world, Node2D? spawnPoint, float stageLeft)
        {
            // 若场景中没有 PlayerSpawnPoint，默认放在关卡左边缘右侧 1500 处
            var target = spawnPoint?.GlobalPosition ?? new Vector2(stageLeft + 1500f, 200f);

            var player = world.GetNodeOrNull<Node2D>("MainCharacter");
            if (player != null)
            {
                player.GlobalPosition = target;
                GameLogger.Info(nameof(StageGeneratorManager), $"玩家重定位 → {target}");
            }

            // P2 同伴稍微偏右
            var p2 = world.GetNodeOrNull<Node2D>("P2");
            if (p2 != null)
                p2.GlobalPosition = target + new Vector2(200f, 0f);
        }
    }
}
