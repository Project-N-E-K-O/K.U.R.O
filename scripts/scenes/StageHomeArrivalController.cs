using System;
using System.Threading.Tasks;
using Godot;
using Kuros.Managers;
using Kuros.Systems.Cutscene;
using Kuros.UI;

namespace Kuros.Scenes
{
    public partial class StageHomeArrivalController : Node
    {
        [Export] public CutsceneSequence? ReturnSequence { get; set; }

        private StageGeneratorManager? _generator;
        private MonitorShutdownTransition? _monitorTransition;
        private bool _returningByTaxi;

        public override void _Ready()
        {
            var root = GetTree().Root;
            var stage = GetParent();
            _returningByTaxi = root.HasMeta(ChangeSceneStep.HomeTaxiReturnMeta)
                && root.GetMeta(ChangeSceneStep.HomeTaxiReturnMeta).AsString() == stage.SceneFilePath;
            _monitorTransition = MonitorShutdownTransition.Find(GetTree());
            if (!_returningByTaxi && _monitorTransition == null) return;

            if (_returningByTaxi) root.RemoveMeta(ChangeSceneStep.HomeTaxiReturnMeta);
            _generator = stage.GetNode<StageGeneratorManager>("StageGeneratorManager");
            _generator.StageGenerated += OnStageGenerated;
            _generator.StageGenerationFailed += OnStageGenerationFailed;
        }

        public override void _ExitTree()
        {
            DisconnectGenerator();
            if (IsInstanceValid(_monitorTransition)) _monitorTransition!.QueueFree();
        }

        private void OnStageGenerated()
        {
            DisconnectGenerator();
            if (IsInstanceValid(_monitorTransition))
                _ = PlayMonitorArrival();
            if (_returningByTaxi) _ = PlayArrival();
        }

        private async Task PlayMonitorArrival()
        {
            var trigger = GetParent().GetNodeOrNull<CutsceneTrigger>("World/ABegin/CutsceneTrigger");
            if (trigger != null) await trigger.TriggerAsync();

            // 配置缺失或镜头步骤提前结束时，避免遮罩留在画面上。
            if (IsInstanceValid(_monitorTransition) && !_monitorTransition!.IsQueuedForDeletion())
                await _monitorTransition.RevealAsync();
        }

        private void OnStageGenerationFailed()
        {
            DisconnectGenerator();
            if (IsInstanceValid(_monitorTransition)) _ = _monitorTransition!.RevealAsync();
            if (_returningByTaxi) CloudSceneTransition.Find(GetTree())?.QueueFree();
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
