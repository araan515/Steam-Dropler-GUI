using System;
using System.IO;

namespace DroplerGUI.Core
{
    public static class Constants
    {
        public const string DROPLER_VERSION = "1.3";
        public const string GUI_VERSION = "3.0.0-beta4";
        public const string STEAMKIT_VERSION = "3.0.0-beta4";
        public static readonly string AppDataPath = AppDomain.CurrentDomain.BaseDirectory;

        public static string GetTaskPath(int taskNumber) => Path.Combine(AppDataPath, $"task_{taskNumber}");
        public static string GetTaskConfigPath(int taskNumber) => Path.Combine(GetTaskPath(taskNumber), "Configs");
        public static string GetTaskLogsPath(int taskNumber) => Path.Combine(GetTaskPath(taskNumber), "Logs");
        public static string GetTaskDropHistoryPath(int taskNumber) => Path.Combine(GetTaskPath(taskNumber), "DropHistory");

        public static string GetVersionInfo() => $"Dropler GUI created with love by araan515 (dropler ver. {DROPLER_VERSION}, steamkit2 ver. {STEAMKIT_VERSION})";
        public static string GetDroplerVersionInfo() => $"Steam-dropler ver. ({DROPLER_VERSION})";
    }
} 