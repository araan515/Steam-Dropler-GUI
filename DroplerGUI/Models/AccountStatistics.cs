using System;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;

namespace DroplerGUI.Models
{
    public class AccountStatistics
    {
        [JsonPropertyName("accountName")]
        public string AccountName { get; set; }

        [JsonPropertyName("totalDropsCount")]
        public int TotalDropsCount { get; set; }

        [JsonPropertyName("lastDropTime")]
        public DateTime? LastDropTime { get; set; }

        [JsonPropertyName("isActive")]
        public string IsActive { get; set; }

        [JsonPropertyName("lastConnectionTime")]
        public DateTime? LastConnectionTime { get; set; }

        [JsonIgnore]
        public string OnlineTime { get; set; }

        [JsonPropertyName("Drops")]
        public Dictionary<string, int> Drops { get; set; } = new Dictionary<string, int>();

        [JsonIgnore]
        public List<GameDropStatistics> GameDrops 
        {
            get
            {
                var gameDrops = new Dictionary<string, GameDropStatistics>();

                foreach (var drop in Drops)
                {
                    var parts = drop.Key.Split('_');
                    if (parts.Length != 2) continue;

                    var gameId = parts[0];
                    var appId = parts[1];

                    if (!gameDrops.ContainsKey(gameId))
                    {
                        gameDrops[gameId] = new GameDropStatistics { GameId = gameId };
                    }

                    gameDrops[gameId].DropsPerAppId[appId] = drop.Value;
                    gameDrops[gameId].TotalDrops += drop.Value;
                }

                return gameDrops.Values.OrderByDescending(g => g.TotalDrops).ToList();
            }
        }

        public AccountStatistics()
        {
            IsActive = "Offline";
            TotalDropsCount = 0;
        }

        public AccountStatistics(string accountName) : this()
        {
            AccountName = accountName;
        }
    }
} 