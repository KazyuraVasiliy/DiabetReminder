using Core.Models;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace Core.Services
{
    public class TelegramClient(ITelegramBotClient _telegramBotClient, IOptions<TelegramBotParameters> _telegramBotParameters) : ITelegramClient
    {
        public async Task SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            await _telegramBotClient.SendMessage(_telegramBotParameters.Value.ChatId, message, cancellationToken: cancellationToken);
        }
    }
}
