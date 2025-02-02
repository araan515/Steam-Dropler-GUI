using System;
using Newtonsoft.Json;

namespace DroplerGUI.Models
{
    public class DropResult
    {
        [JsonProperty("accountid")]
        public string AccountId { get; set; }

        [JsonProperty("itemid")]
        public string ItemId { get; set; }

        [JsonProperty("quantity")]
        public int Quantity { get; set; }

        [JsonProperty("originalitemid")]
        public string OriginalItemId { get; set; }

        [JsonProperty("itemdefid")]
        public string ItemDefId { get; set; }

        [JsonProperty("appid")]
        public int AppId { get; set; }

        [JsonProperty("acquired")]
        public string Acquired { get; set; }

        [JsonProperty("state")]
        public string State { get; set; }

        [JsonProperty("origin")]
        public string Origin { get; set; }

        [JsonProperty("state_changed_timestamp")]
        public string StateChangedTimestamp { get; set; }

        [JsonIgnore]
        public DateTime AcquiredTime => DateTime.ParseExact(Acquired, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

        [JsonIgnore]
        public DateTime StateChangedTime => DateTime.ParseExact(StateChangedTimestamp, "yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
    }
} 