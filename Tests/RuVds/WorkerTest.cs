using Core.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Telegram.Bot;

namespace Tests.RuVds
{
    public class WorkerTest
    {
        private static DateTime Now = DateTime.Now;

        public static IEnumerable<object?[]> TheoryData()
        {
            yield return new object?[] { Now.AddMonths(1), "active", "" };
            yield return new object?[] { Now.AddMonths(1), "blocked", "Сервер отключён! Текущий статус: blocked" };
            yield return new object?[] { Now.AddDays(1), "active", $"Срок оплаты сервера заканчивается {Now.AddDays(1):dd.MM.yyyy HH:mm} по UTC. Необходимо оплатить: 300. Баланс: 200" };
            yield return new object?[] { null, "active", "Не удалось получить информацию о сервере" };
        }

        [Theory]
        [MemberData(nameof(TheoryData))]
        public async void Notifications_Test(DateTime? paidTill, string status, string notification)
        {
            var logger = new Tests.Logger<Services.RuVds.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            var ruvdsClient = new Mock<Services.RuVds.Client>(string.Empty, string.Empty);
            var ruvdsParameters = new Services.RuVds.Models.Parameters()
            {
                ServerId = 0,
                Delay = new Services.RuVds.Models.Delay()
                {
                    Paid = 86400000,
                    Status = 1200000
                }
            };

            var telegramBotClient = new Mock<ITelegramBotClient>();
            var telegramBotParameters = new TelegramBotParameters()
            {
                ChatId = 0
            };

            var balance = new Services.RuVds.Responses.Balance()
            {
                Amount = 200
            };

            ruvdsClient
                .Setup(x => x.GetBalance(It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(balance);

            var server = new Services.RuVds.Responses.Server()
            {
                ServerId = 0,
                PaidTill = paidTill,
                Status = status,
                Cost = new Services.RuVds.Responses.Cost()
                {
                    CostRub = 300,
                    PaymentPeriod = 2
                }
            };

            ruvdsClient
                .Setup(x => x.GetServer(It.IsAny<int>(), It.IsAny<CancellationToken>(), It.IsAny<ILogger>()))
                .ReturnsAsync(server);

            var worker = new Services.RuVds.Worker(
                ruvdsClient.Object,
                Options.Create(ruvdsParameters),
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
