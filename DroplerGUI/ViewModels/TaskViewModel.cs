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
using System.Threading.Tasks;

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
        public ScheduleConfig Config => ScheduleConfig.Load(TaskNumber);
        
        private bool _showDelete;
        public bool ShowDelete
        {
            get => _showDelete;
            private set
            {
                if (_showDelete != value)
                {
                    _showDelete = value;
                    OnPropertyChanged(nameof(ShowDelete));
                }
            }
        }
        
        private Models.TaskStatus _status;
        public Models.TaskStatus Status
        {
            get => _status;
            private set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
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
                    Models.TaskStatus.Stopped => Brushes.Gray,
                    Models.TaskStatus.Initializing => Brushes.Orange,
                    Models.TaskStatus.Running => Brushes.Green,
                    Models.TaskStatus.Stopping => Brushes.Orange,
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
        
        public bool CanStart => _taskInstance.Status == Models.TaskStatus.Stopped || _taskInstance.Status == Models.TaskStatus.Error;
        public bool CanStop => _taskInstance.Status == Models.TaskStatus.Running || _taskInstance.Status == Models.TaskStatus.Initializing;
        public bool CanDelete => _taskInstance.Status == Models.TaskStatus.Stopped && TaskNumber != 1;
        
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
            get => Status == Models.TaskStatus.Stopped;
        }
        
        public TaskViewModel(TaskInstance taskInstance, Action<TaskViewModel> onDelete)
        {
            _taskInstance = taskInstance;
            _onDelete = onDelete;
            _logs = new ObservableCollection<LogEntry>();
            
            StartCommand = new AsyncRelayCommand(StartAsync, () => CanStart);
            StopCommand = new AsyncRelayCommand(StopAsync, () => CanStop);
            DeleteCommand = new RelayCommand(Delete, () => CanDelete);
            
            _buttonText = "Запустить";
            _status = Models.TaskStatus.Stopped;
            _isRunning = false;
            
            // Подписываемся на логи
            _taskInstance.Worker.LogCallback = AddLogMessage;
            
            UpdateStatus();
            UpdateStatistics();
        }
        
        public async Task StartAsync()
        {
            try
            {
                _taskInstance.Start();
                UpdateStatus();
            }
            catch (Exception ex)
            {
                Status = Models.TaskStatus.Error;
                MessageBox.Show($"Ошибка при запуске потока: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public async Task StopAsync()
        {
            try
            {
                // Устанавливаем промежуточный статус
                Status = Models.TaskStatus.Stopping;
                
                // Ждем завершения остановки
                await _taskInstance.StopAsync();
                
                // Обновляем статус только после успешной остановки
                Status = Models.TaskStatus.Stopped;
                ShowDelete = true;
                
                // Обновляем UI
                UpdateStatus();
                UpdateStatistics();
                
                // Добавляем сообщение в лог
                AddLogMessage("Задача успешно остановлена");
            }
            catch (Exception ex)
            {
                Status = Models.TaskStatus.Error;
                AddLogMessage($"Ошибка при остановке задачи: {ex.Message}");
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
                Models.TaskStatus.Stopped => Models.TaskStatus.Stopped,
                Models.TaskStatus.Initializing => Models.TaskStatus.Initializing,
                Models.TaskStatus.Running => Models.TaskStatus.Running,
                Models.TaskStatus.Stopping => Models.TaskStatus.Stopping,
                Models.TaskStatus.Error => Models.TaskStatus.Error,
                _ => Models.TaskStatus.Unknown
            };
            
            ButtonText = _isRunning ? "Остановить" : "Запустить";
            
            // Обновляем состояние команд
            (StartCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
            (StopCommand as AsyncRelayCommand)?.RaiseCanExecuteChanged();
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

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return !_isExecuting && (_canExecute?.Invoke() ?? true);
        }

        public async void Execute(object parameter)
        {
            if (CanExecute(parameter))
            {
                try
                {
                    _isExecuting = true;
                    await _execute();
                }
                finally
                {
                    _isExecuting = false;
                }
            }
        }

        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
} 