using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using DroplerGUI.Models;
using DroplerGUI.ViewModels;
using Microsoft.Extensions.Logging;

namespace DroplerGUI.Services
{
    public class ScheduleManagerService : IDisposable
    {
        private readonly Dictionary<int, TaskViewModel> _taskViewModels;
        private readonly System.Timers.Timer _checkTimer;
        private readonly Dictionary<int, DateTime> _lastScheduleActionTime;
        private readonly Dictionary<int, bool> _manuallyStoppedTasks;
        private readonly ILogger<ScheduleManagerService> _logger;
        private readonly CancellationTokenSource _cancellationTokenSource;

        public ScheduleManagerService(Dictionary<int, TaskViewModel> taskViewModels, ILogger<ScheduleManagerService> logger)
        {
            _taskViewModels = taskViewModels;
            _logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();
            _lastScheduleActionTime = new Dictionary<int, DateTime>();
            _manuallyStoppedTasks = new Dictionary<int, bool>();

            _checkTimer = new System.Timers.Timer(60000); // Проверка раз в минуту
            _checkTimer.Elapsed += async (s, e) => await CheckSchedules();
            _checkTimer.Start();
            
            _logger.LogInformation("Сервис управления расписанием запущен");
        }

        private async Task CheckSchedules()
        {
            var currentTime = DateTime.Now;
            var currentTimeString = currentTime.ToString("HH:mm");
            _logger.LogDebug($"Проверка расписания в {currentTimeString} для {_taskViewModels.Count} потоков");

            foreach (var taskViewModel in _taskViewModels.Values)
            {
                var config = taskViewModel.Config;
                if (!config.UseSchedule) 
                {
                    _logger.LogTrace($"Поток {taskViewModel.TaskNumber} - расписание отключено");
                    continue;
                }

                var taskNumber = taskViewModel.TaskNumber;
                _logger.LogDebug($"Проверка расписания для потока {taskNumber}");

                try
                {
                    bool shouldStop = false;
                    bool shouldStart = false;

                    // Проверяем все условия остановки
                    if (config.Intervals?.Any(i => i.StopTime == currentTimeString) == true)
                    {
                        _logger.LogDebug($"Поток {taskNumber} - найден интервал остановки на {currentTimeString}");
                        shouldStop = true;
                    }
                    else if (config.SingleStopTimes?.Any(t => t.Time == currentTimeString) == true)
                    {
                        _logger.LogDebug($"Поток {taskNumber} - найдено единичное время остановки на {currentTimeString}");
                        shouldStop = true;
                    }
                    else if (config.OneTimeActions?.Any(a => 
                        a.DateTime.ToString("HH:mm") == currentTimeString && 
                        (a.DateTime - currentTime).TotalMinutes >= -1 &&
                        (a.DateTime - currentTime).TotalMinutes < 0 &&
                        a.ActionType == "Стоп") == true)
                    {
                        _logger.LogDebug($"Поток {taskNumber} - найдена разовая задача остановки на {currentTimeString}");
                        shouldStop = true;
                    }

                    // Проверяем все условия запуска
                    if (config.Intervals?.Any(i => i.StartTime == currentTimeString) == true)
                    {
                        _logger.LogDebug($"Поток {taskNumber} - найден интервал запуска на {currentTimeString}");
                        shouldStart = true;
                    }
                    else if (config.SingleStartTimes?.Any(t => t.Time == currentTimeString) == true)
                    {
                        _logger.LogDebug($"Поток {taskNumber} - найдено единичное время запуска на {currentTimeString}");
                        shouldStart = true;
                    }
                    else if (config.OneTimeActions?.Any(a => 
                        a.DateTime.ToString("HH:mm") == currentTimeString && 
                        (a.DateTime - currentTime).TotalMinutes >= -1 &&
                        (a.DateTime - currentTime).TotalMinutes < 0 &&
                        a.ActionType == "Старт") == true)
                    {
                        _logger.LogDebug($"Поток {taskNumber} - найдена разовая задача запуска на {currentTimeString}");
                        shouldStart = true;
                    }

                    if (shouldStop && taskViewModel.CanStop)
                    {
                        _logger.LogInformation($"Начинаю остановку задачи {taskNumber} по расписанию");
                        await taskViewModel.StopAsync();
                        
                        // Удаляем выполненные разовые задачи остановки
                        var executedOneTimeActions = config.OneTimeActions?
                            .Where(a => a.DateTime.ToString("HH:mm") == currentTimeString && 
                                   (a.DateTime - currentTime).TotalMinutes >= -1 &&
                                   (a.DateTime - currentTime).TotalMinutes < 0 && 
                                   a.ActionType == "Стоп")
                            .ToList();
                            
                        if (executedOneTimeActions?.Any() == true)
                        {
                            foreach (var action in executedOneTimeActions)
                            {
                                config.OneTimeActions.Remove(action);
                                _logger.LogInformation($"Удалена разовая задача остановки для потока {taskNumber} на {action.DateTime:HH:mm}");
                            }
                            config.Save(taskNumber);
                        }
                        
                        _logger.LogInformation($"Задача {taskNumber} успешно остановлена по расписанию");
                    }
                    else if (shouldStart && !taskViewModel.IsRunning)
                    {
                        _logger.LogInformation($"Начинаю запуск задачи {taskNumber} по расписанию");
                        await taskViewModel.StartAsync();
                        
                        // Удаляем выполненные разовые задачи запуска
                        var executedOneTimeActions = config.OneTimeActions?
                            .Where(a => a.DateTime.ToString("HH:mm") == currentTimeString && 
                                   (a.DateTime - currentTime).TotalMinutes >= -1 &&
                                   (a.DateTime - currentTime).TotalMinutes < 0 && 
                                   a.ActionType == "Старт")
                            .ToList();
                            
                        if (executedOneTimeActions?.Any() == true)
                        {
                            foreach (var action in executedOneTimeActions)
                            {
                                config.OneTimeActions.Remove(action);
                                _logger.LogInformation($"Удалена разовая задача запуска для потока {taskNumber} на {action.DateTime:HH:mm}");
                            }
                            config.Save(taskNumber);
                        }
                        
                        _logger.LogInformation($"Задача {taskNumber} успешно запущена по расписанию");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Ошибка при обработке расписания для задачи {taskNumber}");
                }
            }
        }

        public void RegisterManualStop(int taskNumber)
        {
            _manuallyStoppedTasks[taskNumber] = true;
            _logger.LogInformation($"Зарегистрирована ручная остановка для потока {taskNumber}");
        }

        public void Dispose()
        {
            Stop();
        }

        public void Stop()
        {
            _logger.LogInformation("Остановка сервиса управления расписанием");
            _checkTimer.Stop();
            _checkTimer.Dispose();
            _cancellationTokenSource.Cancel();
        }
    }
} 