using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DroplerGUI.Models;
using DroplerGUI.Core;
using System.Linq;

namespace DroplerGUI.Services
{
    public class TaskLoggingService
    {
        private readonly ConcurrentQueue<LogMessage> _messageQueue;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly Task _processQueueTask;
        private readonly string _logPath;
        private readonly string _dropHistoryPath;
        private readonly int _taskId;
        private readonly object _logLock = new object();
        
        public Action<string> LogCallback { get; set; }

        public TaskLoggingService(string taskPath, int taskId)
        {
            _messageQueue = new ConcurrentQueue<LogMessage>();
            _cancellationTokenSource = new CancellationTokenSource();
            _taskId = taskId;
            _logPath = Constants.GetTaskLogsPath(taskId);
            _dropHistoryPath = Constants.GetTaskDropHistoryPath(taskId);
            
            // Запускаем обработку очереди сообщений
            _processQueueTask = Task.Run(ProcessMessageQueue);
        }

        public void Log(string message)
        {
            // Проверяем, содержит ли сообщение уже временную метку
            bool hasTimestamp = message.StartsWith("[") && message.Length > 10 && 
                               message[1..9].Replace(":", "").All(char.IsDigit) && 
                               message[9] == ']';
            
            var formattedMessage = hasTimestamp ? 
                $"[Task {_taskId}] {message}" : 
                $"[{DateTime.Now:HH:mm:ss}] [Task {_taskId}] {message}";
            
            // Добавляем сообщение в очередь для записи в файл
            _messageQueue.Enqueue(new LogMessage(formattedMessage));
            
            // Отправляем сообщение в UI
            LogCallback?.Invoke(formattedMessage);
        }

        private async Task ProcessMessageQueue()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    if (_messageQueue.TryDequeue(out var message))
                    {
                        await WriteToLogFile(message.Text);
                    }
                    else
                    {
                        await Task.Delay(100, _cancellationTokenSource.Token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // В случае ошибки записи в файл, пытаемся записать ошибку в лог
                    try
                    {
                        var errorMessage = $"[ERROR] Ошибка при записи в лог: {ex.Message}";
                        await WriteToLogFile(errorMessage);
                        LogCallback?.Invoke(errorMessage);
                    }
                    catch
                    {
                        // Игнорируем ошибки при записи ошибок
                    }
                }
            }
        }

        private async Task WriteToLogFile(string message)
        {
            var logFile = Path.Combine(_logPath, $"{DateTime.Now:yyyy-MM-dd}.log");
            var maxRetries = 3;
            var retryDelay = 100; // миллисекунды

            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    await using var fileStream = new FileStream(logFile, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    await using var writer = new StreamWriter(fileStream, Encoding.UTF8);
                    await writer.WriteLineAsync(message);
                    await writer.FlushAsync();
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    await Task.Delay(retryDelay * (i + 1));
                }
            }
        }

        public void Shutdown()
        {
            _cancellationTokenSource.Cancel();
            try
            {
                _processQueueTask.Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Игнорируем ошибки при завершении
            }
        }

        private class LogMessage
        {
            public string Text { get; }

            public LogMessage(string text)
            {
                Text = text;
            }
        }
    }
} 