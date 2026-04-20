using Core.Models;
using Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Services.Message;
using Services.Message.Models;

namespace Services.RuVds
{
    public class Worker : BackgroundService
    {
        private readonly RuVds.Client _ruvdsClient;
        private readonly RuVds.Models.Parameters _ruvdsParameters;
        private readonly IMessageQueue _messageQueue;
        private readonly ILogger<Worker> _logger;

        public Worker(RuVds.Client ruvdsClient, IOptions<RuVds.Models.Parameters> ruvdsParameters, IMessageQueue messageQueue, ILogger<Worker> logger)
        {
            _ruvdsClient = ruvdsClient;
            _ruvdsParameters = ruvdsParameters.Value;
            _messageQueue = messageQueue;
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
                Message.Models.Message? message = null;
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
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.ServerPaid,
                            Body = $"Срок оплаты сервера заканчивается {server.PaidTill:dd.MM.yyyy HH:mm} по UTC. Необходимо оплатить: {server.Cost.CostRub}. Баланс: {balance.Amount}",
                            Users = _ruvdsParameters.Users?.Paid,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_ruvdsParameters.Channels?.Paid)
                        };
                    }
                }
                catch (Exception exception)
                {
                    // Даже на очень редкие запросы срабатывает DDoS-Guard, поэтому ошибка 403 отбрасывается
                    if (!(exception is RestException restException && restException.HttpStatusCode == StatusCodes.Status403Forbidden))
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.Error,
                            Body = $"Ошибка получения статуса оплаты сервера! {exception.Message}",
                            Users = _ruvdsParameters.Users?.Error,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_ruvdsParameters.Channels?.Error)
                        };
                    }
                }

                try
                {
                    if (message != null)
                        await _messageQueue.EnqueueAsync(message);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, $"Ошибка отправки сообщения в очередь: {message!.Body}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_ruvdsParameters.Delay.Paid), cancellationToken);
            }
        }

        private async Task StatusMonitoring(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();

            while (!cancellationToken.IsCancellationRequested)
            {
                Message.Models.Message? message = null;
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
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.ServerStatus,
                            Body = $"Сервер отключён! Текущий статус: {server?.Status ?? "unknown"}",
                            Users = _ruvdsParameters.Users?.Status,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_ruvdsParameters.Channels?.Status)
                        };
                    }
                }
                catch (Exception exception)
                {
                    // Даже на очень редкие запросы срабатывает DDoS-Guard, поэтому ошибка 403 отбрасывается, а задержка увеличивается
                    if (exception is RestException restException && restException.HttpStatusCode == StatusCodes.Status403Forbidden)
                        delay *= 5;
                    else
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.Error,
                            Body = $"Ошибка получения статуса сервера! {exception.Message}",
                            Users = _ruvdsParameters.Users?.Error,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_ruvdsParameters.Channels?.Error)
                        };
                    }
                }

                try
                {
                    if (message != null)
                        await _messageQueue.EnqueueAsync(message);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, $"Ошибка отправки сообщения в очередь: {message!.Body}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(_ruvdsParameters.Delay.Status), cancellationToken);
            }
        }
    }
}
