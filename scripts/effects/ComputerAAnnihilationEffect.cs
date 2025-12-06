using Godot;
using Kuros.Core;
using Kuros.Core.Effects;

namespace Kuros.Effects
{
    /// <summary>
    /// 计算机A：秒杀屏幕内所有敌人，并在使用次数达到阈值后弹出误报窗口并终止游戏。
    /// </summary>
    [GlobalClass]
    public partial class ComputerAAnnihilationEffect : ActorEffect
    {
        private const string UsageMetaKey = "weapon_a0_computer_a_skill_uses";

        [Export(PropertyHint.Range, "1,10,1")]
        public int CrashThreshold { get; set; } = 3;

        [Export(PropertyHint.Range, "200,800,10")]
        public float FatalDialogWidth { get; set; } = 420f;

        [Export(PropertyHint.MultilineText)]
        public string FatalDialogText { get; set; } =
            "致命异常：SCREEN_OVERLOAD\n\n请按确定以继续。";

        private bool _dialogSpawned;

        protected override void OnApply()
        {
            base.OnApply();

            if (Actor == null)
            {
                Controller?.RemoveEffect(this);
                return;
            }

            KillAllEnemies();
            int uses = IncrementUsageCounter();

            if (uses >= CrashThreshold)
            {
                ShowFatalDialog();
            }

            Controller?.RemoveEffect(this);
        }

        private void KillAllEnemies()
        {
            var actor = Actor;
            if (actor == null) return;

            var tree = actor.GetTree();
            if (tree == null) return;

            foreach (var node in tree.GetNodesInGroup("enemies"))
            {
                if (node is not GameActor enemy || enemy == actor) continue;
                enemy.TakeDamage(int.MaxValue, actor.GlobalPosition, actor);
            }
        }

        private int IncrementUsageCounter()
        {
            if (Actor == null) return 0;

            int uses = 0;
            if (Actor.HasMeta(UsageMetaKey))
            {
                var metaValue = Actor.GetMeta(UsageMetaKey);
                if (metaValue.VariantType == Variant.Type.Int)
                {
                    uses = (int)metaValue;
                }
                else if (metaValue.VariantType == Variant.Type.Float)
                {
                    uses = Mathf.RoundToInt((float)metaValue);
                }
            }

            uses++;
            Actor.SetMeta(UsageMetaKey, uses);
            return uses;
        }

        private void ShowFatalDialog()
        {
            if (_dialogSpawned) return;
            _dialogSpawned = true;

            var tree = Actor?.GetTree();
            if (tree == null) return;

            var dialog = new AcceptDialog
            {
                Name = "ComputerAErrorDialog",
                Title = "系统错误",
                DialogText = FatalDialogText,
                MinSize = new Vector2I(Mathf.RoundToInt(FatalDialogWidth), 220)
            };

            dialog.Confirmed += () => CrashGame(tree);
            dialog.Canceled += () => CrashGame(tree);
            dialog.CloseRequested += () => CrashGame(tree);

            var root = tree.Root;
            if (root != null)
            {
                root.AddChild(dialog);
                dialog.PopupCentered();
            }
            else
            {
                CrashGame(tree);
            }
        }

        private static void CrashGame(SceneTree tree)
        {
            tree.Quit(42);
        }
    }
}

