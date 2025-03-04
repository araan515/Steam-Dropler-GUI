using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Linq;
using DroplerGUI.Models;
using DroplerGUI.Core;
using System.IO;

namespace DroplerGUI
{
    public class DropConfigItem
    {
        public uint Item1 { get; set; }
        public string Item2 { get; set; }

        public DropConfigItem(uint item1, string item2)
        {
            Item1 = item1;
            Item2 = item2;
        }
    }

    public partial class SettingsWindow : Window
    {
        private readonly int _taskNumber;
        private MainConfig Config { get; set; }
        private MainConfig tempConfig;
        private ObservableCollection<DropConfigItem> dropConfigs;

        public SettingsWindow()
        {
            InitializeComponent();
            _taskNumber = 1; // По умолчанию используем первый поток
            LoadConfig();
        }

        public SettingsWindow(int taskNumber)
        {
            InitializeComponent();
            _taskNumber = taskNumber;
            LoadConfig();
        }

        private void LoadConfig()
        {
            try
            {
                Config = MainConfig.GetConfig(_taskNumber);
                tempConfig = Config.Clone();
                DataContext = this;
                LoadSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке конфигурации: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSettings()
        {
            try
            {
                // Заполняем поля формы
                StartTimeOutTextBox.Text = tempConfig.StartTimeOut.ToString();
                ParallelCountTextBox.Text = tempConfig.ParallelCount.ToString();
                IdleTimeTextBox.Text = tempConfig.TimeConfig.IdleTime.ToString();
                PauseBeatwinIdleTimeTextBox.Text = tempConfig.TimeConfig.PauseBeatwinIdleTime.ToString();
                ChkIdleTimeOutTextBox.Text = tempConfig.ChkIdleTimeOut.ToString();

                // Инициализируем коллекцию для дропов
                dropConfigs = new ObservableCollection<DropConfigItem>(
                    tempConfig.DropConfig.Select(x => new DropConfigItem(x.Item1, x.Item2))
                );
                DropConfigList.ItemsSource = dropConfigs;
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при загрузке настроек: {ex.Message}");
            }
        }

        private void DeleteDropConfig_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var dropConfig = (DropConfigItem)button.DataContext;
            dropConfigs.Remove(dropConfig);
        }

        private void AddDropConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(NewGameIdTextBox.Text) || string.IsNullOrWhiteSpace(NewDropIdTextBox.Text))
                {
                    MessageBox.Show("Введите ID игры и ID дропа", "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!uint.TryParse(NewGameIdTextBox.Text, out uint gameId))
                {
                    MessageBox.Show("ID игры должен быть положительным числом", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                dropConfigs.Add(new DropConfigItem(gameId, NewDropIdTextBox.Text));
                NewGameIdTextBox.Clear();
                NewDropIdTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении конфигурации дропа: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(StartTimeOutTextBox.Text, out int startTimeOut) || startTimeOut <= 0)
                    throw new Exception("Некорректное значение интервала запуска");

                if (!int.TryParse(ParallelCountTextBox.Text, out int parallelCount) || parallelCount <= 0)
                    throw new Exception("Некорректное значение количества параллельных задач");

                if (!int.TryParse(IdleTimeTextBox.Text, out int idleTime) || idleTime <= 0)
                    throw new Exception("Некорректное значение времени фарма");

                if (!int.TryParse(PauseBeatwinIdleTimeTextBox.Text, out int pauseTime) || pauseTime <= 0)
                    throw new Exception("Некорректное значение паузы между фармом");

                if (!int.TryParse(ChkIdleTimeOutTextBox.Text, out int chkIdleTimeOut) || chkIdleTimeOut <= 0)
                    throw new Exception("Некорректное значение интервала проверки дропа");

                // Применяем изменения к временной конфигурации
                tempConfig.StartTimeOut = startTimeOut;
                tempConfig.ParallelCount = parallelCount;
                tempConfig.TimeConfig.IdleTime = idleTime;
                tempConfig.TimeConfig.PauseBeatwinIdleTime = pauseTime;
                tempConfig.ChkIdleTimeOut = chkIdleTimeOut;

                // Сохраняем конфигурацию дропов
                tempConfig.DropConfig = dropConfigs.Select(x => (x.Item1, x.Item2)).ToList();

                // Сохраняем конфигурацию
                var configDir = Constants.GetTaskConfigPath(_taskNumber);
                Directory.CreateDirectory(configDir);
                var configPath = Path.Combine(configDir, "MainConfig.json");
                tempConfig.Save(configPath);

                // Очищаем кэш конфигурации
                MainConfig.ClearCache(_taskNumber);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Очищаем кэш конфигурации
            MainConfig.ClearCache(_taskNumber);
            
            DialogResult = false;
            Close();
        }
    }

    public class TimeRangeDialog : Window
    {
        public TimeSpan StartTime { get; private set; }
        public TimeSpan EndTime { get; private set; }

        public TimeRangeDialog()
        {
            Title = "Добавить интервал";
            Width = 300;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Время начала
            var startLabel = new Label { Content = "Время запуска:" };
            var startPicker = new System.Windows.Controls.DatePicker();
            var startTime = new System.Windows.Controls.TextBox { Text = "00:00" };
            
            Grid.SetRow(startLabel, 0);
            grid.Children.Add(startLabel);

            var startPanel = new StackPanel { Orientation = Orientation.Horizontal };
            startPanel.Children.Add(startPicker);
            startPanel.Children.Add(startTime);
            Grid.SetRow(startPanel, 1);
            grid.Children.Add(startPanel);

            // Время окончания
            var endLabel = new Label { Content = "Время остановки:" };
            var endPicker = new System.Windows.Controls.DatePicker();
            var endTime = new System.Windows.Controls.TextBox { Text = "00:00" };
            
            Grid.SetRow(endLabel, 2);
            grid.Children.Add(endLabel);

            var endPanel = new StackPanel { Orientation = Orientation.Horizontal };
            endPanel.Children.Add(endPicker);
            endPanel.Children.Add(endTime);
            Grid.SetRow(endPanel, 3);
            grid.Children.Add(endPanel);

            // Кнопки
            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonPanel, 4);

            var okButton = new Button 
            { 
                Content = "OK",
                Width = 75,
                Margin = new Thickness(0, 0, 5, 0)
            };
            okButton.Click += (s, e) =>
            {
                if (TimeSpan.TryParse(startTime.Text, out var start) &&
                    TimeSpan.TryParse(endTime.Text, out var end))
                {
                    StartTime = start;
                    EndTime = end;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Неверный формат времени. Используйте формат ЧЧ:ММ", 
                        "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            var cancelButton = new Button 
            { 
                Content = "Отмена",
                Width = 75
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;
            Padding = new Thickness(10);
        }
    }
} 