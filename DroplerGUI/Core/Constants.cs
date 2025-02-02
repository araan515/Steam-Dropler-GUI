using System;
using System.IO;

namespace DroplerGUI.Core
{
    public static class Constants
    {
        public static readonly string AppDataPath = AppDomain.CurrentDomain.BaseDirectory;

        public static string GetTaskPath(int taskNumber) => Path.Combine(AppDataPath, $"task_{taskNumber}");
        public static string GetTaskConfigPath(int taskNumber) => Path.Combine(GetTaskPath(taskNumber), "Configs");
        public static string GetTaskLogsPath(int taskNumber) => Path.Combine(GetTaskPath(taskNumber), "Logs");
        public static string GetTaskDropHistoryPath(int taskNumber) => Path.Combine(GetTaskPath(taskNumber), "DropHistory");
    }
} 