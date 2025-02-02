using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using DroplerGUI.Core;
using DroplerGUI.Services;

namespace DroplerGUI.Models
{
    public class TaskInstance
    {
        public int TaskNumber { get; private set; }
        public string TaskPath { get; private set; }
        public MainConfig Config { get; private set; }
        public StatisticsService StatisticsService { get; private set; }
        public DateTime? LastStartTime { get; private set; }
        public TaskWorker Worker => _worker;
        
        // Пути к папкам
        public string TaskRootPath => Constants.GetTaskPath(TaskNumber);
        public string AccountsPath => Path.Combine(Constants.GetTaskPath(TaskNumber), "Accounts");
        public string MaFilesPath => Path.Combine(Constants.GetTaskPath(TaskNumber), "maFiles");
        public string ConfigsPath => Constants.GetTaskConfigPath(TaskNumber);
        public string LogsPath => Constants.GetTaskLogsPath(TaskNumber);
        public string DropHistoryPath => Constants.GetTaskDropHistoryPath(TaskNumber);
        
        // Состояние потока
        public bool IsRunning { get; private set; }
        public TaskStatus Status { get; private set; }
        
        // Статистика
        public int TotalAccounts => _accounts.Count;
        public int EnabledAccounts => _accounts.Count(a => a.Enabled);
        public int FarmingAccounts => _accounts.Count(a => a.IdleNow);
        public string NextStart
        {
            get
            {
                if (!LastStartTime.HasValue)
                {
                    return "-";
                }

                var nextStart = LastStartTime.Value.AddMinutes(Config?.TimeConfig?.IdleTime ?? 180);
                var timeUntilNext = nextStart - DateTime.Now;

                if (timeUntilNext.TotalMinutes > 0)
                {
                    return $"{timeUntilNext.Hours:D2}:{timeUntilNext.Minutes:D2}:{timeUntilNext.Seconds:D2}";
                }

                return "Скоро";
            }
        }
        
        private readonly HashSet<AccountConfig> _accounts = new HashSet<AccountConfig>();
        private TaskWorker _worker;
        
        public TaskInstance(int taskNumber, string basePath)
        {
            TaskNumber = taskNumber;
            TaskPath = Constants.GetTaskPath(taskNumber);
            Status = TaskStatus.Stopped;
            
            // Сначала создаем все директории
            InitializeDirectories();
            
            // Загружаем конфигурацию
            Config = MainConfig.GetConfig(taskNumber);
            
            // Затем инициализируем сервисы, которые будут использовать эти директории
            StatisticsService = new StatisticsService(taskNumber);
            _worker = new TaskWorker(TaskPath, StatisticsService, taskNumber);
            _worker.SetTaskInstance(this);
            
            // И наконец загружаем аккаунты
            LoadAccounts();
        }
        
        private void InitializeDirectories()
        {
            // Создаем все необходимые директории
            Directory.CreateDirectory(TaskRootPath);
            Directory.CreateDirectory(AccountsPath);
            Directory.CreateDirectory(MaFilesPath);
            Directory.CreateDirectory(ConfigsPath);
            Directory.CreateDirectory(LogsPath);
            Directory.CreateDirectory(DropHistoryPath);
            
            // Создаем базовые файлы если их нет
            InitializeConfigFiles();
        }
        
        private void InitializeConfigFiles()
        {
            var logPassPath = Path.Combine(ConfigsPath, "log_pass.txt");
            
            // Создаем log_pass.txt если его нет
            if (!File.Exists(logPassPath))
            {
                var template = @"# Формат: login:password
# Пример:
# login1:password1
# login2:password2";
                File.WriteAllText(logPassPath, template);
            }
        }
        
        private void LoadAccounts()
        {
            _accounts.Clear();
            
            if (!Directory.Exists(AccountsPath))
            {
                return;
            }

            var files = Directory.GetFiles(AccountsPath, "*.json");
            foreach (var file in files)
            {
                try
                {
                    var account = JsonConvert.DeserializeObject<AccountConfig>(File.ReadAllText(file));
                    account.Name = Path.GetFileNameWithoutExtension(file);
                    _accounts.Add(account);
                }
                catch
                {
                    // Игнорируем ошибки при загрузке отдельных аккаунтов
                }
            }
        }
        
        public void LoadConfig()
        {
            try
            {
                var configPath = Path.Combine(ConfigsPath, "MainConfig.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    Config = JsonConvert.DeserializeObject<MainConfig>(json);
                }
                else
                {
                    Config = CreateDefault();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при загрузке конфигурации: {ex.Message}");
            }
        }
        
        private static MainConfig CreateDefault()
        {
            return new MainConfig
            {
                MaFileFolder = "maFiles",
                DropHistoryFolder = "DropHistory",
                ParallelCount = 100,
                StartTimeOut = 30,
                ChkIdleTimeOut = 10,
                ShowStatus = "Online",
                TimeConfig = new TimeConfig(),
                DropConfig = new List<(uint, string)>
                {
                    (2923300, "99999")
                }
            };
        }
        
        public void Start()
        {
            if (IsRunning) return;
            
            try
            {
                Status = TaskStatus.Initializing;
                IsRunning = true;

                _worker.Run();

                LastStartTime = DateTime.Now;
                Status = TaskStatus.Running;
            }
            catch (Exception ex)
            {
                Status = TaskStatus.Error;
                IsRunning = false;
                _worker.Log($"Ошибка при запуске потока: {ex.Message}");
                throw;
            }
        }
        
        public void Stop()
        {
            if (!IsRunning) return;
            
            try
            {
                Status = TaskStatus.Stopping;
                _worker.Stop();
                IsRunning = false;
                Status = TaskStatus.Stopped;
            }
            catch (Exception ex)
            {
                Status = TaskStatus.Error;
                _worker.Log($"Ошибка при остановке потока: {ex.Message}");
                throw;
            }
        }

        public (int Total, int Enabled, int Farming) GetAccountsInfo()
        {
            UpdateAccountsState();
            return (
                Total: _accounts.Count,
                Enabled: _accounts.Count(a => a.Enabled),
                Farming: _accounts.Count(a => a.IdleNow)
            );
        }

        public void UpdateAccountsState()
        {
            if (!Directory.Exists(AccountsPath))
            {
                return;
            }

            _accounts.Clear();
            var files = Directory.GetFiles(AccountsPath, "*.json");

            foreach (var file in files)
            {
                try
                {
                    var account = JsonConvert.DeserializeObject<AccountConfig>(File.ReadAllText(file));
                    account.Name = Path.GetFileNameWithoutExtension(file);
                    _accounts.Add(account);
                }
                catch
                {
                    // Игнорируем ошибки при загрузке отдельных аккаунтов
                }
            }
        }

        public void SaveConfig()
        {
            try
            {
                var configPath = Path.Combine(ConfigsPath, "MainConfig.json");
                var json = JsonConvert.SerializeObject(Config, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при сохранении конфигурации: {ex.Message}");
            }
        }
    }
    
    public enum TaskStatus
    {
        Stopped,
        Initializing,
        Running,
        Stopping,
        Error
    }
} 