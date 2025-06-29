using Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;

namespace Tests.Nightscout
{
    public class WorkerTest
    {
        public static IEnumerable<object[]> TheoryData()
        {
            yield return new object[] { new decimal[] { 5 }, 5, "" };
            yield return new object[] { new decimal[] { 2 }, 5, "Гипогликемия! Глюкоза: 2\n@User" };
            yield return new object[] { new decimal[] { 12 }, 5, "Гипергликимия! Глюкоза: 12" };
            yield return new object[] { new decimal[] { 4, 5 }, 5, "Резкое падение! Дельта: -1; Глюкоза: 4" };
            yield return new object[] { new decimal[] { 9, 8 }, 5, "Резкий рост! Дельта: 1; Глюкоза: 9" };
            yield return new object[] { new decimal[] { }, 5, "Ошибка получения уровня глюкозы! Сервис доступен, но получить данные о текущем сахаре не удалось" };
            yield return new object[] { new decimal[] { 5, 6 }, 12, "Ошибка получения уровня глюкозы! Прошло более 10 минут с последнего измерения" };
        }

        [Theory]
        [MemberData(nameof(TheoryData))]
        public async void Notifications_Test(decimal[] glucoses, int minutesInterval, string notification)
        {
            var logger = new Tests.Logger<Services.Nightscout.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var nightscoutClient = new Mock<Services.Nightscout.Client>(string.Empty, string.Empty);
            var nightscoutParameters = new Services.Nightscout.Models.Parameters()
            {
                Glucose = new Services.Nightscout.Models.Glucose()
                {
                    Delta = 0.3m,
                    Hyperglycemia = 11m,
                    Hypoglycemia = 3.9m,
                    LowGlucose = 4.5m,
                    HighGlucose = 7.9m
                },
                Delay = new Services.Nightscout.Models.Delay()
                {
                    Default = 60000,
                    Warning = 300000,
                    Error = 600000
                },
                Users = new Services.Nightscout.Models.Users()
                {
                    Hypoglycemia = ["@User"]
                },
                Mongo = new Services.Nightscout.Models.Mongo()
                {
                    ConnectionString = "mongo",
                    DatabaseName = "database",
                    Delay = 60000,
                    MaxDatabaseSizeMib = 496,
                    WarningPercent = 80
                },
                Google = new Services.Nightscout.Models.Google()
                {
                    CalendarId = "calendarId"
                }
            };

            var telegramBotClient = new Mock<ITelegramBotClient>();
            var telegramBotParameters = new TelegramBotParameters()
            {
                ChatId = 0
            };

            var entries = glucoses
                .Select(x =>
                    new Services.Nightscout.Responses.Entry()
                    {
                        SGV = x * 18,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-1 * minutesInterval * (Array.IndexOf(glucoses, x) + 1)),
                    })
                .ToList();

            nightscoutClient
                .Setup(x => x.GetCurrentGlucoseAsync(It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(entries);

            var worker = new Services.Nightscout.Worker(
                nightscoutClient.Object,
                Options.Create(nightscoutParameters),
                telegramBotClient.Object,
                telegramBotParameters,
                logger);

            await worker.StartAsync(cancellationTokenSource.Token);
            IInvocationList? invocations = null;

            int iteration = 2;
            while (iteration > 0)
            {
                invocations = telegramBotClient.Invocations;
                if (invocations.Count > 0)
                    break;

                await Task.Delay(500);
                iteration--;
            }

            cancellationTokenSource.Cancel();
            var message = invocations?.Count > 0
                ? (invocations![0].Arguments[0] as dynamic).Text as string
                : string.Empty;

            message.Should().Be(notification);
        }
    }
}
