using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Diagnostics;

namespace RenameGuard.Setup
{
    internal static class Program
    {
        private const string PayloadResource = "RenameGuard.Setup.Payload.zip";
        private const string ProductVersion = "1.0.0";
        private const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RenameGuard";

        [STAThread]
        private static void Main(string[] args)
        {
            if (args.Length > 0 && args[0].StartsWith("/extract=", StringComparison.OrdinalIgnoreCase))
            {
                string destination = args[0].Substring("/extract=".Length).Trim('"');
                try { ExtractPayload(Path.GetFullPath(destination)); Environment.Exit(0); }
                catch (Exception ex) { Console.Error.WriteLine(ex); Environment.Exit(2); }
                return;
            }
            if (args.Length > 0 && String.Equals(args[0], "/uninstall-worker", StringComparison.OrdinalIgnoreCase))
            {
                UninstallWorker(args);
                return;
            }
            if ((args.Length > 0 && String.Equals(args[0], "/uninstall", StringComparison.OrdinalIgnoreCase)) ||
                String.Equals(Path.GetFileName(Assembly.GetExecutingAssembly().Location), "Uninstall.exe", StringComparison.OrdinalIgnoreCase))
            {
                Uninstall();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
        }

        private static void Install(string installDirectory, bool createStartMenu, bool createDesktop)
        {
            if (String.IsNullOrWhiteSpace(installDirectory)) throw new InvalidOperationException("\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u5148\u3092\u6307\u5b9a\u3057\u3066\u304f\u3060\u3055\u3044\u3002");
            installDirectory = Path.GetFullPath(installDirectory.Trim());
            string tempDirectory = Path.Combine(Path.GetTempPath(), "RenameGuard-Setup-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            try
            {
                ExtractPayload(tempDirectory);
                Directory.CreateDirectory(installDirectory);
                foreach (string source in Directory.GetFiles(tempDirectory, "*", SearchOption.AllDirectories))
                {
                    string relative = source.Substring(tempDirectory.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string target = Path.Combine(installDirectory, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(source, target, true);
                }

                string uninstaller = Path.Combine(installDirectory, "Uninstall.exe");
                string currentExecutable = Assembly.GetExecutingAssembly().Location;
                if (!String.Equals(Path.GetFullPath(currentExecutable), Path.GetFullPath(uninstaller), StringComparison.OrdinalIgnoreCase))
                    File.Copy(currentExecutable, uninstaller, true);

                string startMenuPath = GetStartMenuShortcutPath();
                string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RenameGuard.lnk");
                if (createStartMenu) CreateShortcut(startMenuPath, Path.Combine(installDirectory, "RenameGuard.exe"));
                else DeleteShortcut(startMenuPath);
                if (createDesktop) CreateShortcut(desktopPath, Path.Combine(installDirectory, "RenameGuard.exe"));
                else DeleteShortcut(desktopPath);
                RegisterUninstall(installDirectory, uninstaller, createStartMenu ? startMenuPath : String.Empty, createDesktop ? desktopPath : String.Empty);
            }
            finally
            {
                try { if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, true); }
                catch { }
            }
        }

        private static void ExtractPayload(string destination)
        {
            Directory.CreateDirectory(destination);
            string root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource))
            {
                if (stream == null) throw new InvalidDataException("\u30a4\u30f3\u30b9\u30c8\u30fc\u30e9\u30fc\u306b\u30a2\u30d7\u30ea\u30b1\u30fc\u30b7\u30e7\u30f3\u30c7\u30fc\u30bf\u304c\u3042\u308a\u307e\u305b\u3093\u3002");
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                        string target = Path.GetFullPath(Path.Combine(root, relative));
                        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("\u30a2\u30fc\u30ab\u30a4\u30d6\u306e\u30d1\u30b9\u304c\u4e0d\u6b63\u3067\u3059\u3002");
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                        {
                            Directory.CreateDirectory(target);
                            continue;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        using (Stream input = entry.Open())
                        using (FileStream output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                            input.CopyTo(output);
                    }
                }
            }
        }

        private static void RegisterUninstall(string installDirectory, string uninstaller, string startMenu, string desktop)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                if (key == null) throw new IOException("\u30a2\u30d7\u30ea\u306e\u767b\u9332\u306b\u5931\u6557\u3057\u307e\u3057\u305f\u3002");
                key.SetValue("DisplayName", "RenameGuard", RegistryValueKind.String);
                key.SetValue("DisplayVersion", ProductVersion, RegistryValueKind.String);
                key.SetValue("Publisher", "RenameGuard", RegistryValueKind.String);
                key.SetValue("InstallLocation", installDirectory, RegistryValueKind.String);
                key.SetValue("DisplayIcon", Path.Combine(installDirectory, "RenameGuard.exe"), RegistryValueKind.String);
                key.SetValue("UninstallString", "\"" + uninstaller + "\" /uninstall", RegistryValueKind.String);
                key.SetValue("StartMenuShortcut", startMenu, RegistryValueKind.String);
                key.SetValue("DesktopShortcut", desktop, RegistryValueKind.String);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
            }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath));
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) throw new InvalidOperationException("\u30b7\u30e7\u30fc\u30c8\u30ab\u30c3\u30c8\u4f5c\u6210\u306e\u6a5f\u80fd\u3092\u5229\u7528\u3067\u304d\u307e\u305b\u3093\u3002");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type type = shortcut.GetType();
                type.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                type.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(targetPath) });
                type.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "RenameGuard" });
                type.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            finally
            {
                if (shortcut != null && Marshal.IsComObject(shortcut)) Marshal.FinalReleaseComObject(shortcut);
                if (Marshal.IsComObject(shell)) Marshal.FinalReleaseComObject(shell);
            }
        }

        private static string GetStartMenuShortcutPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", "RenameGuard", "RenameGuard.lnk");
        }

        private static void DeleteShortcut(string path)
        {
            try { if (!String.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void Uninstall()
        {
            string installDirectory = null;
            string startMenu = null;
            string desktop = null;
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, false))
            {
                if (key != null)
                {
                    installDirectory = key.GetValue("InstallLocation") as string;
                    startMenu = key.GetValue("StartMenuShortcut") as string;
                    desktop = key.GetValue("DesktopShortcut") as string;
                }
            }
            if (String.IsNullOrEmpty(installDirectory)) installDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            DialogResult removeData = MessageBox.Show(
                "RenameGuard\u3092\u524a\u9664\u3057\u307e\u3059\u3002\u8a2d\u5b9a\u3068\u5c65\u6b74\u3082\u6d88\u53bb\u3057\u307e\u3059\u304b\uff1f\n\n\u300c\u3044\u3044\u3048\u300d\u3092\u9078\u3076\u3068\u3001\u8a2d\u5b9a\u3068\u5c65\u6b74\u3092\u4fdd\u6301\u3057\u307e\u3059\u3002",
                "RenameGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            bool deleteUserData = removeData == DialogResult.Yes;
            DeleteShortcut(startMenu);
            DeleteShortcut(desktop);
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); }
            catch { }

            string ownPath = Assembly.GetExecutingAssembly().Location;
            string workerPath = Path.Combine(Path.GetTempPath(), "RenameGuard-Uninstall-" + Guid.NewGuid().ToString("N") + ".exe");
            File.Copy(ownPath, workerPath, true);
            ProcessStartInfo startInfo = new ProcessStartInfo(workerPath,
                "/uninstall-worker " + Quote(installDirectory) + " " + (deleteUserData ? "1" : "0") + " " + Quote(workerPath));
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            Process.Start(startInfo);
        }

        private static void UninstallWorker(string[] args)
        {
            if (args.Length < 4) return;
            string installDirectory = args[1];
            bool deleteUserData = args[2] == "1";
            string workerPath = args[3];
            Thread.Sleep(900);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                try
                {
                    if (Directory.Exists(installDirectory)) Directory.Delete(installDirectory, true);
                    break;
                }
                catch { Thread.Sleep(250); }
            }
            if (deleteUserData)
            {
                string dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RenameGuard");
                try { if (Directory.Exists(dataPath)) Directory.Delete(dataPath, true); }
                catch { }
            }

            try
            {
                ProcessStartInfo cleanup = new ProcessStartInfo("cmd.exe", "/d /c ping 127.0.0.1 -n 3 >nul & del /f /q " + Quote(workerPath));
                cleanup.UseShellExecute = false;
                cleanup.CreateNoWindow = true;
                Process.Start(cleanup);
            }
            catch { }
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? String.Empty).Replace("\"", "\\\"") + "\"";
        }

        private sealed class InstallerForm : Form
        {
            private readonly TextBox directoryText;
            private readonly CheckBox startMenuCheck;
            private readonly CheckBox desktopCheck;
            private readonly Button installButton;
            private readonly Label statusLabel;
            private readonly ProgressBar progressBar;

            public InstallerForm()
            {
                Text = "RenameGuard \u30bb\u30c3\u30c8\u30a2\u30c3\u30d7";
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new System.Drawing.Size(620, 270);

                Label title = new Label { Text = "RenameGuard \u3092\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb", AutoSize = true, Left = 22, Top = 20, Font = new System.Drawing.Font("Segoe UI", 13, System.Drawing.FontStyle.Bold) };
                Controls.Add(title);
                Label directoryLabel = new Label { Text = "\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u5148", AutoSize = true, Left = 24, Top = 70 };
                Controls.Add(directoryLabel);
                string defaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RenameGuard");
                directoryText = new TextBox { Left = 24, Top = 94, Width = 475, Text = defaultDirectory };
                Controls.Add(directoryText);
                Button browse = new Button { Text = "\u53c2\u7167...", Left = 510, Top = 92, Width = 86, Height = 27 };
                browse.Click += OnBrowse;
                Controls.Add(browse);

                startMenuCheck = new CheckBox { Text = "\u30b9\u30bf\u30fc\u30c8\u30e1\u30cb\u30e5\u30fc\u306b\u767b\u9332", Left = 25, Top = 137, Width = 245, Checked = true };
                desktopCheck = new CheckBox { Text = "\u30c7\u30b9\u30af\u30c8\u30c3\u30d7\u306b\u30b7\u30e7\u30fc\u30c8\u30ab\u30c3\u30c8\u3092\u4f5c\u6210", Left = 270, Top = 137, Width = 325 };
                Controls.Add(startMenuCheck);
                Controls.Add(desktopCheck);

                statusLabel = new Label { Text = "\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u5148\u3092\u78ba\u8a8d\u3057\u3066\u3001\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u3092\u9078\u3093\u3067\u304f\u3060\u3055\u3044\u3002", Left = 24, Top = 177, Width = 570, Height = 24 };
                Controls.Add(statusLabel);
                progressBar = new ProgressBar { Left = 24, Top = 204, Width = 570, Height = 15, Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 25, Visible = false };
                Controls.Add(progressBar);
                installButton = new Button { Text = "\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb", Left = 384, Top = 230, Width = 105, Height = 28 };
                installButton.Click += OnInstall;
                Controls.Add(installButton);
                Button cancel = new Button { Text = "\u30ad\u30e3\u30f3\u30bb\u30eb", Left = 508, Top = 230, Width = 88, Height = 28, DialogResult = DialogResult.Cancel };
                Controls.Add(cancel);
                AcceptButton = installButton;
                CancelButton = cancel;
            }

            private void OnBrowse(object sender, EventArgs e)
            {
                using (FolderBrowserDialog dialog = new FolderBrowserDialog())
                {
                    dialog.Description = "\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u5148\u3092\u9078\u629e";
                    dialog.SelectedPath = directoryText.Text;
                    dialog.ShowNewFolderButton = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        directoryText.Text = Path.Combine(dialog.SelectedPath, "RenameGuard");
                }
            }

            private void OnInstall(object sender, EventArgs e)
            {
                installButton.Enabled = false;
                progressBar.Visible = true;
                statusLabel.Text = "\u30d5\u30a1\u30a4\u30eb\u3092\u914d\u7f6e\u3057\u3066\u3044\u307e\u3059...";
                Cursor = Cursors.WaitCursor;
                Application.DoEvents();
                try
                {
                    Install(directoryText.Text, startMenuCheck.Checked, desktopCheck.Checked);
                    statusLabel.Text = "\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u304c\u5b8c\u4e86\u3057\u307e\u3057\u305f\u3002";
                    DialogResult result = MessageBox.Show(this, "\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u304c\u5b8c\u4e86\u3057\u307e\u3057\u305f\u3002\n\nRenameGuard \u3092\u8d77\u52d5\u3057\u307e\u3059\u304b\uff1f", "RenameGuard", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                    if (result == DialogResult.Yes)
                        Process.Start(new ProcessStartInfo(Path.Combine(Path.GetFullPath(directoryText.Text), "RenameGuard.exe")) { UseShellExecute = true });
                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (Exception ex)
                {
                    statusLabel.Text = "\u30a4\u30f3\u30b9\u30c8\u30fc\u30eb\u306b\u5931\u6557\u3057\u307e\u3057\u305f\u3002";
                    MessageBox.Show(this, ex.Message, "RenameGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    installButton.Enabled = true;
                }
                finally
                {
                    progressBar.Visible = false;
                    Cursor = Cursors.Default;
                }
            }
        }
    }
}
