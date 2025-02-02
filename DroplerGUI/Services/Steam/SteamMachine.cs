using System;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Timers;
using Newtonsoft.Json;
using SteamKit2;
using SteamKit2.Internal;
using DroplerGUI.Models;
using DroplerGUI.Core;
using DroplerGUI.Services.Steam;
using DroplerGUI.Services;
using DroplerGUI.Services.Steam.Auth;

namespace DroplerGUI.Services.Steam
{
    public partial class SteamMachine
    {
        private readonly SteamUnifiedMessages _steamUnifiedMessages;
        private readonly SteamFriends _steamFriends;
        private readonly SteamClient _steamClient;
        private readonly CallbackManager _manager;
        private readonly SteamUser _steamUser;
        private readonly AccountConfig _account;
        private readonly int _taskNumber;
        private readonly SteamUnifiedMessages.UnifiedService<IInventory> _inventoryService;
        private bool _isRunning = true;
        private bool _isLoggedIn = false;
        private bool _isConnected;
        private Action<string> _logCallback;
        private System.Timers.Timer _dropCheckTimer;

        public SteamMachine(AccountConfig account)
        {
            _account = account;
            _taskNumber = int.Parse(account.GetTaskPath().Split('_').Last());
            _steamClient = new SteamClient();
            _manager = new CallbackManager(_steamClient);
            _steamUser = _steamClient.GetHandler<SteamUser>();
            _steamFriends = _steamClient.GetHandler<SteamFriends>();
            _steamUnifiedMessages = _steamClient.GetHandler<SteamUnifiedMessages>();
            _inventoryService = _steamUnifiedMessages.CreateService<IInventory>();
            
            // Инициализируем таймер проверки дропа
            var config = MainConfig.GetConfig(_taskNumber);
            _dropCheckTimer = new System.Timers.Timer(config.ChkIdleTimeOut * 60000); // Конвертируем минуты в миллисекунды
            _dropCheckTimer.Elapsed += OnDropCheckTimer;
            _dropCheckTimer.AutoReset = true;

            // Подписываемся на события
            _manager.Subscribe<SteamClient.ConnectedCallback>(OnConnected);
            _manager.Subscribe<SteamClient.DisconnectedCallback>(OnDisconnected);
            _manager.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
            _manager.Subscribe<SteamUser.LoggedOffCallback>(OnLoggedOff);
        }

        public void SetLogCallback(Action<string> logCallback)
        {
            _logCallback = logCallback;
        }

        private void Log(string message, bool showInConsole = true)
        {
            if (showInConsole)
            {
                _logCallback?.Invoke(message);
            }
        }

        public async Task<EResult> EasyIdling()
        {
            try
            {
                _isRunning = true;
                Log("Инициализация подключения к Steam...", false);

                var connectTask = Task.Run(() =>
                {
                    _steamClient.Connect();

                    while (_isRunning)
                    {
                        _manager.RunWaitCallbacks(TimeSpan.FromSeconds(1));
                    }
                });

                // Ждем подключения
                var connectionTimeout = TimeSpan.FromSeconds(30);
                var startTime = DateTime.UtcNow;
                while (!_isConnected && DateTime.UtcNow - startTime < connectionTimeout)
                {
                    await Task.Delay(100);
                }

                if (!_isConnected)
                {
                    Log("Не удалось подключиться к Steam (таймаут)");
                    return EResult.NoConnection;
                }

                // Выполняем вход
                Log($"Тип аутентификации: {_account.AuthType}", false);
                var authenticator = AuthenticatorFactory.CreateAuthenticator((int)_account.AuthType, _account.SharedSecret);
                
                var twoFactorCode = await authenticator.GetDeviceCodeAsync(false);
                Log($"Получен код аутентификации: {(string.IsNullOrEmpty(twoFactorCode) ? "нет" : "да")}", false);
                
                var logonDetails = new SteamUser.LogOnDetails
                {
                    Username = _account.Alias,
                    Password = _account.Password,
                    TwoFactorCode = twoFactorCode,
                    LoginID = (uint)new Random().Next(1, int.MaxValue),
                    ShouldRememberPassword = true,
                    ClientLanguage = "english",
                    CellID = 1
                };

                Log($"Попытка входа для {_account.Alias}...", false);
                _steamUser.LogOn(logonDetails);

                // Ждем входа
                var loginTimeout = TimeSpan.FromSeconds(30);
                startTime = DateTime.UtcNow;
                while (!_isLoggedIn && DateTime.UtcNow - startTime < loginTimeout)
                {
                    await Task.Delay(100);
                }

                if (!_isLoggedIn)
                {
                    Log("Не удалось войти в Steam (таймаут)");
                    return EResult.InvalidPassword;
                }

                // Запускаем игры
                var config = MainConfig.GetConfig(_taskNumber);
                if (config.DropConfig != null && config.DropConfig.Any())
                {
                    // Получаем уникальные AppId из DropConfig
                    var uniqueAppIds = config.DropConfig
                        .Select(x => x.Item1)
                        .Distinct()
                        .ToList();

                    var playGames = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
                    
                    foreach (var appId in uniqueAppIds)
                    {
                        playGames.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
                        {
                            game_id = new GameID(appId)
                        });
                    }

                    // Небольшая задержка перед запуском игр
                    await Task.Delay(2000);

                    Log($"Запускаю {uniqueAppIds.Count} игр...");
                    _steamClient.Send(playGames);

                    // Ждем еще немного после запуска игр
                    await Task.Delay(2000);

                    // Запускаем таймер проверки дропа
                    _dropCheckTimer.Start();
                }
                else
                {
                    Log("Нет настроенных игр для запуска");
                }

                return EResult.OK;
            }
            catch (Exception ex)
            {
                Log($"Ошибка в процессе входа: {ex.Message}");
                return EResult.Fail;
            }
        }

        private async Task ProcessDropResponse(uint appId, string itemJson)
        {
            if (string.IsNullOrEmpty(itemJson))
            {
                Log($"[DEBUG] Получен пустой ответ для игры {appId}");
                return;
            }

            try
            {
                var items = JsonConvert.DeserializeObject<Models.DropResult[]>(itemJson);
                if (items == null)
                {
                    Log($"[DEBUG] Не удалось десериализовать ответ для игры {appId}");
                    return;
                }

                if (items.Length == 0)
                {
                    Log($"[DEBUG] Нет новых предметов для игры {appId}");
                    return;
                }

                foreach (var item in items)
                {
                    try 
                    {
                        // Оставляем отладочные сообщения
                        Log($"[DEBUG] Разбор предмета:");
                        Log($"[DEBUG] - AppId: {item.AppId}");
                        Log($"[DEBUG] - ItemDefId: {item.ItemDefId}");
                        Log($"[DEBUG] - Origin: {item.Origin}");
                        Log($"[DEBUG] - State: {item.State}");
                        Log($"[DEBUG] - AcquiredTime: {item.AcquiredTime}");
                        Log($"[DEBUG] - StateChangedTime: {item.StateChangedTime}");

                        if (!string.IsNullOrEmpty(item.ItemDefId))
                        {
                            // Используем appId из параметра метода, так как он уже правильного типа
                            Core.TaskWorker.HandleDrop(_account, appId, item.ItemDefId);
                            Log($"[DEBUG] Вызван HandleDrop для appId={appId}, itemDefId={item.ItemDefId}");
                        }
                        else
                        {
                            Log($"[DEBUG] Пропущен предмет с пустым ItemDefId");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"[DEBUG] Ошибка при обработке предмета: {ex.Message}");
                    }
                }
            }
            catch (Newtonsoft.Json.JsonException ex)
            {
                Log($"[DEBUG] Ошибка разбора JSON для игры {appId}: {ex.Message}");
                Log($"[DEBUG] Проблемный JSON: {itemJson}");
            }
        }

        private async void OnDropCheckTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                if (!_isLoggedIn || !_isConnected)
                {
                    Log("Пропуск проверки дропа: не подключен к Steam");
                    return;
                }

                CheckGamesStatus();
                
                var config = MainConfig.GetConfig(_taskNumber);
                if (config.DropConfig != null)
                {
                    Log($"Начинаю проверку дропа для {config.DropConfig.Count} конфигураций...");
                    
                    foreach (var dropConfig in config.DropConfig)
                    {
                        try
                        {
                            Log($"Проверяю дроп для игры {dropConfig.Item1} (itemdefid: {dropConfig.Item2})...");
                            
                            var request = new CInventory_ConsumePlaytime_Request
                            {
                                appid = dropConfig.Item1,
                                itemdefid = uint.Parse(dropConfig.Item2)
                            };

                            Log($"[DEBUG] Создан запрос: appid={request.appid}, itemdefid={request.itemdefid}");
                            Log($"[DEBUG] Отправляю сообщение через SteamUnifiedMessages...");

                            var response = await _inventoryService.SendMessage(x => x.ConsumePlaytime(request));
                            var result = response.GetDeserializedResponse<CInventory_Response>();

                            Log($"[DEBUG] Сообщение отправлено, получен ответ");

                            if (result == null)
                            {
                                Log($"[DEBUG] Получен пустой ответ (result == null) для игры {dropConfig.Item1}");
                                continue;
                            }

                            Log($"[DEBUG] Получен ответ от сервера для игры {dropConfig.Item1}");
                            Log($"Ответ на запрос дропа для игры {dropConfig.Item1}: {result.item_json}");

                            if (string.IsNullOrEmpty(result.item_json))
                            {
                                Log($"[DEBUG] Нет новых предметов (пустой item_json) для игры {dropConfig.Item1}");
                                continue;
                            }

                            Log($"[DEBUG] Получен ответ с item_json: {result.item_json}");
                            await ProcessDropResponse(dropConfig.Item1, result.item_json);
                        }
                        catch (Exception ex) when (ex is TaskCanceledException || ex is TimeoutException)
                        {
                            Log($"[DEBUG] Таймаут при проверке дропа для игры {dropConfig.Item1}: {ex.Message}");
                            Log($"[DEBUG] Тип исключения: {ex.GetType().Name}");
                            Log($"Таймаут при проверке дропа для игры {dropConfig.Item1}, пропускаем до следующей итерации");
                            continue;
                        }
                        catch (Exception ex)
                        {
                            Log($"Ошибка при проверке дропа для игры {dropConfig.Item1}: {ex.Message}");
                        }
                        
                        // Задержка между проверками
                        await Task.Delay(5000);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Неожиданная ошибка при проверке дропов: {ex.Message}");
                Log($"[DEBUG] Стек ошибки: {ex.StackTrace}");
            }
        }

        private void CheckGamesStatus()
        {
            try
            {
                if (!_isLoggedIn || !_isConnected)
                {
                    Log("Невозможно проверить статус игр: не подключен к Steam");
                    return;
                }

                var config = MainConfig.GetConfig(_taskNumber);
                if (config.DropConfig != null && config.DropConfig.Any())
                {
                    var uniqueAppIds = config.DropConfig
                        .Select(x => x.Item1)
                        .Distinct()
                        .ToList();

                    var playGames = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
                    
                    foreach (var appId in uniqueAppIds)
                    {
                        playGames.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
                        {
                            game_id = new GameID(appId)
                        });
                    }

                    Log($"Обновляю статус игр ({uniqueAppIds.Count} игр)...");
                    _steamClient.Send(playGames);
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при проверке статуса игр: {ex.Message}");
            }
        }

        private void OnConnected(SteamClient.ConnectedCallback callback)
        {
            Log("Подключено к Steam!");
            _isConnected = true;
        }

        private void OnDisconnected(SteamClient.DisconnectedCallback callback)
        {
            _isConnected = false;
            _isLoggedIn = false;

            Log("Отключено от Steam");

            if (_isRunning)
            {
                Log("Переподключение...");
                Thread.Sleep(5000);
                _steamClient.Connect();
            }
        }

        private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
        {
            if (callback.Result != EResult.OK)
            {
                string errorMessage = callback.Result switch
                {
                    EResult.AccountLoginDeniedNeedTwoFactor => 
                        $"Требуется двухфакторная аутентификация. Текущий тип auth: {_account.AuthType}, SharedSecret: {(!string.IsNullOrEmpty(_account.SharedSecret))}",
                    EResult.InvalidPassword => 
                        "Неверный пароль",
                    EResult.TwoFactorCodeMismatch => 
                        "Неверный код двухфакторной аутентификации",
                    EResult.ServiceUnavailable => 
                        "Сервис Steam недоступен, попробуйте позже",
                    _ => $"Не удалось войти: {callback.Result}"
                };
                
                Log(errorMessage);
                return;
            }

            _isLoggedIn = true;
            Log("Успешный вход!");

            // Устанавливаем статус
            if (!string.IsNullOrEmpty(_account.ShowStatus))
            {
                _steamFriends.SetPersonaState(EPersonaState.Online);
            }
        }

        private void OnLoggedOff(SteamUser.LoggedOffCallback callback)
        {
            _isLoggedIn = false;
            Log("Выход из Steam");
        }

        public void LogOf()
        {
            try
            {
                Log("Начинаю процесс отключения...");
                
                // 1. Сначала устанавливаем все флаги остановки
                _isRunning = false;
                _isLoggedIn = false;
                _isConnected = false;
                
                // 2. Останавливаем проверку дропа
                if (_dropCheckTimer != null)
                {
                    _dropCheckTimer.Stop();
                    _dropCheckTimer.Dispose();
                    _dropCheckTimer = null;
                }
                
                // 3. Отключаем все игры и выходим из Steam
                if (_steamClient != null)
                {
                    try
                    {
                        if (_steamClient.IsConnected)
                        {
                            // Отключаем игры
                            var playGames = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayed);
                            _steamClient.Send(playGames);
                            
                            // Устанавливаем статус оффлайн перед выходом
                            _steamFriends?.SetPersonaState(EPersonaState.Offline);
                            
                            // Выходим из аккаунта
                            if (_steamUser != null)
                            {
                                _steamUser.LogOff();
                                // Ждем небольшую паузу для корректного выхода
                                Thread.Sleep(500);
                            }
                            
                            // Отключаемся от Steam
                            _steamClient.Disconnect();
                        }
                        
                        // Ждем отключения
                        var disconnectTimeout = DateTime.UtcNow.AddSeconds(5);
                        while (_steamClient.IsConnected && DateTime.UtcNow < disconnectTimeout)
                        {
                            Thread.Sleep(100);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"Ошибка при отключении от Steam: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка при отключении: {ex.Message}");
            }
        }

        public void UpdateDropCheckInterval()
        {
            if (_dropCheckTimer != null)
            {
                var config = MainConfig.GetConfig(_taskNumber);
                _dropCheckTimer.Interval = config.ChkIdleTimeOut * 60000;
            }
        }
    }
} 