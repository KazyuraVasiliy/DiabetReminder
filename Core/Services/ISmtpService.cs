namespace Core.Services
{
    public interface ISmtpService
    {
        Task SendEmailAsync(string subject, string body, bool isHtml = false, CancellationToken cancellationToken = default);
    }
}
