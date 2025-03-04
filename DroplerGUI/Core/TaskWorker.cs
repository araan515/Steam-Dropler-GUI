using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using System.Threading;
using System.Reflection;
using Newtonsoft.Json;
using DroplerGUI.Models;
using DroplerGUI.Services;
using DroplerGUI.Services.Steam;
using SteamKit2;
using System.Collections.Concurrent;

namespace DroplerGUI.Core
{
    public class TaskWorker
    {
        // Статические компоненты
        private static readonly object _staticLogLock = new object();
        private static Dictionary<uint, Dictionary<string, string>> _gameDB = new Dictionary<uint, Dictionary<string, string>>();
        private static Dictionary<string, Task> _taskDictionary = new Dictionary<string, Task>();
        private static DateTime? _lastLoginAttempt;
        private static readonly object _lastLoginLock = new object();
        
        // Публичные статические свойства
        public static Dictionary<uint, Dictionary<string, string>> GameDB => _gameDB;
        public static Dictionary<string, Task> TaskDictionary => _taskDictionary;
        
        // Существующие поля экземпляра
        private readonly string _taskPath;
        private readonly string _accountPath;
        private readonly object _logLock = new object();
        private readonly StatisticsService _statisticsService;
        private readonly TaskLoggingService _logger;
        private readonly int _taskId;
        private MainConfig _config;
        
        // Таймеры
        private System.Timers.Timer _counterCheckTimer;
        private System.Timers.Timer _timer;
        private System.Timers.Timer _scheduleTimer;
        
        // Токен отмены
        private CancellationTokenSource _cancellationTokenSource;
        
        private readonly object _activeCountLock = new object();
        private int _activeAccountsCount = 0;
        private readonly Dictionary<string, SteamMachine> _activeMachines = new Dictionary<string, SteamMachine>();
        private TaskInstance _taskInstance;
        
        // Минимальное значение для StartTimeOut
        private const int MIN_START_TIMEOUT = 10;

        private readonly object _initLock = new object();
        private bool _isInitialized = false;
        private bool _isRunning = false;

        // Коллекции
        private HashSet<AccountConfig> _accounts;
        
        private static List<TaskInstance> TaskInstances = new List<TaskInstance>();

        // Добавляем объект для синхронизации
        private readonly object _accountsLock = new object();

        // Коллекции для управления аккаунтами
        private readonly ConcurrentDictionary<string, AccountConfig> _allAccounts;
        private readonly ConcurrentQueue<AccountConfig> _accountQueue;
        private readonly ConcurrentDictionary<string, AccountConfig> _activeAccounts;
        
        // Обновляем свойство для доступа к аккаунтам
        public IEnumerable<AccountConfig> Accounts => _allAccounts.Values;
        public MainConfig Config => _config;
        public int ActiveAccountsCount => _activeAccounts.Count;

        private DateTime _lastAccountStartTime = DateTime.MinValue;

        public void SetTaskInstance(TaskInstance taskInstance)
        {
            _taskInstance = taskInstance;
            // Удаляем старый экземпляр, если он существует
            TaskInstances.RemoveAll(t => t.TaskNumber == taskInstance.TaskNumber);
            // Добавляем новый экземпляр
            TaskInstances.Add(taskInstance);
        }

        public Action<string> LogCallback
        {
            get => _logger.LogCallback;
            set => _logger.LogCallback = value;
        }

        // Статические методы
        public static void UpdateDropCheckIntervals()
        {
            lock (_staticLogLock)
            {
                foreach (var machine in TaskInstances.SelectMany(t => t.Worker._activeMachines.Values))
                {
                    machine.UpdateDropCheckInterval();
                }
            }
        }

        public static void HandleDrop(AccountConfig account, uint appId, string itemDefId)
        {
            try
            {
                var taskNumber = int.Parse(account.GetTaskPath().Split('_').Last());
                var dropHistoryPath = Constants.GetTaskDropHistoryPath(taskNumber);
                
                var dropFile = Path.Combine(dropHistoryPath, $"{account.Name}.txt");
                var dropInfo = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Получен дроп для игры {appId} (itemdefid: {itemDefId})";
                File.AppendAllText(dropFile, dropInfo + Environment.NewLine);

                foreach (var task in TaskInstances)
                {
                    if (task.Worker._allAccounts.ContainsKey(account.Name.ToLower()))
                    {
                        task.StatisticsService.AddDrop(account.Name, appId, itemDefId);
                        task.Worker.Log($"[SUCCESS] [{account.Alias}] Получен дроп для игры {appId} (itemdefid: {itemDefId})");
                        break;
                    }
                }

                if (!TaskInstances.Any())
                {
                    File.AppendAllText(dropFile, "[ERROR] TaskInstances список пуст!" + Environment.NewLine);
                }
                else if (!TaskInstances.Any(t => t.Worker._allAccounts.ContainsKey(account.Name.ToLower())))
                {
                    File.AppendAllText(dropFile, $"[ERROR] Аккаунт {account.Name} не найден ни в одном из тасков!" + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                var errorFile = Path.Combine(Constants.GetTaskPath(int.Parse(account.GetTaskPath().Split('_').Last())), "Logs", "errors.log");
                File.AppendAllText(errorFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Ошибка при обработке дропа: {ex.Message}" + Environment.NewLine);
            }
        }

        public TaskWorker(string taskPath, StatisticsService statisticsService, int taskId)
        {
            _taskId = taskId;
            _taskPath = Constants.GetTaskPath(taskId);
            _accountPath = Path.Combine(Constants.GetTaskPath(taskId), "Accounts");
            _statisticsService = statisticsService;
            _logger = new TaskLoggingService(_taskPath, taskId);
            _config = MainConfig.GetConfig(taskId);
            
            // Загружаем конфигурацию расписания
            if (_config.Schedule == null)
            {
                _config.Schedule = ScheduleConfig.Load(taskId);
            }
            
            // Инициализируем коллекции
            _accounts = new HashSet<AccountConfig>(new AccountConfigComparer());
            _allAccounts = new ConcurrentDictionary<string, AccountConfig>(StringComparer.OrdinalIgnoreCase);
            _accountQueue = new ConcurrentQueue<AccountConfig>();
            _activeAccounts = new ConcurrentDictionary<string, AccountConfig>(StringComparer.OrdinalIgnoreCase);
            
            Initialize();
        }

        private void Initialize()
        {
            _timer = new System.Timers.Timer();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public void Log(string message)
        {
            _logger.Log(message);
        }

        public void Start()
        {
            if (_isRunning)
                return;

            try
            {
                _config = MainConfig.GetConfig(_taskId);
                
                // Проверяем и загружаем конфигурацию расписания
                if (_config.Schedule == null)
                {
                    _config.Schedule = ScheduleConfig.Load(_taskId);
                    Log("Загружена конфигурация расписания");
                }

                // Сбрасываем флаги для всех аккаунтов при старте
                foreach (var account in _allAccounts.Values)
                {
                    account.IdleNow = false;
                    account.IsRunning = false;
                    account.Action = "none";
                    account.TaskID = null;
                    account.Save();
                }

                // Проверяем и корректируем StartTimeOut
                if (_config.StartTimeOut < MIN_START_TIMEOUT)
                {
                    Log($"StartTimeOut установлен меньше минимального значения ({MIN_START_TIMEOUT} сек). Устанавливаю {MIN_START_TIMEOUT} сек.");
                    _config.StartTimeOut = MIN_START_TIMEOUT;
                }

                // Проверяем ParallelCount
                if (_config.ParallelCount <= 0)
                {
                    Log($"ParallelCount установлен в {_config.ParallelCount}. Устанавливаю значение 1");
                    _config.ParallelCount = 1;
                }

                _isRunning = true;
                Initialize();

                try
                {
                    LogStartupInfo();
                    StartFirstAccount();
                }
                catch (IOException)
                {
                    LogStartupInfo();
                }

                // Явно инициализируем и запускаем таймеры
                InitializeTimers();
                
                // Сразу проверяем расписание
                CheckSchedule(null, null);
                
                Log("Процесс запущен и работает");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при запуске: {ex.Message}");
                _isRunning = false;
            }
        }

        private void LogStartupInfo()
        {
            // Выводим информацию о версии
            Log(Constants.GetDroplerVersionInfo());
            Log("Модифицировано с любовью araan515 (за основу взята версия koperniki)");
            Log($"Аккаунты запускаются с переодичностью {_config.StartTimeOut} секунд");
            Log($"Аккаунты будут фармить на протяжении {_config.TimeConfig.IdleTime} минут после успешного подключения");
            Log($"Интервал проверки дропа: {_config.ChkIdleTimeOut} минут");
            Log($"Максимальное количество параллельных задач: {_config.ParallelCount}");
            Log($"Общее количество аккаунтов {_allAccounts.Count}");
            Log($"Общее количество аккаунтов, которые будут фармиться {_allAccounts.Values.Count(t => t.Enabled)}");
        }

        private string GetVersion()
        {
            return "1.1";
        }

        private void StartFirstAccount()
        {
            RefreshAccountQueue();
            var accountToStart = DequeueNextAccount();

            if (accountToStart != null)
            {
                var timeSinceLastStart = accountToStart.LastStartTime.HasValue ? 
                    (DateTime.Now - accountToStart.LastStartTime.Value).TotalMinutes : 0;
                    
                if (accountToStart.LastStartTime.HasValue)
                {
                    Log($"[{accountToStart.Alias}] Последний запуск был: {accountToStart.LastStartTime.Value:yyyy-MM-dd HH:mm:ss}");
                }
                
                Log($"[{accountToStart.Alias}] Время с последнего запуска: {Math.Round(timeSinceLastStart)} минут, требуемая пауза: {_config.TimeConfig.PauseBeatwinIdleTime} минут");
                Log($"[{accountToStart.Alias}] Запуск первого аккаунта...");
                _ = StartFarming(accountToStart);
            }
            else
            {
                var nextAccount = _allAccounts.Values
                    .Where(t => t.Enabled && !string.IsNullOrEmpty(t.SharedSecret))
                    .OrderBy(t => t.LastStartTime ?? DateTime.MaxValue)
                    .FirstOrDefault();

                if (nextAccount != null && nextAccount.LastStartTime.HasValue)
                {
                    var waitMinutes = _config.TimeConfig.PauseBeatwinIdleTime - 
                        (DateTime.Now - nextAccount.LastStartTime.Value).TotalMinutes;
                    Log($"Все аккаунты на паузе. Следующий запуск [{nextAccount.Alias}] через {Math.Round(waitMinutes)} минут");
                }
                else
                {
                    Log("Нет аккаунтов, готовых к запуску");
                }
            }
        }

        public void Run()
        {
            lock (_initLock)
            {
                if (_isInitialized)
                {
                    Log("Процесс фарминга уже запущен");
                    return;
                }

                try
                {
                    Log("Начало процесса запуска фарма...");
                    
                    // Проверяем окружение
                    if (!ValidateEnvironment())
                    {
                        Log("Проверка окружения не пройдена. Фарм не может быть запущен.");
                        return;
                    }
                    
                    Log("Загрузка аккаунтов и maFiles...");
                    
                    // Сначала загружаем существующие аккаунты
                    LoadAccounts();
                    
                    // Затем привязываем maFiles к существующим аккаунтам
                    BindMaFilesToAccounts();
                    
                    // Проверяем наличие аккаунтов после всех операций
                    if (_allAccounts.Count == 0)
                    {
                        Log("Не удалось загрузить или создать аккаунты. Проверьте файл log_pass.txt и наличие maFiles");
                        return;
                    }
                    
                    var readyAccounts = _allAccounts.Values.Count(a => a.Enabled && !string.IsNullOrEmpty(a.SharedSecret));
                    if (readyAccounts == 0)
                    {
                        Log("Нет аккаунтов, готовых к запуску. Проверьте наличие maFiles и настройки аккаунтов");
                        return;
                    }

                    // Инициализируем таймеры
                    InitializeTimers();
                    _isInitialized = true;
                    Log($"Инициализация завершена успешно. Запуск аккаунтов каждые {_config.StartTimeOut} секунд");

                    // Запускаем процесс
                    Start();
                    
                    // Сразу проверяем расписание
                    CheckSchedule(null, null);
                }
                catch (Exception ex)
                {
                    Log($"Ошибка при инициализации: {ex.Message}");
                    _isInitialized = false;
                    throw;
                }
            }
        }

        private bool ValidateEnvironment()
        {
            try
            {
                // Проверяем наличие всех необходимых директорий
                if (!Directory.Exists(_taskPath))
                {
                    Log($"Создание директории задачи: {_taskPath}");
                    Directory.CreateDirectory(_taskPath);
                }

                if (!Directory.Exists(_accountPath))
                {
                    Log($"Создание директории аккаунтов: {_accountPath}");
                    Directory.CreateDirectory(_accountPath);
                }

                // Проверяем наличие конфигурационных файлов
                var configPath = Path.Combine(_taskPath, "Configs");
                if (!Directory.Exists(configPath))
                {
                    Log($"Создание директории конфигурации: {configPath}");
                    Directory.CreateDirectory(configPath);
                }

                // Проверяем наличие директории для логов
                var logsPath = Path.Combine(_taskPath, "Logs");
                if (!Directory.Exists(logsPath))
                {
                    Log($"Создание директории логов: {logsPath}");
                    Directory.CreateDirectory(logsPath);
                }

                // Проверяем наличие директории для истории дропов
                var dropHistoryPath = Path.Combine(_taskPath, "DropHistory");
                if (!Directory.Exists(dropHistoryPath))
                {
                    Log($"Создание директории истории дропов: {dropHistoryPath}");
                    Directory.CreateDirectory(dropHistoryPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке окружения: {ex.Message}");
                return false;
            }
        }

        private void InitializeWorker()
        {
            Log("Начало процесса запуска фарма...");
            
            // Проверяем окружение
            if (!ValidateEnvironment())
            {
                Log("Проверка окружения не пройдена. Фарм не может быть запущен.");
                return;
            }
            
            Log("Загрузка аккаунтов и maFiles...");
            
            // Сначала загружаем существующие аккаунты
            LoadAccounts();
            
            // Затем привязываем maFiles к существующим аккаунтам
            BindMaFilesToAccounts();
            
            // Проверяем наличие аккаунтов после всех операций
            if (_allAccounts.Count == 0)
            {
                Log("Не удалось загрузить или создать аккаунты. Проверьте файл log_pass.txt и наличие maFiles");
                return;
            }
            
            var readyAccounts = _allAccounts.Values.Count(a => a.Enabled && !string.IsNullOrEmpty(a.SharedSecret));
            if (readyAccounts == 0)
            {
                Log("Нет аккаунтов, готовых к запуску. Проверьте наличие maFiles и настройки аккаунтов");
                return;
            }

            InitializeTimers();
            _isInitialized = true;
            Log($"Инициализация завершена успешно. Запуск аккаунтов каждые {_config.StartTimeOut} секунд");

            Start();
        }

        private void InitializeTimers()
        {
            try
            {
                Log("Инициализация таймеров...");
                
                // Останавливаем существующие таймеры
                if (_counterCheckTimer != null)
                {
                    Log("Остановка существующего таймера проверки аккаунтов");
                    _counterCheckTimer.Stop();
                    _counterCheckTimer.Elapsed -= CheckActiveAccountsCount;
                    _counterCheckTimer.Dispose();
                    _counterCheckTimer = null;
                }
                
                if (_timer != null)
                {
                    Log("Остановка существующего таймера запуска аккаунтов");
                    _timer.Stop();
                    _timer.Elapsed -= CheckToAdd;
                    _timer.Dispose();
                    _timer = null;
                }
                
                if (_scheduleTimer != null)
                {
                    Log("Остановка существующего таймера расписания");
                    _scheduleTimer.Stop();
                    _scheduleTimer.Elapsed -= CheckSchedule;
                    _scheduleTimer.Dispose();
                    _scheduleTimer = null;
                }

                // Инициализируем таймер проверки активных аккаунтов
                _counterCheckTimer = new System.Timers.Timer(10000); // 10 секунд
                _counterCheckTimer.Elapsed += CheckActiveAccountsCount;
                _counterCheckTimer.AutoReset = true;
                Log("Создан таймер проверки активных аккаунтов (интервал: 10 секунд)");

                // Инициализируем таймер запуска аккаунтов
                var startTimeout = Math.Max(_config.StartTimeOut, MIN_START_TIMEOUT);
                _timer = new System.Timers.Timer(1000 * startTimeout);
                _timer.Elapsed += CheckToAdd;
                _timer.AutoReset = true;
                Log($"Создан таймер запуска аккаунтов (интервал: {startTimeout} секунд)");

                // Инициализируем таймер для проверки расписания
                _scheduleTimer = new System.Timers.Timer(60000); // Проверяем каждую минуту
                _scheduleTimer.Elapsed += CheckSchedule;
                _scheduleTimer.AutoReset = true;
                Log("Создан таймер проверки расписания (интервал: 1 минута)");

                // Запускаем все таймеры
                _counterCheckTimer.Start();
                _timer.Start();
                _scheduleTimer.Start();
                Log("Все таймеры запущены");

                // Сбрасываем токен отмены
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = new CancellationTokenSource();

                Log("Таймеры успешно инициализированы");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при инициализации таймеров: {ex.Message}");
                throw; // Пробрасываем исключение дальше, так как это критическая ошибка
            }
        }

        private void CheckSchedule(object sender, ElapsedEventArgs e)
        {
            try
            {
                if (_config?.Schedule == null)
                {
                    _config.Schedule = ScheduleConfig.Load(_taskId);
                    if (_config.Schedule == null)
                    {
                        Log("Ошибка загрузки конфигурации расписания");
                        return;
                    }
                }

                if (!_config.Schedule.UseSchedule || _config.Schedule.Intervals == null || !_config.Schedule.Intervals.Any())
                {
                    return;
                }

                var currentTime = DateTime.Now.ToString("HH:mm");
                var shouldStart = _config.Schedule.Intervals.Any(i => i.StartTime == currentTime);
                var shouldStop = _config.Schedule.Intervals.Any(i => i.StopTime == currentTime);

                if (shouldStart && !_isRunning)
                {
                    Log("Запуск по расписанию");
                    Run();
                }
                else if (shouldStop && _isRunning)
                {
                    Log("Остановка по расписанию");
                    Stop();
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке расписания: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            try
            {
                Log("Начинаю остановку TaskWorker...");
                
                // Останавливаем таймеры
                _timer?.Stop();
                _counterCheckTimer?.Stop();
                _scheduleTimer?.Stop();

                // Отменяем все операции через токен
                _cancellationTokenSource?.Cancel();

                // Ждем завершения всех активных процессов
                var stopTasks = new List<Task>();
                foreach (var machine in _activeMachines.Values.ToList()) // Создаем копию списка
                {
                    stopTasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            machine.LogOf();
                            // Ждем некоторое время, чтобы убедиться, что процесс действительно остановился
                            await Task.Delay(1000);
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при остановке машины: {ex.Message}");
                        }
                    }));
                }

                // Ждем завершения всех задач остановки
                if (stopTasks.Any())
                {
                    await Task.WhenAll(stopTasks);
                    // Даем дополнительное время на завершение всех процессов
                    await Task.Delay(2000);
                }

                // Очищаем все коллекции
                foreach (var account in _activeAccounts.Values.ToList())
                {
                    CleanupAccount(account);
                }
                
                _activeMachines.Clear();
                _accountQueue.Clear();
                _activeAccounts.Clear();
                
                // Сбрасываем счетчик активных аккаунтов
                lock (_activeCountLock)
                {
                    _activeAccountsCount = 0;
                }

                _isRunning = false;
                _isInitialized = false;
                Log("TaskWorker успешно остановлен");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при остановке TaskWorker: {ex.Message}");
                throw;
            }
        }

        // Синхронный метод-обертка для обратной совместимости
        public void Stop()
        {
            StopAsync().GetAwaiter().GetResult();
        }

        // Метод для полной остановки, включая таймер расписания
        public void ShutDown()
        {
            Stop();
            
            // Останавливаем таймер расписания
            if (_scheduleTimer != null)
            {
                Log("Остановка таймера расписания");
                _scheduleTimer.Stop();
                _scheduleTimer.Dispose();
                _scheduleTimer = null;
            }
            
            _isInitialized = false;
            
            // Очищаем все коллекции
            _allAccounts.Clear();
            while (_accountQueue.TryDequeue(out _)) { }
            _activeAccounts.Clear();
            _activeAccountsCount = 0;

            // Останавливаем логгер
            _logger.Shutdown();
            
            Log("Программа полностью остановлена");
        }

        private void CheckActiveAccountsCount(object sender, ElapsedEventArgs e)
        {
            if (!_isInitialized) return;

            try
            {
                lock (_activeCountLock)
                {
                    int actualCount = _allAccounts.Values.Count(a => a.IdleNow);
                    if (actualCount != _activeAccountsCount)
                    {
                        Log($"Корректировка счетчика активных аккаунтов с {_activeAccountsCount} на {actualCount}");
                        _activeAccountsCount = actualCount;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке счетчика активных аккаунтов: {ex.Message}");
            }
        }

        private void CheckToAdd(object sender, ElapsedEventArgs e)
        {
            if (!_isInitialized) return;

            try
            {
                if (_activeAccounts.Count >= _config.ParallelCount)
                {
                    return;
                }

                // Проверяем, прошло ли достаточно времени с последнего запуска
                var timeSinceLastStart = (DateTime.Now - _lastAccountStartTime).TotalSeconds;
                if (timeSinceLastStart < _config.StartTimeOut)
                {
                    return;
                }

                // Обновляем очередь перед проверкой
                RefreshAccountQueue();

                var accountToStart = DequeueNextAccount();
                if (accountToStart != null)
                {
                    var timeSinceLastIdle = accountToStart.LastStartTime.HasValue ? 
                        (DateTime.Now - accountToStart.LastStartTime.Value).TotalMinutes : 0;
                    Log($"[{accountToStart.Alias}] Время с последнего запуска: {Math.Round(timeSinceLastIdle)} минут, требуемая пауза: {_config.TimeConfig.PauseBeatwinIdleTime} минут");
                    Log($"[{accountToStart.Alias}] Подготовка к запуску...");
                    _lastAccountStartTime = DateTime.Now;
                    _ = StartFarming(accountToStart);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке запуска нового аккаунта: {ex.Message}");
            }
        }

        private async Task StartFarming(AccountConfig account)
        {
            if (account == null)
            {
                Log("Ошибка: аккаунт не определен");
                return;
            }

            if (account.IdleNow || !string.IsNullOrEmpty(account.TaskID))
            {
                Log($"[{account.Alias}] Фарм уже запущен");
                return;
            }

            try
            {
                var taskId = Guid.NewGuid().ToString();
                account.TaskID = taskId;
                account.Action = "starting";
                account.IsRunning = true;
                AddToActive(account);

                Log($"[{account.Alias}] Создание новой задачи фарма (ID: {taskId})");

                _taskDictionary[taskId] = Task.Run(async () =>
                {
                    try
                    {
                        await FarmingTask(account);
                    }
                    catch (Exception ex)
                    {
                        Log($"[{account.Alias}] Ошибка при выполнении задачи: {ex.Message}");
                        CleanupAccount(account);
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"[{account.Alias}] Ошибка при создании задачи: {ex.Message}");
                CleanupAccount(account);
            }
        }

        private async Task FarmingTask(AccountConfig account)
        {
            account.Action = "Connecting";
            var machine = new SteamMachine(account);
            machine.SetLogCallback(LogCallback);
            Log($"[{account.Alias}] Подключение к серверу Steam...");
            
            AddActiveMachine(account.Name, machine);
            var result = await machine.EasyIdling();

            if (result != EResult.OK)
            {
                Log($"[{account.Alias}] не удалось войти: {result}");
                CleanupAccount(account);
                RemoveActiveMachine(account.Name);
                return;
            }

            account.IdleNow = true;
            account.Action = "Farming";
            account.LastStartTime = DateTime.Now;
            account.Save();
            IncrementActiveCount();
            _taskInstance?.UpdateAccountsState();

            _statisticsService?.UpdateAccountStatus(account.Name, "Online");

            Log($"[{account.Alias}] Начинаю проверку дропа каждые {_config.ChkIdleTimeOut} минут");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_config.TimeConfig.IdleTime), cts.Token);
                Log($"[{account.Alias}] Завершение фарма");
            }
            catch (OperationCanceledException)
            {
                Log($"[{account.Alias}] Остановка по запросу");
            }
            finally
            {
                try
                {
                    machine.LogOf();
                    // Ждем некоторое время после LogOf
                    await Task.Delay(1000);
                    CleanupAccount(account);
                    RemoveActiveMachine(account.Name);
                    _statisticsService?.UpdateAccountStatus(account.Name, "Offline");
                    Log($"[{account.Alias}] Успешно остановлен");
                }
                catch (Exception ex)
                {
                    Log($"[{account.Alias}] Ошибка при очистке: {ex.Message}");
                }
            }
        }

        private void CleanupAccount(AccountConfig account)
        {
            account.IdleNow = false;
            account.IsRunning = false;
            account.Action = "none";
            account.TaskID = null;
            account.LastStartTime = DateTime.Now;
            account.Save();
            RemoveFromActive(account);
            DecrementActiveCount();
            _taskInstance?.UpdateAccountsState();
        }

        private void IncrementActiveCount()
        {
            lock (_activeCountLock)
            {
                _activeAccountsCount++;
            }
        }

        private void DecrementActiveCount()
        {
            lock (_activeCountLock)
            {
                if (_activeAccountsCount > 0)
                {
                    _activeAccountsCount--;
                }
            }
        }

        private void AddActiveMachine(string accountName, SteamMachine machine)
        {
            lock (_activeCountLock)
            {
                _activeMachines[accountName] = machine;
            }
        }

        private void RemoveActiveMachine(string accountName)
        {
            lock (_activeCountLock)
            {
                _activeMachines.Remove(accountName);
            }
        }

        private void LoadAccounts()
        {
            try
            {
                int loadedAccounts = 0;
                int skippedAccounts = 0;
                int newAccounts = 0;

                // Загружаем существующие аккаунты
                if (Directory.Exists(_accountPath))
                {
                    var files = Directory.GetFiles(_accountPath, "*.json");
                    foreach (var file in files)
                    {
                        try
                        {
                            var account = JsonConvert.DeserializeObject<AccountConfig>(File.ReadAllText(file));
                            account.Name = Path.GetFileNameWithoutExtension(file);
                            account.SetTaskPath(_taskPath);
                            
                            if (account.LastStartTime.HasValue && account.LastStartTime.Value.Year == 1)
                            {
                                account.LastStartTime = null;
                            }
                            
                            if (_allAccounts.TryAdd(account.Name.ToLower(), account))
                            {
                                loadedAccounts++;
                                if (account.LastStartTime.HasValue)
                                {
                                    Log($"[{account.Name}] Загружено время последнего запуска: {account.LastStartTime.Value:yyyy-MM-dd HH:mm:ss}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при загрузке аккаунта {Path.GetFileName(file)}: {ex.Message}");
                            skippedAccounts++;
                        }
                    }
                }

                // Обрабатываем log_pass.txt для создания новых аккаунтов
                var newAccountsData = ParseLogPassFile();
                if (newAccountsData.Any())
                {
                    Log("Обработка новых аккаунтов из log_pass.txt...");

                    // Создаем словарь maFiles по AccountName
                    var maFilesByAccountName = new Dictionary<string, (string path, string secret)>(StringComparer.OrdinalIgnoreCase);
                    var maFiles = Directory.GetFiles(Path.Combine(_taskPath, "maFiles"), "*.maFile");
                    foreach (var maFile in maFiles)
                    {
                        try
                        {
                            var maFileContent = File.ReadAllText(maFile);
                            var mobileAuth = JsonConvert.DeserializeObject<MobileAuth>(maFileContent);
                            if (!string.IsNullOrEmpty(mobileAuth?.AccountName) && !string.IsNullOrEmpty(mobileAuth?.SharedSecret))
                            {
                                maFilesByAccountName[mobileAuth.AccountName] = (maFile, mobileAuth.SharedSecret);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при чтении maFile {Path.GetFileName(maFile)}: {ex.Message}");
                        }
                    }

                    foreach (var (login, password) in newAccountsData)
                    {
                        var accountConfig = new AccountConfig
                        {
                            Name = login,
                            Password = password,
                            Alias = login,
                            Enabled = true,
                            Action = "none",
                            ShowStatus = _config.ShowStatus
                        };
                        accountConfig.SetTaskPath(_taskPath);

                        // Ищем maFile сначала по AccountName
                        if (maFilesByAccountName.TryGetValue(login, out var maFileInfo))
                        {
                            try
                            {
                                accountConfig.SharedSecret = maFileInfo.secret;
                                var accountFilePath = Path.Combine(_accountPath, $"{login}.json");
                                File.WriteAllText(accountFilePath, JsonConvert.SerializeObject(accountConfig, Formatting.Indented));
                                
                                if (_allAccounts.TryAdd(login.ToLower(), accountConfig))
                                {
                                    newAccounts++;
                                    Log($"Создан новый аккаунт: {login} (найден maFile по AccountName: {Path.GetFileName(maFileInfo.path)})");
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"Ошибка при создании аккаунта {login}: {ex.Message}");
                            }
                        }
                        else
                        {
                            Log($"maFile для аккаунта {login} не найден по AccountName");
                        }
                    }
                }

                // Выводим итоговую статистику
                Log("\nСтатистика загрузки аккаунтов:");
                Log($"- Загружено существующих аккаунтов: {loadedAccounts}");
                Log($"- Пропущено поврежденных аккаунтов: {skippedAccounts}");
                Log($"- Создано новых аккаунтов: {newAccounts}");
                Log($"- Всего аккаунтов в системе: {_allAccounts.Count}");

                // После загрузки всех аккаунтов обновляем очередь
                RefreshAccountQueue();
            }
            catch (Exception ex)
            {
                Log($"Ошибка при загрузке аккаунтов: {ex.Message}");
            }
        }

        private void BindMaFilesToAccounts()
        {
            try
            {
                if (_allAccounts.Count == 0)
                {
                    Log("Нет аккаунтов для проверки SharedSecret");
                    return;
                }

                string maFilesPath = Path.Combine(_taskPath, "maFiles");
                if (!Directory.Exists(maFilesPath))
                {
                    Log($"Директория {maFilesPath} не найдена");
                    return;
                }

                var maFiles = Directory.GetFiles(maFilesPath, "*.maFile");
                if (maFiles.Length == 0)
                {
                    Log("maFiles не найдены");
                    return;
                }

                // Создаем словарь для быстрого поиска maFile по account_name
                var maFilesByAccountName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var maFile in maFiles)
                {
                    try
                    {
                        var maFileContent = File.ReadAllText(maFile);
                        var mobileAuth = JsonConvert.DeserializeObject<MobileAuth>(maFileContent);
                        if (!string.IsNullOrEmpty(mobileAuth?.AccountName))
                        {
                            maFilesByAccountName[mobileAuth.AccountName] = maFile;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка при чтении maFile {Path.GetFileName(maFile)}: {ex.Message}");
                    }
                }

                int totalAccounts = _allAccounts.Count;
                int accountsWithSecret = _allAccounts.Values.Count(a => !string.IsNullOrEmpty(a.SharedSecret));
                int accountsNeedingSecret = _allAccounts.Values.Count(a => string.IsNullOrEmpty(a.SharedSecret));
                int bound = 0;
                int failed = 0;

                Log($"\nНачало проверки SharedSecret:");
                Log($"- Всего аккаунтов: {totalAccounts}");
                Log($"- Уже имеют SharedSecret: {accountsWithSecret}");
                Log($"- Требуется привязать SharedSecret: {accountsNeedingSecret}");

                foreach (var account in _allAccounts.Values.Where(a => string.IsNullOrEmpty(a.SharedSecret)))
                {
                    // Сначала ищем по AccountName в нашем словаре
                    if (maFilesByAccountName.TryGetValue(account.Name, out string matchedMaFile))
                    {
                        try
                        {
                            var maFileContent = File.ReadAllText(matchedMaFile);
                            var mobileAuth = JsonConvert.DeserializeObject<MobileAuth>(maFileContent);

                            if (string.IsNullOrEmpty(mobileAuth?.SharedSecret))
                            {
                                Log($"[WARNING] maFile для аккаунта {account.Name} найден по AccountName, но не содержит SharedSecret");
                                failed++;
                                continue;
                            }

                            account.SharedSecret = mobileAuth.SharedSecret;
                            account.Save();
                            bound++;
                            Log($"Привязан SharedSecret к аккаунту {account.Name} (найдено по AccountName в {Path.GetFileName(matchedMaFile)})");
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при чтении maFile для аккаунта {account.Name}: {ex.Message}");
                            failed++;
                        }
                    }
                    else
                    {
                        Log($"maFile для аккаунта {account.Name} не найден по AccountName");
                        failed++;
                    }
                }

                // Выводим итоговую статистику
                Log($"\nРезультаты привязки SharedSecret:");
                Log($"- Успешно привязано: {bound}");
                Log($"- Не удалось привязать: {failed}");
                Log($"- Всего аккаунтов с SharedSecret: {_allAccounts.Values.Count(a => !string.IsNullOrEmpty(a.SharedSecret))}");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке SharedSecret: {ex.Message}");
            }
        }

        private void EnqueueAccount(AccountConfig account)
        {
            if (account != null && 
                account.Enabled && 
                !string.IsNullOrEmpty(account.SharedSecret) &&
                (!account.LastStartTime.HasValue ||
                 (DateTime.Now - account.LastStartTime.Value).TotalMinutes >= _config.TimeConfig.PauseBeatwinIdleTime))
            {
                _accountQueue.Enqueue(account);
            }
        }

        private AccountConfig DequeueNextAccount()
        {
            if (_accountQueue.TryDequeue(out var account))
            {
                return account;
            }
            return null;
        }

        private void AddToActive(AccountConfig account)
        {
            if (account != null)
            {
                _activeAccounts.TryAdd(account.Name, account);
            }
        }

        private void RemoveFromActive(AccountConfig account)
        {
            if (account != null)
            {
                _activeAccounts.TryRemove(account.Name, out _);
            }
        }

        private void RefreshAccountQueue()
        {
            // Очищаем текущую очередь
            while (_accountQueue.TryDequeue(out _)) { }

            // Добавляем аккаунты, готовые к запуску
            foreach (var account in _allAccounts.Values
                .Where(a => a.Enabled &&
                           !a.IdleNow &&
                           !a.IsRunning &&
                           string.IsNullOrEmpty(a.TaskID) &&
                           !string.IsNullOrEmpty(a.SharedSecret) &&
                           (!a.LastStartTime.HasValue ||
                            (DateTime.Now - a.LastStartTime.Value).TotalMinutes >= _config.TimeConfig.PauseBeatwinIdleTime))
                .OrderBy(a => a.LastStartTime ?? DateTime.MaxValue))
            {
                _accountQueue.Enqueue(account);
            }
        }

        // Добавляем класс компаратора для HashSet<AccountConfig>
        private class AccountConfigComparer : IEqualityComparer<AccountConfig>
        {
            public bool Equals(AccountConfig x, AccountConfig y)
            {
                if (ReferenceEquals(x, y)) return true;
                if (x is null || y is null) return false;
                return x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode(AccountConfig obj)
            {
                return obj.Name?.ToLowerInvariant().GetHashCode() ?? 0;
            }
        }

        private List<(string login, string password)> ParseLogPassFile()
        {
            var result = new List<(string login, string password)>();
            string logPassPath = Path.Combine(_taskPath, "Configs", "log_pass.txt");
            
            try
            {
                var lines = File.ReadAllLines(logPassPath)
                    .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                    .ToList();

                int validLines = 0;
                int invalidLines = 0;

                foreach (var line in lines)
                {
                    var parts = line.Split(':');
                    if (parts.Length == 2 && 
                        !string.IsNullOrWhiteSpace(parts[0]) && 
                        !string.IsNullOrWhiteSpace(parts[1]))
                    {
                        string login = parts[0].Trim();
                        string password = parts[1].Trim();
                        
                        // Проверяем, существует ли уже аккаунт с таким логином
                        if (_allAccounts.ContainsKey(login.ToLower()))
                        {
                            Log($"Аккаунт {login} уже существует, пропускаю");
                            continue;
                        }
                        
                        result.Add((login, password));
                        validLines++;
                    }
                    else
                    {
                        invalidLines++;
                    }
                }

                Log($"Обработка log_pass.txt завершена:");
                Log($"- Найдено валидных строк: {validLines}");
                Log($"- Пропущено невалидных строк: {invalidLines}");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при обработке log_pass.txt: {ex.Message}");
            }

            return result;
        }

        public static DateTime? GetLastLoginAttempt()
        {
            lock (_lastLoginLock)
            {
                return _lastLoginAttempt;
            }
        }

        public static void UpdateLastLoginAttempt()
        {
            lock (_lastLoginLock)
            {
                _lastLoginAttempt = DateTime.UtcNow;
            }
        }
    }
} 