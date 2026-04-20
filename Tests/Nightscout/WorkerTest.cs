using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Services.Message;
using Services.Message.Models;

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
            },
            Channels = new Services.Nightscout.Models.Channels()
            {
                Hypoglycemia = ["Telegram"],
                Mongo = ["Email"]
            }
        };

        public static IEnumerable<object?[]> GlucoseTheoryData()
        {
            yield return new object?[] { new decimal[] { 5 }, 5, null };
            yield return new object?[] { new decimal[] { 2 }, 5, new Message()
            {
                Type = Types.Hypoglycemia,
                Body = "Гипогликемия! Глюкоза: 2",
                Channel = Channels.Telegram,
                Users = ["@User"]
            }};
            yield return new object[] { new decimal[] { 12 }, 5, new Message()
            {
                Type = Types.Hyperglycemia,
                Body = "Гипергликимия! Глюкоза: 12",
                Channel = Channels.None,
                Users = null
            }};
            yield return new object[] { new decimal[] { 4, 5 }, 5, new Message()
            {
                Type = Types.LowGlucose,
                Body = "Резкое падение! Дельта: -1; Глюкоза: 4",
                Channel = Channels.None,
                Users = null
            }};
            yield return new object[] { new decimal[] { 9, 8 }, 5, new Message()
            {
                Type = Types.HighGlucose,
                Body = "Резкий рост! Дельта: 1; Глюкоза: 9",
                Channel = Channels.None,
                Users = null
            }};
            yield return new object[] { new decimal[] { }, 5, new Message()
            {
                Type = Types.Error,
                Body = "Ошибка получения уровня глюкозы! Сервис доступен, но получить данные о текущем уровне глюкозы в крови не удалось",
                Channel = Channels.None,
                Users = null
            }};
            yield return new object[] { new decimal[] { 5, 6 }, 12, new Message()
            {
                Type = Types.Error,
                Body = "Ошибка получения уровня глюкозы! Прошло более 10 минут с последнего измерения глюкозы",
                Channel = Channels.None,
                Users = null
            }};
        }

        public static IEnumerable<object?[]> MongoTheoryData()
        {
            yield return new object?[] { 0, 0, null };
            yield return new object?[] { 262144000, 209715200, new Message()
            {
                Type = Types.DatabaseSpace,
                Body = "Размер базы данных превысил допустимое значение: 450,00/496,00. Рекомендуется воспользоваться штатным механизмом Nightscout для очистки базы",
                Channel = Channels.Email,
                Users = null
            }};
        }

        public static IEnumerable<object[]> BatteryTheoryData()
        {
            yield return new object[] { 5, 7, new List<Message>()
            {
                new Message()
                {
                    Type = Types.BatteryLevel,
                    Body = "Низкий уровень заряда Bubble! Уровень заряда: 5%",
                    Channel = Channels.None
                },
                new Message()
                {
                    Type = Types.BatteryLevel,
                    Body = "Низкий уровень заряда Pump! Уровень заряда: 7%",
                    Channel = Channels.None
                }
            }};
            yield return new object[] { 8, 100, new List<Message>() { new Message()
            {
                Type = Types.BatteryLevel,
                Body = "Низкий уровень заряда Bubble! Уровень заряда: 8%",
                Channel = Channels.None
            }}};
            yield return new object[] { 50, 60, new List<Message>() };
        }

        [Theory]
        [MemberData(nameof(GlucoseTheoryData))]
        public async void Glucose_Notifications_Test(decimal[] glucoses, int minutesInterval, Message expectedMessage)
        {
            var logger = new Tests.Logger<Services.Nightscout.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var nightscoutClient = new Mock<Services.Nightscout.Client>(string.Empty, string.Empty);

            Message? actualMessage = null;

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

            var messageQueue = new Mock<IMessageQueue>();
            messageQueue
                .Setup(x => x.EnqueueAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Callback<Message, CancellationToken>((x, y) => actualMessage = x);

            var worker = new Services.Nightscout.Worker(
                nightscoutClient.Object,
                Options.Create(_nightscoutParameters),
                messageQueue.Object,
                logger);

            await worker.StartAsync(cancellationTokenSource.Token);

            int iteration = 2;
            while (iteration > 0)
            {
                if (actualMessage != null)
                    break;

                await Task.Delay(500);
                iteration--;
            }

            cancellationTokenSource.Cancel();
            actualMessage.Should().BeEquivalentTo(expectedMessage);
        }

        [Theory]
        [MemberData(nameof(MongoTheoryData))]
        public async void Mongo_Notifications_Test(long dataSize, long indexSize, Message expectedMessage)
        {
            var logger = new Tests.Logger<Services.Nightscout.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var nightscoutClient = new Mock<Services.Nightscout.Client>(string.Empty, string.Empty);

            Message? actualMessage = null;

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

            var messageQueue = new Mock<IMessageQueue>();
            messageQueue
                .Setup(x => x.EnqueueAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Callback<Message, CancellationToken>((x, y) => actualMessage = x);

            var worker = new Services.Nightscout.Worker(
                nightscoutClient.Object,
                Options.Create(_nightscoutParameters),
                messageQueue.Object,
                logger,
                mongoClient.Object);

            await worker.StartAsync(cancellationTokenSource.Token);

            int iteration = 2;
            while (iteration > 0)
            {
                if (actualMessage != null)
                    break;

                await Task.Delay(500);
                iteration--;
            }

            cancellationTokenSource.Cancel();
            actualMessage.Should().BeEquivalentTo(expectedMessage);
        }

        [Theory]
        [MemberData(nameof(BatteryTheoryData))]
        public async void Battery_Notifications_Test(int bubbleBattery, int pumpBattery, List<Message> expectedMessages)
        {
            var logger = new Tests.Logger<Services.Nightscout.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var nightscoutClient = new Mock<Services.Nightscout.Client>(string.Empty, string.Empty);

            List<Message> actualMessages = new();

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

            var messageQueue = new Mock<IMessageQueue>();
            messageQueue
                .Setup(x => x.EnqueueAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Callback<Message, CancellationToken>((x, y) => actualMessages.Add(x));

            var worker = new Services.Nightscout.Worker(
                nightscoutClient.Object,
                Options.Create(_nightscoutParameters),
                messageQueue.Object,
                logger);

            await worker.StartAsync(cancellationTokenSource.Token);

            int iteration = 2;
            while (iteration > 0)
            {
                if (expectedMessages.Count != 0 && actualMessages.Count == expectedMessages.Count)
                    break;

                await Task.Delay(500);
                iteration--;
            }

            cancellationTokenSource.Cancel();
            actualMessages.Should().BeEquivalentTo(expectedMessages);
        }
    }
}
