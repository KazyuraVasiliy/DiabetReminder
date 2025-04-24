using Core.Models;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Services.Core
{
    public abstract class RestClient
    {
        /// <summary>
        /// Адрес API
        /// </summary>
        protected readonly string _baseUri;

        /// <summary>
        /// Конструктор
        /// </summary>
        public RestClient(string baseUri)
        {
            _baseUri = baseUri;
        }

        /// <summary>
        /// Анализирует ответ и возвращает десериализованные данные
        /// </summary>
        /// <typeparam name="T">Тип</typeparam>
        /// <param name="response">Ответ</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        protected async Task<T?> ResponseAnalysisAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken, ILogger? logger = null) where T : class
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (logger != null)
                logger.LogInformation($"Response {body}");

            if (response.IsSuccessStatusCode)
            {
                return !string.IsNullOrWhiteSpace(body)
                    ? JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions() { PropertyNameCaseInsensitive = true })
                    : default;
            }

            var message = "В результате выполнения запроса возникла ошибка: ";
            message += $"{(int)response.StatusCode} {ReasonPhrases.GetReasonPhrase((int)response.StatusCode)}";

            if (!string.IsNullOrWhiteSpace(body))
                message += $"\n{body}";

            throw new RestException(1, (int)response.StatusCode, message);
        }
    }
}
