namespace LumiCine.Domain.Common;

internal static class EntityGuard
{
    internal static Guid Id(Guid value, string name) => value != Guid.Empty
        ? value
        : throw new ArgumentException("ID cannot be empty.", name);

    internal static string RequiredText(string value, int maxLength, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, name);
        return value.Length <= maxLength
            ? value
            : throw new ArgumentOutOfRangeException(name, $"Must be at most {maxLength} characters.");
    }

    internal static string? OptionalText(string? value, int maxLength, string name) => value is null || value.Length <= maxLength
        ? value
        : throw new ArgumentOutOfRangeException(name, $"Must be at most {maxLength} characters.");

    internal static int Positive(int value, string name) => value > 0
        ? value
        : throw new ArgumentOutOfRangeException(name, "Must be greater than zero.");

    internal static decimal NonNegative(decimal value, string name) => value >= 0
        ? value
        : throw new ArgumentOutOfRangeException(name, "Cannot be negative.");

    internal static DateTime Utc(DateTime value, string name) => value.Kind == DateTimeKind.Utc
        ? value
        : throw new ArgumentException("Timestamp must use UTC.", name);

    internal static DateTime? OptionalUtc(DateTime? value, string name) => value is null || value.Value.Kind == DateTimeKind.Utc
        ? value
        : throw new ArgumentException("Timestamp must use UTC.", name);

    internal static TEnum Defined<TEnum>(TEnum value, string name) where TEnum : struct, Enum => Enum.IsDefined(value)
        ? value
        : throw new ArgumentOutOfRangeException(name, "Value is not defined.");
}