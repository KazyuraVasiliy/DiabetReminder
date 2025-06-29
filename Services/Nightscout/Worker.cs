using Core.Models;
using Core.Services;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;
using Telegram.Bot;

namespace Services.Nightscout
{
    public class Worker : BackgroundService
    {
        private readonly Nightscout.Client _nightscoutClient;
        private readonly Nightscout.Models.Parameters _nightscoutParameters;
        private readonly ITelegramBotClient _telegramBotClient;
        private readonly TelegramBotParameters _telegramBotParameters;        
        private readonly ILogger<Worker> _logger;
        private readonly IMongoClient? _mongoClient;
        private readonly CalendarService? _calendarService;

        public Worker(Nightscout.Client nightscoutClient, IOptions<Nightscout.Models.Parameters> nightscoutParameters, ITelegramBotClient telegramBotClient, TelegramBotParameters telegramBotParameters, ILogger<Worker> logger, IMongoClient? mongoClient = null, CalendarService? calendarService = null)
        {
            _nightscoutClient = nightscoutClient;
            _nightscoutParameters = nightscoutParameters.Value;
            _telegramBotClient = telegramBotClient;
            _telegramBotParameters = telegramBotParameters;
            _logger = logger;
            _mongoClient = mongoClient;
            _calendarService = calendarService;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _ = GlucoseMonitor(cancellationToken);
            _ = MongoMonitor(cancellationToken);

            await Task.CompletedTask;
        }

        private async Task GlucoseMonitor(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();
            var coefficient = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var message = string.Empty;
                var delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Default);

                using var _ = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["SessionId"] = Guid.NewGuid(),
                    ["MethodName"] = methodName
                });

                try
                {
                    var glucose = new List<Responses.Entry>();

                    await Policy.Handle<Exception>()
                        .WaitAndRetryAsync(
                            retryCount: 5,
                            x => TimeSpan.FromSeconds(5))
                        .ExecuteAsync(async () =>
                        {
                            glucose = await _nightscoutClient.GetCurrentGlucoseAsync(cancellationToken, _logger);
                        });

                    if (glucose.Count == 0)
                        throw new Exception("Сервис доступен, но получить данные о текущем сахаре не удалось");

                    var delta = glucose.Count == 2
                        ? glucose[0].Glucose - glucose[1].Glucose
                        : 0;

                    var lastEntry = glucose[0];
                    var utcDate = DateTime.UtcNow;

                    var totalMinutesAgo = (utcDate - (lastEntry.CreatedAt ?? lastEntry.DateCreated))?.TotalMinutes ?? double.MaxValue;

                    if (Math.Abs(totalMinutesAgo) > 10)
                        throw new Exception("Прошло более 10 минут с последнего измерения");

                    if (_calendarService != null && _nightscoutParameters.Google != null)
                    {
                        var events = await _calendarService.Events.List(_nightscoutParameters.Google.CalendarId).ExecuteAsync();
                        foreach (var @event in events.Items)
                            await _calendarService.Events.Delete(_nightscoutParameters.Google.CalendarId, @event.Id).ExecuteAsync();

                        var newEvent = new Event
                        {
                            Summary = $"Глюкоза {lastEntry.Glucose} Δ {delta}",
                            Description = $"Значение получено {(int)totalMinutesAgo} мин. назад",
                            Start = new EventDateTime
                            {
                                Date = DateTime.Today.ToString("yyyy-MM-dd"),
                                TimeZone = TimeZoneInfo.Local.ToString()
                            },
                            End = new EventDateTime
                            {
                                Date = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
                                TimeZone = TimeZoneInfo.Local.ToString()
                            }
                        };

                        await _calendarService.Events.Insert(newEvent, _nightscoutParameters.Google.CalendarId).ExecuteAsync();
                    }

                    if (lastEntry.Glucose <= _nightscoutParameters.Glucose.Hypoglycemia)
                        message = $"Гипогликемия! Глюкоза: {lastEntry.Glucose}\n" + string.Join(", ", _nightscoutParameters.Users?.Hypoglycemia ?? Array.Empty<string>());

                    else if (lastEntry.Glucose >= _nightscoutParameters.Glucose.Hyperglycemia)
                        message = $"Гипергликимия! Глюкоза: {lastEntry.Glucose}\n" + string.Join(", ", _nightscoutParameters.Users?.Hyperglycemia ?? Array.Empty<string>());

                    else if (lastEntry.Glucose <= _nightscoutParameters.Glucose.LowGlucose && delta <= -_nightscoutParameters.Glucose.Delta)
                        message = $"Резкое падение! Дельта: {delta}; Глюкоза: {lastEntry.Glucose}\n" + string.Join(", ", _nightscoutParameters.Users?.LowGlucose ?? Array.Empty<string>());

                    else if (lastEntry.Glucose >= _nightscoutParameters.Glucose.HighGlucose && delta >= _nightscoutParameters.Glucose.Delta)
                        message = $"Резкий рост! Дельта: {delta}; Глюкоза: {lastEntry.Glucose}\n" + string.Join(", ", _nightscoutParameters.Users?.HighGlucose ?? Array.Empty<string>());

                    message = message.TrimEnd('\n');

                    if (message != string.Empty)
                    {
                        delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Warning + _nightscoutParameters.Delay.Warning * 0.5 * coefficient);

                        if (lastEntry.Glucose > _nightscoutParameters.Glucose.LowGlucose)
                            coefficient++;
                    }
                    else coefficient = 0;
                }
                catch (Exception exception)
                {
                    message = $"Ошибка получения уровня глюкозы! {exception.Message}";
                    delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Error);
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

                await Task.Delay(delay, cancellationToken);
            }
        }

        private async Task MongoMonitor(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();

            while (!cancellationToken.IsCancellationRequested)
            {
                using var _ = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["SessionId"] = Guid.NewGuid(),
                    ["MethodName"] = methodName
                });

                if (_mongoClient == null || _nightscoutParameters.Mongo == null)
                {
                    _logger.LogInformation("MongoClient не инициализирован");
                    return;
                }    

                var message = string.Empty;
                var delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Mongo.Delay);                

                try
                {
                    double totalSizeB = 0;

                    await Policy.Handle<Exception>()
                        .WaitAndRetryAsync(
                            retryCount: 5,
                            x => TimeSpan.FromSeconds(5))
                        .ExecuteAsync(async () =>
                        {
                            var database = _mongoClient.GetDatabase(_nightscoutParameters.Mongo.DatabaseName);
                            var stats = await database.RunCommandAsync(new JsonCommand<BsonDocument>("{ dbStats: 1, scale: 1 }"));

                            totalSizeB = stats.GetValue("dataSize").AsInt64 + stats.GetValue("indexSize").AsInt64;
                        });

                    var maxDatabaseSizeMib = _nightscoutParameters.Mongo.MaxDatabaseSizeMib ?? 496;

                    var totalSizeMiB = totalSizeB / 1024d / 1024d;
                    var usedSpacePercent = totalSizeMiB / maxDatabaseSizeMib * 100;

                    if (usedSpacePercent > 100)
                    {
                        _logger.LogWarning("Максимальный размер базы данных MongoDb задан некорректно");
                        return;
                    }

                    if (usedSpacePercent >= (_nightscoutParameters.Mongo.WarningPercent ?? 80))
                        message = $"Размер базы данных превысил допустимое значение: {totalSizeMiB:N2}/{maxDatabaseSizeMib:N2}. Рекомендуется воспользоваться штатным механизмом Nightscout для очистки базы";
                }
                catch (Exception exception)
                {
                    message = $"Ошибка получения данных MongoDb! {exception.Message}";
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

                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
