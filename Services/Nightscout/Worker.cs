using Core.Services;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Polly;
using Services.Message;
using Services.Message.Models;

namespace Services.Nightscout
{
    public class Worker : BackgroundService
    {
        private readonly Nightscout.Client _nightscoutClient;
        private readonly Nightscout.Models.Parameters _nightscoutParameters;
        private readonly IMessageQueue _messageQueue;
        private readonly ILogger<Worker> _logger;
        private readonly IMongoClient? _mongoClient;
        private readonly CalendarService? _calendarService;

        public Worker(Nightscout.Client nightscoutClient, IOptions<Nightscout.Models.Parameters> nightscoutParameters, IMessageQueue messageQueue, ILogger<Worker> logger, IMongoClient? mongoClient = null, CalendarService? calendarService = null)
        {
            _nightscoutClient = nightscoutClient;
            _nightscoutParameters = nightscoutParameters.Value;
            _messageQueue = messageQueue;
            _logger = logger;
            _mongoClient = mongoClient;
            _calendarService = calendarService;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            _ = GlucoseMonitor(cancellationToken);
            _ = EventCreator(cancellationToken);
            _ = MongoMonitor(cancellationToken);
            _ = BatteryMonitor(cancellationToken);

            await Task.CompletedTask;
        }

        private async Task GlucoseMonitor(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();

            while (!cancellationToken.IsCancellationRequested)
            {
                Message.Models.Message? message = null;
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
                        throw new Exception("Сервис доступен, но получить данные о текущем уровне глюкозы в крови не удалось");

                    var delta = glucose.Count == 2
                        ? glucose[0].Glucose - glucose[1].Glucose
                        : 0;

                    var lastEntry = glucose[0];
                    var utcDate = DateTime.UtcNow;

                    var totalMinutesAgo = (utcDate - lastEntry.MeasurementDate)?.TotalMinutes ?? double.MaxValue;

                    if (Math.Abs(totalMinutesAgo) > 10)
                        throw new Exception("Прошло более 10 минут с последнего измерения глюкозы");

                    if (lastEntry.Glucose <= _nightscoutParameters.Glucose.Hypoglycemia)
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.Hypoglycemia,
                            Body = $"Гипогликемия! Глюкоза: {lastEntry.Glucose}",
                            Users = _nightscoutParameters.Users?.Hypoglycemia,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.Hypoglycemia)
                        };
                    }

                    else if (lastEntry.Glucose >= _nightscoutParameters.Glucose.Hyperglycemia)
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.Hyperglycemia,
                            Body = $"Гипергликимия! Глюкоза: {lastEntry.Glucose}",
                            Users = _nightscoutParameters.Users?.Hyperglycemia,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.Hyperglycemia)
                        };
                    }

                    else if (lastEntry.Glucose <= _nightscoutParameters.Glucose.LowGlucose && delta <= -_nightscoutParameters.Glucose.Delta)
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.LowGlucose,
                            Body = $"Резкое падение! Дельта: {delta}; Глюкоза: {lastEntry.Glucose}",
                            Users = _nightscoutParameters.Users?.LowGlucose,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.LowGlucose)
                        };
                    }

                    else if (lastEntry.Glucose >= _nightscoutParameters.Glucose.HighGlucose && delta >= _nightscoutParameters.Glucose.Delta)
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.HighGlucose,
                            Body = $"Резкий рост! Дельта: {delta}; Глюкоза: {lastEntry.Glucose}",
                            Users = _nightscoutParameters.Users?.HighGlucose,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.HighGlucose)
                        };
                    }

                    if (message != null)
                        delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Warning);
                }
                catch (Exception exception)
                {
                    message = new Message.Models.Message()
                    {
                        Type = Message.Models.Types.Error,
                        Body = $"Ошибка получения уровня глюкозы! {exception.Message}",
                        Users = _nightscoutParameters.Users?.Error,
                        Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.Error)
                    };

                    delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Error);
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

                await Task.Delay(delay, cancellationToken);
            }
        }

        private async Task EventCreator(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();

            while (!cancellationToken.IsCancellationRequested)
            {
                using var _ = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["SessionId"] = Guid.NewGuid(),
                    ["MethodName"] = methodName
                });

                if (_calendarService == null || _nightscoutParameters.Google == null)
                {
                    _logger.LogInformation("CalendarService не инициализирован");
                    return;
                }

                var delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Google.Delay);

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
                        throw new Exception("Сервис доступен, но получить данные о текущем уровне глюкозы в крови не удалось");

                    var delta = glucose.Count == 2
                        ? glucose[0].Glucose - glucose[1].Glucose
                        : 0;

                    var lastEntry = glucose[0];
                    var utcDate = DateTime.UtcNow;

                    var totalMinutesAgo = (utcDate - lastEntry.MeasurementDate)?.TotalMinutes ?? double.MaxValue;

                    if (Math.Abs(totalMinutesAgo) > 10)
                        throw new Exception("Прошло более 10 минут с последнего измерения глюкозы");

                    var eventsRequest = _calendarService.Events.List(_nightscoutParameters.Google.CalendarId);
                    eventsRequest.SingleEvents = true;

                    var events = await eventsRequest.ExecuteAsync();
                    foreach (var @event in events.Items)
                        await _calendarService.Events.Delete(_nightscoutParameters.Google.CalendarId, @event.Id).ExecuteAsync();

                    var colorId = Constants.GoogleCalendar.EventColors.Sage;

                    if (lastEntry.Glucose <= _nightscoutParameters.Glucose.Hypoglycemia || lastEntry.Glucose >= _nightscoutParameters.Glucose.Hyperglycemia)
                        colorId = Constants.GoogleCalendar.EventColors.Tomato;

                    else if (lastEntry.Glucose <= _nightscoutParameters.Glucose.LowGlucose || lastEntry.Glucose >= _nightscoutParameters.Glucose.HighGlucose)
                        colorId = Constants.GoogleCalendar.EventColors.Banana;

                    var newEvent = new Event
                    {
                        Summary = $"Глюкоза {lastEntry.Glucose} Δ {delta} от {DateTime.Now.AddMinutes(-totalMinutesAgo):HH:mm:ss dd.MM.yy}",
                        ColorId = colorId,
                        Start = new EventDateTime
                        {
                            Date = DateTime.Today.ToString("yyyy-MM-dd"),
                            TimeZone = TimeZoneInfo.Local.ToString()
                        },
                        End = new EventDateTime
                        {
                            Date = DateTime.Today.AddDays(1).ToString("yyyy-MM-dd"),
                            TimeZone = TimeZoneInfo.Local.ToString()
                        },
                        Transparency = "transparent"
                    };

                    await _calendarService.Events.Insert(newEvent, _nightscoutParameters.Google.CalendarId).ExecuteAsync();
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Ошибка получения уровня глюкозы");
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

                Message.Models.Message? message = null;
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
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.DatabaseSpace,
                            Body = $"Размер базы данных превысил допустимое значение: {totalSizeMiB:N2}/{maxDatabaseSizeMib:N2}. Рекомендуется воспользоваться штатным механизмом Nightscout для очистки базы",
                            Users = _nightscoutParameters.Users?.Mongo,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.Mongo)
                        };
                    }
                }
                catch (Exception exception)
                {
                    message = new Message.Models.Message()
                    {
                        Type = Message.Models.Types.Error,
                        Body = $"Ошибка получения данных MongoDb! {exception.Message}",
                        Users = _nightscoutParameters.Users?.Error,
                        Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.Error)
                    };
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

                await Task.Delay(delay, cancellationToken);
            }
        }

        private async Task BatteryMonitor(CancellationToken cancellationToken)
        {
            var methodName = ReflectionService.GetMethodName();

            while (!cancellationToken.IsCancellationRequested)
            {
                using var _ = _logger.BeginScope(new Dictionary<string, object>
                {
                    ["SessionId"] = Guid.NewGuid(),
                    ["MethodName"] = methodName
                });

                if ((_nightscoutParameters.Battery?.Devices?.Count() ?? 0) == 0)
                {
                    _logger.LogInformation("Не указано ни одного устройства для мониторинга батареи");
                    return;
                }

                var delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Battery!.Delay);

                foreach (var device in _nightscoutParameters.Battery!.Devices!)
                {
                    Message.Models.Message? message = null;
                    Responses.DeviceStatus? deviceStatus = null;

                    try
                    {
                        await Policy.Handle<Exception>()
                            .WaitAndRetryAsync(
                                retryCount: 5,
                                x => TimeSpan.FromSeconds(5))
                            .ExecuteAsync(async () =>
                            {
                                deviceStatus = await _nightscoutClient.GetCurrentDeviceStatusAsync(device, cancellationToken, _logger);
                            });

                        if (deviceStatus == null)
                            throw new Exception($"Сервис доступен, но получить данные о текущем уровне заряда {device} не удалось");

                        var deviceName = deviceStatus.Pump != null
                            ? "Pump"
                            : device;

                        var utcDate = DateTime.UtcNow;
                        var totalMinutesAgo = (utcDate - deviceStatus.CreatedAt).TotalMinutes;

                        if (Math.Abs(totalMinutesAgo) > 30)
                            throw new Exception($"Прошло более 30 минут с последнего измерения заряда батареи {deviceName}");

                        var battery = deviceStatus.Pump != null
                            ? deviceStatus.Pump.Battery.Percent
                            : deviceStatus.Uploader.Battery;

                        if (battery <= _nightscoutParameters.Battery!.WarningPercent)
                        {
                            message = new Message.Models.Message()
                            {
                                Type = Message.Models.Types.BatteryLevel,
                                Body = $"Низкий уровень заряда {deviceName}! Уровень заряда: {battery}%",
                                Users = _nightscoutParameters.Users?.Battery,
                                Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.Battery)
                            };
                        }
                    }
                    catch (Exception exception)
                    {
                        message = new Message.Models.Message()
                        {
                            Type = Message.Models.Types.Error,
                            Body = $"Ошибка получения уровня заряда {device}! {exception.Message}",
                            Users = _nightscoutParameters.Users?.Error,
                            Channel = EnumService.ConvertToEnumFlag<Channels>(_nightscoutParameters.Channels?.Error)
                        };
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
                }

                await Task.Delay(delay, cancellationToken);
            }
        }
    }
}
