using Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Services.Message
{
    public class Worker(IMessageQueue _queue, ILogger<Worker> _logger, ISmtpService? _smtpService, ITelegramClient? _telegramClient) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            await foreach (var message in _queue.DequeueAllAsync(cancellationToken))
            {
                if (message == null)
                    continue;

                try
                {
                    if (message.Channel == null)
                        throw new ArgumentNullException("Не указан канал рассылки");

                    if (message.Type == null)
                        throw new ArgumentNullException("Не указан тип");

                    if (message.Channel.Value.HasFlag(Models.Channels.Email))
                    {
                        if (_smtpService == null)
                            throw new ArgumentNullException("Не указаны параметры для отправки по SMTP");

                        await _smtpService.SendEmailAsync(message.Subject ?? string.Empty, message.Body, false, cancellationToken);
                    }                        

                    if (message.Channel.Value.HasFlag(Models.Channels.Telegram))
                    {
                        if (_telegramClient == null)
                            throw new ArgumentNullException("Не указаны параметры для отправки в Telegram");

                        if (message.Users?.Length > 0)
                            message.Body += "\n" + string.Join(", ", message.Users);

                        await _telegramClient.SendMessageAsync(message.Body, cancellationToken); 
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Не удалось отправить сообщение: {message}", message.Body);
                }
            }
        }
    }
}
