using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DroplerGUI.Core;

namespace DroplerGUI.Models
{
    public class TaskManager
    {
        private readonly Dictionary<int, TaskInstance> _tasks = new();
        private readonly string _appDataPath;
        
        public IReadOnlyDictionary<int, TaskInstance> Tasks => _tasks;
        
        public TaskManager(string appDataPath)
        {
            _appDataPath = appDataPath;
            LoadTasks();
        }
        
        private void LoadTasks()
        {
            var taskDirs = Directory.GetDirectories(_appDataPath, "task_*");
            foreach (var dir in taskDirs)
            {
                try
                {
                    var dirName = Path.GetFileName(dir);
                    if (int.TryParse(dirName.Replace("task_", ""), out int taskNumber))
                    {
                        var task = new TaskInstance(taskNumber, _appDataPath);
                        _tasks[taskNumber] = task;
                    }
                }
                catch (Exception ex)
                {
                    // _logger.Log($"Ошибка при загрузке таска {Path.GetFileName(dir)}: {ex.Message}");
                }
            }
        }
        
        public TaskInstance CreateTask()
        {
            var nextTaskNumber = 1;
            while (_tasks.ContainsKey(nextTaskNumber))
            {
                nextTaskNumber++;
            }
            
            var task = new TaskInstance(nextTaskNumber, _appDataPath);
            _tasks[nextTaskNumber] = task;
            return task;
        }
        
        public void DeleteTask(int taskNumber)
        {
            if (!_tasks.ContainsKey(taskNumber))
            {
                throw new ArgumentException($"Таск {taskNumber} не существует");
            }
            
            var task = _tasks[taskNumber];
            task.Stop();
            _tasks.Remove(taskNumber);
            
            try
            {
                var taskPath = Path.Combine(_appDataPath, $"task_{taskNumber}");
                if (Directory.Exists(taskPath))
                {
                    Directory.Delete(taskPath, true);
                }
            }
            catch (Exception ex)
            {
                // _logger.Log($"Ошибка при удалении директории таска {taskNumber}: {ex.Message}");
                throw;
            }
        }
        
        public TaskInstance GetTask(int taskNumber)
        {
            if (_tasks.TryGetValue(taskNumber, out var task))
            {
                return task;
            }
            throw new InvalidOperationException($"Таск с номером {taskNumber} не найден");
        }
        
        public void StartAll()
        {
            foreach (var task in _tasks.Values)
            {
                if (!task.IsRunning)
                {
                    task.Start();
                    // Ждем пока первый аккаунт начнет подключаться
                    System.Threading.Thread.Sleep(5000);
                }
            }
        }
        
        public void StopAll()
        {
            foreach (var task in _tasks.Values)
            {
                try
                {
                    task.Stop();
                }
                catch
                {
                    // Игнорируем ошибки при остановке отдельных тасков
                }
            }
        }
    }
} 