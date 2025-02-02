using System;
using System.Linq;
using System.Windows;
using System.IO;
using System.Text;
using Microsoft.Win32;
using DroplerGUI.Services;
using DroplerGUI.Core;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using DroplerGUI.Models;

namespace DroplerGUI
{
    public partial class StatisticsWindow : Window
    {
        private readonly TaskManager _taskManager;
        private readonly DispatcherTimer _updateTimer;
        private readonly ObservableCollection<AccountStatistics> _statistics;
        private int _selectedTaskNumber = 1;

        public StatisticsWindow(TaskManager taskManager)
        {
            InitializeComponent();
            _taskManager = taskManager;
            _statistics = new ObservableCollection<AccountStatistics>();
            StatisticsGrid.ItemsSource = _statistics;

            // Инициализация ComboBox для выбора таска
            TaskComboBox.ItemsSource = _taskManager.Tasks.Keys;
            TaskComboBox.SelectedItem = _selectedTaskNumber;

            // Инициализация таймера обновления
            _updateTimer = new DispatcherTimer();
            _updateTimer.Tick += UpdateTimer_Tick;
            UpdateInterval();

            // Загружаем начальные данные
            RefreshStatistics();

            // Подписываемся на событие закрытия окна
            Closed += StatisticsWindow_Closed;
        }

        private void UpdateInterval()
        {
            if (int.TryParse(UpdateIntervalTextBox.Text, out int seconds) && seconds > 0)
            {
                _updateTimer.Interval = TimeSpan.FromSeconds(seconds);
                _updateTimer.Start();
            }
            else
            {
                MessageBox.Show("Введите корректный интервал обновления (в секундах)", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateIntervalTextBox.Text = "5";
                _updateTimer.Interval = TimeSpan.FromSeconds(5);
                _updateTimer.Start();
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            RefreshStatistics();
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            UpdateInterval();
            RefreshStatistics();
        }

        private void TaskComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (TaskComboBox.SelectedItem != null)
            {
                _selectedTaskNumber = (int)TaskComboBox.SelectedItem;
                RefreshStatistics();
            }
        }

        private void RefreshStatistics()
        {
            try
            {
                _statistics.Clear();
                if (_taskManager.Tasks.TryGetValue(_selectedTaskNumber, out var task))
                {
                    foreach (var stat in task.StatisticsService.GetAllStatistics())
                    {
                        // Обновляем время в сети для активных аккаунтов
                        if (stat.IsActive == "Online" && stat.LastConnectionTime.HasValue)
                        {
                            var onlineTime = DateTime.Now - stat.LastConnectionTime.Value;
                            stat.OnlineTime = $"{onlineTime.Hours:D2}:{onlineTime.Minutes:D2}:{onlineTime.Seconds:D2}";
                        }
                        else
                        {
                            stat.OnlineTime = "-";
                        }
                        _statistics.Add(stat);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при обновлении статистики: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StatisticsWindow_Closed(object sender, EventArgs e)
        {
            _updateTimer.Stop();
        }

        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new SaveFileDialog
                {
                    Filter = "CSV files (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    FileName = $"statistics_task{_selectedTaskNumber}_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    if (_taskManager.Tasks.TryGetValue(_selectedTaskNumber, out var task))
                    {
                        var statistics = task.StatisticsService.GetAllStatistics().ToList();
                        var csv = new StringBuilder();
                        
                        // Заголовки
                        csv.AppendLine("Аккаунт,Всего дропов,Последний дроп,Статус,Последнее подключение");

                        // Данные
                        foreach (var stat in statistics)
                        {
                            csv.AppendLine($"{stat.AccountName}," +
                                         $"{stat.TotalDropsCount}," +
                                         $"{stat.LastDropTime:dd.MM.yyyy HH:mm:ss}," +
                                         $"{stat.IsActive}," +
                                         $"{stat.LastConnectionTime:dd.MM.yyyy HH:mm:ss}");
                        }

                        File.WriteAllText(saveFileDialog.FileName, csv.ToString(), Encoding.UTF8);
                        MessageBox.Show("Статистика успешно экспортирована!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка при экспорте статистики: {ex.Message}");
                MessageBox.Show($"Ошибка при экспорте статистики: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 