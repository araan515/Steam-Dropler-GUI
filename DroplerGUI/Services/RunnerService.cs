using System;
using System.Threading.Tasks;
using DroplerGUI.Models;
using DroplerGUI.Core;

namespace DroplerGUI.Services
{
    public class RunnerService
    {
        private readonly Action<string> _logCallback;
        private bool _isRunning;
        private readonly TaskWorker _taskWorker;

        public RunnerService(Action<string> logCallback, TaskWorker taskWorker)
        {
            _logCallback = logCallback;
            _taskWorker = taskWorker;
            _isRunning = false;
        }

        public void LogMessage(string message)
        {
            _logCallback?.Invoke(message);
        }

        public async Task StartAsync()
        {
            try 
            {
                if (_isRunning)
                    return;

                _isRunning = true;
                
                // Запускаем процесс фарма
                _taskWorker.Start();
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _isRunning = false;
                _logCallback?.Invoke($"Ошибка при запуске фарма: {ex.Message}");
                throw;
            }
        }

        public async Task StopAsync()
        {
            if (!_isRunning)
                return;

            try
            {
                _isRunning = false;
                LogMessage("Остановка фарма...");
                
                // Останавливаем все процессы и ждем их завершения
                await Task.Run(() => 
                {
                    _taskWorker.Stop();
                });
                
                // Даем дополнительное время на завершение всех процессов
                await Task.Delay(3000);
            }
            catch (Exception ex)
            {
                LogMessage($"Ошибка при остановке фарма: {ex.Message}");
                throw;
            }
        }
    }
} 