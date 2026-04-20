namespace Core.Models
{
    public class SmtpParameters
    {
        public required string[] To { get; init; }
        public required string Server { get; init; }
        public required int Port { get; init; }
        public required string Username { get; init; }
        public required string Password { get; init; }
    }
}
