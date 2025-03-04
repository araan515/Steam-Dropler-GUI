using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using DroplerGUI.Core;
using DroplerGUI.Services;
using System.ComponentModel;
using System.Threading.Tasks;

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
        
        // Статистика - теперь берем данные напрямую из Worker
        public int TotalAccounts => Worker?.Accounts.Count() ?? 0;
        public int EnabledAccounts => Worker?.Accounts.Count(a => a.Enabled) ?? 0;
        public int FarmingAccounts => Worker?.Accounts.Count(a => a.IdleNow) ?? 0;

        [DisplayNameAttribute("Следующий аккаунт запустится через")]
        public string NextStart
        {
            get
            {
                if (!IsRunning || Status == TaskStatus.Stopped)
                    return "-";

                try
                {
                    // Получаем активные аккаунты
                    var activeAccounts = Worker.Accounts
                        .Where(t => t.IdleNow && t.LastStartTime.HasValue)
                        .ToList();

                    // Если нет активных аккаунтов, проверяем время паузы для следующего запуска
                    if (!activeAccounts.Any())
                    {
                        var nextAccount = Worker.Accounts
                            .Where(t => t.Enabled && 
                                      !t.IdleNow && 
                                      !t.IsRunning && 
                                      t.LastStartTime.HasValue && 
                                      !string.IsNullOrEmpty(t.SharedSecret))
                            .OrderBy(t => t.LastStartTime)
                            .FirstOrDefault();

                        if (nextAccount != null)
                        {
                            var timeSinceLastStart = (DateTime.Now - nextAccount.LastStartTime.Value).TotalMinutes;
                            var pauseTimeRemaining = Worker.Config.TimeConfig.PauseBeatwinIdleTime - timeSinceLastStart;
                            
                            if (pauseTimeRemaining > 0)
                            {
                                return $"{Math.Round(pauseTimeRemaining)} мин";
                            }
                        }
                        
                        // Если нет аккаунтов с LastStartTime или время паузы истекло
                        var anyReadyAccounts = Worker.Accounts
                            .Any(t => t.Enabled && 
                                    !t.IdleNow && 
                                    !t.IsRunning && 
                                    !string.IsNullOrEmpty(t.SharedSecret) &&
                                    (!t.LastStartTime.HasValue || 
                                     (DateTime.Now - t.LastStartTime.Value).TotalMinutes >= Worker.Config.TimeConfig.PauseBeatwinIdleTime));

                        return anyReadyAccounts ? "Скоро" : "-";
                    }

                    // Если есть активные аккаунты, проверяем следующий запуск
                    var firstToFinish = activeAccounts
                        .OrderBy(a => a.LastStartTime.Value.AddMinutes(Worker.Config.TimeConfig.IdleTime))
                        .First();

                    var remainingIdleTime = Worker.Config.TimeConfig.IdleTime - 
                        (DateTime.Now - firstToFinish.LastStartTime.Value).TotalMinutes;

                    // Проверяем, есть ли аккаунты, готовые к запуску
                    var nextQueuedAccount = Worker.Accounts
                        .Where(t => t.Enabled &&
                                  !t.IdleNow &&
                                  !t.IsRunning &&
                                  !string.IsNullOrEmpty(t.SharedSecret))
                        .OrderBy(t => t.LastStartTime ?? DateTime.MaxValue)
                        .FirstOrDefault();

                    if (nextQueuedAccount == null)
                    {
                        return "-";
                    }

                    // Проверяем время паузы для следующего аккаунта
                    var remainingPauseTime = 0.0;
                    if (nextQueuedAccount.LastStartTime.HasValue)
                    {
                        var timeSinceLastStart = (DateTime.Now - nextQueuedAccount.LastStartTime.Value).TotalMinutes;
                        if (timeSinceLastStart < Worker.Config.TimeConfig.PauseBeatwinIdleTime)
                        {
                            remainingPauseTime = Worker.Config.TimeConfig.PauseBeatwinIdleTime - timeSinceLastStart;
                        }
                    }

                    // Если есть место для нового аккаунта и он готов к запуску
                    if (Worker.ActiveAccountsCount < Worker.Config.ParallelCount && 
                        (remainingPauseTime <= 0 || !nextQueuedAccount.LastStartTime.HasValue))
                    {
                        return "Скоро";
                    }

                    // Общее время ожидания - максимум из оставшегося IdleTime и PauseBeatwinIdleTime
                    var totalWaitTime = Math.Max(remainingIdleTime, remainingPauseTime);
                    return $"{Math.Round(totalWaitTime)} мин";
                }
                catch (Exception)
                {
                    return "-";
                }
            }
        }
        
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
        
        public async Task StopAsync()
        {
            if (Worker == null) return;

            try
            {
                await Worker.StopAsync();
                IsRunning = false;
                Status = TaskStatus.Stopped;
            }
            catch (Exception ex)
            {
                Status = TaskStatus.Error;
                throw new Exception($"Ошибка при остановке задачи {TaskNumber}: {ex.Message}", ex);
            }
        }

        // Синхронный метод-обертка для обратной совместимости
        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        public (int Total, int Enabled, int Farming) GetAccountsInfo()
        {
            return (
                Total: TotalAccounts,
                Enabled: EnabledAccounts,
                Farming: FarmingAccounts
            );
        }

        public void UpdateAccountsState()
        {
            // Теперь этот метод просто уведомляет UI об изменениях
            // Все данные берутся напрямую из Worker через свойства выше
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
        Error,
        Unknown
    }
} 