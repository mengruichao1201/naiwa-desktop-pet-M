using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Point = System.Windows.Point;
using Brushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;

internal sealed class NaiwaPetSprite : Window
{
    private const double BaseWidth = 330;
    private const double BaseHeight = 660;
    private readonly System.Windows.Controls.Image sprite = new System.Windows.Controls.Image();
    private readonly BitmapImage[] laughFrames = new BitmapImage[92];
    private readonly ScaleTransform bodyScale = new ScaleTransform(1, 1);
    private readonly RotateTransform bodyRotate = new RotateTransform(0);
    private readonly TranslateTransform bodyMove = new TranslateTransform(0, 0);
    private readonly DispatcherTimer timer = new DispatcherTimer();
    private readonly MediaPlayer laughPlayer = new MediaPlayer();
    private readonly DispatcherTimer laughStartTimer = new DispatcherTimer();
    private bool laughPlayback;
    private bool laughReady;
    private double laughDurationSeconds;

    private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
    private string action = "";
    private int frame;
    private double sizeScale = 0.62;
    private Point mouseStart;
    private Point windowStart;
    private bool dragging;
    private bool exiting;

    [STAThread]
    private static void Main()
    {
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        app.Run(new NaiwaPetSprite());
    }

    private NaiwaPetSprite()
    {
        Title = "奶娃桌宠";
        Width = BaseWidth * sizeScale;
        Height = BaseHeight * sizeScale;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = true;
        Topmost = true;
        ShowActivated = true;
        Focusable = true;

        BitmapImage image = LoadSprite();
        image.Freeze();
        sprite.Source = image;
        sprite.Stretch = Stretch.Fill;
        sprite.RenderTransformOrigin = new Point(0.5, 0.52);
        var transforms = new TransformGroup();
        transforms.Children.Add(bodyScale);
        transforms.Children.Add(bodyRotate);
        transforms.Children.Add(bodyMove);
        sprite.RenderTransform = transforms;

        var surface = new Grid { Background = Brushes.Transparent, ClipToBounds = false };
        surface.Children.Add(sprite);
        Content = surface;

        for (int i = 0; i < laughFrames.Length; i++)
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("laugh-" + i.ToString("D3") + ".png"))
            {
                if (stream == null) throw new InvalidOperationException("捧腹动画资源缺失：第 " + i + " 帧");
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                laughFrames[i] = bitmap;
            }
        }

        string laughPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "laugh.mp3");
        if (!File.Exists(laughPath)) throw new InvalidOperationException("请将 laugh.mp3 放在 NaiwaPet.exe 同一目录。");
        laughPlayer.MediaOpened += delegate
        {
            laughReady = true;
            if (laughPlayer.NaturalDuration.HasTimeSpan) laughDurationSeconds = laughPlayer.NaturalDuration.TimeSpan.TotalSeconds;
            if (action == "belly") StartLaughPlayback();
        };
        laughPlayer.Open(new Uri(laughPath));
        laughStartTimer.Interval = TimeSpan.FromMilliseconds(250);
        laughStartTimer.Tick += delegate { laughStartTimer.Stop(); if (action == "belly") StartLaughPlayback(); };
        Closing += delegate { laughPlayer.Stop(); };

        PreviewMouseLeftButtonDown += OnMouseDownPet;
        PreviewMouseLeftButtonUp += OnMouseUpPet;
        PreviewMouseMove += OnMouseMovePet;
        PreviewMouseWheel += OnMouseWheelPet;
        PreviewMouseRightButtonDown += OnRightClickPet;
        PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) Hide(); };
        Closing += OnClosingPet;

        timer.Interval = TimeSpan.FromMilliseconds(55);
        timer.Tick += OnAnimationTick;

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示奶娃", null, delegate { ShowPet(); });
        menu.Items.Add("隐藏奶娃", null, delegate { Hide(); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出奶娃桌宠", null, delegate { ExitApplication(); });
        tray.Icon = CreateTrayIcon();
        tray.Text = "奶娃桌宠正在运行";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += delegate { if (IsVisible) Hide(); else ShowPet(); };
        UpdateSizeTitle();
    }

    private static BitmapImage LoadSprite()
    {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("naiwa-cutout.png"))
        {
            if (stream == null) throw new InvalidOperationException("内置奶娃图片资源未找到。");
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            return bitmap;
        }
    }

    private void OnMouseDownPet(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        mouseStart = e.GetPosition(this);
        windowStart = new Point(Left, Top);
        dragging = false;
        CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMovePet(object sender, MouseEventArgs e)
    {
        if (!IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
        Vector delta = e.GetPosition(this) - mouseStart;
        if (!dragging && (Math.Abs(delta.X) > 5 || Math.Abs(delta.Y) > 5)) dragging = true;
        if (dragging) { Left = windowStart.X + delta.X; Top = windowStart.Y + delta.Y; }
    }

    private void OnMouseUpPet(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        Point p = e.GetPosition(this);
        bool moved = dragging;
        ReleaseMouseCapture();
        dragging = false;
        if (moved) return;
        double nx = p.X / ActualWidth;
        double ny = p.Y / ActualHeight;
        if (ny < 0.27) StartAction("head");
        else if (ny > 0.91) StartAction("foot");
        else if (ny > 0.49 && (nx < 0.20 || nx > 0.80)) StartAction("hand");
        else StartAction("belly");
        e.Handled = true;
    }

    private void OnMouseWheelPet(object sender, MouseWheelEventArgs e)
    {
        double old = sizeScale;
        sizeScale = Math.Max(0.34, Math.Min(1.30, sizeScale + (e.Delta > 0 ? 0.08 : -0.08)));
        if (Math.Abs(old - sizeScale) < 0.001) return;
        double cx = Left + Width / 2, cy = Top + Height / 2;
        double actionScale = action == "belly" ? 1.20 : 1.0;
        Width = BaseWidth * sizeScale * actionScale;
        Height = BaseHeight * sizeScale * actionScale;
        Left = cx - Width / 2;
        Top = cy - Height / 2;
        UpdateSizeTitle();
        e.Handled = true;
    }

    private void OnRightClickPet(object sender, MouseButtonEventArgs e)
    {
        var menu = new ContextMenu();
        var hide = new MenuItem { Header = "隐藏到通知区域" };
        hide.Click += delegate { Hide(); };
        var exit = new MenuItem { Header = "退出奶娃桌宠" };
        exit.Click += delegate { ExitApplication(); };
        menu.Items.Add(hide);
        menu.Items.Add(exit);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void StartAction(string next)
    {
        if (action == "belly")
        {
            sprite.Source = LoadSprite();
            SetLaughWindow(false);
        }
        action = next;
        if (action == "belly") SetLaughWindow(true);
        frame = 0;
        timer.Stop();
        timer.Interval = next == "belly" ? TimeSpan.FromMilliseconds(83) : TimeSpan.FromMilliseconds(55);
        bodyScale.ScaleX = bodyScale.ScaleY = 1;
        bodyRotate.Angle = bodyMove.X = bodyMove.Y = 0;
        if (next == "belly") { laughStartTimer.Stop(); laughStartTimer.Start(); }
        timer.Start();
    }

    private void StartLaughPlayback()
    {
        laughStartTimer.Stop();
        if (laughPlayback || action != "belly" || !laughReady) return;
        laughPlayer.Stop();
        laughPlayer.Position = TimeSpan.Zero;
        if (laughDurationSeconds > 0)
        {
            double animationSeconds = (laughFrames.Length - 1) * 0.083;
            laughPlayer.SpeedRatio = Math.Max(1.0, Math.Min(2.0, laughDurationSeconds / animationSeconds));
        }
        laughPlayer.Play();
        laughPlayback = true;
    }

    private void OnAnimationTick(object sender, EventArgs e)
    {
        frame++;
        if (action == "belly")
        {
            if (frame < laughFrames.Length) sprite.Source = laughFrames[frame];
            else FinishAction();
        }
        else if (action == "head")
        {
            bodyRotate.Angle = frame < 4 ? -4 : 0;
            if (frame >= 11) FinishAction();
        }
        else if (action == "hand")
        {
            bodyRotate.Angle = Math.Sin(frame * 1.1) * 1.8;
            if (frame >= 18) FinishAction();
        }
        else if (action == "foot")
        {
            bodyMove.Y = frame < 7 ? -7 : (frame < 12 ? 3 : 0);
            bodyRotate.Angle = frame < 7 ? 2 : (frame < 12 ? -2 : 0);
            if (frame >= 17) FinishAction();
        }
    }

    private void FinishAction()
    {
        timer.Stop();
        laughStartTimer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(55);
        if (laughPlayback) { laughPlayer.Stop(); laughPlayback = false; }

        if (action == "belly") SetLaughWindow(false);
        action = "";
        frame = 0;
        sprite.Source = LoadSprite();
        bodyScale.ScaleX = bodyScale.ScaleY = 1;
        bodyRotate.Angle = bodyMove.X = bodyMove.Y = 0;
    }

    private void SetLaughWindow(bool enlarged)
    {
        double centerX = Left + Width / 2;
        double centerY = Top + Height / 2;
        double factor = enlarged ? 1.20 : 1.0;
        Width = BaseWidth * sizeScale * factor;
        Height = BaseHeight * sizeScale * factor;
        Left = centerX - Width / 2;
        Top = centerY - Height / 2;
    }

    private void UpdateSizeTitle()
    {
        string pct = Math.Round(sizeScale * 100) + "%";
        Title = "奶娃桌宠 · " + pct;
        tray.Text = "奶娃桌宠正在运行 · " + pct;
    }

    private void ShowPet()
    {
        Show();
        WindowState = WindowState.Normal;
        Topmost = true;
        Activate();
    }

    private void OnClosingPet(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        Hide();
    }

    private void ExitApplication()
    {
        exiting = true;
        timer.Stop();
        tray.Visible = false;
        tray.Dispose();
        Close();
        Application.Current.Shutdown();
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        using (var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp))
        using (var gold = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 197, 47)))
        using (var green = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(101, 149, 56)))
        using (var dark = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(25, 25, 18)))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.FillEllipse(gold, 4, 2, 24, 28);
            g.FillEllipse(green, 8, 9, 8, 10); g.FillEllipse(green, 18, 9, 8, 10);
            g.FillEllipse(dark, 10, 11, 5, 7); g.FillEllipse(dark, 20, 11, 5, 7);
            IntPtr handle = bmp.GetHicon();
            System.Drawing.Icon icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(handle).Clone();
            DestroyIcon(handle);
            return icon;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}
