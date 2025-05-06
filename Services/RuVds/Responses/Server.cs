using System.Text.Json.Serialization;

namespace Services.RuVds.Responses
{
    public class Server
    {
        /// <summary>
        /// Id сервера
        /// </summary>
        [JsonPropertyName("virtual_server_id")]
        public required int ServerId { get; set; }

        /// <summary>
        /// Статус сервера (Enum: "initializing", "active", "notpaid", "blocked", "deleted")
        /// </summary>
        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>
        /// Дата и время UTC до которой оплачен сервер
        /// </summary>
        [JsonPropertyName("paid_till")]
        public DateTime? PaidTill { get; set; }

        /// <summary>
        /// Стоимость сервера
        /// </summary>
        public Cost? Cost { get; set; }
    }

    public class Cost
    {
        /// <summary>
        /// Стоимость сервера в рублях за указанный период
        /// </summary>
        [JsonPropertyName("cost_rub")]
        public double? CostRub { get; set; }

        /// <summary>
        /// Период (Enum: 1 - тестовый период, 2 - 1 месяц, 3 - 3 месяца, 4 - 6 месяцев, 5 - 1 год, 0 - Не задан)
        /// </summary>
        [JsonPropertyName("payment_period")]
        public int? PaymentPeriod { get; set; }
    }
}
