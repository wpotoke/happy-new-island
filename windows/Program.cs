using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Happy New Island")]
[assembly: AssemblyDescription("Зимняя панель приложений и воспроизведения Windows")]
[assembly: AssemblyVersion("1.21.2.0")]
[assembly: AssemblyFileVersion("1.21.2.0")]

namespace WinterIsland
{
    internal static class Program
    {
        internal const string AppName = "Happy New Island";
        internal static readonly int ShowIsland = RegisterWindowMessage("WinterIsland.Show");

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string name);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string windowClass, string caption);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static void Main(string[] args)
        {
            SetProcessDPIAware();
            if (args.Length == 1 && args[0] == "--smoke-test")
            {
                try
                {
                    using (LeopardModel model = LeopardModel.Load(1f))
                    {
                        if (model == null) { Environment.ExitCode = 1; return; }
                        model.Render(new LeopardPose { ShakeAge = -1, Orientation = .17f, Sit = .75f }, 0, 0);
                        if (!model.OpaqueAt((int)model.Pivot, (int)(model.Ground * .77f))) Environment.ExitCode = 1;
                    }
                }
                catch (Exception) { Environment.ExitCode = 1; }
                return;
            }
            if (args.Length == 3 && args[0] == "--taskbar-guard")
            {
                int ownerId;
                Guid token;
                if (Int32.TryParse(args[1], out ownerId) && Guid.TryParseExact(args[2], "N", out token))
                    TaskbarVisibility.Watch(ownerId, token.ToString("N"));
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool fresh;
            using (var guard = new Mutex(true, @"Local\WinterIsland.Desktop", out fresh))
            {
                if (!fresh)
                {
                    if (!args.Contains("--restore") && ShowIsland != 0)
                    {
                        IntPtr window = FindWindow(null, AppName);
                        if (window != IntPtr.Zero) PostMessage(window, ShowIsland, IntPtr.Zero, IntPtr.Zero);
                    }
                    return;
                }
                try
                {
                    TaskbarVisibility.RecoverAutoHide();
                    ShellProfile.Recover();
                    if (args.Contains("--restore")) return;
                    using (var island = new Island(args.Contains("--expanded"), args.Contains("--hide-taskbar")))
                        Application.Run(island);
                }
                catch (Exception error)
                {
                    MessageBox.Show(error.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
    }
}
