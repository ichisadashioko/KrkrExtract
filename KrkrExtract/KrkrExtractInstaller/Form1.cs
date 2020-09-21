﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace KrkrExtractInstaller
{
    public partial class Form1 : Form
    {
        private readonly string crtDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private Command Cmd = Command.KeEmptyCmd;

        public Form1(Command cmd = Command.KeEmptyCmd)
        {
            DisableDPIScale();
            InitializeComponent();

            Cmd = cmd;
        }

        private void RunAsAdmin(string Arguments = "")
        {
            var startup = new ProcessStartInfo();
            startup.WindowStyle = ProcessWindowStyle.Normal;
            startup.UseShellExecute = true;
            startup.WorkingDirectory = Environment.CurrentDirectory;
            startup.Arguments = Arguments;
            startup.FileName = Application.ExecutablePath;
            startup.Verb = "runas";

            try
            {
                using (var proc = Process.Start(startup))
                {
                    Environment.Exit(0);
                    return;
                }
            }
            catch (SystemException)
            {
                MessageBox.Show(this, "Error with Launching Application as administrator\r\n" +
                    "\r\n" +
                    "Please run this application as administrator and try again.",
                    "KrkrExtract Context Menu Installer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            if (Environment.OSVersion.Version.CompareTo(new Version(6, 0)) <= 0)
            {
                MessageBox.Show(this, "Sorry, KrkrExtract is only for Windows 7, 8/8.1 and 10.",
                    "OS Outdated",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);

                Environment.Exit(0);
            }

            Text += " - V" + GetKeVersion();

            if (IsAdministrator())
            {
                Text += " (Administrator)";

                InstallAll.Enabled = false;
            }

            AddShieldToButton(InstallAll);
            AddShieldToButton(UninstallAll);

            UninstallUser.Enabled = ShellExtensionManager.IsInstalled(false);
            UninstallAll.Enabled  = ShellExtensionManager.IsInstalled(true);

            switch (Cmd)
            {
                case Command.KeInstallUser:
                    InstallUser.PerformClick();
                    break;

                case Command.keUninstallUser:
                    UninstallUser.PerformClick();
                    break;

                case Command.KeInstallAll:
                    InstallAll.PerformClick();
                    break;

                case Command.KeUnInstallAll:
                    UninstallAll.PerformClick();
                    break;
            }
        }

        public static bool IsAdministrator()
        {
            var wp = new WindowsPrincipal(WindowsIdentity.GetCurrent());

            return wp.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private void DoRegister(bool allUsers)
        {
            try
            {
                if (!allUsers)
                    OverrideHKCR();

                var rs = new RegistrationServices();
                rs.RegisterAssembly(Assembly.LoadFrom(Path.Combine(crtDir, @"KeContextMenuHandler.dll")),
                    AssemblyRegistrationFlags.SetCodeBase);

                ShellExtensionManager.RegisterShellExtContextMenuHandler(allUsers);

                if (!allUsers)
                    OverrideHKCR(true);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message + "\r\n\r\n" + e.StackTrace);
            }
        }


        private void OverrideHKCR(bool restore = false)
        {
            UIntPtr HKEY_CLASSES_ROOT = Is64BitOS() ? new UIntPtr(0xFFFFFFFF80000000) : new UIntPtr(0x80000000);
            UIntPtr HKEY_CURRENT_USER = Is64BitOS() ? new UIntPtr(0xFFFFFFFF80000001) : new UIntPtr(0x80000001);

            // 0xF003F = KEY_ALL_ACCESS
            UIntPtr key = UIntPtr.Zero;

            RegOpenKeyEx(HKEY_CURRENT_USER, @"Software\Classes", 0, 0xF003F, out key);
            RegOverridePredefKey(HKEY_CLASSES_ROOT, restore ? UIntPtr.Zero : key);
        }


        private void DeleteOldFiles()
        {
            foreach (var file in Directory.EnumerateFiles(crtDir, "*.installer.bak"))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception)
                {
                }
            }
        }


        private bool ReplaceDll()
        {
            var dllPath1 = Path.Combine(crtDir, @"KeContextMenuHandler.dll");

            // try delete old files. If failed, rename them
            try
            {
                File.Delete(dllPath1);
            }
            catch (Exception)
            {
                try
                {
                    File.Move(dllPath1, $"{Guid.NewGuid()}.installer.bak");
    
                }
                catch (Exception ee)
                {
                    MessageBox.Show(this, ee.Message + "\r\nPlease try rebooting your PC.");
                    return false;
                }
            }

            // Write new files
            try
            {
                //File.WriteAllBytes(dllPath1, Resources.LEContextMenuHandler);

                return true;
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message + "\r\nPlease try install/uninstall again.");
                return false;
            }
        }



        private void NotifyShell()
        {
            const uint SHCNE_ASSOCCHANGED = 0x08000000;
            const ushort SHCNF_IDLIST = 0x0000;

            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        }

        static internal void AddShieldToButton(Button b)
        {
            const uint BCM_FIRST = 0x1600; //Normal button
            const uint BCM_SETSHIELD = (BCM_FIRST + 0x000C); //Elevated button

            b.FlatStyle = FlatStyle.System;
            SendMessage(b.Handle, BCM_SETSHIELD, 0, 0xFFFFFFFF);
        }


        private static bool Is64BitOS()
        {
            //The code below is from http://1code.codeplex.com/SourceControl/changeset/view/39074#842775
            //which is under the Microsoft Public License: http://www.microsoft.com/opensource/licenses.mspx#Ms-PL.

            if (UIntPtr.Size == 8) // 64-bit programs run only on Win64
            {
                return true;
            }
            // Detect whether the current process is a 32-bit process 
            // running on a 64-bit system.
            bool flag;
            return DoesWin32MethodExist("kernel32.dll", "IsWow64Process") &&
                   IsWow64Process(GetCurrentProcess(), out flag) && flag;
        }

        private static string GetKeVersion()
        {
            try
            {
                var versionPath =
                    Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                        "KrkrExtractVersion.xml");

                var doc = XDocument.Load(versionPath);

                return doc.Descendants("KeVersion").First().Attribute("Version").Value;
            }
            catch
            {
                return "0.0.0.0";
            }
        }

        public static void DisableDPIScale()
        {
            SetProcessDPIAware();
        }

        

        private static bool DoesWin32MethodExist(string moduleName, string methodName)
        {
            var moduleHandle = GetModuleHandle(moduleName);
            if (moduleHandle == UIntPtr.Zero)
            {
                return false;
            }
            return GetProcAddress(moduleHandle, methodName) != UIntPtr.Zero;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int DeleteFile(string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void SetLastError(int errorCode);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern void SHChangeNotify(uint wEventId, ushort uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern UIntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern UIntPtr GetModuleHandle(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern UIntPtr GetProcAddress(UIntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWow64Process(UIntPtr hProcess, out bool wow64Process);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegOpenKeyEx(UIntPtr hKey, string subKey, int ulOptions, uint samDesired,
            out UIntPtr hkResult);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegOverridePredefKey(UIntPtr hKey, UIntPtr hNewKey);

        [DllImport("advapi32.dll", SetLastError = true)]
        public static extern int RegCloseKey(UIntPtr hKey);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern UInt32 SendMessage(IntPtr hWnd, UInt32 msg, UInt32 wParam, UInt32 lParam);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();


        private void DoUnRegister(bool allUsers)
        {
            try
            {
                if (!allUsers)
                    OverrideHKCR();

                var rs = new RegistrationServices();
                rs.UnregisterAssembly(Assembly.LoadFrom(Path.Combine(crtDir, @"KeContextMenuHandler.dll")));

                ShellExtensionManager.UnregisterShellExtContextMenuHandler(allUsers);

                if (!allUsers)
                    OverrideHKCR(true);
            }
            catch (Exception e)
            {
                MessageBox.Show(this, e.Message + "\r\n\r\n" + e.StackTrace);
            }
        }

        private void InstallUser_Click(object sender, EventArgs e)
        {
            DoRegister(false);

            NotifyShell();

            MessageBox.Show(this, "Install finished. Right click any executable and enjoy :)\r\n" +
                            "\r\n" +
                            "PS: A reboot (or restart of \"explorer.exe\") is required if you are upgrading from an old version.",
                "KrkrExtract Context Menu Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            UninstallUser.Enabled = true;
        }

        private void UninstallUser_Click(object sender, EventArgs e)
        {
            DoUnRegister(false);

            NotifyShell();

            MessageBox.Show(this, "Uninstall finished.\r\n" +
                            "\r\n" +
                            "PS: A reboot is required to unlock some components.",
                "KrkrExtract Context Menu Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            UninstallUser.Enabled = false;
        }

        private void InstallAll_Click(object sender, EventArgs e)
        {
            if (!IsAdministrator())
            {
                RunAsAdmin("--InstallAll");
                return;
            }

            DoRegister(true);

            NotifyShell();

            MessageBox.Show(this, "Install finished.\r\n" +
                            "\r\n" +
                            "PS: A reboot (or restart of \"explorer.exe\") is required if you are upgrading from an old version.",
                "KrkrExtract Context Menu Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            UninstallAll.Enabled = true;
        }

        private void UninstallAll_Click(object sender, EventArgs e)
        {
            if (!IsAdministrator())
            {
                RunAsAdmin("--UninstallAll");
                return;
            }

            DoUnRegister(true);

            NotifyShell();

            MessageBox.Show(this, "Uninstall finished.\r\n" +
                            "\r\n" +
                            "PS: A reboot is required to unlock some components.",
                "KrkrExtract Context Menu Installer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            UninstallAll.Enabled = false;
        }
    }
}