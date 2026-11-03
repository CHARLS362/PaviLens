namespace PaveLens.Domain.Common;

internal static class Guard
{
    public static string NotBlank(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException($"{name} es obligatorio.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} no puede superar {maxLength} caracteres.");
        }

        return trimmed;
    }

    public static string? Optional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new DomainException($"{name} no puede superar {maxLength} caracteres.");
        }

        return trimmed;
    }

    public static TEnum Defined<TEnum>(TEnum value, string name) where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new DomainException($"{name} no es un valor válido: {value}.");

    public static double InRange(double value, double min, double max, string name)
    {
        if (double.IsNaN(value) || value < min || value > max)
        {
            throw new DomainException($"{name} debe estar entre {min} y {max}.");
        }

        return value;
    }
}
