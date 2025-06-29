namespace Services.Nightscout.Models
{
    public class Parameters
    {
        public required Glucose Glucose { get; init; }

        public required Delay Delay { get; init; }

        public required Users? Users { get; init; }

        public required Mongo? Mongo { get; init; }

        public required Google? Google { get; init; }
    }

    // Установка required привод к ошибке https://github.com/dotnet/runtime/issues/95006
    public class Glucose
    {
        public decimal Hypoglycemia { get; init; }

        public decimal Hyperglycemia { get; init; }

        public decimal Delta { get; init; }

        public decimal HighGlucose { get; init; }

        public decimal LowGlucose { get; init; }
    }

    public class Delay
    {
        public double Error { get; init; }

        public double Warning { get; init; }

        public double Default { get; init; }
    }

    public class Users
    {
        public string[]? Hypoglycemia { get; init; }

        public string[]? Hyperglycemia { get; init; }

        public string[]? HighGlucose { get; init; }

        public string[]? LowGlucose { get; init; }
    }

    public class Mongo
    {
        public string ConnectionString { get; init; } = string.Empty;

        public string DatabaseName { get; init; } = string.Empty;

        public int? MaxDatabaseSizeMib { get; init; }

        public int? WarningPercent { get; init; }

        public double Delay { get; init; }
    }

    public class Google
    {
        public string CalendarId { get; init; } = string.Empty;
    }
}
