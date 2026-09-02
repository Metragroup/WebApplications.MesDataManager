using System.Globalization;

namespace MesDataManager.Infrastructure.Archives;

/// <summary>
/// Riconduce i valori che arrivano dalla UI (dove tutto passa da <c>object?</c>) al tipo esatto
/// della proprieta' sull'entita'. Serve perche' la griglia generica non conosce a compile time
/// se una colonna e' <c>short</c>, <c>int</c> o <c>decimal?</c>.
/// </summary>
internal static class FieldValueConverter
{
    public static object? Coerce(object? value, Type targetType)
    {
        var underlying = Nullable.GetUnderlyingType(targetType);
        var isNullable = underlying is not null;
        var effectiveType = underlying ?? targetType;

        if (value is null)
        {
            return isNullable || !effectiveType.IsValueType ? null : Activator.CreateInstance(effectiveType);
        }

        if (effectiveType.IsInstanceOfType(value))
        {
            return value;
        }

        if (value is string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                // Una casella lasciata vuota e' assenza di valore, non stringa vuota,
                // tranne dove la colonna e' NOT NULL: in quel caso la validazione
                // del servizio intercetta il campo obbligatorio prima del salvataggio.
                return effectiveType == typeof(string)
                    ? (isNullable ? null : text)
                    : isNullable ? null : Activator.CreateInstance(effectiveType);
            }

            if (effectiveType == typeof(string))
            {
                return text;
            }

            return Convert.ChangeType(text, effectiveType, CultureInfo.InvariantCulture);
        }

        return effectiveType == typeof(string)
            ? Convert.ToString(value, CultureInfo.InvariantCulture)
            : Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Intervallo ammesso dal tipo intero della colonna, oppure <c>null</c> se il tipo non e'
    /// intero. Serve a validare prima di convertire: <see cref="Convert.ChangeType(object?, Type)"/>
    /// su un valore troppo grande lancia <see cref="OverflowException"/>.
    /// </summary>
    public static (decimal Minimum, decimal Maximum)? IntegerRange(Type targetType)
    {
        var effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return true switch
        {
            _ when effectiveType == typeof(byte) => (byte.MinValue, byte.MaxValue),
            _ when effectiveType == typeof(sbyte) => (sbyte.MinValue, sbyte.MaxValue),
            _ when effectiveType == typeof(short) => (short.MinValue, short.MaxValue),
            _ when effectiveType == typeof(ushort) => (ushort.MinValue, ushort.MaxValue),
            _ when effectiveType == typeof(int) => (int.MinValue, int.MaxValue),
            _ when effectiveType == typeof(uint) => (uint.MinValue, uint.MaxValue),
            _ when effectiveType == typeof(long) => (long.MinValue, long.MaxValue),
            _ => null,
        };
    }

    /// <summary>Valore iniziale sensato per un campo di un nuovo record.</summary>
    public static object? DefaultFor(Type targetType)
    {
        if (targetType == typeof(string))
        {
            return string.Empty;
        }

        var underlying = Nullable.GetUnderlyingType(targetType);
        return underlying is not null ? null : Activator.CreateInstance(targetType);
    }
}
