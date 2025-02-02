using System;
using System.Windows.Media;
using System.Text.RegularExpressions;

namespace DroplerGUI.Models
{
    public class LogEntry
    {
        public string Text { get; }
        public Brush Color { get; }
        public DateTime Timestamp { get; }

        private static readonly Regex TimeStampRegex = new Regex(@"^\[\d{2}:\d{2}:\d{2}\]");

        public LogEntry(string message)
        {
            Timestamp = DateTime.Now;
            
            // Проверяем, есть ли уже временная метка
            bool hasTimestamp = TimeStampRegex.IsMatch(message.Trim());
            string timestampPrefix = hasTimestamp ? "" : $"[{Timestamp:HH:mm:ss}] ";
            
            // Определяем цвет сообщения на основе его содержимого
            if (message.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || 
                message.Contains("ОШИБКА", StringComparison.OrdinalIgnoreCase))
            {
                Color = Brushes.Red;
                Text = $"{timestampPrefix}[ERROR] {message}";
            }
            else if (message.Contains("WARNING", StringComparison.OrdinalIgnoreCase) || 
                     message.Contains("ПРЕДУПРЕЖДЕНИЕ", StringComparison.OrdinalIgnoreCase))
            {
                Color = Brushes.Yellow;
                Text = $"{timestampPrefix}[WARNING] {message}";
            }
            else if (message.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) || 
                     message.Contains("УСПЕХ", StringComparison.OrdinalIgnoreCase))
            {
                Color = Brushes.Green;
                Text = $"{timestampPrefix}{message}";
            }
            else if (message.Contains("INFO", StringComparison.OrdinalIgnoreCase) || 
                     message.Contains("ИНФО", StringComparison.OrdinalIgnoreCase))
            {
                Color = Brushes.White;
                Text = $"{timestampPrefix}[INFO] {message}";
            }
            else
            {
                Color = Brushes.Gray;
                Text = hasTimestamp ? message : $"{timestampPrefix}{message}";
            }
        }
    }
} 