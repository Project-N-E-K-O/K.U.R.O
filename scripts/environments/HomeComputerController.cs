using Godot;
using Kuros.Actors.Heroes;
using Kuros.Managers;
using Kuros.Scenes;
using Kuros.Systems.Cutscene;
using System.Threading.Tasks;

namespace Kuros.Environments;

/// <summary>通过桌子的交互区推镜到内屏，并在当前房间打开桌面。</summary>
public partial class HomeComputerController : Node2D
{
    internal const int DesktopCanvasLayer = 150;

    [Export] public PackedScene DesktopScene { get; set; } = null!;
    [Export] public NodePath RoomCameraZonePath { get; set; } = new("../CameraZone");
    [Export(PropertyHint.Range, "0,5,0.05")] public float CenterDuration { get; set; } = 0.35f;
    [Export(PropertyHint.Range, "0,5,0.05")] public float ZoomDuration { get; set; } = 0.65f;
    [Export(PropertyHint.Range, "0,2,0.05")] public float ScreenFadeDuration { get; set; } = 0.2f;
    /// <summary>桌面关闭至内屏熄屏的等待时间。镜头拉远会立即开始。</summary>
    [Export(PropertyHint.Range, "0,10,0.1,or_greater")] public float SnapshotHoldSeconds { get; set; } = 1f;

    private Area2D _interactionArea = null!;
    private Marker2D _screenCenter = null!;
    private Marker2D _topLeft = null!;
    private Marker2D _bottomRight = null!;
    private ColorRect _snapshot = null!;
    private ShaderMaterial _snapshotMaterial = null!;
    private AnimationPlayer _screenAnimation = null!;
    private bool _sessionRunning;
    private bool _closeRequested;
    private CutsceneManager? _activeManager;
    private PauseManager? _desktopPauseManager;

    public override void _Ready()
    {
        _interactionArea = GetNode<Area2D>("InteractArea");
        const string bounds = "Outline/Screen/ScreenBounds/";
        _screenCenter = GetNode<Marker2D>(bounds + "ScreenCenter");
        _topLeft = GetNode<Marker2D>(bounds + "TopLeft");
        _bottomRight = GetNode<Marker2D>(bounds + "BottomRight");
        _snapshot = GetNode<ColorRect>(bounds + "Snapshot");
        _snapshotMaterial = (ShaderMaterial)_snapshot.Material;
        _screenAnimation = GetNode<AnimationPlayer>("ScreenAnimationPlayer");
    }

    public override void _ExitTree() => ReleaseDesktopPause();

    public override void _Process(double delta)
    {
        if (_sessionRunning || PauseManager.Instance?.IsPaused == true || DialogueManager.Instance?.IsDialogueActive == true
            || !SamplePlayer.IsActionJustPressedGlobal("interact")) return;

        var manager = GetTree().GetFirstNodeInGroup("cutscene_manager") as CutsceneManager;
        if (manager == null || manager.IsPlaying || manager.Camera == null
            || manager.Player is not SamplePlayer player || player.IsDeadOrDying || !player.CanProcess()
            || !IsPlayerInRange(player)) return;

        _ = PlayComputerAsync(manager);
    }

    public override void _Input(InputEvent @event)
    {
        if (!_sessionRunning || _closeRequested || !DesktopMenu.IsEscapePressed(@event)) return;
        GetViewport().SetInputAsHandled();
        RequestClose();
    }

    private void RequestClose()
    {
        if (_closeRequested) return;
        _closeRequested = true;
        ReleaseDesktopPause();
        if (IsInstanceValid(_activeManager)) _activeManager!.ReleaseInteractivePlayer();
    }

    private void ReleaseDesktopPause()
    {
        var manager = _desktopPauseManager;
        _desktopPauseManager = null;
        if (IsInstanceValid(manager)) manager!.PopPause();
    }

    internal async Task PlayComputerAsync(CutsceneManager manager, CanvasLayer? arrivalLayer = null)
    {
        if (_sessionRunning || manager.IsPlaying || manager.Camera is not { } camera) return;

        var sequence = new CutsceneSequence
        {
            SequenceId = "HomeComputer",
            TakeOverCamera = true,
            PreciseCamera = true,
            HidePlayer = false,
        };
        var previousCenter = camera.GetScreenCenterPosition();
        var roomZone = GetNodeOrNull<CameraZoneArea>(RoomCameraZonePath);
        _sessionRunning = true;
        _closeRequested = false;
        _activeManager = manager;
        SetProcessInput(true);
        try
        {
            await manager.PlayInteractiveCutscene(sequence,
                ctx => RunComputerAsync(ctx, previousCenter, roomZone, arrivalLayer));
        }
        finally
        {
            if (IsInstanceValid(this)) SetProcessInput(false);
            _sessionRunning = false;
            _activeManager = null;
        }
    }

    private bool IsPlayerInRange(SamplePlayer player)
    {
        foreach (var area in _interactionArea.GetOverlappingAreas())
            if (player.IsAncestorOf(area)) return true;
        return false;
    }

    private float GetScreenZoom(Camera2D camera)
    {
        var viewSize = camera.GetViewportRect().Size;
        var size = (_bottomRight.GlobalPosition - _topLeft.GlobalPosition).Abs();
        return 1.02f * Mathf.Max(viewSize.X / size.X, viewSize.Y / size.Y);
    }

    private async Task RunComputerAsync(CutsceneContext ctx, Vector2 previousCenter, CameraZoneArea? roomZone,
        CanvasLayer? arrivalLayer)
    {
        var camera = ctx.Manager.Camera!;
        var zoomBefore = ctx.Manager.NormalCameraZoom.X;
        if (arrivalLayer == null && DesktopScene == null) return;
        var screenSize = (_bottomRight.GlobalPosition - _topLeft.GlobalPosition).Abs();
        if (screenSize.X < 1f || screenSize.Y < 1f)
        {
            GD.PushWarning("[HomeComputer] 屏幕边界无效");
            return;
        }

        var desktopLayer = arrivalLayer;
        DesktopMenu? desktop = null;
        ImageTexture? snapshot = null;
        bool cameraMoved = false;
        bool screenFramed = false;
        try
        {
            desktop = arrivalLayer == null ? DesktopScene.Instantiate<DesktopMenu>() : arrivalLayer.GetChild<DesktopMenu>(0);
            if (!desktop.IsInGameComputer)
            {
                GD.PushError("[HomeComputer] 电脑桌面必须启用 IsInGameComputer");
                return;
            }

            if (roomZone != null) ctx.Manager.SetCameraReturnZone(roomZone);
            cameraMoved = true;
            if (arrivalLayer == null)
            {
                // 从玩家实际看到的画面中心开始，避免停用死区和跟随平滑时跳镜。
                camera.GlobalPosition = previousCenter;
                camera.ResetSmoothing();
                camera.ForceUpdateScroll();
                await MoveCamera(ctx, _screenCenter.GlobalPosition, 0f, CenterDuration);
                if (_closeRequested || !IsSessionActive(ctx)) return;
                await MoveCamera(ctx, _screenCenter.GlobalPosition, GetScreenZoom(camera), ZoomDuration);
                if (!IsSessionActive(ctx)) return;
                screenFramed = true;
                if (_closeRequested) return;

                desktopLayer = new CanvasLayer { Name = "ComputerDesktopLayer", Layer = DesktopCanvasLayer };
                desktop.Modulate = new Color(1, 1, 1, 0);
                desktop.ProcessMode = ProcessModeEnum.Disabled;
                desktopLayer.AddChild(desktop);
                AddChild(desktopLayer);
            }
            else
            {
                // 保留换场时已显示的桌面实例，镜头在同一帧对准内屏。
                arrivalLayer.Reparent(this);
                screenFramed = true;
            }

            desktop.ComputerClosed += RequestClose;
            FrameScreen(camera);
            if (arrivalLayer == null) await FadeDesktop(ctx, desktopLayer!, desktop, 1f);
            if (!IsSessionActive(ctx)) return;
            if (!_closeRequested)
            {
                // 桌面开始接收输入后，由它和子窗口逐级处理 Esc。
                SetProcessInput(false);
                desktop.ProcessMode = ProcessModeEnum.Always;
                desktop.FocusComputerCloseButton();
                _desktopPauseManager = PauseManager.Instance;
                _desktopPauseManager?.PushPause();
                while (!_closeRequested && IsSessionActive(ctx) && IsInstanceValid(desktop) && !desktop.IsQueuedForDeletion())
                    await ctx.NextFrame();
            }

            if (!IsSessionActive(ctx) || !IsInstanceValid(desktop)) return;
            desktop.ProcessMode = ProcessModeEnum.Disabled;
            bool desktopReady = desktop.Modulate.A >= 0.999f;
            FrameScreen(camera);
            if (desktopReady) snapshot = await CaptureDesktop(ctx, camera);
        }
        finally
        {
            ReleaseDesktopPause();
            try
            {
                if (cameraMoved && !ctx.IsCancelled && IsInstanceValid(camera))
                {
                    var returnZoom = IsInstanceValid(roomZone) ? roomZone!.ZoomLevel : zoomBefore;
                    if (snapshot != null && IsInstanceValid(desktopLayer))
                        desktopLayer!.Hide();

                    Task screenTransition = Task.CompletedTask;
                    if (snapshot != null && IsSessionActive(ctx))
                        screenTransition = ShutdownScreen(ctx);
                    else if (IsSessionActive(ctx) && IsInstanceValid(desktopLayer) && IsInstanceValid(desktop))
                        screenTransition = FadeDesktop(ctx, desktopLayer!, desktop!, 0f);

                    if (screenFramed && IsSessionActive(ctx))
                    {
                        // 保持 CameraZone 限位。视口碰到房间边界后，由 Camera2D 修正画面中心。
                        var screenCenter = _screenCenter.GlobalPosition;
                        Tween? fitSnapshot = snapshot != null ? FitSnapshotToScreen() : null;
                        try
                        {
                            camera.GlobalPosition = screenCenter;
                            camera.ResetSmoothing();
                            camera.ForceUpdateScroll();
                            await MoveCamera(ctx, screenCenter, returnZoom, ZoomDuration);
                        }
                        finally
                        {
                            if (fitSnapshot?.IsRunning() == true) fitSnapshot.Kill();
                        }
                    }
                    if (!ctx.IsCancelled && IsInstanceValid(camera))
                    {
                        await ReturnToPlayerAsync(ctx, camera, previousCenter, returnZoom);
                        if (!ctx.IsCancelled) ctx.Manager.ReleaseInteractiveCamera();
                    }
                    await screenTransition;
                }
            }
            finally
            {
                if (IsInstanceValid(desktopLayer)) desktopLayer!.QueueFree();
                else if (IsInstanceValid(desktop)) desktop!.QueueFree();
                if (snapshot != null)
                {
                    if (IsInstanceValid(_snapshot)) _snapshot.Hide();
                    if (IsInstanceValid(_screenAnimation)) _screenAnimation.Stop();
                    if (IsInstanceValid(_snapshotMaterial))
                        _snapshotMaterial.SetShaderParameter("desktop_snapshot", default(Variant));
                    snapshot.Dispose();
                }
            }
        }
    }

    private Tween? FitSnapshotToScreen()
    {
        if (ZoomDuration <= 0f)
        {
            _snapshotMaterial.SetShaderParameter("snapshot_area", Vector2.One);
            return null;
        }

        // 交接帧保持原视野，拉远时将完整桌面逐渐贴合内屏。
        var tween = CreateTween();
        tween.TweenProperty(_snapshot, "material:shader_parameter/snapshot_area", Vector2.One, ZoomDuration)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
        return tween;
    }

    private async Task<ImageTexture?> CaptureDesktop(CutsceneContext ctx, Camera2D camera)
    {
        // 无渲染的运行环境没有可用帧，沿用淡出。
        if (DisplayServer.GetName() == "headless") return null;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        if (!IsSessionActive(ctx) || !IsInstanceValid(camera)) return null;

        using var image = GetViewport().GetTexture().GetImage();
        if (image == null || image.IsEmpty()) return null;

        var screenSize = (_bottomRight.GlobalPosition - _topLeft.GlobalPosition).Abs();
        var viewSize = camera.GetViewportRect().Size;
        // 还原刚才的可见区域，避免交接时把整张桌面硬拉伸到宽比例内屏。
        var snapshotArea = viewSize / (screenSize * camera.Zoom);
        var texture = ImageTexture.CreateFromImage(image);
        _snapshotMaterial.SetShaderParameter("desktop_snapshot", texture);
        _snapshotMaterial.SetShaderParameter("snapshot_area", snapshotArea);
        _snapshotMaterial.SetShaderParameter("progress", 0f);
        _snapshot.Show();
        return texture;
    }

    private async Task ShutdownScreen(CutsceneContext ctx)
    {
        if (SnapshotHoldSeconds > 0f)
        {
            var timer = ctx.Tree.CreateTimer(SnapshotHoldSeconds, processAlways: false);
            while (IsSessionActive(ctx) && timer.TimeLeft > 0d)
                await ctx.NextFrame();
        }
        if (!IsSessionActive(ctx)) return;

        _screenAnimation.Play("shutdown");
        _screenAnimation.Advance(0);
        while (IsSessionActive(ctx) && _screenAnimation.IsPlaying())
            await ctx.NextFrame();
        if (IsSessionActive(ctx)) _snapshot.Hide();
    }

    private async Task ReturnToPlayerAsync(CutsceneContext ctx, Camera2D camera, Vector2 previousCenter, float zoom)
    {
        Vector2 GetTarget() => camera is CameraFollow follow && follow.TryGetFollowPosition(out var position)
            ? position : previousCenter;

        if (CenterDuration <= 0f)
        {
            await MoveCamera(ctx, GetTarget(), zoom, CenterDuration);
            return;
        }

        // Camera2D 的节点坐标仍可能停在内屏上，从已受房间限位约束的画面中心开始平移。
        camera.ForceUpdateScroll();
        var from = camera.GetScreenCenterPosition() - camera.Offset;
        var fromZoom = camera.Zoom;
        var tween = camera.CreateTween().SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
        tween.TweenMethod(Callable.From<float>(weight =>
        {
            camera.GlobalPosition = from.Lerp(GetTarget(), weight);
            camera.Zoom = fromZoom.Lerp(Vector2.One * zoom, weight);
        }), 0f, 1f, CenterDuration);
        while (!ctx.IsCancelled && tween.IsRunning())
            await ctx.NextFrame();
        if (ctx.IsCancelled && tween.IsRunning()) tween.Kill();
    }

    private bool IsSessionActive(CutsceneContext ctx)
        => !ctx.IsCancelled && IsInstanceValid(this) && IsInsideTree() && !IsQueuedForDeletion();

    private void FrameScreen(Camera2D camera)
    {
        camera.GlobalPosition = _screenCenter.GlobalPosition;
        camera.Offset = Vector2.Zero;
        camera.Zoom = Vector2.One * GetScreenZoom(camera);
        camera.ResetSmoothing();
        camera.ForceUpdateScroll();
    }

    private async Task FadeDesktop(CutsceneContext ctx, CanvasLayer layer, Control desktop, float opacity)
    {
        if (ScreenFadeDuration <= 0f)
        {
            desktop.Modulate = new Color(1, 1, 1, opacity);
            return;
        }

        // 桌面在过渡期间禁用输入。将 Tween 绑定到仍在处理的覆盖层。
        var tween = layer.CreateTween();
        tween.TweenProperty(desktop, "modulate:a", opacity, ScreenFadeDuration);
        bool opening = opacity > 0f;
        while (IsSessionActive(ctx) && IsInstanceValid(desktop) && tween.IsRunning()
            && (!opening || !_closeRequested))
            await ctx.NextFrame();
        if (tween.IsRunning()) tween.Kill();
    }

    private static async Task MoveCamera(CutsceneContext ctx, Vector2 position, float zoom, float duration)
    {
        using var step = new CameraMoveStep
        {
            TargetPosition = position,
            TargetZoom = zoom,
            Duration = duration,
            WaitForCompletion = true,
        };
        await step.Execute(ctx);
    }
}
