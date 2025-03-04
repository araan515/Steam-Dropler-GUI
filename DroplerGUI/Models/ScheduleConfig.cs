using System;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using DroplerGUI.Core;

namespace DroplerGUI.Models
{
    public class SingleTimeAction
    {
        public string Time { get; set; }

        public SingleTimeAction()
        {
            Time = "00:00";
        }

        public SingleTimeAction(string time)
        {
            Time = time;
        }
    }

    public class OneTimeAction
    {
        public DateTime DateTime { get; set; }
        public string ActionType { get; set; } // "Start" или "Stop"

        public OneTimeAction()
        {
            DateTime = DateTime.Now;
            ActionType = "Start";
        }

        public OneTimeAction(DateTime dateTime, string actionType)
        {
            DateTime = dateTime;
            ActionType = actionType;
        }
    }

    public class TimeInterval
    {
        public string StartTime { get; set; }
        public string StopTime { get; set; }

        public TimeInterval()
        {
            StartTime = "00:00";
            StopTime = "00:00";
        }

        public TimeInterval(string startTime, string stopTime)
        {
            StartTime = startTime;
            StopTime = stopTime;
        }
    }

    public class ScheduleConfig
    {
        public bool UseSchedule { get; set; }
        public List<TimeInterval> Intervals { get; set; }
        public List<SingleTimeAction> SingleStartTimes { get; set; }
        public List<SingleTimeAction> SingleStopTimes { get; set; }
        public List<OneTimeAction> OneTimeActions { get; set; }

        public ScheduleConfig()
        {
            UseSchedule = false;
            Intervals = new List<TimeInterval>();
            SingleStartTimes = new List<SingleTimeAction>();
            SingleStopTimes = new List<SingleTimeAction>();
            OneTimeActions = new List<OneTimeAction>();
        }

        public static ScheduleConfig Load(int taskNumber)
        {
            try
            {
                var configPath = Path.Combine(Constants.GetTaskConfigPath(taskNumber), "Schedule.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    return JsonConvert.DeserializeObject<ScheduleConfig>(json) ?? new ScheduleConfig();
                }
            }
            catch (Exception)
            {
                // В случае ошибки возвращаем новый конфиг
            }
            return new ScheduleConfig();
        }

        public void Save(int taskNumber)
        {
            try
            {
                var configPath = Path.Combine(Constants.GetTaskConfigPath(taskNumber), "Schedule.json");
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception)
            {
                // Игнорируем ошибки сохранения
            }
        }
    }
} 