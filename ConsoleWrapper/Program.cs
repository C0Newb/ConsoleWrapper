using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleWrapper {
    class Program {

        private static Process process;

        private static string shutdownCommand;
        private static string shutdownText;
        private static string command;
        private static string commandArgs;
        private static string workingDirectory;
        private static bool showStatsWindow;

        private static int shutdownTimeout = 30000; // The time allowed to wait for the main process to close after the shutdown command closed, does not stop absoluteShutdownTimeout.
        private static int absoluteShutdownTimeout = 60000; // The total allowed time from receiving the CTRL signal to closing.

        [DllImport("Kernel32")]
        private static extern bool SetConsoleCtrlHandler(EventHandler handler, bool add);

        private delegate bool EventHandler(CtrlType sig);
        static EventHandler _handler;

        enum CtrlType {
            CTRL_C_EVENT = 0,
            CTRL_BREAK_EVENT = 1,
            CTRL_CLOSE_EVENT = 2,
            CTRL_LOGOFF_EVENT = 5,
            CTRL_SHUTDOWN_EVENT = 6
        }

        private static bool CloseHandler(CtrlType sig) {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("SIG TERM: " + sig);
            Console.ResetColor();

            Task.Run(() => {
                Thread.Sleep(absoluteShutdownTimeout); // We'll give this thing 60 seconds before force closing.
                if (process != null)
                    if (!process.HasExited)
                        process.Kill();
                Environment.Exit(-1);
            });

            if (!string.IsNullOrEmpty(shutdownCommand)) {
                // Try to create a new process, wrap the STDOUT and wait.
                Process sC = new Process() {
                    StartInfo = new ProcessStartInfo(shutdownCommand) {
                        RedirectStandardError = true,
                        RedirectStandardOutput = true,
                        RedirectStandardInput = false,
                        UseShellExecute = false,
                        WorkingDirectory = workingDirectory,
                        CreateNoWindow = true,
                        ErrorDialog = false
                    },
                };
                sC.OutputDataReceived += (sender, args) => {
                    SendToProcess(args.Data);
                };
                sC.ErrorDataReceived += (sender, args) => {
                    SendToProcess(args.Data);
                };

                sC.Start();
                sC.BeginOutputReadLine();
                sC.BeginErrorReadLine();

                sC.WaitForExit(); // Wait for this sucker to close.

                Task.Run(() => {
                    Thread.Sleep(shutdownTimeout);
                    if (process != null)
                        if (!process.HasExited)
                            process.Kill();
                    Environment.Exit(-1);
                });

                process.WaitForExit();
                process.CancelOutputRead();
                process.CancelErrorRead();
            } else if (!string.IsNullOrEmpty(shutdownText)) {
                SendToProcess(shutdownText);
                process.CancelOutputRead();
                process.CancelErrorRead();
            } else {
                process.CancelOutputRead();
                process.CancelErrorRead();
                process.Close(); // Try to be nice :shrug:
            }

            Environment.Exit(0);
            //Thread.Sleep(10000);

            return false;
        }

        static void SendToProcess(String msg) {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(msg);
            Console.ResetColor();
            process.StandardInput.WriteLine(msg);
        }

        static void Main(string[] args) {
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

            workingDirectory = Environment.CurrentDirectory;

#if DEBUG
            command = "java";
            commandArgs = "-Xms512M -Xmx512M -jar Waterfall.jar";
            workingDirectory = "C:\\tmp\\MC";
            //shutdownCommand = "C:\\tmp\\MC\\shutdown.bat";
            shutdownText = "end";
#endif


            string arg;
            for(int i = 0; i<args.Length; i++) {
                arg = args[i];
                if (arg == "-shutdown-command" || arg == "-scommand") {
                    if (i + 1 < args.Length) {
                        shutdownCommand = args[i + 1];
                        i++;
                    }
                    continue;
                } else if (arg == "-command" || arg == "-program" || arg == "-process") {
                    if (i + 1 < args.Length) {
                        string[] cArgs = args[i + 1].Split(' ');
                        command = cArgs[0];
                        if (cArgs.Length > 1) {
                            for (int a = 0; a < cArgs.Length; a++)
                                commandArgs += cArgs[a];
                        }
                        i++;
                    }
                    continue;
                } else if (arg == "-directory" || arg == "-workingdirectory") {
                    if (i + 1 < args.Length) {
                        workingDirectory = args[i + 1];
                        i++;
                    }
                    continue;
                } else if (arg == "-statswindow") {
                    showStatsWindow = true;
                    continue;
                } else if (arg == "-shutdowntext" || arg == "-stext") {
                    if (i + 1 < args.Length) {
                        shutdownText = args[i + 1];
                        i++;
                    }
                    continue;
                } 
            }
            
            process = new Process {
                StartInfo = new ProcessStartInfo(command) {
                    Arguments = commandArgs,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    WorkingDirectory = workingDirectory,
                    CreateNoWindow = true,
                    RedirectStandardInput = true
                }
            };
            process.OutputDataReceived += P_OutputDataReceived;
            process.ErrorDataReceived += P_ErrorDataReceived;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Capture term signals
            _handler += new EventHandler(CloseHandler);
            SetConsoleCtrlHandler(_handler, true);

            Task.Run(() => {
                while (!process.HasExited) {
                    string Line = Console.ReadLine();
                    if (process.HasExited)
                        break;
                    process.StandardInput.WriteLine(Line);
                }
            });

            try {
                process.WaitForExit();
            } catch (Exception) {
                //Environment.Exit(0);
            } finally {
                Thread.Sleep(10000);
                process.CancelOutputRead();
                process.CancelErrorRead();
                if (process.HasExited)
                    Environment.Exit(process.ExitCode);
                else
                    Environment.Exit(-1);
            }
        }

        private static void P_ErrorDataReceived(object sender, DataReceivedEventArgs e) {
            Console.WriteLine(e.Data);
        }

        public static void P_OutputDataReceived(object sender, DataReceivedEventArgs e) {
            Console.WriteLine(e.Data);
        }
    }
}
