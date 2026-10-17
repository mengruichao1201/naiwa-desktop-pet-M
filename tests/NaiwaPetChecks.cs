using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class NaiwaPetChecks
{
    private static int assertions;
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Near(double a, double b, string message)
    {
        Check(Math.Abs(a - b) < 0.00001, message + ": " + a + " versus " + b);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        NaiwaPetSprite pet = null;
        try
        {
            string output = args.Length > 0 ? args[0] : "check-output";
            Directory.CreateDirectory(output);
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            // Exercise fixed screen anchors, negative monitor coordinates and DPI-induced size changes.
            foreach (Point p in new Point[] { new Point(-1700, -300), new Point(200, 700), new Point(3600, 150) })
            foreach (double dpi in new double[] { 1, 1.25, 1.5, 2 })
            {
                double w = PetMotion.SceneWidth * 0.62 * dpi, h = PetMotion.SceneHeight * 0.62 * dpi;
                Point origin = PetMotion.DragOrigin(p, 0.36, 0.24, w, h);
                Near(origin.X + w * 0.36, p.X, "Pointer X remains anchored");
                Near(origin.Y + h * 0.24, p.Y, "Pointer Y remains anchored");
            }
            Near(PetMotion.HeadAngle(0), 0, "Head begins neutral");
            Near(PetMotion.HeadAngle(1.05), 0, "Head ends neutral");
            double last = 0, maximum = 0;
            for (int i = 0; i <= 63; i++)
            {
                double angle = PetMotion.HeadAngle(i / 60.0);
                Check(Math.Abs(angle - last) < 1.7, "Head motion must not snap between display frames");
                maximum = Math.Max(maximum, Math.Abs(angle)); last = angle;
            }
            Check(maximum > 1.5, "Head interaction is visibly animated");
            Near(PetMotion.BattleHoldSeconds, 2.0, "Combat hold lasts exactly two seconds");
            Near(PetMotion.BattleWeight(0), 0, "Combat begins neutral");
            Near(PetMotion.BattleWeight(0.30), 1, "Combat enters full pose");
            Near(PetMotion.BattleWeight(2.29999), 1, "Combat holds for full two seconds");
            Near(PetMotion.BattleWeight(2.60), 0, "Combat returns neutral");
            Check(PetMotion.BattleFrame(0.31) >= 2 && PetMotion.BattleFrame(2.299) <= 3, "Combat uses held stance frames");

            // Regression: light enclosed belly/eye pixels are not exterior background.
            byte[] testPixels = new byte[30 * 30 * 4];
            for (int y = 0; y < 30; y++) for (int x = 0; x < 30; x++)
            {
                bool ring = x >= 6 && x <= 23 && y >= 6 && y <= 23 && !(x >= 10 && x <= 19 && y >= 10 && y <= 19);
                int i = (y * 30 + x) * 4;
                testPixels[i] = (byte)(ring ? 60 : 255); testPixels[i + 1] = (byte)(ring ? 190 : 255);
                testPixels[i + 2] = 255; testPixels[i + 3] = 255;
            }
            var keyed = new PetFrame(BitmapSource.Create(30, 30, 96, 96, PixelFormats.Bgra32, null, testPixels, 120), true);
            Check(keyed.Contains(0.5, 0.5), "Enclosed white belly pixels stay solid");
            Check(!keyed.Contains(0.02, 0.02), "Exterior white becomes transparent");

            pet = new NaiwaPetSprite();
            Check(pet.RunClip != null && pet.RunClip.Frames.Length == 12, "Twelve real gait phases are present");
            Check(pet.BattleClip != null && pet.BattleClip.Frames.Length == 6, "Six combat sheet frames are present");
            Near(VisibleRect(pet.IdleClip, 0).Height, PetMotion.CharacterHeight, "Static visible height");
            Near(VisibleRect(pet.LaughClip, 0).Height, PetMotion.CharacterHeight, "Laugh starts at the same visible height");
            Near(VisibleRect(pet.IdleClip, 0).Bottom, VisibleRect(pet.LaughClip, 0).Bottom, "Static and laugh feet align");
            VerifyClip(pet.IdleClip, "idle"); VerifyClip(pet.LaughClip, "laugh");
            VerifyClip(pet.RunClip, "run"); VerifyClip(pet.BattleClip, "battle");
            int narrow = 0, wide = 0; double maxSpan = 0;
            double[] spans = new double[pet.RunClip.Frames.Length];
            for (int i = 0; i < spans.Length; i++) { spans[i] = FootSpan(pet.RunClip.Frames[i]); maxSpan = Math.Max(maxSpan, spans[i]); }
            for (int i = 0; i < spans.Length; i++)
            {
                if (spans[i] < maxSpan * 0.70) narrow++; else wide++;
                PetFrame f = pet.RunClip.Frames[i]; Rect r = pet.RunClip.Rectangles[i];
                double headToBody = pet.RunClip.Frames[0].BodyCenterX - pet.RunClip.Frames[0].HeadCenterX;
                Near(r.X + (f.HeadCenterX + headToBody) * r.Width / f.Bitmap.PixelWidth, pet.IdleClip.BodyAnchorX,
                    "Arm swings do not drag the head and body anchor sideways");
                Near(r.Y, pet.RunClip.Rectangles[0].Y, "Running preserves original vertical foot lift");
            }
            Console.WriteLine("Gait support: " + narrow + " compact phases, " + wide + " extended phases.");
            Check(narrow >= 4 && wide >= 2, "Gait must contain passing/tucked legs as well as extended legs");
            int forwardHandPhases = 0;
            for (int i = 0; i < pet.RunClip.Frames.Length; i++)
                if (FrontHandCoverage(pet.RunClip.Frames[i]) >= 0.0015) forwardHandPhases++;
            Console.WriteLine("Arm swing: " + forwardHandPhases + " phases have a hand passing across the belly.");
            Check(forwardHandPhases >= 4, "Hands must pass across the foreground instead of staying glued to the hip");
            Check(VisibleRect(pet.RunClip, 4).Bottom < VisibleRect(pet.RunClip, 0).Bottom - 30,
                "Flight pose must lift its feet instead of being pinned to the ground");
            Near(PetMotion.RunFlight(0.8 / 12), 0, "Compression remains grounded");
            Check(PetMotion.RunFlight(0.8 / 3) > 0.99, "Flight has a smooth body lift");
            Near(PetMotion.RunFlight(0), PetMotion.RunFlight(PetMotion.RunCycleSeconds), "Run bob closes its loop");
            pet.Show(); pet.Left = -20000; pet.Top = -20000;
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(pet).Handle;
            var nativeMoves = System.Diagnostics.Stopwatch.StartNew();
            for (int n = 0; n < 120; n++)
            {
                Point cursor = new Point(-19000 + n * 8, -19000 + n * 3);
                Check(pet.FollowCursor(cursor, 0.36, 0.24), "Native move succeeds");
                NativeRect rect; Check(GetWindowRect(hwnd, out rect), "Native window rectangle is available");
                Check(Math.Abs(rect.Left + (rect.Right - rect.Left) * 0.36 - cursor.X) <= 0.51, "Native drag X follows without feedback drift");
                Check(Math.Abs(rect.Top + (rect.Bottom - rect.Top) * 0.24 - cursor.Y) <= 0.51, "Native drag Y follows without feedback drift");
            }
            nativeMoves.Stop();
            Console.WriteLine("Native drag: 120 moves, " + nativeMoves.Elapsed.TotalMilliseconds.ToString("F1") + " ms total.");
            NativeRect sizeBefore, sizeAfter;
            GetWindowRect(hwnd, out sizeBefore); pet.ResizePet(0.74); GetWindowRect(hwnd, out sizeAfter);
            double footBefore = sizeBefore.Top + (sizeBefore.Bottom - sizeBefore.Top) * PetMotion.FeetY / PetMotion.SceneHeight;
            double footAfter = sizeAfter.Top + (sizeAfter.Bottom - sizeAfter.Top) * PetMotion.FeetY / PetMotion.SceneHeight;
            Check(Math.Abs(footBefore - footAfter) <= 1, "Native resize keeps feet anchored");
            PetScene scene = pet.Scene;
            scene.Measure(new Size(PetMotion.SceneWidth, PetMotion.SceneHeight));
            scene.Arrange(new Rect(0, 0, PetMotion.SceneWidth, PetMotion.SceneHeight));
            pet.SetAction(PetAction.Idle); Snapshot(pet, output, "idle");
            pet.SetAction(PetAction.Head); pet.EvaluateAction(0.20); Snapshot(pet, output, "head");
            pet.EvaluateAction(1.06); Check(scene.Head == 0 && scene.CurrentClip == pet.IdleClip, "Head recovers to static art");
            pet.SetAction(PetAction.Laugh); Check(scene.Frame == 0, "Laugh displays its first frame immediately");
            pet.EvaluateAction(1.75); Check(scene.Frame == 21, "Laugh frame derives from elapsed time");
            Snapshot(pet, output, "laugh");
            pet.SetAction(PetAction.Battle); pet.EvaluateAction(1.3); Snapshot(pet, output, "battle");
            pet.EvaluateAction(2.59); Check(scene.CurrentClip == pet.BattleClip, "Combat recovery is still visible");
            pet.EvaluateAction(2.61); Check(scene.CurrentClip == pet.IdleClip, "Combat returns after hold and transitions");
            pet.SetAction(PetAction.Run); pet.EvaluateAction(0.15); Snapshot(pet, output, "run-left");
            scene.Mirrored = true; scene.InvalidateVisual(); Snapshot(pet, output, "run-right");
            Near(scene.MirrorAnchorX, pet.IdleClip.BodyAnchorX, "Mirroring keeps the torso under the grab anchor");
            pet.SetAction(PetAction.Run);
            var phases = new System.Collections.Generic.HashSet<int>();
            for (int i = 0; i < 24; i++)
            {
                pet.EvaluateAction(i * PetMotion.RunCycleSeconds / 24);
                phases.Add(scene.Frame);
                Snapshot(pet, output, "run-phase-" + i.ToString("D2"));
            }
            Check(phases.Count == 12, "A displayed cycle actually visits all twelve phases");
            Check(PetMotion.RunFrame(PetMotion.RunCycleSeconds + 0.00001) == 0, "Run timing wraps cleanly");
            pet.SetAction(PetAction.Head); Check(scene.CurrentClip == pet.IdleClip && !scene.Mirrored, "New interaction interrupts the previous action cleanly");
            Check(scene.InputHitTest(new Point(2, 2)) == null, "Transparent canvas margin does not capture clicks");
            var previewHashes = new System.Collections.Generic.HashSet<string>();
            foreach (string name in new string[] { "idle", "head", "laugh", "battle", "run-left", "run-right" })
                using (var sha = System.Security.Cryptography.SHA256.Create())
                    previewHashes.Add(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(Path.Combine(output, name + ".png")))));
            Check(previewHashes.Count == 6, "Every action and mirror must actually render a distinct preview");
            Console.WriteLine("PASS: " + assertions + " assertions, all sprite bounds, timing, screen anchors, hit areas and six visual previews.");
            File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: " + assertions + " assertions; six visual previews.\r\n");
            pet.ExitApplication(); app.Shutdown(); return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            if (pet != null) pet.ExitApplication(); return 1;
        }
    }
    private static Rect VisibleRect(PetClip clip, int index)
    {
        PetFrame f = clip.Frames[index]; Rect r = clip.Rectangles[index];
        double sx = r.Width / f.Bitmap.PixelWidth, sy = r.Height / f.Bitmap.PixelHeight;
        return new Rect(r.X + f.AlphaBounds.X * sx, r.Y + f.AlphaBounds.Y * sy,
            f.AlphaBounds.Width * sx, f.AlphaBounds.Height * sy);
    }
    private static double FootSpan(PetFrame frame)
    {
        int w = frame.Bitmap.PixelWidth, h = frame.Bitmap.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        new FormatConvertedBitmap(frame.Bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, w * 4, 0);
        int left = w, right = -1;
        int top = frame.AlphaBounds.Y + frame.AlphaBounds.Height * 80 / 100;
        for (int y = top; y < h; y++) for (int x = 0; x < w; x++)
            if (pixels[(y * w + x) * 4 + 3] >= 40) { left = Math.Min(left, x); right = Math.Max(right, x); }
        return right - left + 1;
    }
    private static double FrontHandCoverage(PetFrame frame)
    {
        int w = frame.Bitmap.PixelWidth, h = frame.Bitmap.PixelHeight;
        byte[] pixels = new byte[w * h * 4];
        new FormatConvertedBitmap(frame.Bitmap, PixelFormats.Bgra32, null, 0).CopyPixels(pixels, w * 4, 0);
        int left = Math.Max(0, (int)(frame.BodyCenterX - w * 0.21));
        int right = Math.Min(w - 1, (int)(frame.BodyCenterX + w * 0.03));
        int top = frame.AlphaBounds.Y + frame.AlphaBounds.Height * 28 / 100;
        int bottom = frame.AlphaBounds.Y + frame.AlphaBounds.Height * 60 / 100;
        int covered = 0;
        for (int y = top; y <= bottom; y++) for (int x = left; x <= right; x++)
        {
            int p = (y * w + x) * 4;
            if (pixels[p + 3] >= 100 && pixels[p + 2] < 170 && pixels[p + 1] < 165 && pixels[p] < 130) covered++;
        }
        return (double)covered / (w * h);
    }
    private static void VerifyClip(PetClip clip, string name)
    {
        double scale = clip.Rectangles[0].Height / clip.Frames[0].Bitmap.PixelHeight;
        for (int i = 0; i < clip.Frames.Length; i++)
        {
            Rect visible = VisibleRect(clip, i);
            Check(visible.Left >= 1 && visible.Top >= 1 && visible.Right <= PetMotion.SceneWidth - 1 && visible.Bottom <= PetMotion.SceneHeight - 1,
                name + " frame " + i + " would clip at " + visible);
            Near(clip.Rectangles[i].Height / clip.Frames[i].Bitmap.PixelHeight, scale,
                name + " keeps one scale throughout the animation");
        }
    }
    private static void Snapshot(NaiwaPetSprite pet, string output, string name)
    {
        pet.StopRendering();
        PetScene scene = pet.Scene;
        scene.UpdateLayout();
        scene.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(delegate { }));
        var image = new RenderTargetBitmap((int)PetMotion.SceneWidth, (int)PetMotion.SceneHeight, 96, 96, PixelFormats.Pbgra32);
        image.Render(scene);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using (FileStream f = File.Create(Path.Combine(output, name + ".png"))) encoder.Save(f);
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
}
