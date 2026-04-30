using Godot;
using System;

namespace Kuros.Managers
{
    /// <summary>
    /// 战斗区域边界（"空气墙"）：
    /// 由4个StaticBody2D + CollisionShape2D构成的矩形边界。
    /// 阻挡玩家和敌人离开战斗区域。
    /// </summary>
    [GlobalClass]
    public partial class BattleArenaBoundary : Node2D
    {
        /// <summary>战斗区域矩形（世界坐标）。</summary>
        public Rect2 ArenaRect { get; set; }

        /// <summary>墙体厚度（向内缩进多少）。</summary>
        public float WallThickness { get; set; } = 2f;

        /// <summary>碰撞层。</summary>
        public uint CollisionLayer { get; set; } = 5u;

        /// <summary>碰撞掩码。</summary>
        public uint CollisionMask { get; set; } = 0b101u; // Layer 0 + Layer 2

        private StaticBody2D? _topWall;
        private StaticBody2D? _bottomWall;
        private StaticBody2D? _leftWall;
        private StaticBody2D? _rightWall;

        public override void _Ready()
        {
            CreateWalls();
        }

        /// <summary>
        /// 创建四面体边界。
        /// </summary>
        private void CreateWalls()
        {
            Vector2 arenaEnd = ArenaRect.Position + ArenaRect.Size;
            Vector2 arenaCenter = ArenaRect.Position + ArenaRect.Size / 2f;

            // 上墙（Y-）
            _topWall = CreateWall(
                "TopWall",
                new Vector2(arenaCenter.X, ArenaRect.Position.Y - WallThickness / 2f),
                new Vector2(ArenaRect.Size.X, WallThickness)
            );

            // 下墙（Y+）
            _bottomWall = CreateWall(
                "BottomWall",
                new Vector2(arenaCenter.X, arenaEnd.Y + WallThickness / 2f),
                new Vector2(ArenaRect.Size.X, WallThickness)
            );

            // 左墙（X-）
            _leftWall = CreateWall(
                "LeftWall",
                new Vector2(ArenaRect.Position.X - WallThickness / 2f, arenaCenter.Y),
                new Vector2(WallThickness, ArenaRect.Size.Y)
            );

            // 右墙（X+）
            _rightWall = CreateWall(
                "RightWall",
                new Vector2(arenaEnd.X + WallThickness / 2f, arenaCenter.Y),
                new Vector2(WallThickness, ArenaRect.Size.Y)
            );
        }

        /// <summary>
        /// 创建单个墙体。
        /// </summary>
        private StaticBody2D CreateWall(string wallName, Vector2 wallCenter, Vector2 wallSize)
        {
            var body = new StaticBody2D
            {
                Name = wallName,
                CollisionLayer = CollisionLayer,
                CollisionMask = CollisionMask
            };

            var shape = new CollisionShape2D
            {
                Shape = new RectangleShape2D { Size = wallSize }
            };

            body.AddChild(shape);
            AddChild(body);

            body.GlobalPosition = wallCenter;

            return body;
        }
    }
}
