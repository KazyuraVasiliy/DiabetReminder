using Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Telegram.Bot;

namespace Tests.Nightscout
{
    public class WorkerTest
    {
        private Services.Nightscout.Models.Parameters _nightscoutParameters = new Services.Nightscout.Models.Parameters()
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
                CalendarId = "calendarId",
                Delay = 60000
            },
            Battery = new Services.Nightscout.Models.Battery()
            {
                WarningPercent = 10,
                Devices = ["Bubble", "openaps://Phone"],
                Delay = 60000
            }
        };

        public static IEnumerable<object[]> GlucoseTheoryData()
        {
            yield return new object[] { new decimal[] { 5 }, 5, "" };
            yield return new object[] { new decimal[] { 2 }, 5, "Гипогликемия! Глюкоза: 2\n@User" };
            yield return new object[] { new decimal[] { 12 }, 5, "Гипергликимия! Глюкоза: 12" };
            yield return new object[] { new decimal[] { 4, 5 }, 5, "Резкое падение! Дельта: -1; Глюкоза: 4" };
            yield return new object[] { new decimal[] { 9, 8 }, 5, "Резкий рост! Дельта: 1; Глюкоза: 9" };
            yield return new object[] { new decimal[] { }, 5, "Ошибка получения уровня глюкозы! Сервис доступен, но получить данные о текущем уровне глюкозы в крови не удалось" };
            yield return new object[] { new decimal[] { 5, 6 }, 12, "Ошибка получения уровня глюкозы! Прошло более 10 минут с последнего измерения глюкозы" };
        }

        public static IEnumerable<object[]> MongoTheoryData()
        {
            yield return new object[] { 0, 0, "" };
            yield return new object[] { 262144000, 209715200, "Размер базы данных превысил допустимое значение: 450,00/496,00. Рекомендуется воспользоваться штатным механизмом Nightscout для очистки базы" };
        }

        public static IEnumerable<object[]> BatteryTheoryData()
        {
            yield return new object[] { 5, 7, new string[] { "Низкий уровень заряда Bubble! Уровень заряда: 5%", "Низкий уровень заряда Pump! Уровень заряда: 7%" } };
            yield return new object[] { 8, 100, new string[] { "Низкий уровень заряда Bubble! Уровень заряда: 8%" } };
            yield return new object[] { 50, 60, new string[] { } };
        }

        [Theory]
        [MemberData(nameof(GlucoseTheoryData))]
        public async void Glucose_Notifications_Test(decimal[] glucoses, int minutesInterval, string notification)
        {
            var logger = new Tests.Logger<Services.Nightscout.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var nightscoutClient = new Mock<Services.Nightscout.Client>(string.Empty, string.Empty);

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

            nightscoutClient
                .Setup(x => x.GetCurrentDeviceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(new Services.Nightscout.Responses.DeviceStatus()
                {
                    Uploader = new Services.Nightscout.Responses.Uploader()
                    {
                        Battery = 100
                    },
                    CreatedAt = DateTime.UtcNow
                });

            var worker = new Services.Nightscout.Worker(
                nightscoutClient.Object,
                Options.Create(_nightscoutParameters),
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

        [Theory]
        [MemberData(nameof(MongoTheoryData))]
        public async void Mongo_Notifications_Test(long dataSize, long indexSize, string notification)
        {
            var logger = new Tests.Logger<Services.Nightscout.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var nightscoutClient = new Mock<Services.Nightscout.Client>(string.Empty, string.Empty);

            var telegramBotClient = new Mock<ITelegramBotClient>();
            var telegramBotParameters = new TelegramBotParameters()
            {
                ChatId = 0
            };

            var entries = new List<Services.Nightscout.Responses.Entry>()
            {
                new Services.Nightscout.Responses.Entry()
                {
                    SGV = 5 * 18,
                    CreatedAt = DateTime.UtcNow,
                }
            };

            nightscoutClient
                .Setup(x => x.GetCurrentGlucoseAsync(It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(entries);

            nightscoutClient
                .Setup(x => x.GetCurrentDeviceStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(new Services.Nightscout.Responses.DeviceStatus()
                {
                    Uploader = new Services.Nightscout.Responses.Uploader()
                    {
                        Battery = 100
                    },
                    CreatedAt = DateTime.UtcNow
                });

            var mongoClient = new Mock<IMongoClient>();
            var database = new Mock<IMongoDatabase>();

            mongoClient
                .Setup(x => x.GetDatabase(It.IsAny<string>(), null))
                .Returns(database.Object);

            database
                .Setup(x => x.RunCommandAsync(It.IsAny<JsonCommand<BsonDocument>>(), null, default))
                .ReturnsAsync(new BsonDocument(new Dictionary<string, long>()
                {
                    ["dataSize"] = dataSize,
                    ["indexSize"] = indexSize
                }));

            var worker = new Services.Nightscout.Worker(
                nightscoutClient.Object,
                Options.Create(_nightscoutParameters),
                telegramBotClient.Object,
                telegramBotParameters,
                logger,
                mongoClient.Object);

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

        [Theory]
        [MemberData(nameof(BatteryTheoryData))]
        public async void Battery_Notifications_Test(int bubbleBattery, int pumpBattery, string[] notifications)
        {
            var logger = new Tests.Logger<Services.Nightscout.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var nightscoutClient = new Mock<Services.Nightscout.Client>(string.Empty, string.Empty);

            var telegramBotClient = new Mock<ITelegramBotClient>();
            var telegramBotParameters = new TelegramBotParameters()
            {
                ChatId = 0
            };

            var entries = new List<Services.Nightscout.Responses.Entry>()
            {
                new Services.Nightscout.Responses.Entry()
                {
                    SGV = 5 * 18,
                    CreatedAt = DateTime.UtcNow,
                }
            };

            nightscoutClient
                .Setup(x => x.GetCurrentGlucoseAsync(It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(entries);

            nightscoutClient
                .Setup(x => x.GetCurrentDeviceStatusAsync("Bubble", It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(new Services.Nightscout.Responses.DeviceStatus()
                {
                    Uploader = new Services.Nightscout.Responses.Uploader()
                    {
                        Battery = bubbleBattery
                    },
                    CreatedAt = DateTime.UtcNow
                });

            nightscoutClient
                .Setup(x => x.GetCurrentDeviceStatusAsync("openaps://Phone", It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(new Services.Nightscout.Responses.DeviceStatus()
                {
                    Uploader = new Services.Nightscout.Responses.Uploader(),
                    Pump = new Services.Nightscout.Responses.Pump()
                    {
                        Battery = new Services.Nightscout.Responses.Battery()
                        {
                            Percent = pumpBattery
                        }
                    },
                    CreatedAt = DateTime.UtcNow
                });

            var worker = new Services.Nightscout.Worker(
                nightscoutClient.Object,
                Options.Create(_nightscoutParameters),
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
            string[] messages = invocations?.Count > 0
                ? invocations!
                    .Select(x => ((x.Arguments[0] as dynamic).Text as string) ?? string.Empty)
                    .ToArray()
                : Array.Empty<string>();

            messages.Should().BeEquivalentTo(notifications);
        }
    }
}
