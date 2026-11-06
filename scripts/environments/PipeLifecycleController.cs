using Godot;
using Kuros.Core;
using Kuros.Systems.Cutscene;

namespace Kuros.Environments;

/// <summary>
/// 管网生命周期：有 SpawnTag 时初始透明，目标罐子登记后淡入；最终死亡时淡出并释放；
/// Duration 从 Ready 独立计时，到期释放整个管网（0 = 不限时）。
/// 使用过场登记表识别左右罐子，不把 SpawnTag 当作节点名或分组。
/// </summary>
[GlobalClass]
public partial class PipeLifecycleController : Node2D
{
    [ExportCategory("Jar Binding")]
    [Export] public string SpawnTag { get; set; } = "JarLeft";
    [Export] public string JarGroup { get; set; } = "rogueai_part2";
    /// <summary>空路径时自动查找 cutscene_manager 组；多管理器场景可显式指定。</summary>
    [Export] public NodePath CutsceneManagerPath { get; set; } = new();

    [ExportCategory("Lifetime")]
    /// <summary>目标出现后淡入耗时，淡入到场景原本的 Modulate.A；0 = 立即显示。</summary>
    [Export(PropertyHint.Range, "0,60,0.1,or_greater")]
    public float FadeInSeconds { get; set; } = 1.0f;
    /// <summary>击破后淡出耗时，完成后释放整个管网；0 = 立即隐藏并释放。</summary>
    [Export(PropertyHint.Range, "0,60,0.1,or_greater")]
    public float FadeSeconds { get; set; } = 3.0f;
    /// <summary>从 Ready 开始的独立寿命，0 = 不限时（仍会在淡出后释放）；到期可中断淡出。</summary>
    [Export(PropertyHint.Range, "0,600,0.1,or_greater")]
    public float Duration { get; set; } = 0.0f;

    private CutsceneManager? _manager;
    private bool _subscribed;
    private bool _fadeStarted;
    private bool _fadeCompleted;
    private float _fadeStartAlpha;
    private double _fadeElapsed;
    private double _aliveElapsed;
    private bool _fadeInStarted;
    private bool _fadeInCompleted;
    private double _fadeInElapsed;
    private double _searchElapsed;
    private float _visibleAlpha;

    public override void _Ready()
    {
        _fadeStarted = false;
        _fadeCompleted = false;
        _fadeElapsed = 0.0;
        _aliveElapsed = 0.0;
        _fadeInStarted = false;
        _fadeInCompleted = false;
        _fadeInElapsed = _searchElapsed = 0.0;
        _visibleAlpha = Modulate.A;
        if (!string.IsNullOrWhiteSpace(SpawnTag))
        {
            SetAlpha(0.0f);
            GameActor.DeathFinalized += OnDeathFinalized;
            _subscribed = true;
            // 注册发生在生成节点 Ready 之后；延后一拍也覆盖管道晚于罐子生成的情况。
            Callable.From(CheckExistingJar).CallDeferred();
        }
        else
        {
            GD.PushWarning($"[{nameof(PipeLifecycleController)}] {Name}: SpawnTag 为空，仅启用 Duration。");
        }
        SetProcess(Duration > 0.0f || !string.IsNullOrWhiteSpace(SpawnTag));
    }

    private DestructibleStageController? FindJar()
    {
        if (_manager == null || !IsInstanceValid(_manager) || !_manager.IsInsideTree())
        {
            _manager = !CutsceneManagerPath.IsEmpty
                ? GetNodeOrNull<CutsceneManager>(CutsceneManagerPath)
                : GetTree().GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
        }
        if (_manager == null) return null;

        // CollectSpawnedRoots 按登记倒序返回：同标签复用时优先本次最新生成物。
        foreach (var root in _manager.CollectSpawnedRoots(SpawnTag))
        {
            // 死亡回调期间可能已经 QueueFree，但本帧对象仍有效，仍须识别。
            if (root is DestructibleStageController jar && jar.IsInsideTree()
                && (string.IsNullOrEmpty(JarGroup) || jar.IsInGroup(JarGroup)))
                return jar;
        }
        return null;
    }

    private void CheckExistingJar()
    {
        if (!IsInsideTree() || IsQueuedForDeletion() || _fadeStarted) return;
        var jar = FindJar();
        if (jar == null) return;
        if (jar.IsDeadOrDying) { BeginFade(); return; }
        if (_fadeInStarted || jar.IsQueuedForDeletion()) return;
        _fadeInStarted = true;
        _fadeInElapsed = 0.0;
        _fadeInCompleted = FadeInSeconds <= 0.0f;
        if (_fadeInCompleted) SetAlpha(_visibleAlpha);
        UpdateProcessing();
    }

    private void OnDeathFinalized(GameActor actor)
    {
        if (_fadeStarted || !IsInsideTree() || IsQueuedForDeletion()) return;
        if (actor is DestructibleStageController && FindJar() == actor) BeginFade();
    }

    private void BeginFade()
    {
        if (_fadeStarted) return;
        _fadeStarted = true;
        _fadeStartAlpha = Modulate.A;
        _fadeElapsed = 0.0;
        Unsubscribe();
        if (FadeSeconds <= 0.0f)
        {
            SetAlpha(0.0f);
            _fadeCompleted = true;
            QueueFree();
            return;
        }
        SetProcess(Duration > 0.0f || !_fadeCompleted);
    }

    public override void _Process(double delta)
    {
        _aliveElapsed += delta;
        if (Duration > 0.0f && _aliveElapsed >= Duration)
        {
            QueueFree();
            return;
        }
        // 仅等待阶段低频查询，兼容管理器或罐子晚于管网生成；绑定后停止轮询。
        if (!_fadeStarted && !_fadeInStarted && !string.IsNullOrWhiteSpace(SpawnTag))
        {
            _searchElapsed += delta;
            if (_searchElapsed >= 0.1)
            {
                _searchElapsed = 0.0;
                CheckExistingJar();
            }
        }
        if (!_fadeStarted && _fadeInStarted && !_fadeInCompleted)
        {
            _fadeInElapsed += delta;
            float t = FadeInSeconds <= 0.0f ? 1.0f
                : (float)System.Math.Clamp(_fadeInElapsed / FadeInSeconds, 0.0, 1.0);
            SetAlpha(_visibleAlpha * t);
            _fadeInCompleted = t >= 1.0f;
        }
        if (_fadeStarted && !_fadeCompleted)
        {
            _fadeElapsed += delta;
            float t = FadeSeconds <= 0.0f ? 1.0f
                : (float)System.Math.Clamp(_fadeElapsed / FadeSeconds, 0.0, 1.0);
            SetAlpha(_fadeStartAlpha * (1.0f - t));
            _fadeCompleted = t >= 1.0f;
            if (_fadeCompleted)
            {
                QueueFree();
                return;
            }
        }
        UpdateProcessing();
    }

    private void UpdateProcessing()
        => SetProcess(Duration > 0.0f || (_fadeStarted ? !_fadeCompleted
            : !string.IsNullOrWhiteSpace(SpawnTag) && (!_fadeInStarted || !_fadeInCompleted)));

    private void SetAlpha(float alpha)
    {
        var tint = Modulate;
        tint.A = alpha;
        Modulate = tint;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        GameActor.DeathFinalized -= OnDeathFinalized;
        _subscribed = false;
    }

    public override void _ExitTree()
    {
        Unsubscribe();
        _manager = null;
    }
}
