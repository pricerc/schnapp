using System.Net.Http.Json;
using System.Text.Json;
using Schnapp.Contracts;

namespace Schnapp.Api.Tests;

/// <summary>Shared STJ options for API integration tests (includes Uuid7 converters).</summary>
internal static class JsonTest
{
    public static JsonSerializerOptions Options { get; } = Uuid7Json.DefaultOptions;
}

internal static class HttpContentJsonExtensions
{
    public static Task<T?> ReadFromJsonAsyncUuid7<T>(this HttpContent content, CancellationToken ct = default)
        => content.ReadFromJsonAsync<T>(JsonTest.Options, ct);
}
