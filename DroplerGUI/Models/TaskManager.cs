using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DroplerGUI.Core;
using DroplerGUI.Services;

namespace DroplerGUI.Models
{
    public class TaskManager
    {
        private readonly string _basePath;
        private readonly Dictionary<int, TaskInstance> _tasks;
        private readonly object _lock = new object();

        public IReadOnlyDictionary<int, TaskInstance> Tasks => _tasks;

        public TaskManager(string basePath)
        {
            _basePath = basePath;
            _tasks = new Dictionary<int, TaskInstance>();
            LoadExistingTasks();
        }

        private void LoadExistingTasks()
        {
            try
            {
                var taskDirectories = Directory.GetDirectories(_basePath)
                    .Where(d => Path.GetFileName(d).StartsWith("task_", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var dir in taskDirectories)
                {
                    try
                    {
                        var taskNumber = int.Parse(Path.GetFileName(dir).Split('_')[1]);
                        var task = new TaskInstance(taskNumber, _basePath);
                        _tasks[taskNumber] = task;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при загрузке задачи из директории {dir}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка при поиске существующих задач: {ex.Message}");
                throw;
            }
        }

        public TaskInstance CreateTask()
        {
            lock (_lock)
            {
                try
                {
                    var taskNumber = GetNextTaskNumber();
                    var task = new TaskInstance(taskNumber, _basePath);
                    _tasks[taskNumber] = task;
                    return task;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка при создании новой задачи: {ex.Message}");
                    throw;
                }
            }
        }

        private int GetNextTaskNumber()
        {
            return _tasks.Count > 0 ? _tasks.Keys.Max() + 1 : 1;
        }

        public void DeleteTask(int taskNumber)
        {
            lock (_lock)
            {
                if (_tasks.TryGetValue(taskNumber, out var task))
                {
                    try
                    {
                        task.Stop();
                        var taskPath = Path.Combine(_basePath, $"task_{taskNumber}");
                        if (Directory.Exists(taskPath))
                        {
                            Directory.Delete(taskPath, true);
                        }
                        _tasks.Remove(taskNumber);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка при удалении задачи {taskNumber}: {ex.Message}");
                        throw;
                    }
                }
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
                task.Start();
            }
        }

        public void StopAll()
        {
            foreach (var task in _tasks.Values)
            {
                task.Stop();
            }
        }
    }
} 