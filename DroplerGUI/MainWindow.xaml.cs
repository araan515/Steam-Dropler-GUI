using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using DroplerGUI.Services;
using System.Windows.Threading;
using DroplerGUI.Models;
using System.Collections.Generic;
using System.ComponentModel;
using DroplerGUI.ViewModels;
using DroplerGUI.Core;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace DroplerGUI
{
    public partial class MainWindow : Window
    {
        private bool isClosing = false;
        private readonly DispatcherTimer _updateTimer;
        private readonly TaskManager _taskManager;
        private readonly ObservableCollection<TaskViewModel> _tasks;
        private readonly ScheduleManagerService _scheduleManager;
        private readonly ILogger<ScheduleManagerService> _logger;
        private Dictionary<int, StatisticsWindow> _statisticsWindows = new Dictionary<int, StatisticsWindow>();

        public MainWindow()
        {
            InitializeComponent();
            
            try
            {
                // Настраиваем логирование
                var serviceCollection = new ServiceCollection();
                serviceCollection.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.AddDebug();
                });
                var serviceProvider = serviceCollection.BuildServiceProvider();
                _logger = serviceProvider.GetRequiredService<ILogger<ScheduleManagerService>>();

                // Устанавливаем информацию о версии
                VersionInfoTextBlock.Text = Constants.GetVersionInfo();

                // Подписываемся на событие закрытия окна
                this.Closing += MainWindow_Closing;

                _taskManager = new TaskManager(Constants.AppDataPath);
                _tasks = new ObservableCollection<TaskViewModel>();
                
                TasksListView.ItemsSource = _tasks;
                
                // Создаем TaskViewModel для каждого таска
                foreach (var task in _taskManager.Tasks.Values)
                {
                    var taskViewModel = new TaskViewModel(task, OnTaskDelete);
                    _tasks.Add(taskViewModel);
                }

                // Инициализируем менеджер расписаний
                var taskViewModels = _tasks.ToDictionary(t => t.TaskNumber, t => t);
                _scheduleManager = new ScheduleManagerService(taskViewModels, _logger);

                // Устанавливаем менеджер расписаний в App
                if (Application.Current is App app)
                {
                    app.ScheduleManager = _scheduleManager;
                }

                // Инициализируем и запускаем таймер обновления
                _updateTimer = new DispatcherTimer();
                _updateTimer.Interval = TimeSpan.FromSeconds(1);
                _updateTimer.Tick += UpdateTimer_Tick;
                _updateTimer.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при инициализации: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // Обновляем статусы всех тасков
                foreach (var task in _tasks)
                {
                    task.UpdateStatus();
                    task.UpdateStatistics();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении статистики: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (isClosing) return;
            
            isClosing = true;
            e.Cancel = true;

            try
            {
                // Останавливаем таймер обновления
                _updateTimer.Stop();

                // Останавливаем менеджер расписаний
                _scheduleManager.Dispose();

                // Останавливаем все потоки
                _taskManager.StopAll();
                foreach (var task in _tasks)
                {
                    task.UpdateStatus();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при закрытии приложения: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Application.Current.Shutdown();
            }
        }

        private void AddTaskToList(TaskInstance taskInstance)
        {
            var taskViewModel = new TaskViewModel(taskInstance, OnTaskDelete);
            _tasks.Add(taskViewModel);
        }

        private void OnTaskDelete(TaskViewModel taskViewModel)
        {
            try
            {
                _taskManager.DeleteTask(taskViewModel.TaskNumber);
                _tasks.Remove(taskViewModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddTaskButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var taskInstance = _taskManager.CreateTask();
                AddTaskToList(taskInstance);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при создании нового потока: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var taskViewModel = (TaskViewModel)((FrameworkElement)button.Parent).DataContext;
            taskViewModel.ShowLogs = !taskViewModel.ShowLogs;
            button.Content = taskViewModel.ShowLogs ? "Скрыть логи" : "Показать логи";
        }

        private void ShowStatistics(int taskNumber)
        {
            if (!_statisticsWindows.ContainsKey(taskNumber))
            {
                var statisticsWindow = new StatisticsWindow(_taskManager);
                statisticsWindow.Owner = this;
                _statisticsWindows[taskNumber] = statisticsWindow;
                statisticsWindow.Closed += (s, e) => _statisticsWindows.Remove(taskNumber);
            }

            var window = _statisticsWindows[taskNumber];
            if (!window.IsVisible)
            {
                // Позиционируем окно статистики правее главного окна
                window.Left = this.Left + this.Width + 10;
                window.Top = this.Top;
                window.Show();
            }
            window.Activate();
        }
        
        private void StatisticsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var taskViewModel = (TaskViewModel)button.DataContext;
            var taskNumber = taskViewModel.TaskNumber;
            ShowStatistics(taskNumber);
        }
        
        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var taskViewModel = (TaskViewModel)button.DataContext;
            var taskNumber = taskViewModel.TaskNumber;

            var settingsWindow = new SettingsWindow(taskNumber);
            settingsWindow.Owner = this;
            settingsWindow.ShowDialog();
        }

        private void ScheduleButton_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var taskViewModel = (TaskViewModel)button.DataContext;
            var taskNumber = taskViewModel.TaskNumber;

            var scheduleWindow = new ScheduleWindow(taskNumber);
            scheduleWindow.Owner = this;
            scheduleWindow.ShowDialog();
        }

        private void LogMessage(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => LogMessage(message));
                return;
            }

            // Добавляем сообщение в лог всех тасков
            foreach (var taskViewModel in _tasks)
            {
                taskViewModel.AddLogMessage(message);
            }
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _taskManager.StartAll();
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting tasks: {ex.Message}");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _taskManager.StopAll();
            }
            catch (Exception ex)
            {
                LogMessage($"Error stopping tasks: {ex.Message}");
            }
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer == null) return;
            
            // Получаем TaskViewModel
            var taskViewModel = ((FrameworkElement)scrollViewer.TemplatedParent)?.DataContext as TaskViewModel;
            if (taskViewModel == null) return;

            // Если изменилась высота контента
            if (e.ExtentHeightChange > 0)
            {
                if (taskViewModel.AutoScroll)
                {
                    scrollViewer.ScrollToBottom();
                }
            }

            // Проверяем, находится ли скролл внизу
            bool isAtBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight;
            
            // Если пользователь прокрутил вверх, отключаем автопрокрутку
            if (e.VerticalChange < 0)
            {
                taskViewModel.AutoScroll = false;
            }
            // Если пользователь прокрутил в самый низ, включаем автопрокрутку
            else if (isAtBottom)
            {
                taskViewModel.AutoScroll = true;
            }
        }
    }
} 