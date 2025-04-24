using System.Text.Json.Serialization;

namespace Services.Nightscout.Responses
{
    public class Entry
    {
        /// <summary>
        /// Глюкоза мг/дл
        /// </summary>
        [JsonPropertyName("sgv")]
        public decimal SGV { get; set; }

        /// <summary>
        /// Глюкоза ммол/л
        /// </summary>
        public decimal Glucose =>
            Math.Round(SGV / 18, 1);

        /// <summary>
        /// Дата в новом формате
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset? CreatedAt { get; set; }

        /// <summary>
        /// Дата в устаревшем формате
        /// </summary>
        [JsonPropertyName("dateString")]
        public DateTimeOffset? DateCreated { get; set; }
    }
}
