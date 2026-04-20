namespace Core.Services
{
    public static class EnumService
    {
        public static T ConvertToEnumFlag<T>(string[]? stringFlags) where T : Enum
        {
            if (!typeof(T).IsDefined(typeof(FlagsAttribute), false))
                throw new ArgumentException($"Type '{typeof(T)}' must be a [Flags] enum.");

            long combinedValue = 0;
            foreach (var flag in stringFlags ?? Array.Empty<string>())
            {
                if (Enum.TryParse(typeof(T), flag, true, out object? result))
                    combinedValue |= Convert.ToInt64(result);
                else
                    throw new ArgumentException($"Warning: '{flag}' is not a valid member of {typeof(T).Name}");
            }

            return (T)Enum.ToObject(typeof(T), combinedValue);
        }
    }
}
