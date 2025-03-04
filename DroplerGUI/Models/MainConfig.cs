using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using System.Linq;
using System.Windows;
using DroplerGUI.Core;

namespace DroplerGUI.Models
{
    public class MainConfig
    {
        private static readonly Dictionary<int, MainConfig> _configs = new Dictionary<int, MainConfig>();
        
        public static MainConfig GetConfig(int taskNumber)
        {
            if (!_configs.ContainsKey(taskNumber))
            {
                var configPath = Path.Combine(Constants.GetTaskConfigPath(taskNumber), "MainConfig.json");
                if (File.Exists(configPath))
                {
                    _configs[taskNumber] = Load(configPath);
                }
                else
                {
                    _configs[taskNumber] = GetDefault();
                    Save(_configs[taskNumber], configPath);
                }
            }
            return _configs[taskNumber];
        }

        public string MaFileFolder { get; set; }
        public string DropHistoryFolder { get; set; }
        public int ParallelCount { get; set; }
        public int StartTimeOut { get; set; }
        public int ChkIdleTimeOut { get; set; }
        public string ShowStatus { get; set; }
        public TimeConfig TimeConfig { get; set; }
        public List<(uint, string)> DropConfig { get; set; }
        public ScheduleConfig Schedule { get; set; }

        [JsonIgnore]
        public bool SavCfg { get; set; }

        public static void Load(int taskNumber)
        {
            var configPath = Path.Combine(Constants.GetTaskConfigPath(taskNumber), "MainConfig.json");
            if (File.Exists(configPath))
            {
                _configs[taskNumber] = Load(configPath);
            }
            else
            {
                _configs[taskNumber] = GetDefault();
                Save(_configs[taskNumber], configPath);
            }
        }

        private static MainConfig Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<MainConfig>(json);
        }

        public static void Save(MainConfig config, string path)
        {
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            File.WriteAllText(path, json);
            
            // Получаем номер задачи из полного пути
            var fullPath = Path.GetFullPath(path);
            var pathParts = fullPath.Split(Path.DirectorySeparatorChar);
            var taskDir = pathParts.FirstOrDefault(p => p.StartsWith("task_") || p.StartsWith("Task_"));
            if (taskDir != null)
            {
                var taskNumber = int.Parse(taskDir.Split('_').Last());
                _configs[taskNumber] = config;
            }
        }

        public void Save(string path)
        {
            Save(this, path);
        }

        public MainConfig Clone()
        {
            var json = JsonConvert.SerializeObject(this);
            return JsonConvert.DeserializeObject<MainConfig>(json);
        }

        public static MainConfig GetDefault()
        {
            return new MainConfig
            {
                MaFileFolder = "maFiles",
                DropHistoryFolder = "DropHistory",
                ParallelCount = 100,
                StartTimeOut = 30,
                ChkIdleTimeOut = 10,
                ShowStatus = "Online",
                TimeConfig = new TimeConfig
                {
                    IdleTime = 80,
                    PauseBeatwinIdleTime = 90
                },
                DropConfig = new List<(uint, string)>
                {
                    (2923300, "99999")
                },
                Schedule = new ScheduleConfig()
            };
        }

        public static void ClearCache(int taskNumber)
        {
            if (_configs.ContainsKey(taskNumber))
            {
                _configs.Remove(taskNumber);
            }
        }
    }

    public class TimeConfig
    {
        public int IdleTime { get; set; } = 180;
        public int PauseBeatwinIdleTime { get; set; } = 30;
    }
}
