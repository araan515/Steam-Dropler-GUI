using System.Collections.Generic;

namespace DroplerGUI.Models
{
    public class GameDropStatistics
    {
        public string GameId { get; set; }
        public int TotalDrops { get; set; }
        public Dictionary<string, int> DropsPerAppId { get; set; }

        public GameDropStatistics()
        {
            DropsPerAppId = new Dictionary<string, int>();
        }
    }
} 