using Core.Models;
using MongoDB.Driver;
using Serilog;
using System.Reflection;
using Telegram.Bot;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddUserSecrets(Assembly.GetExecutingAssembly(), true, true);
var configuration = builder.Configuration;

// Serilog
var logger = new LoggerConfiguration()
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3} {Properties:j}] {Message:lj} {NewLine}{Exception}");

if (builder.Environment.EnvironmentName == Environments.Production)
    logger = logger
    .WriteTo.File("log_",
        rollingInterval: RollingInterval.Day, 
        shared: true, 
        retainedFileCountLimit: 10,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Properties:j} {Message:lj}{NewLine}{Exception}");

Log.Logger = logger.CreateLogger();
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

// Mongo
var mongoConnectionString = configuration["Nightscout:Parameters:Mongo:ConnectionString"] ?? string.Empty;

if (mongoConnectionString != string.Empty)
    builder.Services.AddSingleton<IMongoClient, MongoClient>(x =>
        new MongoClient(mongoConnectionString));

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

// RUVDS
var ruvdsUri = configuration["RUVDS:Uri"] ?? string.Empty;
var ruvdsToken = configuration["RUVDS:Token"] ?? string.Empty;

if (ruvdsUri != string.Empty && ruvdsToken != string.Empty)
{
    builder.Services.Configure<Services.RuVds.Models.Parameters>(configuration.GetSection("RUVDS:Parameters"));

    builder.Services.AddSingleton(x =>
        new Services.RuVds.Client(
            ruvdsToken,
            ruvdsUri));
}

// Hosted Services
if (telegramToken != string.Empty && nightscoutUri != string.Empty)
    builder.Services.AddHostedService<Services.Nightscout.Worker>();

if (telegramToken != string.Empty && ruvdsUri != string.Empty)
    builder.Services.AddHostedService<Services.RuVds.Worker>();

if (!builder.Services.Any(x => x.ServiceType == typeof(IHostedService)))
    throw new Exception("Ни один сервис не активирован");

// App
var app = builder.Build();
app.Run();
