using Core.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;

namespace Core.Services
{
    public class SmtpService(IOptions<SmtpParameters> _smtpParameters) : ISmtpService
    {
        public async Task SendEmailAsync(string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default)
        {
            var email = new MimeMessage();

            email.From.Add(MailboxAddress.Parse(_smtpParameters.Value.Username));
            email.To.AddRange(_smtpParameters.Value.To.Select(x => MailboxAddress.Parse(x)));
            email.Subject = subject;
            email.Body = isHtml
                ? new TextPart(TextFormat.Html) { Text = body }
                : new TextPart(TextFormat.Plain) { Text = body };

            using var smtp = new SmtpClient();

            await smtp.ConnectAsync(_smtpParameters.Value.Server, _smtpParameters.Value.Port, SecureSocketOptions.Auto, cancellationToken);
            await smtp.AuthenticateAsync(_smtpParameters.Value.Username, _smtpParameters.Value.Password, cancellationToken);
            await smtp.SendAsync(email, cancellationToken);
            await smtp.DisconnectAsync(true, cancellationToken);
        }
    }
}
