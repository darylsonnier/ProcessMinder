/*
* Created by SharpDevelop.
* User: SonnierDW
* Date: 5/25/2018
* Time: 11:08 AM
* 
* To change this template use Tools | Options | Coding | Edit Standard Headers.
*/
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
[assembly: log4net.Config.XmlConfigurator(Watch = true)]

namespace ProcMinder
{
    // Class for processes to monitor
    public class monitoredProcs
    {
        public string FileName { get; set; }
        public string Arguments { get; set; }

        public monitoredProcs(string name, string args)
        {
            FileName = name;
            Arguments = args;
        }
    }

    public partial class MainForm : Form
    {
        private static List<monitoredProcs> procs = new List<monitoredProcs>();
        private static readonly log4net.ILog log = log4net.LogManager.GetLogger("ProcMinder");
        private static Mutex mutex = new Mutex();

        public MainForm()
        {
            InitializeComponent();
            log.Info("Starting Process Minder");
            init();
        }

        // Timer event
        void Timer1Tick(object sender, EventArgs e)
        {
            checkProcs();
        }

        // Initialize process minder state
        void init()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            foreach (var key in ConfigurationManager.AppSettings)
            {
                if (File.Exists(key.ToString()))
                {
                    procs.Add(new monitoredProcs(key.ToString().ToLower(), ConfigurationManager.AppSettings[key.ToString()]));
                }
                else log.Info(key.ToString() + " does not exist.  Nothing to monitor.");
            }
            log.Info("Monitoring " + procs.Count + " processes.");
            timer1.Interval = 1000;
            timer1.Start();
        }

        // Gracefully shut down timer when form closes
        private void Stop(object sender, EventArgs e)
        {
            timer1.Stop();
        }

        // Loop through all minded process objects and check for running state
        void checkProcs()
        {
            mutex.WaitOne(5000, false);
            try
            {
                var pList = new List<string>();
                foreach (var p in Process.GetProcesses())
                {
                    pList.Add(GetExecutablePathAboveVista(p.Id).ToLower());
                }

                foreach (var p in procs)
                {
                    if (!pList.Contains(Path.GetFullPath(p.FileName)))
                    {
                        log.Info(p.FileName + " not running.");
                        Directory.SetCurrentDirectory(Path.GetDirectoryName(p.FileName));
                        StartProcessNoActivate(p.FileName, p.Arguments);
                        if (p.Arguments.Length > 0)
                        {
                            log.Info("Starting " + p.FileName + " " + p.Arguments + ".");
                        }
                        else log.Info("Starting " + p.FileName + ".");
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                log.Error(e.Message);
            }
            mutex.ReleaseMutex();
        }

        private static string GetExecutablePathAboveVista(int dwProcessId)
        {
            StringBuilder buffer = new StringBuilder(1024);
            IntPtr hprocess = OpenProcess(ProcessAccessFlags.PROCESS_QUERY_LIMITED_INFORMATION, false, dwProcessId);
            if (hprocess != IntPtr.Zero)
            {
                try
                {
                    int size = buffer.Capacity;
                    if (QueryFullProcessImageName(hprocess, 0, buffer, ref size))
                    {
                        return buffer.ToString();
                    }
                }
                finally
                {
                    CloseHandle(hprocess);
                }
            }
            return string.Empty;
        }        
        
        // Win API STARTUPINFO structure
        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public Int32 cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public Int32 dwX;
            public Int32 dwY;
            public Int32 dwXSize;
            public Int32 dwYSize;
            public Int32 dwXCountChars;
            public Int32 dwYCountChars;
            public Int32 dwFillAttribute;
            public Int32 dwFlags;
            public Int16 wShowWindow;
            public Int16 cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        // Win AQPI ProcessAccessFlags enum
        [Flags]
        public enum ProcessAccessFlags : uint
        {
            All = 0x001F0FFF,
            Terminate = 0x00000001,
            CreateThread = 0x00000002,
            VirtualMemoryOperation = 0x00000008,
            VirtualMemoryRead = 0x00000010,
            VirtualMemoryWrite = 0x00000020,
            DuplicateHandle = 0x00000040,
            CreateProcess = 0x000000080,
            SetQuota = 0x00000100,
            SetInformation = 0x00000200,
            QueryInformation = 0x00000400,
            QueryLimitedInformation = 0x00001000,
            Synchronize = 0x00100000,
            PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
        }

        // Win API PROCESS_INFORMATION structure
        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        // Win API QueryFullProcessPath wrapper
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool QueryFullProcessImageName([In]IntPtr hProcess, [In]int dwFlags, [Out]StringBuilder lpExeName, ref int lpdwSize);

        // Win API OpenProcess wrapper
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(
             ProcessAccessFlags processAccess,
             bool bInheritHandle,
             int processId
        );

        // Win API CreateProcess wrapper
        [DllImport("kernel32.dll")]
        static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            [In] ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation
        );

        // Win API CloseHandle wrapper
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        const int STARTF_USESHOWWINDOW = 1;
        const int SW_SHOWNOACTIVATE = 4;
        const int SW_SHOWMINNOACTIVE = 7;

        // Start a process without activating the window
        public static void StartProcessNoActivate(string executable, string args)
        {
            string cmdLine = executable + " " + args;
            string startFolder = Path.GetDirectoryName(executable);
            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.dwFlags = STARTF_USESHOWWINDOW;
            si.wShowWindow = SW_SHOWMINNOACTIVE;

            PROCESS_INFORMATION pi = new PROCESS_INFORMATION();

            CreateProcess(null, cmdLine, IntPtr.Zero, IntPtr.Zero, true, 0, IntPtr.Zero, startFolder, ref si, out pi);

            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }
    }
}
