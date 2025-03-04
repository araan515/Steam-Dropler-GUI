using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using DroplerGUI.Models;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace DroplerGUI
{
    public partial class ScheduleWindow : Window
    {
        private readonly int _taskNumber;
        private readonly ScheduleConfig _config;
        private static readonly Regex TimeRegex = new Regex(@"^([0-1]?[0-9]|2[0-3]):[0-5][0-9]$");
        private ObservableCollection<TimeInterval> _intervals;
        private ObservableCollection<SingleTimeAction> _singleStartTimes;
        private ObservableCollection<SingleTimeAction> _singleStopTimes;
        private ObservableCollection<OneTimeAction> _oneTimeActions;

        public ScheduleWindow(int taskNumber)
        {
            InitializeComponent();
            _taskNumber = taskNumber;
            _config = ScheduleConfig.Load(taskNumber);
            _intervals = new ObservableCollection<TimeInterval>(_config.Intervals);
            _singleStartTimes = new ObservableCollection<SingleTimeAction>(_config.SingleStartTimes);
            _singleStopTimes = new ObservableCollection<SingleTimeAction>(_config.SingleStopTimes);
            _oneTimeActions = new ObservableCollection<OneTimeAction>(_config.OneTimeActions);

            // Инициализация UI
            UseScheduleCheckBox.IsChecked = _config.UseSchedule;
            IntervalsListView.ItemsSource = _intervals;
            SingleStartTimesListView.ItemsSource = _singleStartTimes;
            SingleStopTimesListView.ItemsSource = _singleStopTimes;
            OneTimeActionsListView.ItemsSource = _oneTimeActions;

            // Устанавливаем текущую дату
            OneTimeDatePicker.SelectedDate = DateTime.Today;
            OneTimeTextBox.Text = DateTime.Now.ToString("HH:mm");
            OneTimeActionType.SelectedIndex = 0;

            // Добавляем обработчики для валидации ввода времени
            NewStartTimeTextBox.TextChanged += TimeTextBox_TextChanged;
            NewStopTimeTextBox.TextChanged += TimeTextBox_TextChanged;
            OneTimeTextBox.TextChanged += TimeTextBox_TextChanged;
            SingleStartTimeTextBox.TextChanged += TimeTextBox_TextChanged;
            SingleStopTimeTextBox.TextChanged += TimeTextBox_TextChanged;
        }

        private void TimeTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = (TextBox)sender;
            var text = textBox.Text;

            // Если текст пустой, не делаем ничего
            if (string.IsNullOrEmpty(text))
                return;

            // Если введен один символ, добавляем двоеточие после часов
            if (text.Length == 2 && !text.Contains(":"))
            {
                textBox.Text = text + ":";
                textBox.CaretIndex = 3;
                return;
            }

            // Проверяем формат времени
            if (!TimeRegex.IsMatch(text))
            {
                // Если формат неверный, оставляем только цифры и двоеточие
                var cleanText = new string(text.Where(c => char.IsDigit(c) || c == ':').ToArray());
                if (cleanText != text)
                {
                    textBox.Text = cleanText;
                    textBox.CaretIndex = cleanText.Length;
                }
            }
        }

        private void AddInterval_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var startTime = NewStartTimeTextBox.Text.Trim();
                var stopTime = NewStopTimeTextBox.Text.Trim();

                if (string.IsNullOrEmpty(startTime) || string.IsNullOrEmpty(stopTime))
                {
                    MessageBox.Show("Пожалуйста, введите время начала и окончания.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TimeSpan.TryParse(startTime, out _) || !TimeSpan.TryParse(stopTime, out _))
                {
                    MessageBox.Show("Неверный формат времени. Используйте формат ЧЧ:мм", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _intervals.Add(new TimeInterval { StartTime = startTime, StopTime = stopTime });
                NewStartTimeTextBox.Clear();
                NewStopTimeTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении интервала: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddSingleStartTime_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var time = SingleStartTimeTextBox.Text.Trim();

                if (string.IsNullOrEmpty(time))
                {
                    MessageBox.Show("Пожалуйста, введите время.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TimeSpan.TryParse(time, out _))
                {
                    MessageBox.Show("Неверный формат времени. Используйте формат ЧЧ:мм", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _singleStartTimes.Add(new SingleTimeAction(time));
                SingleStartTimeTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении времени старта: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddSingleStopTime_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var time = SingleStopTimeTextBox.Text.Trim();

                if (string.IsNullOrEmpty(time))
                {
                    MessageBox.Show("Пожалуйста, введите время.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TimeSpan.TryParse(time, out _))
                {
                    MessageBox.Show("Неверный формат времени. Используйте формат ЧЧ:мм", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _singleStopTimes.Add(new SingleTimeAction(time));
                SingleStopTimeTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении времени остановки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteInterval_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is TimeInterval interval)
            {
                _intervals.Remove(interval);
            }
        }

        private void DeleteSingleStartTime_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SingleTimeAction time)
            {
                _singleStartTimes.Remove(time);
            }
        }

        private void DeleteSingleStopTime_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SingleTimeAction time)
            {
                _singleStopTimes.Remove(time);
            }
        }

        private void AddOneTimeAction_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedDate = OneTimeDatePicker.SelectedDate;
                var time = OneTimeTextBox.Text.Trim();
                var actionType = ((ComboBoxItem)OneTimeActionType.SelectedItem).Content.ToString();

                if (!selectedDate.HasValue)
                {
                    MessageBox.Show("Пожалуйста, выберите дату.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(time))
                {
                    MessageBox.Show("Пожалуйста, введите время.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!TimeSpan.TryParse(time, out var timeSpan))
                {
                    MessageBox.Show("Неверный формат времени. Используйте формат ЧЧ:мм", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dateTime = selectedDate.Value.Date.Add(timeSpan);

                // Проверяем, что выбранное время в будущем
                if (dateTime <= DateTime.Now)
                {
                    MessageBox.Show("Выбранное время должно быть в будущем.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _oneTimeActions.Add(new OneTimeAction(dateTime, actionType));
                OneTimeTextBox.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при добавлении разовой задачи: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteOneTimeAction_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is OneTimeAction action)
            {
                _oneTimeActions.Remove(action);
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _config.UseSchedule = UseScheduleCheckBox.IsChecked ?? false;
                _config.Intervals = _intervals.ToList();
                _config.SingleStartTimes = _singleStartTimes.ToList();
                _config.SingleStopTimes = _singleStopTimes.ToList();
                _config.OneTimeActions = _oneTimeActions.ToList();
                _config.Save(_taskNumber);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении расписания: {ex.Message}", 
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 