using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;

namespace Island.App;

public sealed class SkinPreview3D : Grid
{
    private readonly Viewport3D viewport = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly ModelVisual3D character = new();
    private readonly Model3DGroup scene = new();
    private readonly Ellipse shadow = new() { IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
        Fill = new RadialGradientBrush(Color.FromArgb(65, 0, 0, 0), Colors.Transparent) };
    private readonly AxisAngleRotation3D rotation = new(new Vector3D(0, 1, 0), -20);
    private readonly OrthographicCamera camera = new() { Position = new Point3D(0, 18, 70), LookDirection = new Vector3D(0, -1, -70), UpDirection = new Vector3D(0, 1, 0), NearPlaneDistance = .1, FarPlaneDistance = 200 };
    private readonly Stopwatch time = new();
    private TimeSpan? lastRenderingTime;
    private Window? owner;
    private Point dragStart;
    private double dragAngle, zoom = 1, targetYaw, targetPitch, yaw, pitch;
    private bool dragging, followPointer = true, showOuter = true, renderingSubscribed;
    private byte[]? skin;
    private string variant = "classic";
    public SkinRig Rig { get; private set; }
    public SkinPose Pose { get; private set; }
    public bool IsAnimating => renderingSubscribed;
    public bool FollowPointer
    {
        get => followPointer;
        set { followPointer = value; if (!value) { targetYaw = targetPitch = yaw = pitch = 0; RenderFrame(time.Elapsed.TotalSeconds); } }
    }
    public bool ShowOuterLayer { get => showOuter; set { showOuter = value; Rig.OuterVisible = value; } }
    public SkinPreview3D()
    {
        Background = Brushes.Transparent; ClipToBounds = true; Focusable = true;
        AutomationProperties.SetName(this, "玩家三维皮肤预览");
        Rig = new SkinRig(SkinImages.TextureOrDefault(null), "classic"); scene.Transform = new RotateTransform3D(rotation); scene.Children.Add(Rig.Model); character.Content = scene;
        viewport.Camera = camera;
        var lighting = new Model3DGroup(); lighting.Children.Add(new AmbientLight(Color.FromRgb(190, 190, 190)));
        lighting.Children.Add(new DirectionalLight(Color.FromRgb(85, 85, 85), new Vector3D(-.4, -.6, -1)));
        viewport.Children.Add(new ModelVisual3D { Content = lighting }); viewport.Children.Add(character); Children.Add(shadow); Children.Add(viewport);
        SizeChanged += (_, _) => FitCamera();
        Loaded += (_, _) => Attach(); Unloaded += (_, _) => Detach();
        IsVisibleChanged += (_, _) => UpdateAnimationState();
        MouseLeftButtonDown += (_, e) =>
        {
            Focus();
            if (e.ClickCount == 2) { ResetView(); e.Handled = true; return; }
            dragging = true; dragStart = e.GetPosition(this); dragAngle = rotation.Angle; CaptureMouse(); e.Handled = true;
        };
        MouseMove += (_, e) =>
        {
            if (!dragging) return;
            rotation.Angle = dragAngle + (e.GetPosition(this).X - dragStart.X) * .65; targetYaw = targetPitch = 0; e.Handled = true;
        };
        MouseLeftButtonUp += (_, e) => { if (dragging) { dragging = false; ReleaseMouseCapture(); e.Handled = true; } };
        LostMouseCapture += (_, _) => dragging = false;
        MouseWheel += (_, e) => { zoom = Math.Clamp(zoom * (e.Delta > 0 ? 1.1 : 1 / 1.1), .8, 1.5); FitCamera(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Left) rotation.Angle -= 10;
            else if (e.Key == Key.Right) rotation.Angle += 10;
            else if (e.Key == Key.Home) ResetView();
            else return;
            e.Handled = true;
        };
        BuildContextMenu(); FitCamera();
    }
    public void SetSkin(byte[]? png, string model)
    {
        if (ReferenceEquals(skin, png) && variant == model) return;
        skin = png; variant = model;
        Rig = new SkinRig(SkinImages.TextureOrDefault(png), model) { OuterVisible = showOuter };
        scene.Children.Clear(); scene.Children.Add(Rig.Model); RenderFrame(time.Elapsed.TotalSeconds);
    }
    public void SetPose(SkinPose pose) { Pose = pose; time.Reset(); if (renderingSubscribed) time.Start(); RenderFrame(0); UpdatePoseMenu(); }
    public void ResetView() { rotation.Angle = -20; zoom = 1; targetYaw = targetPitch = yaw = pitch = 0; FitCamera(); RenderFrame(time.Elapsed.TotalSeconds); }
    public void RenderFrame(double seconds, double deltaSeconds = 1d / 60)
    {
        var blend = 1 - Math.Exp(-12 * Math.Clamp(deltaSeconds, 0, .1));
        yaw += (targetYaw - yaw) * blend; pitch += (targetPitch - pitch) * blend;
        Rig.SetPose(Pose, seconds, yaw, pitch);
    }
    private void OnRendering(object? sender, EventArgs e)
    {
        if (e is not RenderingEventArgs frame || frame.RenderingTime == lastRenderingTime) return;
        var delta = lastRenderingTime.HasValue ? (frame.RenderingTime - lastRenderingTime.Value).TotalSeconds : 1d / 60;
        lastRenderingTime = frame.RenderingTime;
        RenderFrame(time.Elapsed.TotalSeconds, delta);
    }
    public void LookAt(Point point)
    {
        if (!FollowPointer || dragging || ActualHeight <= 0) return;
        var distance = Math.Max(90, ActualHeight * .8);
        var angle = Math.Atan2(point.X - ActualWidth / 2, distance) * 180 / Math.PI - rotation.Angle;
        angle = (angle + 180) % 360; if (angle < 0) angle += 360; angle -= 180;
        targetYaw = Math.Clamp(angle, -50, 50);
        targetPitch = Math.Clamp(Math.Atan2(point.Y - ActualHeight * .2, distance) * 180 / Math.PI, -28, 28);
    }
    private void FitCamera()
    {
        var aspect = ActualHeight > 0 ? Math.Max(.1, ActualWidth / ActualHeight) : .7;
        camera.Width = Math.Max(23, 38 * aspect) / zoom;
        shadow.Width = Math.Max(25, ActualHeight * .43); shadow.Height = Math.Max(5, ActualHeight * .035); shadow.Margin = new Thickness(0, 0, 0, ActualHeight * .035);
    }
    private void Attach()
    {
        Detach(); owner = Window.GetWindow(this);
        if (owner is not null)
        {
            owner.AddHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(TrackPointer), true);
            owner.MouseLeave += PointerLeft; owner.Activated += OwnerStateChanged; owner.Deactivated += OwnerStateChanged; owner.StateChanged += OwnerStateChanged;
        }
        UpdateAnimationState();
    }
    private void Detach()
    {
        StopRendering();
        if (owner is not null)
        {
            owner.RemoveHandler(Mouse.PreviewMouseMoveEvent, new MouseEventHandler(TrackPointer));
            owner.MouseLeave -= PointerLeft; owner.Activated -= OwnerStateChanged; owner.Deactivated -= OwnerStateChanged; owner.StateChanged -= OwnerStateChanged;
            owner = null;
        }
    }
    private void TrackPointer(object sender, MouseEventArgs e) { if (IsVisible) LookAt(e.GetPosition(this)); }
    private void PointerLeft(object sender, MouseEventArgs e) { targetYaw = targetPitch = 0; }
    private void OwnerStateChanged(object? sender, EventArgs e) => UpdateAnimationState();
    private void UpdateAnimationState()
    {
        if (IsLoaded && IsVisible && owner is { IsActive: true } && owner.WindowState != WindowState.Minimized)
        {
            if (renderingSubscribed) return;
            lastRenderingTime = null; time.Start(); renderingSubscribed = true; CompositionTarget.Rendering += OnRendering;
        }
        else StopRendering();
    }
    private void StopRendering()
    {
        if (renderingSubscribed) CompositionTarget.Rendering -= OnRendering;
        renderingSubscribed = false; lastRenderingTime = null; time.Stop();
    }
    private void BuildContextMenu()
    {
        var menu = new ContextMenu();
        foreach (var pose in Enum.GetValues<SkinPose>())
        {
            var item = new MenuItem { Header = PoseName(pose), Tag = pose, IsCheckable = true };
            item.Click += (_, _) => SetPose(pose); menu.Items.Add(item);
        }
        menu.Items.Add(new Separator()); var reset = new MenuItem { Header = "重置视角" }; reset.Click += (_, _) => ResetView(); menu.Items.Add(reset);
        ContextMenu = menu; UpdatePoseMenu();
    }
    private void UpdatePoseMenu()
    {
        if (ContextMenu is not null) foreach (var item in ContextMenu.Items.OfType<MenuItem>()) if (item.Tag is SkinPose pose) item.IsChecked = pose == Pose;
    }
    public static string PoseName(SkinPose pose) => pose switch { SkinPose.Walking => "行走", SkinPose.Running => "奔跑", SkinPose.Waving => "招手", _ => "站立" };
}
