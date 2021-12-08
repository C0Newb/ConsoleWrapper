using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ConsoleWrapper {
    internal class ConsoleHelper {
        public const int STD_OUTPUT_HANDLE = -11;

        [DllImport("kernel32.dll", SetLastError = true, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Auto)]
        public static extern IntPtr GetStdHandle(int handle);
        //[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "ReadConsoleInputW")]
        //static extern bool ReadConsoleInput(IntPtr hConsoleInput, [Out] INPUT_RECORD[] lpBuffer, uint nLength, out uint lpNumberOfEventsRead);
        


        [DllImport("kernel32.dll", CharSet = CharSet.None, ExactSpelling = false, SetLastError = true)]
        internal static extern int WriteFile(IntPtr handle, ref byte bytes, int numBytesToWrite, out int numBytesWritten, IntPtr mustBeZero);

        public static void Main() {
            Console.WriteLine(PInvokeWriteLine("Hello world"));
        }

        public static int PInvokeWriteLine(string message) {
            if (!message.EndsWith(Environment.NewLine)) {
                message += Environment.NewLine;
            }

            byte[] messageBytes = Encoding.ASCII.GetBytes(message);

            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            int charsWritten = 0;

            WriteFile(handle, ref messageBytes[0], messageBytes.Length, out charsWritten, IntPtr.Zero);

            return charsWritten;
        }
    }
}
