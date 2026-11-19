using System;
using System.Threading.Tasks;
using Godot;
using Kuros.Environments;
using Kuros.Managers;
using Kuros.Systems.Cutscene;
using Kuros.UI;

namespace Kuros.Scenes
{
    public partial class StageHomeArrivalController : Node
    {
        internal const string HomeScenePath = "res://scenes/Stage_home.tscn";
        private const string DesktopScenePath = "res://scenes/ui/menus/InGameDesktopMenu.tscn";
        private const string EntryLayerName = "StageHomeDesktopEntry";
        private const string ExcludePlayTimeGroup = "exclude_play_time";

        [Export] public CutsceneSequence? ReturnSequence { get; set; }

        private StageGeneratorManager? _generator;
        private CanvasLayer? _entryDesktop;
        private bool _returningByTaxi;
        private bool _excludePlayTime;

        internal static Error EnterFromDesktop(SceneTree tree, PackedScene? home = null)
        {
            if (tree.Root.HasNode(EntryLayerName)) return Error.Busy;

            home ??= GD.Load<PackedScene>(HomeScenePath);
            var desktopScene = GD.Load<PackedScene>(DesktopScenePath);
            if (home == null || desktopScene == null) return Error.CantOpen;

            var desktop = desktopScene.InstantiateOrNull<DesktopMenu>();
            if (desktop == null) return Error.CantCreate;

            desktop.IsHomeEntry = true;
            desktop.ProcessMode = ProcessModeEnum.Disabled;
            var layer = new CanvasLayer { Name = EntryLayerName, Layer = HomeComputerController.DesktopCanvasLayer };
            layer.AddChild(desktop);
            tree.Root.AddChild(layer);

            PauseManager.Instance?.ClearAllPauses();
            var error = tree.ChangeSceneToPacked(home);
            if (error != Error.Ok)
            {
                layer.Hide();
                layer.QueueFree();
            }
            return error;
        }

        public override void _Ready()
        {
            var root = GetTree().Root;
            var stage = GetParent();
            _returningByTaxi = root.HasMeta(ChangeSceneStep.HomeTaxiReturnMeta)
                && root.GetMeta(ChangeSceneStep.HomeTaxiReturnMeta).AsString() == stage.SceneFilePath;
            var entry = root.GetNodeOrNull<CanvasLayer>(EntryLayerName);
            if (entry != null && !entry.IsQueuedForDeletion())
            {
                _entryDesktop = entry;
                _excludePlayTime = !stage.IsInGroup(ExcludePlayTimeGroup);
                if (_excludePlayTime) stage.AddToGroup(ExcludePlayTimeGroup);
            }
            if (!_returningByTaxi && _entryDesktop == null) return;

            if (_returningByTaxi) root.RemoveMeta(ChangeSceneStep.HomeTaxiReturnMeta);
            _generator = stage.GetNode<StageGeneratorManager>("StageGeneratorManager");
            _generator.StageGenerated += OnStageGenerated;
            _generator.StageGenerationFailed += OnStageGenerationFailed;
        }

        public override void _ExitTree()
        {
            DisconnectGenerator();
            if (IsInstanceValid(_entryDesktop)) _entryDesktop!.QueueFree();
        }

        private void OnStageGenerated()
        {
            DisconnectGenerator();
            if (IsInstanceValid(_entryDesktop))
            {
                // 等待 BattleScene 的延迟初始化载入存档和 HUD，桌面覆盖层继续保留。
                Callable.From(() => { _ = PlayDesktopArrival(); }).CallDeferred();
            }
            else if (_returningByTaxi) _ = PlayArrival();
        }

        private async Task PlayDesktopArrival()
        {
            if (!IsInstanceValid(this) || !IsInsideTree() || _entryDesktop is not { } layer || !IsInstanceValid(layer)) return;
            _entryDesktop = null;
            var desktop = layer.GetChild<DesktopMenu>(0);
            desktop.ComputerClosed += BeginPlayTime;
            try
            {
                var stage = GetParent();
                var computer = stage.GetNodeOrNull<HomeComputerController>("World/ABegin/homeTable");
                var manager = stage.GetNodeOrNull<CutsceneManager>("CutsceneManager");
                if (computer == null || manager == null)
                {
                    GD.PushError("[StageHomeArrivalController] 缺少 homeTable 或 CutsceneManager");
                    return;
                }
                await computer.PlayComputerAsync(manager, layer);
            }
            finally
            {
                if (IsInstanceValid(desktop)) desktop.ComputerClosed -= BeginPlayTime;
                BeginPlayTime();
                if (IsInstanceValid(layer) && !layer.IsQueuedForDeletion()) layer.QueueFree();
            }
        }

        private void BeginPlayTime()
        {
            if (!_excludePlayTime) return;
            _excludePlayTime = false;
            if (IsInstanceValid(this) && IsInsideTree()) GetParent().RemoveFromGroup(ExcludePlayTimeGroup);
        }

        private void OnStageGenerationFailed()
        {
            DisconnectGenerator();
            if (IsInstanceValid(_entryDesktop))
            {
                _entryDesktop!.QueueFree();
                _entryDesktop = null;
                BeginPlayTime();
                var error = GetTree().ChangeSceneToFile(TitleScreen.ScenePath);
                if (error == Error.Ok) SaveManager.Instance?.ClearCurrentGameData();
                else GD.PushError($"[StageHomeArrivalController] 无法返回标题页：{error}");
            }
            else if (_returningByTaxi) CloudSceneTransition.Find(GetTree())?.QueueFree();
        }

        private void DisconnectGenerator()
        {
            if (!IsInstanceValid(_generator)) return;
            _generator!.StageGenerated -= OnStageGenerated;
            _generator.StageGenerationFailed -= OnStageGenerationFailed;
            _generator = null;
        }

        private async Task PlayArrival()
        {
            var stage = GetParent();
            var cloud = CloudSceneTransition.Find(GetTree());
            var sequence = ReturnSequence;
            if (sequence == null)
            {
                // 使用关卡生成器选好的默认落点，只结束换场遮罩。
                if (cloud != null) await cloud.RevealAsync();
                return;
            }

            var manager = stage.GetNodeOrNull<CutsceneManager>("CutsceneManager");
            var room = stage.GetNodeOrNull<Node2D>("World/AEnd");
            var taxiCall = room?.GetNodeOrNull<Node>("CutsceneTrigger");
            var taxi = room?.GetNodeOrNull<AnimationPlayer>("AnimationTaxi/AnimationPlayer");
            var actor = room?.GetNodeOrNull<Node2D>("ReturnCharacter");
            if (manager == null || taxiCall == null || taxi == null || actor == null
                || room?.GetNodeOrNull<Marker2D>("ReturnSpawnPoint") == null)
            {
                GD.PushError("[StageHomeArrivalController] 缺少 A_end 返程配置");
                if (cloud != null) await cloud.RevealAsync();
                return;
            }

            bool taxiWasProcessing = taxiCall.IsProcessing();
            taxiCall.SetProcess(false);
            try
            {
                // 首步同步把玩家从默认出生点移到 A_end，再开始揭开云层。
                var arrival = manager.PlayCutscene(sequence);
                var reveal = cloud?.RevealAsync() ?? Task.CompletedTask;
                await Task.WhenAll(arrival, reveal);
            }
            catch (Exception exception)
            {
                GD.PushError($"[StageHomeArrivalController] 返程过场失败：{exception}");
            }
            finally
            {
                if (IsInstanceValid(actor)) actor.QueueFree();
                if (IsInstanceValid(taxi))
                {
                    taxi.Stop();
                    taxi.Play("RESET");
                    taxi.Advance(0);
                }
                if (IsInstanceValid(taxiCall)) taxiCall.SetProcess(taxiWasProcessing);
            }
        }
    }
}
