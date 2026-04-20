using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Services.Message;
using Services.Message.Models;

namespace Tests.RuVds
{
    public class WorkerTest
    {
        private static DateTime Now = DateTime.Now;

        public static IEnumerable<object?[]> TheoryData()
        {
            yield return new object?[] { Now.AddMonths(1), "active", null };
            yield return new object?[] { Now.AddMonths(1), "blocked", new Message()
            {
                Type = Types.ServerStatus,
                Body = "Сервер отключён! Текущий статус: blocked",
                Channel = Channels.Email,
                Users = [ "@User1" ]
            }};
            yield return new object?[] { Now.AddDays(1), "active", new Message()
            {
                Type = Types.ServerPaid,
                Body = $"Срок оплаты сервера заканчивается {Now.AddDays(1):dd.MM.yyyy HH:mm} по UTC. Необходимо оплатить: 300. Баланс: 200",
                Channel = Channels.Telegram,
                Users = [ "@User2" ]
            }};
            yield return new object?[] { null, "active", new Message()
            {
                Type = Types.Error,
                Body = "Ошибка получения статуса оплаты сервера! Не удалось получить информацию о сервере",
                Channel = Channels.None,
                Users = null
            }};
        }

        [Theory]
        [MemberData(nameof(TheoryData))]
        public async void Notifications_Test(DateTime? paidTill, string status, Message expectedMessage)
        {
            var logger = new Tests.Logger<Services.RuVds.Worker>();
            var cancellationTokenSource = new CancellationTokenSource();

            Message? actualMessage = null;

            var ruvdsClient = new Mock<Services.RuVds.Client>(string.Empty, string.Empty);
            var ruvdsParameters = new Services.RuVds.Models.Parameters()
            {
                ServerId = 0,
                Delay = new Services.RuVds.Models.Delay()
                {
                    Paid = 86400000,
                    Status = 1200000
                },
                Users = new Services.RuVds.Models.Users()
                {
                    Error = null,
                    Status = ["@User1"],
                    Paid = ["@User2"]
                },
                Channels = new Services.RuVds.Models.Channels()
                {
                    Error = null,
                    Status = ["Email"],
                    Paid = ["Telegram"]
                }
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

            var messageQueue = new Mock<IMessageQueue>();
            messageQueue
                .Setup(x => x.EnqueueAsync(It.IsAny<Message>(), It.IsAny<CancellationToken>()))
                .Callback<Message, CancellationToken>((x, y) => actualMessage = x);

            var worker = new Services.RuVds.Worker(
                ruvdsClient.Object,
                Options.Create(ruvdsParameters),
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
    }
}
