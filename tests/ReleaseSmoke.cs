using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// No reference to the application or its dependencies: resolve only the packaged EXE.
internal static class ReleaseSmoke
{
    private sealed class FailingPaintControl : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            throw new InvalidOperationException("RELEASE_SMOKE_EXPECTED_PAINT_FAILURE");
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            var assembly = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "WindowTabs.exe"));
            Console.WriteLine("Release smoke: assembly loaded");
            // The exe ships without a .config file; Bootstrap supplies its WinForms DPI option first.
            assembly.GetType("Bemo.Dpi", true).GetMethod("enableWinFormsRescaling").Invoke(null, null);
            // Match Bootstrap before creating controls; SystemEvents must not
            // own its broadcast window on the main STA.
            assembly.GetType("Bemo.ThemeService", true).GetMethod("moveSystemEventsOffMainThread").Invoke(null, null);
            Application.EnableVisualStyles();
            assembly.GetTypes(); // Resolve signatures, including statically linked dependencies.
            Console.WriteLine("Release smoke: types resolved");
            var dpiHelper = typeof(Form).Assembly.GetType("System.Windows.Forms.DpiHelper", true);
            if (!(bool)dpiHelper.GetProperty("EnableDpiChangedMessageHandling", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null, null))
                throw new Exception("Forms would not rescale on a monitor with another scale");
            foreach (var reference in assembly.GetReferencedAssemblies())
                if (reference.Name == "FSharp.Core" || reference.Name == "Newtonsoft.Json" || reference.Name == "Win32")
                    throw new Exception("Unexpected external dependency: " + reference.Name);
            using (var stream = assembly.GetManifestResourceStream("Bemo.ico"))
            using (var icon = new Icon(stream))
                if (icon.Width <= 0) throw new Exception("Invalid packaged icon");
            var theme = assembly.GetType("Bemo.Theme", true);
            var light = theme.GetProperty("light").GetValue(null, null);
            var dark = theme.GetProperty("dark").GetValue(null, null);
            if ((bool)theme.GetMethod("sameColors").Invoke(null, new[] { light, dark }))
                throw new Exception("Light and dark palettes unexpectedly identical");
            Console.WriteLine("Release smoke: icon and palettes checked");
            // Painting reads Services.settings. Use a standalone store in this
            // isolated working directory, never the user's AppData settings.
            using (var settings = (IDisposable)Activator.CreateInstance(assembly.GetType("Bemo.Settings", true),
                new object[] { true, null }))
            using (var form = new Form())
            using (var combo = (Control)Activator.CreateInstance(assembly.GetType("Bemo.SettingsCombo", true),
                new object[] { new[] { "System", "Light", "Dark" } }))
            {
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-20000, -20000);
                form.Controls.Add(combo);
                form.Show();
                Application.DoEvents();
                using (var bitmap = new Bitmap(combo.Width, combo.Height))
                    combo.DrawToBitmap(bitmap, combo.ClientRectangle);
                // Negative probe: exercise the same native WM_PRINT paint path.
                // An exception must fail this process, never open a dialog and
                // continue to print PASS after the user dismisses it.
                if (Array.IndexOf(args, "--inject-paint-failure") >= 0)
                    using (var broken = new FailingPaintControl())
                    using (var bitmap = new Bitmap(100, 30))
                    {
                        broken.Size = bitmap.Size;
                        form.Controls.Add(broken);
                        broken.DrawToBitmap(bitmap, broken.ClientRectangle);
                        throw new Exception("Expected paint exception was swallowed");
                    }
                form.Close();
            }
            ((IDisposable)assembly.GetType("Bemo.InvokerService", true).GetProperty("invoker").GetValue(null, null)).Dispose();
            Application.Exit();
            Application.ExitThread();
            Console.WriteLine("PASS: isolated Release assembly, dependencies, icon, palettes and WinForms control rendering.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
