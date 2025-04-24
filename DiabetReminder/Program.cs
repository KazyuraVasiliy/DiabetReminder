using Core.Models;
using Serilog;
using System.Reflection;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), true, true);
var configuration = builder.Configuration;

// Serilog
Log.Logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Properties:j}] {Message:lj} {NewLine}{Exception}")
    .WriteTo.File("log_",
        rollingInterval: RollingInterval.Day, 
        shared: true, 
        retainedFileCountLimit: 10,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Properties:j} {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Telegram
var telegramToken = configuration["Telegram:Token"] ?? string.Empty;

if (telegramToken != string.Empty)
{
    builder.Services.AddSingleton<ITelegramBotClient, TelegramBotClient>(x =>
        new TelegramBotClient(telegramToken));

    builder.Services.AddSingleton(new TelegramBotParameters()
    {
        ChatId = configuration.GetValue<long>("Telegram:ChatId")
    });
}

// Nightscout
var nightscoutUri = configuration["Nightscout:Uri"] ?? string.Empty;
var nightscoutApiSecret = configuration["Nightscout:ApiSecret"] ?? string.Empty;

if (nightscoutUri != string.Empty && nightscoutApiSecret != string.Empty)
{
    builder.Services.Configure<Services.Nightscout.Models.Parameters>(configuration.GetSection("Nightscout:Parameters"));

    builder.Services.AddSingleton(x =>
        new Services.Nightscout.Client(
            nightscoutApiSecret,
            nightscoutUri));
}

// Hosted Services
if (telegramToken != string.Empty && nightscoutUri != string.Empty)
    builder.Services.AddHostedService<Services.Nightscout.Worker>();

// App
var app = builder.Build();
app.Run();
