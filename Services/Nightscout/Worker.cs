using Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

        public Worker(Nightscout.Client nightscoutClient, IOptions<Nightscout.Models.Parameters> nightscoutParameters, ITelegramBotClient telegramBotClient, TelegramBotParameters telegramBotParameters, ILogger<Worker> logger)
        {
            _nightscoutClient = nightscoutClient;
            _nightscoutParameters = nightscoutParameters.Value;
            _telegramBotClient = telegramBotClient;
            _telegramBotParameters = telegramBotParameters;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var chatId = _telegramBotParameters.ChatId;

            while (!cancellationToken.IsCancellationRequested)
            {
                var message = string.Empty;
                var delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Default);

                using var _ = _logger.BeginScope(new Dictionary<string, object> { ["SessionId"] = Guid.NewGuid() });

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

                    if (lastEntry.Glucose <= _nightscoutParameters.Glucose.Hypoglycemia)
                        message = $"Гипогликемия! Глюкоза: {lastEntry.Glucose}";

                    else if (lastEntry.Glucose >= _nightscoutParameters.Glucose.Hyperglycemia)
                        message = $"Гипергликимия! Глюкоза: {lastEntry.Glucose}";

                    else if (lastEntry.Glucose <= _nightscoutParameters.Glucose.LowGlucose && delta <= -_nightscoutParameters.Glucose.Delta)
                        message = $"Резкое падение! Дельта: {delta}; Глюкоза: {lastEntry.Glucose}";

                    else if (lastEntry.Glucose >= _nightscoutParameters.Glucose.HighGlucose && delta >= _nightscoutParameters.Glucose.Delta)
                        message = $"Резкий рост! Дельта: {delta}; Глюкоза: {lastEntry.Glucose}";

                    if (message != string.Empty)
                        delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Warning);
                }
                catch (Exception exception)
                {
                    message = $"Ошибка получения уровня глюкозы! {exception.Message}";
                    delay = TimeSpan.FromMilliseconds(_nightscoutParameters.Delay.Error);
                }

                try
                {
                    if (message != string.Empty)
                        await _telegramBotClient.SendMessage(chatId, message, cancellationToken: cancellationToken);
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
