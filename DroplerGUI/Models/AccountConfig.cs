using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SteamKit2;
using DroplerGUI.Services.Steam;
using DroplerGUI.Core;
using DroplerGUI.Services;
using System.Text;

namespace DroplerGUI.Models
{
    public class AccountConfig
    {
        /// <summary>
        /// Логин аккаунта
        /// </summary>
        [JsonIgnore]
        public string Name { get; set; }

        /// <summary>
        /// Имя бота
        /// </summary>
        [JsonProperty("alias")]
        public string Alias { get; set; }

        /// <summary>
        /// Пароль аккаунта
        /// </summary>
        [JsonProperty("password")]
        public string Password { get; set; }

        /// <summary>
        /// Участвует в фарме
        /// </summary>
        [JsonProperty("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// Показывать статус "Online"
        /// </summary>
        public string ShowStatus { get; set; }

        /// <summary>
        /// Время последнего запуска
        /// </summary>
        public DateTime? LastStartTime { get; set; }

        /// <summary>
        /// Статус запуска аккаунта
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// Текущая игра
        /// </summary>
        public uint? CurrentGameId { get; set; }

        /// <summary>
        /// Название текущей игры
        /// </summary>
        public string CurrentGameName { get; set; }

        /// <summary>
        /// Флаг активного фарма
        /// </summary>
        public bool IdleNow { get; set; }

        /// <summary>
        /// Команда боту
        /// </summary>
        [JsonProperty("action")]
        public string Action { get; set; }

        /// <summary>
        /// Task Id
        /// </summary>
        [JsonIgnore]
        public string TaskID { get; set; }

        /// <summary>
        /// Путь до файла настроек
        /// </summary>
        private string FilePath { get; set; }

        /// <summary>
        /// Упрощенный вход Steam Guard
        /// </summary>
        public string SharedSecret { get; set; }

        /// <summary>
        /// Настройки времени
        /// </summary>
        public TimeConfig TimeConfig { get; set; }

        /// <summary>
        /// Время последнего обновления ключа для авторизации
        /// </summary>
        public uint Ltime { get; set; }

        /// <summary>
        /// Переменная loggeding
        /// </summary>
        public int? Loggeding { get; set; }

        /// <summary>
        /// Ма файл пути
        /// </summary>
        [JsonProperty("mafile_path")]
        public string MaFilePath { get; set; }

        [JsonIgnore]
        private string _taskPath;
        [JsonIgnore]
        private TaskLoggingService _logger;

        public string GetTaskPath()
        {
            if (string.IsNullOrEmpty(_taskPath))
            {
                throw new InvalidOperationException("TaskPath не установлен");
            }
            return _taskPath;
        }

        public void SetTaskPath(string taskPath)
        {
            _taskPath = taskPath;
            _logger = new TaskLoggingService(taskPath, 0); // 0 для общего логгера
        }

        /// <summary>
        /// Конструктор для json
        /// </summary>
        public AccountConfig()
        {
            Action = "none";
            ShowStatus = "Online";
            Enabled = true;
            IsRunning = false;
        }

        /// <summary>
        /// Конструктор по файлу
        /// </summary>
        /// <param name="path"></param>
        public AccountConfig(string path)
        {
            var obj = JsonConvert.DeserializeObject<AccountConfig>(File.ReadAllText(path));
            Password = obj.Password;
            Enabled = obj.Enabled;
            Action = "none";
            ShowStatus = obj.ShowStatus;
            
            var taskNumber = int.Parse(Path.GetDirectoryName(path).Split('_').Last());
            var config = MainConfig.GetConfig(taskNumber);
            
            if (ShowStatus == null)
            {
                ShowStatus = config.ShowStatus;
            }
            IdleNow = obj.IdleNow;
            LastStartTime = obj.LastStartTime;
            SharedSecret = obj.SharedSecret;
            Name = Path.GetFileNameWithoutExtension(path);
            Alias = obj.Alias ?? Name;
            Ltime = obj.Ltime != 0 ? obj.Ltime : 1;

            int naml = Alias.Length;
            if (naml > Util.unmax) {
                Util.unmax = naml;
            }
            TimeConfig = obj.TimeConfig ?? config.TimeConfig ?? new TimeConfig {IdleTime = 1440, PauseBeatwinIdleTime = 1};
            if (config.ChkIdleTimeOut > TimeConfig.IdleTime) TimeConfig.IdleTime = config.ChkIdleTimeOut;
            FilePath = path;

            // Проверка Loggeding
            Loggeding = obj.Loggeding ?? 1;
        }

        /// <summary>
        /// Сохранить изменения
        /// </summary>
        public void Save()
        {
            try
            {
                var taskNumber = int.Parse(GetTaskPath().Split('_').Last());
                var accountsPath = Path.Combine(Constants.GetTaskPath(taskNumber), "Accounts");
                var accountPath = Path.Combine(accountsPath, $"{Name}.json");
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(accountPath, json, Encoding.UTF8);
                _logger?.Log($"[{Alias}] Сохранение конфигурации: IdleNow={IdleNow}, IdleEnable={Enabled}, Action={Action}");
            }
            catch (Exception ex)
            {
                _logger?.Log($"[{Alias}] Ошибка при сохранении конфигурации: {ex.Message}");
            }
        }

        public override string ToString()
        {
            return $"Alias: {Alias}, LastRun: {LastStartTime}";
        }

        public void ResetState(bool resetLastStartTime = false)
        {
            IdleNow = false;
            IsRunning = false;
            Action = "none";
            TaskID = null;
            if (resetLastStartTime)
            {
                LastStartTime = DateTime.MinValue;
            }
        }
    }
}
