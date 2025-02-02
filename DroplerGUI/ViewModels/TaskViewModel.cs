using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using DroplerGUI.Models;
using DroplerGUI.Core;

namespace DroplerGUI.ViewModels
{
    public class TaskViewModel : INotifyPropertyChanged
    {
        private readonly TaskInstance _taskInstance;
        private readonly Action<TaskViewModel> _onDelete;
        private readonly ObservableCollection<LogEntry> _logs;
        
        public TaskInstance TaskInstance => _taskInstance;
        public int TaskNumber => _taskInstance.TaskNumber;
        public ObservableCollection<LogEntry> Logs => _logs;
        
        private string _status;
        public string Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(CanStart));
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(CanDelete));
                    OnPropertyChanged(nameof(IsSettingsEnabled));
                }
            }
        }
        
        public Brush StatusColor
        {
            get
            {
                return Status switch
                {
                    "Остановлен" => Brushes.Gray,
                    "Запускается" => Brushes.Orange,
                    "Работает" => Brushes.Green,
                    "Останавливается" => Brushes.Orange,
                    _ => Brushes.Red
                };
            }
        }
        
        private double _progress;
        public double Progress
        {
            get => _progress;
            set
            {
                if (_progress != value)
                {
                    _progress = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private int _totalAccounts;
        public int TotalAccounts
        {
            get => _totalAccounts;
            set
            {
                if (_totalAccounts != value)
                {
                    _totalAccounts = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _enabledAccounts;
        public int EnabledAccounts
        {
            get => _enabledAccounts;
            set
            {
                if (_enabledAccounts != value)
                {
                    _enabledAccounts = value;
                    OnPropertyChanged();
                }
            }
        }

        private int _farmingAccounts;
        public int FarmingAccounts
        {
            get => _farmingAccounts;
            set
            {
                if (_farmingAccounts != value)
                {
                    _farmingAccounts = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _nextStart = "-";
        public string NextStart
        {
            get => _nextStart;
            set
            {
                if (_nextStart != value)
                {
                    _nextStart = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool CanStart => _taskInstance.Status == TaskStatus.Stopped || _taskInstance.Status == TaskStatus.Error;
        public bool CanStop => _taskInstance.Status == TaskStatus.Running || _taskInstance.Status == TaskStatus.Initializing;
        public bool CanDelete => _taskInstance.Status == TaskStatus.Stopped && TaskNumber != 1;
        public Visibility ShowDelete => TaskNumber == 1 ? Visibility.Collapsed : Visibility.Visible;
        
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand DeleteCommand { get; }
        
        private bool _showLogs;
        public bool ShowLogs
        {
            get => _showLogs;
            set
            {
                if (_showLogs != value)
                {
                    _showLogs = value;
                    OnPropertyChanged();

                    if (value) // Если логи открываются
                    {
                        AutoScroll = true; // Включаем автопрокрутку при открытии логов
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            // Даем время на отрисовку UI
                            System.Threading.Thread.Sleep(100);

                            // Находим ScrollViewer для текущего потока
                            if (Application.Current.MainWindow is MainWindow mainWindow)
                            {
                                var itemsControl = mainWindow.TasksListView;
                                var container = itemsControl.ItemContainerGenerator.ContainerFromItem(this) as FrameworkElement;
                                if (container != null)
                                {
                                    var logScrollViewer = container.FindName("LogScrollViewer") as ScrollViewer;
                                    logScrollViewer?.ScrollToBottom();
                                }
                            }
                        }));
                    }
                }
            }
        }
        
        private string _buttonText;
        public string ButtonText
        {
            get => _buttonText;
            private set
            {
                if (_buttonText != value)
                {
                    _buttonText = value;
                    OnPropertyChanged();
                }
            }
        }
        
        private bool _isRunning;
        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set
            {
                if (_autoScroll != value)
                {
                    _autoScroll = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsRunning
        {
            get => _isRunning;
            private set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ButtonText));
                    OnPropertyChanged(nameof(Status));
                }
            }
        }
        
        public bool IsSettingsEnabled
        {
            get => Status == "Остановлен";
        }
        
        public TaskViewModel(TaskInstance taskInstance, Action<TaskViewModel> onDelete)
        {
            _taskInstance = taskInstance;
            _onDelete = onDelete;
            _logs = new ObservableCollection<LogEntry>();
            
            StartCommand = new RelayCommand(Start, () => CanStart);
            StopCommand = new RelayCommand(Stop, () => CanStop);
            DeleteCommand = new RelayCommand(Delete, () => CanDelete);
            
            _buttonText = "Запустить";
            _status = "Остановлен";
            _isRunning = false;
            
            // Подписываемся на логи
            _taskInstance.Worker.LogCallback = AddLogMessage;
            
            UpdateStatus();
            UpdateStatistics();
        }
        
        public void Start()
        {
            try
            {
                _taskInstance.Start();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Status = "Ошибка";
                MessageBox.Show($"Ошибка при запуске потока: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Stop()
        {
            try
            {
                _taskInstance.Stop();
                UpdateStatus();
                UpdateStatistics();
            }
            catch (Exception ex)
            {
                Status = "Ошибка";
                MessageBox.Show($"Ошибка при остановке потока: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void Delete()
        {
            _onDelete?.Invoke(this);
        }
        
        public void UpdateStatus()
        {
            var isRunning = _taskInstance.IsRunning;
            var taskStatus = _taskInstance.Status;
            
            _isRunning = isRunning;
            
            Status = taskStatus switch
            {
                TaskStatus.Stopped => "Остановлен",
                TaskStatus.Initializing => "Запускается",
                TaskStatus.Running => "Работает",
                TaskStatus.Stopping => "Останавливается",
                TaskStatus.Error => "Ошибка",
                _ => "Неизвестно"
            };
            
            ButtonText = _isRunning ? "Остановить" : "Запустить";
            
            // Обновляем состояние команд
            (StartCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }
        
        public void UpdateStatistics()
        {
            TotalAccounts = _taskInstance.TotalAccounts;
            EnabledAccounts = _taskInstance.EnabledAccounts;
            FarmingAccounts = _taskInstance.FarmingAccounts;
            NextStart = _taskInstance.NextStart;
        }
        
        public void AddLogMessage(string message)
        {
            var logEntry = new LogEntry(message);
            Application.Current.Dispatcher.Invoke(() =>
            {
                Logs.Add(logEntry);
                while (Logs.Count > 1000)
                {
                    Logs.RemoveAt(0);
                }

                if (AutoScroll)
                {
                    OnPropertyChanged(nameof(Logs));
                }
            });
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 