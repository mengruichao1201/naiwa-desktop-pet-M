using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

internal enum PetAction { Idle, Head, Hand, Laugh, Battle, Run, Settle }

internal static class PetMotion
{
    internal const double SceneWidth = 600, SceneHeight = 840;
    internal const double CharacterHeight = 660, FeetY = 750;
    internal const double LaughFps = 12;
    internal const double BattleEnterSeconds = 0.30, BattleHoldSeconds = 2.0, BattleExitSeconds = 0.30;
    internal static double Clamp(double x, double a, double b) { return Math.Max(a, Math.Min(b, x)); }
    internal static double Smooth(double x) { x = Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    internal static double HeadAngle(double seconds)
    {
        double u = Clamp(seconds / 1.05, 0, 1);
        return -10 * Math.Sin(Math.PI * u) * Math.Sin(3 * Math.PI * u) * Math.Exp(-1.5 * u);
    }
    internal static double BattleWeight(double seconds)
    {
        if (seconds < BattleEnterSeconds) return Smooth(seconds / BattleEnterSeconds);
        if (seconds < BattleEnterSeconds + BattleHoldSeconds) return 1;
        return 1 - Smooth((seconds - BattleEnterSeconds - BattleHoldSeconds) / BattleExitSeconds);
    }
    internal static int BattleFrame(double seconds)
    {
        if (seconds < BattleEnterSeconds) return Math.Min(2, (int)(seconds / (BattleEnterSeconds / 3)));
        if (seconds < BattleEnterSeconds + BattleHoldSeconds) return 2;
        return Math.Min(5, 4 + (int)((seconds - BattleEnterSeconds - BattleHoldSeconds) / (BattleExitSeconds / 2)));
    }
    internal static Rect FrameRect(PetFrame reference, PetFrame image)
    {
        // One scale and one anchor per sequence. Never fit each bent pose separately.
        double s = CharacterHeight / reference.AlphaBounds.Height;
        return new Rect(SceneWidth / 2 - (reference.AlphaBounds.X + reference.AlphaBounds.Width / 2) * s,
            FeetY - (reference.AlphaBounds.Y + reference.AlphaBounds.Height) * s, image.Bitmap.PixelWidth * s, image.Bitmap.PixelHeight * s);
    }
    internal static Point DragOrigin(Point cursor, double anchorX, double anchorY, double width, double height)
    {
        return new Point(cursor.X - anchorX * width, cursor.Y - anchorY * height);
    }
}

internal sealed class PetFrame
{
    internal readonly BitmapSource Bitmap;
    internal readonly Int32Rect AlphaBounds;
    private readonly byte[] alpha;
    internal PetFrame(BitmapSource bitmap) : this(bitmap, false) { }
    internal PetFrame(BitmapSource bitmap, bool removeWhiteBackground)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        converted.CopyPixels(pixels, w * 4, 0);
        if (removeWhiteBackground) ClearOutsideWhite(pixels, w, h);
        if (removeWhiteBackground)
        {
            Bitmap = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4);
            Bitmap.Freeze();
        }
        else Bitmap = bitmap;
        alpha = new byte[w * h];
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
        {
            byte a = pixels[(y * w + x) * 4 + 3];
            alpha[y * w + x] = a;
            if (a < 40) continue;
            x0 = Math.Min(x0, x); y0 = Math.Min(y0, y);
            x1 = Math.Max(x1, x); y1 = Math.Max(y1, y);
        }
        if (x1 < x0) throw new InvalidDataException("动作图片没有可见角色。");
        AlphaBounds = new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }
    private static bool IsOutsideWhite(byte[] pixels, int p)
    {
        int i = p * 4;
        int low = Math.Min(pixels[i], Math.Min(pixels[i + 1], pixels[i + 2]));
        int high = Math.Max(pixels[i], Math.Max(pixels[i + 1], pixels[i + 2]));
        return pixels[i + 3] < 8 || (low >= 220 && high - low <= 30);
    }
    private static void ClearOutsideWhite(byte[] pixels, int w, int h)
    {
        // Flood-fill the exterior only: pale belly/eye pixels enclosed by the character stay opaque.
        var seen = new bool[w * h]; var queue = new int[w * h]; int read = 0, write = 0;
        for (int x = 0; x < w; x++) { EnqueueWhite(x, pixels, seen, queue, ref write); EnqueueWhite((h - 1) * w + x, pixels, seen, queue, ref write); }
        for (int y = 0; y < h; y++) { EnqueueWhite(y * w, pixels, seen, queue, ref write); EnqueueWhite(y * w + w - 1, pixels, seen, queue, ref write); }
        while (read < write)
        {
            int p = queue[read++], x = p % w, y = p / w; pixels[p * 4 + 3] = 0;
            if (x > 0) EnqueueWhite(p - 1, pixels, seen, queue, ref write);
            if (x + 1 < w) EnqueueWhite(p + 1, pixels, seen, queue, ref write);
            if (y > 0) EnqueueWhite(p - w, pixels, seen, queue, ref write);
            if (y + 1 < h) EnqueueWhite(p + w, pixels, seen, queue, ref write);
        }
    }
    private static void EnqueueWhite(int p, byte[] pixels, bool[] seen, int[] queue, ref int write)
    {
        if (seen[p]) return; seen[p] = true;
        if (IsOutsideWhite(pixels, p)) queue[write++] = p;
    }
    internal bool Contains(double u, double v)
    {
        int x = (int)(u * Bitmap.PixelWidth), y = (int)(v * Bitmap.PixelHeight);
        return u >= 0 && v >= 0 && u < 1 && v < 1 && alpha[y * Bitmap.PixelWidth + x] >= 24;
    }
}

internal sealed class PetClip
{
    internal readonly PetFrame[] Frames;
    internal readonly Rect[] Rectangles;
    internal PetClip(PetFrame[] frames) : this(frames, false) { }
    internal PetClip(PetFrame[] frames, bool stabilize)
    {
        Frames = frames; Rectangles = new Rect[frames.Length];
        for (int i = 0; i < frames.Length; i++)
        {
            Rectangles[i] = PetMotion.FrameRect(frames[0], frames[i]);
            if (stabilize)
            {
                double s = PetMotion.CharacterHeight / frames[0].AlphaBounds.Height;
                Rectangles[i] = new Rect(PetMotion.SceneWidth / 2 - (frames[i].AlphaBounds.X + frames[i].AlphaBounds.Width / 2) * s,
                    PetMotion.FeetY - (frames[i].AlphaBounds.Y + frames[i].AlphaBounds.Height) * s, frames[i].Bitmap.PixelWidth * s, frames[i].Bitmap.PixelHeight * s);
            }
        }
    }
    internal static BitmapSource ReadBitmap(string name)
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (stream == null) return null;
            var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad;
            b.StreamSource = stream; b.EndInit(); b.Freeze(); return b;
        }
    }
    internal static PetClip LoadSheet(string name, int columns, int rows)
    {
        BitmapSource sheet = ReadBitmap(name);
        if (sheet == null) return null;
        if (sheet.PixelWidth % columns != 0 || sheet.PixelHeight % rows != 0)
            throw new InvalidDataException(name + " 的网格尺寸不正确。");
        int w = sheet.PixelWidth / columns, h = sheet.PixelHeight / rows;
        var frames = new PetFrame[columns * rows];
        for (int i = 0; i < frames.Length; i++)
        {
            var b = new CroppedBitmap(sheet, new Int32Rect(i % columns * w, i / columns * h, w, h));
            b.Freeze(); frames[i] = new PetFrame(b, true);
        }
        return new PetClip(frames, true);
    }
}

// Draw in a fixed logical canvas; the visible character has its own hit area.
internal sealed class PetScene : FrameworkElement
{
    internal PetClip CurrentClip;
    internal int Frame;
    internal bool Mirrored;
    internal double Head, Bob, Angle, ScaleX = 1, ScaleY = 1, LegPhase, Crouch;
    internal bool PuppetRun;
    private readonly PetClip idle;
    private readonly BitmapSource[] headStrips;
    private readonly int[] stripY;
    private readonly int neckY;
    internal PetScene(PetClip idleClip)
    {
        idle = idleClip; CurrentClip = idle; Width = PetMotion.SceneWidth; Height = PetMotion.SceneHeight;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        BitmapSource b = idle.Frames[0].Bitmap;
        neckY = (int)(b.PixelHeight * 0.31);
        headStrips = new BitmapSource[28]; stripY = new int[headStrips.Length];
        for (int i = 0; i < headStrips.Length; i++)
        {
            int y = neckY * i / headStrips.Length;
            int end = Math.Min(neckY, neckY * (i + 1) / headStrips.Length + 2);
            stripY[i] = y;
            var strip = new CroppedBitmap(b, new Int32Rect(0, y, b.PixelWidth, end - y));
            strip.Freeze(); headStrips[i] = strip;
        }
    }
    internal void Reset()
    {
        Head = Bob = Angle = LegPhase = Crouch = 0; ScaleX = ScaleY = 1; PuppetRun = Mirrored = false;
    }
    protected override HitTestResult HitTestCore(PointHitTestParameters parameters)
    {
        Point p = parameters.HitPoint;
        if (Mirrored) p.X = PetMotion.SceneWidth - p.X;
        Rect r = CurrentClip.Rectangles[Frame];
        return CurrentClip.Frames[Frame].Contains((p.X - r.X) / r.Width, (p.Y - r.Y) / r.Height)
            ? new PointHitTestResult(this, parameters.HitPoint) : null;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double cx = PetMotion.SceneWidth / 2, feet = PetMotion.FeetY;
        dc.PushTransform(new ScaleTransform(ScaleX, ScaleY, cx, feet));
        dc.PushTransform(new RotateTransform(Angle, cx, feet));
        dc.PushTransform(new TranslateTransform(0, Bob));
        if (Mirrored) dc.PushTransform(new ScaleTransform(-1, 1, cx, feet));
        if (CurrentClip == idle && Math.Abs(Head) > 0.001) DrawHead(dc);
        else if (CurrentClip == idle && PuppetRun) DrawRunningLegs(dc);
        else dc.DrawImage(CurrentClip.Frames[Frame].Bitmap, CurrentClip.Rectangles[Frame]);
        if (Mirrored) dc.Pop();
        dc.Pop(); dc.Pop(); dc.Pop();
    }
    private void DrawHead(DrawingContext dc)
    {
        Rect r = idle.Rectangles[0]; BitmapSource b = idle.Frames[0].Bitmap;
        double scale = r.Height / b.PixelHeight, split = r.Y + neckY * scale;
        dc.PushClip(new RectangleGeometry(new Rect(r.X - 20, split, r.Width + 40, r.Bottom - split + 2)));
        dc.DrawImage(b, r); dc.Pop();
        // Overlapping thin strips bend the neck continuously instead of cutting out a rigid head.
        for (int i = headStrips.Length - 1; i >= 0; i--)
        {
            double weight = 1 - PetMotion.Smooth((double)stripY[i] / neckY);
            dc.PushTransform(new RotateTransform(Head * weight, r.X + r.Width * 0.52, split));
            dc.DrawImage(headStrips[i], new Rect(r.X, r.Y + stripY[i] * scale,
                r.Width, headStrips[i].PixelHeight * scale)); dc.Pop();
        }
    }
    private void DrawRunningLegs(DrawingContext dc)
    {
        Rect r = idle.Rectangles[0]; double hips = r.Y + r.Height * 0.70;
        for (int leg = 0; leg < 2; leg++)
        {
            double phase = LegPhase + leg * Math.PI;
            dc.PushTransform(new RotateTransform(Math.Sin(phase) * 13,
                r.X + r.Width * (leg == 0 ? 0.32 : 0.69), hips));
            dc.PushTransform(new TranslateTransform(0, -Math.Max(0, Math.Sin(phase)) * 13));
            dc.PushClip(new RectangleGeometry(new Rect(leg == 0 ? r.X : r.X + r.Width * 0.52,
                hips, r.Width * (leg == 0 ? 0.52 : 0.48), r.Bottom - hips + 1)));
            dc.DrawImage(idle.Frames[0].Bitmap, r); dc.Pop(); dc.Pop(); dc.Pop();
        }
        dc.PushClip(new RectangleGeometry(new Rect(r.X, r.Y, r.Width, hips - r.Y + r.Height * 0.025)));
        dc.DrawImage(idle.Frames[0].Bitmap, r); dc.Pop();
    }
}

internal sealed class NaiwaPetSprite : Window
{
    private readonly PetClip idle, laugh, run, battle;
    private readonly PetScene scene;
    private readonly MediaPlayer laughPlayer = new MediaPlayer();
    private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
    private readonly Stopwatch clock = new Stopwatch();
    private PetAction action;
    private bool pointerDown, dragging, exiting, rendering, audioReady, audioPlaying;
    private bool soundEnabled = true;
    private double sizeScale = 0.62, audioDuration, dragAnchorX, dragAnchorY;
    private NativePoint mouseStart, previousMouse;
    private Point clickPoint;
    private IntPtr handle;
    private int runDirection = -1;
    private TimeSpan lastRenderTime;
    internal PetScene Scene { get { return scene; } }
    internal PetClip IdleClip { get { return idle; } }
    internal PetClip LaughClip { get { return laugh; } }
    internal PetClip RunClip { get { return run; } }
    internal PetClip BattleClip { get { return battle; } }

    [STAThread]
    private static int Main()
    {
        try
        {
            var app = new Application(); app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            app.Run(new NaiwaPetSprite()); return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("奶蛙桌宠启动失败：\n" + ex.Message, "奶蛙桌宠", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }
    internal NaiwaPetSprite()
    {
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = PetMotion.SceneWidth * sizeScale; Height = PetMotion.SceneHeight * sizeScale;
        BitmapSource standing = PetClip.ReadBitmap("naiwa-cutout.png");
        if (standing == null) throw new InvalidDataException("内置立绘缺失。");
        idle = new PetClip(new PetFrame[] { new PetFrame(standing) });
        var frames = new PetFrame[92];
        for (int i = 0; i < frames.Length; i++)
        {
            BitmapSource b = PetClip.ReadBitmap("laugh-" + i.ToString("D3") + ".png");
            if (b == null) throw new InvalidDataException("捧腹动画资源缺失：第 " + i + " 帧");
            frames[i] = new PetFrame(b);
        }
        laugh = new PetClip(frames);
        run = PetClip.LoadSheet("run-sheet.png", 3, 3);
        battle = PetClip.LoadSheet("battle-sheet.png", 3, 2);
        scene = new PetScene(idle);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = scene };
        PreviewMouseLeftButtonDown += OnMouseDownPet;
        PreviewMouseLeftButtonUp += OnMouseUpPet;
        PreviewMouseMove += OnMouseMovePet;
        PreviewMouseWheel += OnMouseWheelPet;
        PreviewMouseRightButtonDown += OnRightClickPet;
        LostMouseCapture += delegate { if (pointerDown) EndPointer(); };
        PreviewKeyDown += delegate(object sender, KeyEventArgs e)
        { if (e.Key == Key.Escape) { HidePet(); e.Handled = true; } };
        SourceInitialized += delegate { handle = new WindowInteropHelper(this).Handle; PlaceOnCursorScreen(); };
        IsVisibleChanged += delegate { if (!IsVisible) { EndPointer(); SetAction(PetAction.Idle); } };
        Closing += OnClosingPet;
        Closed += delegate { Cleanup(); };
        string audio = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "laugh.mp3");
        laughPlayer.MediaOpened += delegate
        {
            audioReady = true;
            if (laughPlayer.NaturalDuration.HasTimeSpan) audioDuration = laughPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (action == PetAction.Laugh) PlayLaugh();
        };
        laughPlayer.MediaFailed += delegate { audioReady = false; audioPlaying = false; };
        if (File.Exists(audio)) laughPlayer.Open(new Uri(audio));
        tray.Icon = CreateTrayIcon(); tray.ContextMenuStrip = MakeTrayMenu();
        tray.Visible = true; tray.DoubleClick += delegate { if (IsVisible) HidePet(); else ShowPet(false); };
        UpdateTitle();
    }
    private void OnMouseDownPet(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !GetCursorPos(out mouseStart)) return;
        NativeRect r; if (!GetWindowRect(handle, out r)) return;
        dragAnchorX = (double)(mouseStart.X - r.Left) / (r.Right - r.Left);
        dragAnchorY = (double)(mouseStart.Y - r.Top) / (r.Bottom - r.Top);
        previousMouse = mouseStart; clickPoint = e.GetPosition(scene);
        dragging = false; pointerDown = true; CaptureMouse(); e.Handled = true;
    }
    private void OnMouseMovePet(object sender, MouseEventArgs e)
    {
        if (!pointerDown) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndPointer(); return; }
        NativePoint cursor; if (!GetCursorPos(out cursor)) return;
        if (!dragging && (Math.Abs(cursor.X - mouseStart.X) >= 5 || Math.Abs(cursor.Y - mouseStart.Y) >= 5))
        {
            dragging = true;
            runDirection = cursor.X >= mouseStart.X ? 1 : -1;
            SetAction(PetAction.Run);
        }
        if (dragging)
        {
            // Screen coordinates stay fixed when the window moves. No smoothing queue or local-coordinate feedback.
            if (Math.Abs(cursor.X - previousMouse.X) >= 2) runDirection = cursor.X > previousMouse.X ? 1 : -1;
            FollowCursor(new Point(cursor.X, cursor.Y), dragAnchorX, dragAnchorY);
        }
        previousMouse = cursor; e.Handled = true;
    }
    private void OnMouseUpPet(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !pointerDown) return;
        bool moved = dragging; Point p = clickPoint;
        EndPointer(); e.Handled = true;
        if (moved) return;
        Rect r = idle.Rectangles[0];
        double nx = (p.X - r.X) / r.Width, ny = (p.Y - r.Y) / r.Height;
        if (ny < 0.29) SetAction(PetAction.Head);
        else if (ny > 0.73) SetAction(PetAction.Battle);
        else if (ny > 0.42 && (nx < 0.20 || nx > 0.82)) SetAction(PetAction.Hand);
        else SetAction(PetAction.Laugh);
    }
    private void EndPointer()
    {
        bool wasDragging = dragging; pointerDown = dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        if (wasDragging && action == PetAction.Run) SetAction(PetAction.Settle);
    }
    private void OnMouseWheelPet(object sender, MouseWheelEventArgs e)
    {
        double next = PetMotion.Clamp(sizeScale + e.Delta / 120.0 * 0.06, 0.34, 1.30);
        ResizePet(next); e.Handled = true;
    }
    internal void ResizePet(double next)
    {
        if (Math.Abs(next - sizeScale) < 0.0001) return;
        NativeRect before = new NativeRect(); bool positioned = handle != IntPtr.Zero && GetWindowRect(handle, out before);
        double anchorX = dragging ? dragAnchorX : 0.5;
        double anchorY = dragging ? dragAnchorY : PetMotion.FeetY / PetMotion.SceneHeight;
        double px = before.Left + (before.Right - before.Left) * anchorX;
        double py = before.Top + (before.Bottom - before.Top) * anchorY;
        sizeScale = next; Width = PetMotion.SceneWidth * next; Height = PetMotion.SceneHeight * next;
        UpdateLayout();
        NativeRect after;
        if (positioned && GetWindowRect(handle, out after))
            MoveWindowTo(px - (after.Right - after.Left) * anchorX, py - (after.Bottom - after.Top) * anchorY);
        UpdateTitle();
    }
    internal void SetAction(PetAction next)
    {
        if (audioPlaying) { laughPlayer.Stop(); audioPlaying = false; }
        action = next; clock.Restart(); lastRenderTime = TimeSpan.MinValue;
        scene.Reset(); scene.CurrentClip = idle; scene.Frame = 0;
        EvaluateAction(0);
        if (next == PetAction.Laugh) PlayLaugh();
        if (next == PetAction.Idle) StopRendering();
        else if (!rendering) { CompositionTarget.Rendering += OnRendering; rendering = true; }
    }
    private void OnRendering(object sender, EventArgs e)
    {
        var render = e as RenderingEventArgs;
        if (render != null && render.RenderingTime == lastRenderTime) return;
        if (render != null) lastRenderTime = render.RenderingTime;
        EvaluateAction(clock.Elapsed.TotalSeconds);
    }
    internal void EvaluateAction(double t)
    {
        scene.Reset(); scene.CurrentClip = idle; scene.Frame = 0;
        if (action == PetAction.Head)
        {
            if (t >= 1.05) { SetAction(PetAction.Idle); return; }
            scene.Head = PetMotion.HeadAngle(t);
            scene.Angle = scene.Head * 0.08;
        }
        else if (action == PetAction.Hand)
        {
            if (t >= 0.85) { SetAction(PetAction.Idle); return; }
            double envelope = Math.Sin(Math.PI * t / 0.85);
            scene.Angle = 2.5 * Math.Sin(t * 15) * envelope;
            scene.ScaleX = 1 + 0.012 * Math.Sin(t * 15) * envelope;
        }
        else if (action == PetAction.Laugh)
        {
            int index = (int)(t * PetMotion.LaughFps);
            if (index >= laugh.Frames.Length) { SetAction(PetAction.Idle); return; }
            scene.CurrentClip = laugh; scene.Frame = Math.Max(0, index);
        }
        else if (action == PetAction.Battle)
        {
            if (t >= PetMotion.BattleEnterSeconds + PetMotion.BattleHoldSeconds + PetMotion.BattleExitSeconds)
            { SetAction(PetAction.Idle); return; }
            if (battle != null)
            {
                scene.CurrentClip = battle; scene.Frame = PetMotion.BattleFrame(t);
                if (t >= PetMotion.BattleEnterSeconds && t < PetMotion.BattleEnterSeconds + PetMotion.BattleHoldSeconds)
                    scene.ScaleY = 1 + 0.003 * Math.Sin((t - PetMotion.BattleEnterSeconds) * Math.PI);
            }
            else
            {
                double weight = PetMotion.BattleWeight(t);
                scene.ScaleX = 1 + 0.055 * weight; scene.ScaleY = 1 - 0.09 * weight;
                scene.Angle = -2 * weight;
            }
        }
        else if (action == PetAction.Run)
        {
            scene.Mirrored = runDirection > 0;
            if (run != null) { scene.CurrentClip = run; scene.Frame = 1 + ((int)(t * 12) % (run.Frames.Length - 1)); }
            else { scene.PuppetRun = true; scene.LegPhase = t * 15; scene.Bob = -5 * (1 - Math.Cos(t * 30)); scene.Angle = -2; }
        }
        else if (action == PetAction.Settle)
        {
            if (t >= 0.22) { SetAction(PetAction.Idle); return; }
            double weight = Math.Sin(Math.PI * t / 0.22);
            scene.ScaleY = 1 - 0.018 * weight; scene.ScaleX = 1 + 0.01 * weight;
        }
        scene.InvalidateVisual();
    }
    private void PlayLaugh()
    {
        if (!audioReady || !soundEnabled || audioPlaying || action != PetAction.Laugh) return;
        double seconds = laugh.Frames.Length / PetMotion.LaughFps;
        double speed = audioDuration > 0 ? PetMotion.Clamp(audioDuration / seconds, 0.5, 2.0) : 1;
        laughPlayer.Stop(); laughPlayer.SpeedRatio = speed;
        laughPlayer.Position = TimeSpan.FromSeconds(Math.Min(clock.Elapsed.TotalSeconds * speed, Math.Max(0, audioDuration - 0.05)));
        laughPlayer.Play(); audioPlaying = true;
    }
    internal void StopRendering()
    {
        if (rendering) CompositionTarget.Rendering -= OnRendering;
        rendering = false; clock.Stop();
    }
    private void OnRightClickPet(object sender, MouseButtonEventArgs e)
    {
        EndPointer();
        var menu = new ContextMenu();
        AddMenu(menu, "轻晃脑袋", delegate { SetAction(PetAction.Head); });
        AddMenu(menu, "捧腹大笑", delegate { SetAction(PetAction.Laugh); });
        AddMenu(menu, "战斗姿态（保持 2 秒）", delegate { SetAction(PetAction.Battle); });
        menu.Items.Add(new Separator());
        AddMenu(menu, "恢复默认大小", delegate { ResizePet(0.62); });
        AddMenu(menu, "移到鼠标所在屏幕", delegate { ShowPet(true); });
        AddMenu(menu, soundEnabled ? "关闭笑声音效" : "开启笑声音效", ToggleSound);
        menu.Items.Add(new Separator());
        AddMenu(menu, "隐藏到通知区域", HidePet); AddMenu(menu, "退出奶蛙桌宠", ExitApplication);
        menu.PlacementTarget = scene; menu.IsOpen = true; e.Handled = true;
    }
    private static void AddMenu(ContextMenu menu, string title, Action callback)
    {
        var item = new MenuItem { Header = title }; item.Click += delegate { callback(); }; menu.Items.Add(item);
    }
    private Forms.ContextMenuStrip MakeTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示奶蛙", null, delegate { ShowPet(false); });
        menu.Items.Add("移到鼠标所在屏幕", null, delegate { ShowPet(true); });
        menu.Items.Add("隐藏奶蛙", null, delegate { HidePet(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出奶蛙桌宠", null, delegate { ExitApplication(); }); return menu;
    }
    private void ToggleSound()
    {
        soundEnabled = !soundEnabled;
        if (!soundEnabled && audioPlaying) { laughPlayer.Stop(); audioPlaying = false; }
    }
    private void UpdateTitle()
    {
        string scale = Math.Round(sizeScale * 100) + "%";
        Title = "奶蛙桌宠 1.1 · " + scale; tray.Text = "奶蛙桌宠正在运行 · " + scale;
    }
    private void ShowPet(bool recenter)
    {
        Show(); WindowState = WindowState.Normal; Topmost = true;
        if (recenter || !IsOnScreen()) PlaceOnCursorScreen(); Activate();
    }
    private bool IsOnScreen()
    {
        NativeRect rect; if (!GetWindowRect(handle, out rect)) return false;
        foreach (Forms.Screen screen in Forms.Screen.AllScreens)
            if (screen.WorkingArea.IntersectsWith(new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top))) return true;
        return false;
    }
    private void PlaceOnCursorScreen()
    {
        NativeRect rect; if (!GetWindowRect(handle, out rect)) return;
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        double w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        MoveWindowTo(Math.Max(area.Left, area.Right - w - 28), Math.Max(area.Top, area.Bottom - h - 16));
    }
    internal bool FollowCursor(Point cursor, double anchorX, double anchorY)
    {
        NativeRect rect;
        if (!GetWindowRect(handle, out rect)) return false;
        Point origin = PetMotion.DragOrigin(cursor, anchorX, anchorY, rect.Right - rect.Left, rect.Bottom - rect.Top);
        if (Math.Abs(origin.X - rect.Left) < 0.5 && Math.Abs(origin.Y - rect.Top) < 0.5) return true;
        return MoveWindowTo(origin.X, origin.Y);
    }
    private bool MoveWindowTo(double x, double y)
    {
        return SetWindowPos(handle, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, 0x0001 | 0x0004 | 0x0010);
    }
    private void HidePet() { EndPointer(); SetAction(PetAction.Idle); Hide(); }
    private void OnClosingPet(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (exiting) return; e.Cancel = true; HidePet();
    }
    internal void ExitApplication() { exiting = true; Cleanup(); Close(); }
    private bool cleaned;
    private void Cleanup()
    {
        if (cleaned) return; cleaned = true;
        StopRendering(); laughPlayer.Close(); tray.Visible = false; tray.Dispose();
    }
    private static System.Drawing.Icon CreateTrayIcon()
    {
        using (var bmp = new System.Drawing.Bitmap(32, 32))
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        using (var gold = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 197, 47)))
        using (var green = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(101, 149, 56)))
        using (var dark = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(25, 25, 18)))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(gold, 4, 2, 24, 28); g.FillEllipse(green, 8, 9, 8, 10); g.FillEllipse(green, 18, 9, 8, 10);
            g.FillEllipse(dark, 10, 11, 5, 7); g.FillEllipse(dark, 20, 11, 5, 7);
            IntPtr h = bmp.GetHicon(); var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone(); DestroyIcon(h); return icon;
        }
    }
    [StructLayout(LayoutKind.Sequential)] internal struct NativePoint { internal int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct NativeRect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
