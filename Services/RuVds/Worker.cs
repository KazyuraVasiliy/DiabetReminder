using Core.Models;
using Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Telegram.Bot;

namespace Services.RuVds
{
    public class Worker : BackgroundService
    {
        private readonly RuVds.Client _ruvdsClient;
        private readonly RuVds.Models.Parameters _ruvdsParameters;
        private readonly ITelegramBotClient _telegramBotClient;
        private readonly TelegramBotParameters _telegramBotParameters;
        private readonly ILogger<Worker> _logger;

        public Worker(RuVds.Client ruvdsClient, IOptions<RuVds.Models.Parameters> ruvdsParameters, ITelegramBotClient telegramBotClient, TelegramBotParameters telegramBotParameters, ILogger<Worker> logger)
        {
            _ruvdsClient = ruvdsClient;
            _ruvdsParameters = ruvdsParameters.Value;
            _telegramBotClient = telegramBotClient;
            _telegramBotParameters = telegramBotParameters;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _ = PaidMonitoring(cancellationToken);
            _ = StatusMonitoring(cancellationToken);

            await Task.CompletedTask;
        }

        private async Task PaidMonitoring(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();

            while (!cancellationToken.IsCancellationRequested)
            {
                var message = string.Empty;
                var date = DateTime.UtcNow;

                using var _ = _logger.BeginScope(new Dictionary<string, object> 
                { 
                    ["SessionId"] = Guid.NewGuid(),
                    ["MethodName"] = methodName
                });

                try
                {
                    Responses.Server? server = null;
                    Responses.Balance? balance = null;

                    await Policy.Handle<Exception>()
                        .WaitAndRetryAsync(
                            retryCount: 5,
                            x => TimeSpan.FromSeconds(5))
                        .ExecuteAsync(async () =>
                        {
                            server = await _ruvdsClient.GetServer(_ruvdsParameters.ServerId, cancellationToken, _logger);
                            balance = await _ruvdsClient.GetBalance(cancellationToken, _logger);
                        });

                    if (server?.PaidTill == null || server?.Cost?.CostRub == null)
                        throw new Exception("Не удалось получить информацию о сервере");

                    if (balance == null)
                        throw new Exception("Не удалось получить информацию о балансе");

                    if (date.AddDays(3) >= server.PaidTill)
                        message = $"Срок оплаты сервера заканчивается {server.PaidTill:dd.MM.yyyy HH:mm} по UTC. Необходимо оплатить: {server.Cost.CostRub}. Баланс: {balance.Amount}";
                }
                catch (Exception exception)
                {
                    // Даже на очень редкие запросы срабатывает DDoS-Guard, поэтому ошибка 403 отбрасывается
                    if (!(exception is RestException restException && restException.HttpStatusCode == StatusCodes.Status403Forbidden))
                        message = exception.Message;
                }

                try
                {
                    if (message != string.Empty)
                        await _telegramBotClient.SendMessage(_telegramBotParameters.ChatId, message, cancellationToken: cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(message, exception);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_ruvdsParameters.Delay.Paid), cancellationToken);
            }
        }

        private async Task StatusMonitoring(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();

            while (!cancellationToken.IsCancellationRequested)
            {
                var message = string.Empty;
                var delay = _ruvdsParameters.Delay.Status;

                using var _ = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["SessionId"] = Guid.NewGuid(),
                    ["MethodName"] = methodName
                });

                try
                {
                    Responses.Server? server = null;

                    await Policy.Handle<Exception>()
                        .WaitAndRetryAsync(
                            retryCount: 2,
                            x => TimeSpan.FromSeconds(10))
                        .ExecuteAsync(async () =>
                        {
                            server = await _ruvdsClient.GetServer(_ruvdsParameters.ServerId, cancellationToken, _logger);
                        });

                    if (server?.Status != "active")
                        message = $"Сервер отключён! Текущий статус: {server?.Status ?? "unknown"}";
                }
                catch (Exception exception)
                {
                    // Даже на очень редкие запросы срабатывает DDoS-Guard, поэтому ошибка 403 отбрасывается, а задержка увеличивается
                    if (exception is RestException restException && restException.HttpStatusCode == StatusCodes.Status403Forbidden)
                        delay *= 5;
                    else message = exception.Message;
                }

                try
                {
                    if (message != string.Empty)
                        await _telegramBotClient.SendMessage(_telegramBotParameters.ChatId, message, cancellationToken: cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogError(message, exception);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_ruvdsParameters.Delay.Status), cancellationToken);
            }
        }
    }
}
