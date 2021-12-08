using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ConsoleWrapper {
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application {

        public static Process? Process;


        public static string? ShutdownCommand; // The command we run and pipe into Command when a session end signal is received.
        public static string? ShutdownText; // The text we "type" when a session end signal is received.
        public static string Command = ""; // The command we are wrapping (We proxy they console STDIN and STDOUT so we can shutdown gracefully)
        public static string? CommandArgs; // The arguments we send

        public static string WorkingDirectory = Environment.CurrentDirectory; // Working directory for the command

        public static readonly int ShutdownTimeout = 30000; // The time allowed to wait for the main process to close after the shutdown command closed, does not stop absoluteShutdownTimeout.
        public static readonly int AbsoluteShutdownTimeout = 60000; // The total allowed time from receiving the session end signal to closing the whole application.

        #region Console Methods
        [DllImport("Kernel32")]
        private static extern void AllocConsole();
        [DllImport("Kernel32")]
        private static extern void FreeConsole();
        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, uint nCmdShow);
        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);
        
        const uint SW_HIDE = 0;
        const uint SW_SHOWNORMAL = 1;
        const uint SW_SHOWNOACTIVATE = 4; // Show without activating
        public static bool ConsoleVisible { get; private set; }

        public static void HideConsole() {
            IntPtr handle = GetConsoleWindow();
            ShowWindow(handle, SW_HIDE);
            ConsoleVisible = false;

        }
        public static void ShowConsole(bool active = true) {
            IntPtr handle = GetConsoleWindow();
            if (active) { ShowWindow(handle, SW_SHOWNORMAL); } else { ShowWindow(handle, SW_SHOWNOACTIVATE); }
            ConsoleVisible = true;
        }

        // Disable Console Exit Button
        [DllImport("user32.dll")]
        static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
        [DllImport("user32.dll")]
        static extern IntPtr DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

        const uint SC_CLOSE = 0xF060;
        const uint MF_BYCOMMAND = (uint)0x00000000L;

        public static void DisableConsoleExit() {
            IntPtr handle = GetConsoleWindow();
            IntPtr exitButton = GetSystemMenu(handle, false);
            DeleteMenu(exitButton, SC_CLOSE, MF_BYCOMMAND);
        }

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler? _handler;

        #endregion

        enum CtrlType {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }
        enum ReasonSessionEnding {
            LOGOFF = 0,
            SHUTDOWN = 1
        }

        public enum ShutdownReason {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6,
            SESSION_LOGOFF_EVENT = 7,
            SESSION_SHUTDOWN_EVENT = 8,
            WINDOW_CLOSING = 9
        }

        void Application_Startup(object sender, StartupEventArgs args) {
            Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            StatsWindow statsWindow = new StatsWindow();
            AllocConsole();

            DisableConsoleExit();

            Current.MainWindow = statsWindow;


            /*
             Arguments:
                -shutdown-command | -scommand: The program we run, redirect output to the input of the main process, and wait to close when a SIG TERM command is received. (I.E. we're told to close)
                    1) Run <scommand>
                        - Redirect STDOUT to STDIN of main process
                    2) Wait for <scommand> to close
                    3) Start "TimeOut" task, (waits X seconds and then force closes the main process)
                    3) Wait for main process to close
                    4) Clean up and quit

                -program | -process | -command: The program we wrapping around
                    For example: calc.exe {OR} "java -jar minecraft.jar"
                    Use quotemarks to include the command arguments.

                -statswindow: Show main process stats (mem usage, cpu, etc)

                -workingdirectory | -directory: The working directory of the program
             
                If one of these does not show up, assume the args are actually the command.
             */

            bool showStatsWindow = false;


#if DEBUG
            Command = "java";
            CommandArgs = "-Xms512M -Xmx512M -jar Waterfall.jar";
            WorkingDirectory = "C:\\tmp\\MC";
            ShutdownCommand = "C:\\tmp\\MC\\shutdown.bat";
            //ShutdownText = "end";
            showStatsWindow = true;
#endif

            string arg;
            for (int i = 0; i < args.Args.Length; i++) {
                arg = args.Args[i].Remove(0,1);
                if (arg == "shutdown-command" || arg == "scommand") {
                    if (i + 1 < args.Args.Length) {
                        ShutdownCommand = args.Args[i + 1];
                        i++;
                    }
                    continue;
                } else if (arg == "command" || arg == "program" || arg == "process") {
                    if (i + 1 < args.Args.Length) {
                        string[] cArgs = args.Args[i + 1].Split(' ');
                        Command = cArgs[0];
                        if (cArgs.Length > 1) {
                            for (int a = 0; a < cArgs.Length; a++)
                                CommandArgs += cArgs[a];
                        }
                        i++;
                    }
                    continue;
                } else if (arg == "directory" || arg == "workingdirectory") {
                    if (i + 1 < args.Args.Length) {
                        WorkingDirectory = args.Args[i + 1];
                        i++;
                    }
                    continue;
                } else if (arg == "statswindow") {
                    showStatsWindow = true;
                    continue;
                } else if (arg == "shutdowntext" || arg == "stext") {
                    if (i + 1 < args.Args.Length) {
                        ShutdownText = args.Args[i + 1];
                        i++;
                    }
                    continue;
                }
            }

            // Start the process
            Process = new Process {
                StartInfo = new ProcessStartInfo(Command) {
                    Arguments = CommandArgs,

                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,

                    WorkingDirectory = WorkingDirectory,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true
            };
            Process.OutputDataReceived += Process_OutputDataReceived;
            Process.ErrorDataReceived += Process_OutputDataReceived;

            Process.Start(); // Start the command
            Process.BeginOutputReadLine();
            Process.BeginErrorReadLine(); // So we can read outputs

            // Capture console ctrl signals
            _handler += new EventHandler(ConsoleCtrlHandler);
            SetConsoleCtrlHandler(_handler, true);

            

            if (showStatsWindow) {
                statsWindow.Show();
            }

            // Background task to read console input (in the background so that close even while waiting for console input)
            Task.Run(() => {
                string? line;
                while (!Process.HasExited) {
                    line = Console.ReadLine();
                    if (Process.HasExited)
                        break;
                    Process.StandardInput.WriteLine(line);
                }
            });

            // Wait for process to end
            //try {
            //    Process.WaitForExit();
            //} finally {
            //    Process.CancelOutputRead();
            //    Process.CancelErrorRead();
            //    if (Process.HasExited)
            //        Environment.Exit(Process.ExitCode);
            //    else
            //        Environment.Exit(-1);
            //}

        }

        void Application_SessionEnding(object sender, SessionEndingCancelEventArgs e) {
            // Close the application.
            CloseCommand(e.ReasonSessionEnding == System.Windows.ReasonSessionEnding.Logoff ? ShutdownReason.SESSION_LOGOFF_EVENT : ShutdownReason.SESSION_SHUTDOWN_EVENT);
        }

        void Application_Exit(object sender, ExitEventArgs e) {

        }

        public static bool CloseCommand(ShutdownReason shutdownReason) {
            // This is called whenever we need to shutdown the application and the command
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Beep(777, 250);
            Console.Beep(525, 200);
            Console.WriteLine("ConsoleWrapper Shutdown Event: " + shutdownReason);
            Console.ResetColor();

            // Okay, so the OS is likely trying to kill us. We HAVE to be quick, which means you should be quick in your shutdown command.
            // Default is 20 seconds, and you can change this in RegEdit: HKEY_CURRENT_USER\Control Panel\Desktop\WaitToKillAppTimeout
            // Services: HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\WaitToKillServiceTimeout

            if (Process == null)
                Environment.Exit(0);

            Task.Run(() => {
                Thread.Sleep(AbsoluteShutdownTimeout);
                if (Process != null)
                    if (!Process.HasExited)
                        Process.Kill(true);
                Environment.Exit(100);
            });

            if (!string.IsNullOrWhiteSpace(ShutdownCommand)) {
                // Try to create a new process, wrap STDOUT and wait
                Process sC = new Process() {
                    StartInfo = new ProcessStartInfo(ShutdownCommand) {
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        RedirectStandardInput = false,
                        UseShellExecute = false,

                        WorkingDirectory = WorkingDirectory,
                        CreateNoWindow = true,
                        ErrorDialog = false
                    },
                };
                sC.OutputDataReceived += (sender, e) => {
                    SendToProcess(e.Data);
                };
                sC.ErrorDataReceived += (sender, e) => {
                    SendToProcess(e.Data);
                };

                sC.Start();
                sC.BeginOutputReadLine();
                sC.BeginErrorReadLine();

                sC.WaitForExit();

                Task.Run(() => {
                    Thread.Sleep(ShutdownTimeout);
                    if (Process != null)
                        if (!Process.HasExited)
                            Process.Kill(true);
                    Environment.Exit(101);
                });

                Process.WaitForExit();
                Process.CancelOutputRead();
                Process.CancelErrorRead();
            } else if (!string.IsNullOrWhiteSpace(ShutdownText)) {
                SendToProcess(ShutdownText);
                Task.Run(() => {
                    Thread.Sleep(ShutdownTimeout);
                    if (Process != null)
                        if (!Process.HasExited)
                            Process.Kill(true);
                    Environment.Exit(101);
                });

                Process.WaitForExit();
                Process.CancelOutputRead();
                Process.CancelErrorRead();
            } else {
                Process.CancelOutputRead();
                Process.CancelErrorRead();
                Process.Close();
            }

            if (shutdownReason != ShutdownReason.SESSION_SHUTDOWN_EVENT && shutdownReason != ShutdownReason.SESSION_LOGOFF_EVENT) {
                Environment.Exit(0);
            }

            return true;
        }

        public static void SendToProcess(string? msg) {
            if (Process == null)
                return;

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(msg);
            Console.ResetColor();
            Process.StandardInput.WriteLine(msg);
        }

        void Process_OutputDataReceived(object sender, DataReceivedEventArgs e) {
            Console.WriteLine(e.Data);
        }

        bool ConsoleCtrlHandler(CtrlType sig) {
            // This shouldn't happen unless the console itself is closed. If the console closes, however, our application doesn't necessarily close.
            // Also this gives us about 3 seconds.
            FreeConsole();
            CloseCommand((ShutdownReason)sig);
            return true;
        }

        
    }
}
