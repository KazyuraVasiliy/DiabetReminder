using System.Text.Json.Serialization;

namespace Services.RuVds.Responses
{
    public class Balance
    {
        /// <summary>
        /// Баланс
        /// </summary>
        [JsonPropertyName("amount")]
        public double Amount { get; set; }
    }
}
