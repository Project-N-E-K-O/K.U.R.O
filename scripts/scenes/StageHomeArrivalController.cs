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

        public override void _Ready()
        {
            var root = GetTree().Root;
            var stage = GetParent();
            if (!root.HasMeta(ChangeSceneStep.HomeTaxiReturnMeta)
                || root.GetMeta(ChangeSceneStep.HomeTaxiReturnMeta).AsString() != stage.SceneFilePath)
                return;

            root.RemoveMeta(ChangeSceneStep.HomeTaxiReturnMeta);
            _generator = stage.GetNode<StageGeneratorManager>("StageGeneratorManager");
            _generator.StageGenerated += OnStageGenerated;
        }

        public override void _ExitTree()
        {
            if (IsInstanceValid(_generator)) _generator!.StageGenerated -= OnStageGenerated;
        }

        private void OnStageGenerated()
        {
            _generator!.StageGenerated -= OnStageGenerated;
            _generator = null;
            _ = PlayArrival();
        }

        private async Task PlayArrival()
        {
            var stage = GetParent();
            var cloud = CloudSceneTransition.Find(GetTree());
            var manager = stage.GetNodeOrNull<CutsceneManager>("CutsceneManager");
            var room = stage.GetNodeOrNull<Node2D>("World/AEnd");
            var taxiCall = room?.GetNodeOrNull<Node>("CutsceneTrigger");
            var taxi = room?.GetNodeOrNull<AnimationPlayer>("AnimationTaxi/AnimationPlayer");
            var actor = room?.GetNodeOrNull<Node2D>("ReturnCharacter");
            if (ReturnSequence == null || manager == null || taxiCall == null || taxi == null || actor == null
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
                var arrival = manager.PlayCutscene(ReturnSequence);
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
