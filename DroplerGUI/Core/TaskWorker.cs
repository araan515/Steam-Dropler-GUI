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

namespace DroplerGUI.Core
{
    public class TaskWorker
    {
        // Статические компоненты
        private static readonly object _staticLogLock = new object();
        private static Dictionary<uint, Dictionary<string, string>> _gameDB = new Dictionary<uint, Dictionary<string, string>>();
        private static Dictionary<string, Task> _taskDictionary = new Dictionary<string, Task>();
        
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

        // Коллекции
        private HashSet<AccountConfig> _accounts;
        private Dictionary<string, MobileAuth> _mobileAuths;
        
        private static List<TaskInstance> TaskInstances = new List<TaskInstance>();

        public void SetTaskInstance(TaskInstance taskInstance)
        {
            _taskInstance = taskInstance;
            // Добавляем экземпляр в статический список если его там еще нет
            if (!TaskInstances.Contains(taskInstance))
            {
                TaskInstances.Add(taskInstance);
            }
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
                    if (task.Worker._accounts.Contains(account))
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
                else if (!TaskInstances.Any(t => t.Worker._accounts.Contains(account)))
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
            
            _accounts = new HashSet<AccountConfig>();
            _mobileAuths = new Dictionary<string, MobileAuth>();
            
            Initialize();
            
            // Добавляем экземпляр в статический список
            var existingTask = TaskInstances.FirstOrDefault(t => t.TaskNumber == taskId);
            if (existingTask == null)
            {
                var taskInstance = TaskInstances.FirstOrDefault(t => t.Worker == this);
                if (taskInstance != null)
                {
                    TaskInstances.Add(taskInstance);
                }
            }
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
            _config = MainConfig.GetConfig(_taskId);

            // Сбрасываем флаги для всех аккаунтов при старте
            if (_accounts != null)
            {
                foreach (var account in _accounts)
                {
                    account.IdleNow = false;
                    account.IsRunning = false;
                    account.Action = "none";
                    account.TaskID = null;
                    account.Save();
                }
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

            try
            {
                LogStartupInfo();
                StartFirstAccount();
            }
            catch (IOException)
            {
                // Если не удалось установить цвет, логируем без цвета
                LogStartupInfo();
            }
        }

        private void LogStartupInfo()
        {
            string version = GetVersion();
            Log($"Steam-dropler ver. ({version})");
            Log("Модифицировано с любовью araan515 (за основу взята версия koperniki)");
            Log($"Аккаунты запускаются с переодичностью {_config.StartTimeOut} секунд");
            Log($"Аккаунты будут фармить на протяжении {_config.TimeConfig.IdleTime} минут после успешного подключения");
            Log($"Интервал проверки дропа: {_config.ChkIdleTimeOut} минут");
            Log($"Максимальное количество параллельных задач: {_config.ParallelCount}");
            Log($"Общее количество аккаунтов {_accounts.Count}");
            Log($"Общее количество аккаунтов, которые будут фармиться {_accounts.Count(t => t.Enabled)}");
        }

        private string GetVersion()
        {
            return "1.1";
        }

        private void StartFirstAccount()
        {
            var accountToStart = _accounts
                .Where(t => t.Enabled && 
                          t.MobileAuth?.SharedSecret != null &&
                          (!t.LastStartTime.HasValue ||
                           (DateTime.Now - t.LastStartTime.Value).TotalMinutes >= _config.TimeConfig.PauseBeatwinIdleTime))
                .OrderBy(t => t.LastStartTime ?? DateTime.MinValue)
                .FirstOrDefault();

            if (accountToStart != null)
            {
                Log($"[{accountToStart.Alias}] Запуск первого аккаунта...");
                _ = StartFarming(accountToStart);
            }
            else
            {
                var nextAccount = _accounts
                    .Where(t => t.Enabled && t.MobileAuth?.SharedSecret != null)
                    .OrderBy(t => t.LastStartTime ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (nextAccount != null)
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
                    InitializeWorker();
                }
                catch (Exception ex)
                {
                    Log($"Ошибка при инициализации: {ex.Message}");
                    _isInitialized = false;
                    throw;
                }
            }
        }

        private void InitializeWorker()
        {
            Log("Начало процесса запуска фарма...");
            Log("Загрузка аккаунтов и maFiles...");
            
            LoadAccounts();
            if (_accounts.Count == 0)
            {
                Log("Не удалось загрузить или создать аккаунты. Проверьте файл log_pass.txt");
                return;
            }
            
            BindMaFilesToAccounts();
            var readyAccounts = _accounts.Count(a => a.Enabled && a.MobileAuth?.SharedSecret != null);
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
            // Останавливаем существующие таймеры
            _counterCheckTimer?.Stop();
            _counterCheckTimer?.Dispose();
            _timer?.Stop();
            _timer?.Dispose();

            // Инициализируем таймер проверки активных аккаунтов
            _counterCheckTimer = new System.Timers.Timer(10000);
            _counterCheckTimer.Elapsed += CheckActiveAccountsCount;
            _counterCheckTimer.AutoReset = true;
            _counterCheckTimer.Start();

            // Инициализируем таймер запуска аккаунтов
            _timer = new System.Timers.Timer(1000 * Math.Max(_config.StartTimeOut, MIN_START_TIMEOUT));
            _timer.Elapsed += CheckToAdd;
            _timer.AutoReset = true;
            _timer.Start();

            // Сбрасываем токен отмены
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task StopAsync()
        {
            try
            {
                if (!_isInitialized)
                {
                    Log("Процесс фарминга не был запущен");
                    return;
                }

                Log("Начало процесса остановки...");
                _isInitialized = false;

                // Останавливаем таймеры сразу
                _timer?.Stop();
                _counterCheckTimer?.Stop();

                // Отменяем все задачи через CancellationToken
                _cancellationTokenSource.Cancel();

                // Параллельное отключение всех машин с таймаутом
                var disconnectTasks = _activeMachines.Values.Select(async machine =>
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                        await Task.Run(() => 
                        {
                            try
                            {
                                machine.LogOf();
                                return Task.CompletedTask;
                            }
                            catch
                            {
                                return Task.CompletedTask;
                            }
                        }, timeoutCts.Token);
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка при остановке машины: {ex.Message}");
                    }
                });

                // Ждем завершения всех отключений с общим таймаутом
                await Task.WhenAll(disconnectTasks).WaitAsync(TimeSpan.FromSeconds(10));

                // Пакетный сброс состояния только активных аккаунтов
                if (_accounts != null)
                {
                    var activeAccounts = _accounts.Where(a => a.IdleNow || a.IsRunning || !string.IsNullOrEmpty(a.TaskID));
                    var accountUpdates = activeAccounts.Select(account =>
                    {
                        account.IdleNow = false;
                        account.IsRunning = false;
                        account.Action = "none";
                        account.TaskID = null;
                        return Task.Run(() => account.Save());
                    });
                    
                    // Параллельное сохранение состояний с таймаутом
                    await Task.WhenAll(accountUpdates).WaitAsync(TimeSpan.FromSeconds(5));
                }

                // Очистка коллекций
                _activeMachines.Clear();
                _taskDictionary.Clear();

                // Останавливаем логгер
                _logger.Shutdown();

                Log("Процесс фарминга остановлен");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при остановке: {ex.Message}");
            }
        }

        public void Stop()
        {
            try
            {
                if (!_isInitialized)
                {
                    Log("Процесс фарминга не был запущен");
                    return;
                }

                Log("Начало процесса остановки...");
                
                // 1. Сразу отмечаем как неинициализированный
                _isInitialized = false;
                
                // 2. Останавливаем таймеры
                _timer?.Stop();
                _counterCheckTimer?.Stop();
                
                // 3. Отменяем все задачи
                _cancellationTokenSource?.Cancel();
                
                // 4. Быстрое отключение машин без ожидания
                foreach (var machine in _activeMachines.Values.ToList())
                {
                    try
                    {
                        Task.Run(() => machine.LogOf());
                    }
                    catch
                    {
                        // Игнорируем ошибки при отключении
                    }
                }
                
                // 5. Очищаем коллекции
                _activeMachines.Clear();
                _taskDictionary.Clear();
                
                // 6. Сбрасываем флаги только для активных аккаунтов
                if (_accounts != null)
                {
                    var activeAccounts = _accounts.Where(a => a.IdleNow || a.IsRunning || !string.IsNullOrEmpty(a.TaskID));
                    foreach (var account in activeAccounts)
                    {
                        account.IdleNow = false;
                        account.IsRunning = false;
                        account.Action = "none";
                        account.TaskID = null;
                    }
                    
                    // Сохраняем состояния в фоновом режиме только для активных аккаунтов
                    Task.Run(() => 
                    {
                        foreach (var account in activeAccounts)
                        {
                            try
                            {
                                account.Save();
                            }
                            catch
                            {
                                // Игнорируем ошибки сохранения
                            }
                        }
                    });
                }
                
                // 7. Останавливаем логгер
                _logger.Shutdown();

                Log("Процесс фарминга остановлен");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при остановке: {ex.Message}");
            }
        }

        private void CheckActiveAccountsCount(object sender, ElapsedEventArgs e)
        {
            try
            {
                lock (_activeCountLock)
                {
                    int actualCount = _accounts?.Count(a => a.IdleNow) ?? 0;
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
            try
            {
                if (_activeAccountsCount >= _config.ParallelCount)
                {
                    return;
                }

                var accountToStart = _accounts
                    .Where(t => t.Enabled &&
                              !t.IdleNow &&
                              !t.IsRunning &&
                              string.IsNullOrEmpty(t.TaskID) &&
                              t.MobileAuth?.SharedSecret != null &&
                              (!t.LastStartTime.HasValue ||
                               (DateTime.Now - t.LastStartTime.Value).TotalMinutes >= _config.TimeConfig.PauseBeatwinIdleTime))
                    .OrderBy(t => t.LastStartTime ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (accountToStart != null)
                {
                    Log($"[{accountToStart.Alias}] Подготовка к запуску...");
                    _ = StartFarming(accountToStart);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке аккаунтов для запуска: {ex.Message}");
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
                // При остановке не выводим дополнительных сообщений
            }
            finally
            {
                machine.LogOf();
                CleanupAccount(account);
                RemoveActiveMachine(account.Name);
                _statisticsService?.UpdateAccountStatus(account.Name, "Offline");
            }
        }

        private void CleanupAccount(AccountConfig account)
        {
            account.IdleNow = false;
            account.IsRunning = false;
            account.Action = "none";
            account.TaskID = null;
            account.Save();
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
                // Не создаем папку здесь, она должна быть создана в TaskInstance
                if (!Directory.Exists(_accountPath))
                {
                    Log("Папка аккаунтов не найдена");
                    return;
                }

                // Загружаем существующие аккаунты
                var files = Directory.GetFiles(_accountPath, "*.json");
                var existingAccounts = new Dictionary<string, AccountConfig>();

                foreach (var file in files)
                {
                    try
                    {
                        var account = JsonConvert.DeserializeObject<AccountConfig>(File.ReadAllText(file));
                        account.Name = Path.GetFileNameWithoutExtension(file);
                        account.LastStartTime = DateTime.MinValue;
                        account.SetTaskPath(_taskPath);
                        existingAccounts[account.Name.ToLower()] = account;
                        _accounts.Add(account);
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка при загрузке аккаунта {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                // Проверяем log_pass.txt на наличие новых аккаунтов
                string logPassPath = Path.Combine(Constants.GetTaskConfigPath(_taskId), "log_pass.txt");
                if (File.Exists(logPassPath))
                {
                    var lines = File.ReadAllLines(logPassPath)
                        .Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                        .ToList();

                    foreach (var line in lines)
                    {
                        var parts = line.Split(new[] { ':' }, 2);
                        if (parts.Length != 2)
                        {
                            Log($"Пропущена некорректная строка: {line}. Формат должен быть login:password");
                            continue;
                        }

                        var login = parts[0].Trim();
                        var password = parts[1].Trim();

                        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
                        {
                            Log($"Пропущена строка с пустым логином или паролем: {line}");
                            continue;
                        }

                        var loginLower = login.ToLower();
                        
                        // Проверяем, существует ли уже такой аккаунт
                        if (existingAccounts.TryGetValue(loginLower, out var existingAccount))
                        {
                            // Проверяем, совпадает ли пароль
                            if (existingAccount.Password != password)
                            {
                                Log($"[WARNING] Обнаружено несоответствие пароля для аккаунта {login}:");
                                Log($"- В log_pass.txt: {password}");
                                Log($"- В файле аккаунта: {existingAccount.Password}");
                                Log($"Для обновления пароля удалите файл аккаунта и при следующем запуске фарма он будет создан с новым паролем, либо отредактируйте его вручную");
                            }
                            continue;
                        }

                        // Проверяем наличие maFile для нового аккаунта
                        var maFilePath = Path.Combine(Constants.GetTaskPath(_taskId), "maFiles", $"{login}.maFile");
                        if (!File.Exists(maFilePath))
                        {
                            Log($"[WARNING] Для нового аккаунта {login} не найден maFile. Добавьте файл {login}.maFile в папку maFiles и перезапустите фарм");
                            continue;
                        }

                        try
                        {
                            var maFileContent = File.ReadAllText(maFilePath);
                            var mobileAuth = JsonConvert.DeserializeObject<MobileAuth>(maFileContent);
                            
                            if (string.IsNullOrEmpty(mobileAuth?.SharedSecret))
                            {
                                Log($"[WARNING] maFile для аккаунта {login} не содержит SharedSecret. Проверьте корректность файла");
                                continue;
                            }

                            // Создаем новый аккаунт
                            var newAccount = new AccountConfig
                            {
                                Name = login,
                                Alias = login,
                                Password = password,
                                Enabled = true,
                                IdleNow = false,
                                ShowStatus = _config?.ShowStatus ?? "Online",
                                LastStartTime = DateTime.MinValue,
                                Action = "none",
                                MobileAuth = mobileAuth,
                                SharedSecret = mobileAuth.SharedSecret,
                                AuthType = (AuthType)2
                            };
                            newAccount.SetTaskPath(_taskPath);

                            // Сохраняем файл аккаунта
                            var accountPath = Path.Combine(_accountPath, $"{login}.json");
                            File.WriteAllText(accountPath, JsonConvert.SerializeObject(newAccount, Formatting.Indented));
                            
                            // Добавляем в коллекцию
                            _accounts.Add(newAccount);
                            Log($"Создан новый аккаунт: {login} (с привязанным maFile)");
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при создании аккаунта {login}: {ex.Message}");
                        }
                    }
                }
                else
                {
                    Log("Файл log_pass.txt не найден. Используются только существующие аккаунты");
                }

                if (_accounts.Count == 0)
                {
                    Log("Нет аккаунтов для запуска. Проверьте наличие файлов аккаунтов или добавьте новые аккаунты в log_pass.txt");
                }
                else
                {
                    Log($"Загружено аккаунтов: {_accounts.Count}");
                }
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
                if (_accounts == null || _accounts.Count == 0)
                {
                    Log("Нет аккаунтов для привязки maFiles");
                    return;
                }

                string maFilesPath = Path.Combine(Constants.GetTaskPath(_taskId), "maFiles");
                if (!Directory.Exists(maFilesPath))
                {
                    Log($"Директория {maFilesPath} не найдена");
                    return;
                }

                var maFiles = Directory.GetFiles(maFilesPath, "*.maFile");
                if (maFiles.Length == 0)
                {
                    Log("maFiles не найдены. Поместите файлы .maFile в папку maFiles");
                    return;
                }

                int bound = 0;
                foreach (var account in _accounts)
                {
                    // Ищем maFile по имени аккаунта или алиасу
                    var maFile = maFiles.FirstOrDefault(f => 
                        Path.GetFileNameWithoutExtension(f).Equals(account.Name, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileNameWithoutExtension(f).Equals(account.Alias, StringComparison.OrdinalIgnoreCase));

                    if (maFile != null)
                    {
                        try
                        {
                            var maFileContent = File.ReadAllText(maFile);
                            var mobileAuth = JsonConvert.DeserializeObject<MobileAuth>(maFileContent);
                            
                            if (string.IsNullOrEmpty(mobileAuth?.SharedSecret))
                            {
                                Log($"[WARNING] maFile для аккаунта {account.Name} не содержит SharedSecret");
                                continue;
                            }

                            account.MobileAuth = mobileAuth;
                            account.SharedSecret = mobileAuth.SharedSecret;
                            bound++;
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при привязке maFile к аккаунту {account.Name}: {ex.Message}");
                        }
                    }
                }

                Log($"Привязано maFiles: {bound} из {_accounts.Count} аккаунтов");
            }
            catch (Exception ex)
            {
                Log($"Ошибка при привязке maFiles: {ex.Message}");
            }
        }
    }
} 