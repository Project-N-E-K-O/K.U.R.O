using Godot;
using System;
using Kuros.Environments;
using Kuros.Systems.Cutscene;

namespace Kuros.Tests;

/// <summary>真实登记表与死亡事件回归；手动推进生命周期以精确检查淡入/淡出中点。</summary>
public partial class PipeLifecycleRuntimeCheck : Node
{
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        GD.Print($"[PipeCheck] PASS {name}");
    }

    private PipeLifecycleController Pipe(string tag, float fadeIn = 1, float duration = 0)
    {
        var pipe = new PipeLifecycleController
        {
            SpawnTag = tag, JarGroup = "pipe_test", FadeInSeconds = fadeIn,
            FadeSeconds = 1, Duration = duration, Modulate = new Color(0.3f, 0.6f, 0.8f, 0.8f)
        };
        AddChild(pipe);
        return pipe;
    }

    private DestructibleStageController Jar(CutsceneManager manager, string tag, string group = "pipe_test")
    {
        var jar = new DestructibleStageController
        {
            Stages = new() { new DestructibleStage { Threshold = 10 } }
        };
        AddChild(jar);
        jar.AddToGroup(group);
        manager.RegisterSpawnedRoot(jar, tag);
        return jar;
    }

    public override void _Ready() => Callable.From(Run).CallDeferred();

    private void Run()
    {
        try
        {
            var pipe = Pipe("left");
            Check(pipe.Modulate.A == 0, "等待罐子时初始透明");
            pipe._Process(2);
            Check(!pipe.IsQueuedForDeletion() && pipe.Modulate.A == 0, "无管理器且 Duration=0 可持续等待");
            var manager = new CutsceneManager();
            AddChild(manager);
            Jar(manager, "right");
            Jar(manager, "left", "wrong_group");
            pipe._Process(0.1);
            Check(pipe.Modulate.A == 0, "标签或分组不匹配不会淡入");
            var jar = Jar(manager, "left");
            pipe._Process(0.25);
            Check(pipe.Modulate.A > 0 && pipe.Modulate.A < 0.8f, "目标晚生成后开始淡入");
            Check(Mathf.IsEqualApprox(pipe.Modulate.R, 0.3f), "淡入保留原 RGB");
            float beforeDeath = pipe.Modulate.A;
            jar.KillForced();
            pipe._Process(0.5);
            Check(Mathf.IsEqualApprox(pipe.Modulate.A, beforeDeath * 0.5f), "淡入途中死亡从当前 alpha 淡出");
            pipe._Process(0.5);
            Check(pipe.IsQueuedForDeletion(), "淡出完成释放管网");

            var existing = Jar(manager, "existing");
            var latePipe = Pipe("existing");
            latePipe._Process(0.5);
            Check(Mathf.IsEqualApprox(latePipe.Modulate.A, 0.4f), "管网晚于罐子生成也会淡入");
            latePipe._Process(0.5);
            Check(Mathf.IsEqualApprox(latePipe.Modulate.A, 0.8f) && !latePipe.IsProcessing(), "淡入到原 alpha 后停止无用轮询");
            existing.KillForced();
            latePipe._Process(1);
            Check(latePipe.IsQueuedForDeletion(), "常亮后仍响应死亡释放");

            Jar(manager, "instant");
            var instant = Pipe("instant", 0);
            instant._Process(0.1);
            Check(Mathf.IsEqualApprox(instant.Modulate.A, 0.8f), "零秒淡入立即显示");
            var expired = Pipe("never", duration: 0.2f);
            expired._Process(0.21);
            Check(expired.IsQueuedForDeletion(), "等待阶段 Duration 独立到期释放");
            var unbound = Pipe("");
            Check(Mathf.IsEqualApprox(unbound.Modulate.A, 0.8f), "空标签保留不绑定管网原可见性");
            var dead = Jar(manager, "dead");
            dead.KillForced();
            var deadPipe = Pipe("dead");
            deadPipe._Process(1);
            Check(deadPipe.IsQueuedForDeletion() && deadPipe.Modulate.A == 0, "已死亡目标不会导致管网闪现");
            GD.Print("[PipeCheck] ALL PASSED");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"[PipeCheck] FAILED {exception}");
            GetTree().Quit(1);
        }
    }
}
