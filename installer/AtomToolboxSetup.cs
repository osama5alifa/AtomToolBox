using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AtomToolboxSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = Array.IndexOf(args, "--uninstall") >= 0;
            bool auto = false;
            bool quiet = false;
            string directory = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("--dir=", StringComparison.OrdinalIgnoreCase))
                {
                    directory = arg.Substring(6).Trim('"');
                }
                else if (string.Equals(arg, "--auto", StringComparison.OrdinalIgnoreCase))
                {
                    auto = true;
                }
                else if (string.Equals(arg, "--quiet", StringComparison.OrdinalIgnoreCase))
                {
                    quiet = true;
                }
            }
            Application.Run(new SetupForm(uninstall, directory, auto, quiet));
        }
    }

    internal sealed class SetupForm : Form
    {
        private const string AppName = "AtomToolBox";
        private const string Version = "0.2.5";
        private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AtomToolBox";

        private readonly bool _uninstall;
        private readonly Button _actionButton;
        private readonly Button _closeButton;
        private readonly ProgressBar _progress;
        private readonly Label _status;
        private readonly string _installDir;
        private readonly bool _auto;
        private readonly bool _quiet;

        internal SetupForm(bool uninstall, string directory, bool auto, bool quiet)
        {
            _uninstall = uninstall;
            _auto = auto;
            _quiet = quiet;
            _installDir = string.IsNullOrWhiteSpace(directory)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName)
                : Path.GetFullPath(directory);

            Text = uninstall ? AppName + " " + Version + " Uninstall" : AppName + " " + Version + " Setup";
            ClientSize = new Size(560, 250);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(246, 247, 249);
            Font = new Font("Segoe UI", 9F);

            Label title = new Label();
            title.Text = uninstall ? "Remove AtomToolBox" : "Install AtomToolBox " + Version;
            title.Font = new Font("Segoe UI Semibold", 16F);
            title.AutoSize = true;
            title.Location = new Point(22, 20);
            Controls.Add(title);

            Label description = new Label();
            description.Text = uninstall
                ? "This removes the application, shortcuts and the Apps & Features entry."
                : "The app will be installed to " + _installDir + ". No temp folder is used.";
            description.AutoSize = false;
            description.Size = new Size(510, 42);
            description.Location = new Point(25, 60);
            description.ForeColor = Color.FromArgb(80, 84, 92);
            Controls.Add(description);

            _progress = new ProgressBar();
            _progress.Location = new Point(25, 112);
            _progress.Size = new Size(510, 18);
            _progress.Style = ProgressBarStyle.Continuous;
            Controls.Add(_progress);

            _status = new Label();
            _status.Text = uninstall ? "Ready to remove." : "Ready to install.";
            _status.AutoSize = true;
            _status.Location = new Point(25, 138);
            _status.ForeColor = Color.FromArgb(80, 84, 92);
            Controls.Add(_status);

            _actionButton = new Button();
            _actionButton.Text = uninstall ? "Uninstall" : "Install";
            _actionButton.Size = new Size(110, 34);
            _actionButton.Location = new Point(314, 196);
            _actionButton.Click += OnAction;
            Controls.Add(_actionButton);

            _closeButton = new Button();
            _closeButton.Text = "Cancel";
            _closeButton.Size = new Size(110, 34);
            _closeButton.Location = new Point(430, 196);
            _closeButton.Click += delegate { Close(); };
            Controls.Add(_closeButton);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_auto)
            {
                BeginInvoke(new Action(delegate { OnAction(null, EventArgs.Empty); }));
            }
        }

        private void OnAction(object sender, EventArgs e)
        {
            _actionButton.Enabled = false;
            _closeButton.Enabled = false;
            Thread thread = new Thread(_uninstall ? (ThreadStart)RunUninstall : RunInstall);
            thread.IsBackground = true;
            thread.Start();
        }

        private void Info(string text, string caption, MessageBoxIcon icon)
        {
            if (!_quiet)
            {
                MessageBox.Show(this, text, caption, MessageBoxButtons.OK, icon);
            }
        }

        private void Report(int percent, string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<int, string>(Report), new object[] { percent, text });
                return;
            }

            if (percent >= 0)
            {
                _progress.Value = Math.Max(0, Math.Min(100, percent));
            }
            _status.Text = text;
        }

        private void RunInstall()
        {
            try
            {
                Report(0, "Closing running AtomToolBox...");
                StopApp();

                string backup = null;
                if (Directory.Exists(_installDir))
                {
                    backup = _installDir + ".old-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try
                    {
                        Directory.Move(_installDir, backup);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception("Could not replace " + _installDir + ". Close the old AtomToolBox app and try again. " + ex.Message);
                    }
                }

                Directory.CreateDirectory(_installDir);
                Report(2, "Extracting application files...");

                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
                {
                    if (stream == null)
                    {
                        throw new Exception("The embedded payload is missing.");
                    }

                    using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                    {
                        int total = archive.Entries.Count;
                        int index = 0;
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            string destination = Path.Combine(_installDir, entry.FullName.Replace('/', '\\'));
                            if (entry.Name.Length == 0)
                            {
                                Directory.CreateDirectory(destination);
                            }
                            else
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                                entry.ExtractToFile(destination, true);
                            }

                            index++;
                            if (index % 5 == 0 || index == total)
                            {
                                Report(2 + (int)(index * 90.0 / total), "Extracting " + index + " / " + total);
                            }
                        }
                    }
                }

                string exe = Path.Combine(_installDir, "Atomtoolbox.exe");
                if (!File.Exists(exe))
                {
                    throw new Exception("The application files were not installed correctly.");
                }

                Report(94, "Creating shortcuts...");
                File.Copy(Application.ExecutablePath, Path.Combine(_installDir, "Uninstall.exe"), true);
                CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"), exe);
                string programFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
                Directory.CreateDirectory(programFolder);
                CreateShortcut(Path.Combine(programFolder, AppName + ".lnk"), exe);

                Report(97, "Registering uninstall information...");
                using (RegistryKey key = Registry.LocalMachine.CreateSubKey(UninstallKey))
                {
                    key.SetValue("DisplayName", AppName);
                    key.SetValue("DisplayVersion", Version);
                    key.SetValue("Publisher", "Project Atom");
                    key.SetValue("InstallLocation", _installDir);
                    key.SetValue("DisplayIcon", exe + ",0");
                    key.SetValue("UninstallString", "\"" + Path.Combine(_installDir, "Uninstall.exe") + "\" --uninstall");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                }

                Report(100, "Installed.");
                BeginInvoke(new Action(delegate
                {
                    Info(AppName + " " + Version + " has been installed." + Environment.NewLine + Environment.NewLine + "Run it from the desktop shortcut.", AppName + " Setup", MessageBoxIcon.Information);
                    Close();
                }));
            }
            catch (Exception ex)
            {
                BeginInvoke(new Action(delegate
                {
                    Info("Installation failed." + Environment.NewLine + Environment.NewLine + ex.Message, AppName + " Setup", MessageBoxIcon.Error);
                    _actionButton.Enabled = true;
                    _closeButton.Enabled = true;
                    Report(0, "Installation failed.");
                }));
            }
        }

        private void RunUninstall()
        {
            try
            {
                StopApp();
                Report(15, "Removing shortcuts...");
                File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppName + ".lnk"));
                string programFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppName);
                File.Delete(Path.Combine(programFolder, AppName + ".lnk"));
                try { Directory.Delete(programFolder, true); } catch { }
                Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false);

                Report(45, "Removing application files...");
                string self = Application.ExecutablePath;
                foreach (string file in Directory.GetFiles(_installDir))
                {
                    if (!string.Equals(file, self, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                foreach (string dir in Directory.GetDirectories(_installDir))
                {
                    try { Directory.Delete(dir, true); } catch { }
                }

                ProcessStartInfo cleanup = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + _installDir + "\"");
                cleanup.WindowStyle = ProcessWindowStyle.Hidden;
                cleanup.CreateNoWindow = true;
                Process.Start(cleanup);

                Report(100, "Removed.");
                BeginInvoke(new Action(delegate
                {
                    MessageBox.Show(this, AppName + " " + Version + " has been removed.", AppName + " Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    Close();
                }));
            }
            catch (Exception ex)
            {
                BeginInvoke(new Action(delegate
                {
                    MessageBox.Show(this, "Uninstall failed." + Environment.NewLine + Environment.NewLine + ex.Message, AppName + " Uninstall", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _actionButton.Enabled = true;
                    _closeButton.Enabled = true;
                }));
            }
        }

        private static void StopApp()
        {
            foreach (Process process in Process.GetProcessesByName("Atomtoolbox"))
            {
                try { process.Kill(); process.WaitForExit(2000); } catch { }
            }
            Thread.Sleep(300);
        }

        private static void CreateShortcut(string path, string target)
        {
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(shellType);
            object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { path });
            Type shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(target) });
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { target + ",0" });
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { AppName + " " + Version });
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
        }
    }
}
