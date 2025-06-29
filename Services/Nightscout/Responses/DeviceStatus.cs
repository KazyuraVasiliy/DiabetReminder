using System.Text.Json.Serialization;

namespace Services.Nightscout.Responses
{
    public class DeviceStatus
    {
        /// <summary>
        /// Загрузчик
        /// </summary>
        [JsonPropertyName("uploader")]
        public required Uploader Uploader {  get; set; }

        /// <summary>
        /// Помпа
        /// </summary>
        [JsonPropertyName("pump")]
        public Pump? Pump { get; set; }

        /// <summary>
        /// Дата
        /// </summary>
        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; set; }
    }

    public class Uploader
    {
        /// <summary>
        /// Уровень заряда батареи
        /// </summary>
        [JsonPropertyName("battery")]
        public int? Battery { get; set; }
    }

    public class Pump
    {
        /// <summary>
        /// Уровень заряда батареи
        /// </summary>
        [JsonPropertyName("battery")]
        public required Battery Battery { get; set; }
    }

    public class Battery
    {
        /// <summary>
        /// Уровень заряда батареи
        /// </summary>
        [JsonPropertyName("percent")]
        public int Percent { get; set; }
    }
}
