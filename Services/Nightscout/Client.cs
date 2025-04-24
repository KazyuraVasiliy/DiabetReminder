using Microsoft.Extensions.Logging;
using Services.Core;
using Services.Nightscout.Responses;
using System.Security.Cryptography;
using System.Text;

namespace Services.Nightscout
{
    public class Client : RestClient
    {
        private readonly string _apiSecret;

        /// <summary>
        /// Режим поиска глюкозы (зависит от версий приложений, которые пишут в Nightscout)
        /// </summary>
        private string _mode = "created_at";

        public Client(string apiSecret, string baseUri) : base(baseUri)
        {
            var hash = SHA1.HashData(Encoding.UTF8.GetBytes(apiSecret));
            var hashStr = string.Concat(hash.Select(b => b.ToString("x2")));

            _apiSecret = hashStr;
        }

        public virtual async Task<List<Entry>> GetCurrentGlucoseAsync(CancellationToken cancellationToken, ILogger? logger = null)
        {
            using var client = new HttpClient();

            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.Add("api-secret", _apiSecret);

            var date = DateTimeOffset.Now.AddDays(-1).UtcDateTime;
            HttpResponseMessage? response;

            List<Entry>? entries = null;

            if (_mode == "created_at")
            {
                response = await client.GetAsync(_baseUri + $"/entries.json?find[type][$eq]=sgv&find[created_at][$gte]={date:yyyy-MM-dd}&count=2", cancellationToken);
                entries = await ResponseAnalysisAsync<List<Entry>>(response, cancellationToken, logger);
            }

            if ((entries?.Count ?? 0) == 0)
            {
                _mode = "dateString";

                response = await client.GetAsync(_baseUri + $"/entries.json?find[type][$eq]=sgv&find[dateString][$gte]={date:yyyy-MM-dd}&count=2", cancellationToken);
                entries = await ResponseAnalysisAsync<List<Entry>>(response, cancellationToken, logger);
            }

            return entries!;
        }
    }
}
