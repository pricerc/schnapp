namespace Schnapp.Contracts;

using System;
using System.Text.Json;
using System.Text.Json.Serialization;

using Medo;

/// <summary>
/// Serializes <see cref="Uuid7"/> as Id26 text. Accepts Id26 or legacy RFC UUID on read.
/// </summary>
public sealed class Uuid7JsonConverter : JsonConverter<Uuid7>
{
    /// <summary>Reads an Id26 or RFC UUID string; empty/null becomes <see cref="Uuid7.Empty"/>.</summary>
    public override Uuid7 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Uuid7.Empty;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Uuid7 JSON value must be a string.");
        }

        var s = reader.GetString();
        if (string.IsNullOrEmpty(s))
        {
            return Uuid7.Empty;
        }

        if (Uuid7Text.TryParse(s, out var id))
        {
            return id;
        }

        throw new JsonException("Invalid Uuid7 value: " + s);
    }

    /// <summary>Writes the value as Id26 text.</summary>
    public override void Write(Utf8JsonWriter writer, Uuid7 value, JsonSerializerOptions options)
        => writer.WriteStringValue(Uuid7Text.Format(value));
}

/// <summary>
/// Serializes nullable <see cref="Uuid7"/> as Id26 text (or JSON null).
/// </summary>
public sealed class NullableUuid7JsonConverter : JsonConverter<Uuid7?>
{
    /// <summary>Reads an Id26 or RFC UUID string, or null.</summary>
    public override Uuid7? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Uuid7? JSON value must be a string or null.");
        }

        var s = reader.GetString();
        if (string.IsNullOrEmpty(s))
        {
            return null;
        }

        if (Uuid7Text.TryParse(s, out var id))
        {
            return id;
        }

        throw new JsonException("Invalid Uuid7 value: " + s);
    }

    /// <summary>Writes null or Id26 text.</summary>
    public override void Write(Utf8JsonWriter writer, Uuid7? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(Uuid7Text.Format(value.Value));
        }
    }
}

/// <summary>
/// Shared JSON options helpers for Uuid7 converters.
/// </summary>
public static class Uuid7Json
{
    /// <summary>
    /// Default options for HTTP clients and tests: camelCase, case-insensitive properties, and Uuid7 converters.
    /// </summary>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Adds Uuid7 converters to <paramref name="options"/>. Does not check for existing converters.
    /// </summary>
    /// <param name="options">Options instance to mutate.</param>
    /// <returns>The same <paramref name="options"/> instance for chaining.</returns>
    public static JsonSerializerOptions AddUuid7Converters(this JsonSerializerOptions options)
    {
        options.Converters.Add(new Uuid7JsonConverter());
        options.Converters.Add(new NullableUuid7JsonConverter());
        return options;
    }

    private static JsonSerializerOptions CreateDefaultOptions()
    {
        var o = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        return o.AddUuid7Converters();
    }
}
