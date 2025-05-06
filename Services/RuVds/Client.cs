using Microsoft.Extensions.Logging;
using Services.Core;
using Services.RuVds.Responses;
using System.Net.Http.Headers;

namespace Services.RuVds
{
    /// <summary>
    /// API RUVDS https://ruvds.com/api-docs
    /// </summary>
    public class Client : RestClient
    {
        private readonly string _token;

        public Client(string token, string baseUri) : base(baseUri)
        {
            _token = token;
        }

        public virtual async Task<Server> GetServer(int serverId, CancellationToken cancellationToken, ILogger? logger = null)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            var response = await client.GetAsync(_baseUri + $"/servers/{serverId}", cancellationToken);
            var server = await ResponseAnalysisAsync<Server>(response, cancellationToken, logger);

            // В документации указано, что можно получить срок, до которого оплачен сервер, в предыдущем endpoint добавив параметр get_paid_till=true, одна такой запрос не работает
            response = await client.GetAsync(_baseUri + $"/servers/{serverId}/paid_till", cancellationToken);
            var paidTill = await ResponseAnalysisAsync<Server>(response, cancellationToken, logger);

            response = await client.GetAsync(_baseUri + $"/servers/{serverId}/cost", cancellationToken);
            var cost = await ResponseAnalysisAsync<Cost>(response, cancellationToken, logger);

            server!.PaidTill = paidTill!.PaidTill;
            server!.Cost = cost;

            return server!;
        }

        public virtual async Task<Balance> GetBalance(CancellationToken cancellationToken, ILogger? logger = null)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

            var response = await client.GetAsync(_baseUri + $"/balance", cancellationToken);
            var balance = await ResponseAnalysisAsync<Balance>(response, cancellationToken, logger);

            return balance!;
        }
    }
}
