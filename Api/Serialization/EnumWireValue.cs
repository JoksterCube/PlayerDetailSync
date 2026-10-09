using System;
using System.Runtime.Serialization;

namespace JoksterCube.PlayerDetailSync.Api;

internal static class EnumWireValue
{
    internal static string ToWireValue<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        string name = value.ToString();
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
    }

    internal static string[] ToWireValues<TEnum>(TEnum[] values) where TEnum : struct, Enum
    {
        var result = new string[values.Length];
        for (int index = 0; index < values.Length; index++)
            result[index] = ToWireValue(values[index]);
        return result;
    }

    internal static TEnum Parse<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (value != null)
        {
            foreach (TEnum enumValue in Enum.GetValues(typeof(TEnum)))
                if (string.Equals(ToWireValue(enumValue), value, StringComparison.Ordinal)) return enumValue;
        }

        throw new SerializationException($"'{value}' is not a valid {typeof(TEnum).Name} value.");
    }

    internal static TEnum[] ParseValues<TEnum>(string[] values) where TEnum : struct, Enum
    {
        var result = new TEnum[values.Length];
        for (int index = 0; index < values.Length; index++)
            result[index] = Parse<TEnum>(values[index]);
        return result;
    }
}