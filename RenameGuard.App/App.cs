using System;
using System.Windows;

namespace RenameGuard.App
{
    internal static class App
    {
        [STAThread]
        private static void Main()
        {
            Application application = new Application();
            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            application.Run(new MainWindow());
        }
    }
}
