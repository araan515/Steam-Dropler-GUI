using System;

namespace DroplerGUI.Core
{
    public static class Logger
    {
        public static void LogError(string message)
        {
            Log($"[ERROR] {message}");
        }

        public static void LogWarning(string message)
        {
            Log($"[WARNING] {message}");
        }

        public static void LogInfo(string message)
        {
            Log($"[INFO] {message}");
        }

        public static void LogSuccess(string message)
        {
            Log($"[SUCCESS] {message}");
        }

        private static void Log(string message)
        {
            Console.WriteLine(message);
        }
    }
} 