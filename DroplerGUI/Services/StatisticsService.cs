using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DroplerGUI.Models;
using DroplerGUI.Core;

namespace DroplerGUI.Services
{
    public class StatisticsService
    {
        private readonly string _taskPath;
        private readonly ConcurrentDictionary<string, AccountStatistics> _statistics;
        private readonly object _saveLock = new object();
        private readonly object _lock = new object();

        public StatisticsService(int taskNumber)
        {
            _taskPath = Constants.GetTaskPath(taskNumber);
            _statistics = new ConcurrentDictionary<string, AccountStatistics>();

            // Создаем файл статистики, если его нет
            var statisticsPath = Path.Combine(_taskPath, "statistics.json");
            if (!File.Exists(statisticsPath))
            {
                File.WriteAllText(statisticsPath, "{}");
                Log("Создан новый файл статистики");
            }

            LoadStatistics();
        }

        public AccountStatistics GetAccountStatistics(string accountName)
        {
            if (string.IsNullOrEmpty(accountName)) return null;

            var stats = _statistics.GetOrAdd(accountName, name => 
            {
                var newStats = new AccountStatistics(name);
                SaveStatistics(); // Сохраняем сразу после создания новой статистики
                return newStats;
            });

            return stats;
        }

        public void RegisterDrop(string accountName, uint gameId)
        {
            if (string.IsNullOrEmpty(accountName)) return;

            var stats = GetAccountStatistics(accountName);
            if (stats == null)
            {
                Log($"Не удалось получить статистику для аккаунта {accountName}");
                return;
            }

            stats.TotalDropsCount++;
            stats.LastDropTime = DateTime.Now;

            SaveStatistics();
            Log($"[{accountName}] Обновлена статистика: всего дропов {stats.TotalDropsCount}");
        }

        public void UpdateAccountStatus(string accountName, string status)
        {
            if (string.IsNullOrEmpty(accountName))
                return;

            lock (_lock)
            {
                try
                {
                    var stats = _statistics.GetOrAdd(accountName, name => new AccountStatistics { AccountName = name });
                    stats.IsActive = status;

                    if (status == "Online")
                    {
                        stats.LastConnectionTime = DateTime.Now;
                    }

                    SaveStatistics();
                    Log($"Обновлен статус аккаунта {accountName}: {status}");
                }
                catch (Exception ex)
                {
                    Log($"Ошибка при обновлении статуса аккаунта {accountName}: {ex.Message}");
                }
            }
        }

        public IEnumerable<AccountStatistics> GetAllStatistics()
        {
            lock (_lock)
            {
                return _statistics.Values.ToList();
            }
        }

        private void LoadStatistics()
        {
            try
            {
                var statisticsPath = Path.Combine(_taskPath, "statistics.json");
                if (File.Exists(statisticsPath))
                {
                    var json = File.ReadAllText(statisticsPath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        json = "{}";
                        File.WriteAllText(statisticsPath, json);
                    }

                    var stats = JsonSerializer.Deserialize<Dictionary<string, AccountStatistics>>(json);
                    foreach (var pair in stats)
                    {
                        _statistics[pair.Key] = pair.Value;
                    }
                    Log($"Загружена статистика для {_statistics.Count} аккаунтов");
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при загрузке статистики: {ex.Message}");
                // В случае ошибки создаем новый файл
                File.WriteAllText(Path.Combine(_taskPath, "statistics.json"), "{}");
            }
        }

        private void SaveStatistics()
        {
            lock (_saveLock)
            {
                try
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(_statistics, options);
                    File.WriteAllText(Path.Combine(_taskPath, "statistics.json"), json);
                }
                catch (Exception ex)
                {
                    Log($"Ошибка при сохранении статистики: {ex.Message}");
                }
            }
        }

        public void UpdateLastDropTime(string accountName, DateTime dropTime)
        {
            if (string.IsNullOrEmpty(accountName)) return;

            var stats = GetAccountStatistics(accountName);
            stats.LastDropTime = dropTime;
            SaveStatistics();
        }

        public void AddDrop(string accountName, uint appId, string itemDefId)
        {
            var accountStats = GetOrCreateAccountStats(accountName);
            var dropKey = $"{appId}_{itemDefId}";
            
            if (!accountStats.Drops.ContainsKey(dropKey))
            {
                accountStats.Drops[dropKey] = 0;
            }
            
            accountStats.Drops[dropKey]++;
            accountStats.LastDropTime = DateTime.Now;
            accountStats.TotalDropsCount++;
            
            SaveStatistics();
            Log($"Статистика обновлена для {accountName} (всего дропов: {accountStats.TotalDropsCount})");
        }

        private AccountStatistics GetOrCreateAccountStats(string accountName)
        {
            if (!_statistics.ContainsKey(accountName))
            {
                _statistics[accountName] = new AccountStatistics
                {
                    AccountName = accountName
                };
            }
            
            return _statistics[accountName];
        }

        private void Log(string message)
        {
            try
            {
                var logPath = Path.Combine(_taskPath, "Logs", "statistics.log");
                var logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(logPath, logMessage);
            }
            catch
            {
                // Игнорируем ошибки логирования
            }
        }
    }
} 