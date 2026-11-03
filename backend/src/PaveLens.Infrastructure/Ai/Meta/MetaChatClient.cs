using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using PaveLens.Application.Abstractions;
using PaveLens.Application.Common;
using Microsoft.Extensions.Options;

namespace PaveLens.Infrastructure.Ai.Meta;

/// <summary>Thin client for an OpenAI-compatible <c>/chat/completions</c> endpoint serving Muse Wonder.</summary>
internal sealed class MetaChatClient(HttpClient http, IOptions<AiOptions> options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly EndpointOptions _endpoint = options.Value.Meta.Reasoning;
    private readonly MetaOptions _meta = options.Value.Meta;

    public string Model => _endpoint.Model;

    public static JsonObject System(string text) => new() { ["role"] = "system", ["content"] = text };

    public static JsonObject Assistant(string text) => new() { ["role"] = "assistant", ["content"] = text };

    public static JsonObject User(string text) => new() { ["role"] = "user", ["content"] = text };

    public static JsonObject UserWithImage(string text, ImagePayload image) => new()
    {
        ["role"] = "user",
        ["content"] = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = text },
            new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject
                {
                    ["url"] = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes.Span)}"
                }
            }
        }
    };

    public async Task<string> CompleteAsync(IReadOnlyList<JsonObject> messages, bool jsonMode, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["model"] = _endpoint.Model,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)m.DeepClone()).ToArray())
        };
        if (_meta.Temperature is { } temperature)
        {
            payload["temperature"] = temperature;
        }

        if (_meta.ReasoningEffort is { } effort)
        {
            payload["reasoning_effort"] = effort;
        }

        if (jsonMode && _meta.JsonMode)
        {
            payload["response_format"] = new JsonObject { ["type"] = "json_object" };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("chat/completions", UriKind.Relative))
        {
            Content = JsonContent.Create(payload, options: Json)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _endpoint.ApiKey);

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new ModelUnavailableException($"El servicio de razonamiento respondió {(int)response.StatusCode}.");
        }

        try
        {
            var body = await response.Content.ReadFromJsonAsync<JsonObject>(Json, cancellationToken);
            var content = body?["choices"]?[0]?["message"]?["content"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(content)
                ? throw new ModelOutputException("El modelo devolvió una respuesta vacía.")
                : content;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new ModelOutputException($"Formato de respuesta inesperado: {ex.Message}");
        }
    }
}
