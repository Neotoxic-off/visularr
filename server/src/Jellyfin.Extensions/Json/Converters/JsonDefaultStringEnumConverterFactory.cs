using System;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Extensions.Json.Converters;

/// <summary>
/// Utilizes the JsonStringEnumConverter and sets a default value if not provided.
/// </summary>
public class JsonDefaultStringEnumConverterFactory : JsonConverterFactory
{
    private static readonly JsonStringEnumConverter _baseConverterFactory = new();

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert)
    {
        return _baseConverterFactory.CanConvert(typeToConvert)
               && typeToConvert.IsDefined(typeof(DefaultValueAttribute));
    }

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        JsonConverter baseConverter = _baseConverterFactory.CreateConverter(typeToConvert, options);
        Type converterType = typeof(JsonDefaultStringEnumConverter<>).MakeGenericType(typeToConvert);

        return (JsonConverter?)Activator.CreateInstance(converterType, baseConverter);
    }
}
