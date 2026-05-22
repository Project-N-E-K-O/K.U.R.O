using Godot;

namespace Kuros.Environments
{
    /// <summary>
    /// 电梯场景延迟加载器。
    ///
    /// 在关卡编辑器中作为 Loading_elevator.tscn 的轻量占位节点。
    /// 仅当玩家进入 LoadDistance 范围时，才通过后台线程异步加载电梯场景，
    /// 加载完成后立即实例化并插入场景树，本占位节点随即销毁。
    ///
    /// 效果：电梯的 48 帧 ×45MB = 约 2.1 GB 纹理数据不再随关卡启动时进入内存，
    /// 只在玩家实际接近电梯时才按需加载。
    ///
    /// 使用方法（在 .tscn 中替换 Loading_elevator.tscn 实例）：
    ///   1. 将此脚本挂载到一个空 Node2D，放置在原电梯位置。
    ///   2. 设置 ElevatorScenePath = "res://scenes/loading/Loading_elevator.tscn"
    ///   3. 设置 NextStagePath（与原实例相同的目标关卡 uid/路径）。
    /// </summary>
    [GlobalClass]
    public partial class ElevatorSceneLoader : Node2D
    {
        [ExportCategory("Scene")]
        [Export(PropertyHint.File, "*.tscn")]
        public string ElevatorScenePath { get; set; } = "res://scenes/loading/Loading_elevator.tscn";

        [Export(PropertyHint.File, "*.tscn,*.scn")]
        public string NextStagePath { get; set; } = "";

        [ExportCategory("Proximity")]
        [Export(PropertyHint.Range, "100,6000,50")]
        public float LoadDistance { get; set; } = 1200f;

        private enum State { Idle, Loading, Done }
        private State _state = State.Idle;

        public override void _Process(double delta)
        {
            switch (_state)
            {
                case State.Idle:
                    if (IsPlayerWithinRange())
                        BeginLoad();
                    break;

                case State.Loading:
                    PollLoad();
                    break;
            }
        }

        private bool IsPlayerWithinRange()
        {
            var players = GetTree().GetNodesInGroup("player");
            foreach (var p in players)
            {
                if (p is Node2D p2d && GlobalPosition.DistanceTo(p2d.GlobalPosition) <= LoadDistance)
                    return true;
            }
            return false;
        }

        private void BeginLoad()
        {
            if (string.IsNullOrEmpty(ElevatorScenePath))
            {
                GD.PushWarning("[ElevatorSceneLoader] ElevatorScenePath 未设置，无法加载电梯场景。");
                return;
            }

            _state = State.Loading;
            var err = ResourceLoader.LoadThreadedRequest(ElevatorScenePath);
            if (err != Error.Ok)
            {
                GD.PushWarning($"[ElevatorSceneLoader] LoadThreadedRequest 失败: {err}，路径: {ElevatorScenePath}");
                _state = State.Idle;
            }
        }

        private void PollLoad()
        {
            var status = ResourceLoader.LoadThreadedGetStatus(ElevatorScenePath);
            switch (status)
            {
                case ResourceLoader.ThreadLoadStatus.Loaded:
                    InstantiateElevator();
                    break;

                case ResourceLoader.ThreadLoadStatus.Failed:
                    GD.PushError($"[ElevatorSceneLoader] 电梯场景加载失败: {ElevatorScenePath}");
                    _state = State.Done;
                    QueueFree();
                    break;
            }
        }

        private void InstantiateElevator()
        {
            _state = State.Done;

            var packed = ResourceLoader.LoadThreadedGet(ElevatorScenePath) as PackedScene;
            if (packed == null)
            {
                GD.PushError("[ElevatorSceneLoader] LoadThreadedGet 返回 null，实例化中止。");
                QueueFree();
                return;
            }

            var instance = packed.Instantiate();
            if (instance == null)
            {
                GD.PushError("[ElevatorSceneLoader] PackedScene.Instantiate() 返回 null。");
                QueueFree();
                return;
            }

            // 通过 Godot 属性反射将 NextStagePath 转发给 ElevatorController
            // （根节点是 Node2D，脚本 ElevatorController : Node，不能直接 C# 强转为 Node2D）
            if (!string.IsNullOrEmpty(NextStagePath))
                instance.Set(nameof(ElevatorController.NextStagePath), NextStagePath);

            var parent = GetParent();
            if (parent == null)
            {
                GD.PushError("[ElevatorSceneLoader] 没有父节点，无法添加电梯实例。");
                instance.Free();
                QueueFree();
                return;
            }

            parent.AddChild(instance);

            // 将实例移动到占位节点的本地坐标位置（Node2D 属性通过反射设置）
            instance.Set("position", Position);

            // 占位节点完成使命，销毁自身
            QueueFree();
        }
    }
}
