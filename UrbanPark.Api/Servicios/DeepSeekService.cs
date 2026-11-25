using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Motor de IA de Urban Park. Habla con cualquier endpoint compatible con la API de OpenAI
/// (por defecto Ollama en el puerto 11434) o con Grok mediante la clave configurada.
/// Si no hay clave o el servicio no responde, quien llama usa el respaldo heuristico local.
/// </summary>
public sealed class DeepSeekService
{
    private static readonly JsonSerializerOptions Opciones = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;

    public DeepSeekService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
    }

    public string Modelo => _cfg["DeepSeek:Model"] is { Length: > 0 } m ? m : "llama3.2:1b";

    public string BaseUrl => (_cfg["DeepSeek:BaseUrl"] is { Length: > 0 } b
        ? b
        : "http://127.0.0.1:11434/v1").TrimEnd('/');

    public string ApiKey => _cfg["DeepSeek:ApiKey"] ?? "";
    public int TimeoutSegundos => _cfg.GetValue("DeepSeek:TimeoutSeconds", 90);
    public double Temperatura => _cfg.GetValue("DeepSeek:Temperature", 0.3);

    public bool EsLocal =>
        BaseUrl.Contains("11434", StringComparison.Ordinal) ||
        BaseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
        BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase);

    public string Nombre => EsLocal ? "Ollama" : "Grok";

    public string Proveedor => EsLocal ? $"ollama/{Modelo}" : $"grok/{Modelo}";

    public bool Configurado =>
        EsLocal ||
        (!string.IsNullOrWhiteSpace(ApiKey) &&
         !ApiKey.StartsWith("TU_", StringComparison.OrdinalIgnoreCase) &&
         !ApiKey.Equals("ollama", StringComparison.OrdinalIgnoreCase));

    public Task<string> GenerarAsync(string prompt, bool json, int maxTokens, CancellationToken ct = default)
        => ConversarAsync(new[] { ("user", prompt) }, json, maxTokens, ct);

    public async Task<string> ConversarAsync(IEnumerable<(string rol, string contenido)> mensajes, bool json,
        int maxTokens, CancellationToken ct = default)
    {
        if (!Configurado)
            throw new InvalidOperationException($"{Nombre} no esta configurado.");

        return await GenerarConChatAsync(mensajes, json, maxTokens, ct);
    }

    private async Task<string> GenerarConChatAsync(IEnumerable<(string rol, string contenido)> mensajes, bool json,
        int maxTokens, CancellationToken ct)
    {
        var lista = mensajes.Select(m => new { role = m.rol, content = m.contenido }).ToArray();
        var cuerpo = JsonSerializer.Serialize(new
        {
            model = Modelo,
            messages = lista,
            temperature = Temperatura,
            max_tokens = maxTokens,
            stream = false,
            response_format = json ? new { type = "json_object" } : null
        }, Opciones);

        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
        if (!string.IsNullOrWhiteSpace(ApiKey) && !ApiKey.StartsWith("TU_", StringComparison.OrdinalIgnoreCase))
            peticion.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        peticion.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(TimeoutSegundos));

        using var respuesta = await _http.SendAsync(peticion, cts.Token);
        var texto = await respuesta.Content.ReadAsStringAsync(cts.Token);
        if (!respuesta.IsSuccessStatusCode)
            throw new InvalidOperationException($"{Nombre} HTTP {(int)respuesta.StatusCode}: {Recortar(texto)}");

        try
        {
            using var doc = JsonDocument.Parse(texto);
            return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new InvalidOperationException($"Respuesta inesperada de {Nombre}: {Recortar(texto)}");
        }
    }

    public static string? ExtraerObjetoJson(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpio = texto.Trim();
        if (limpio.StartsWith('{') && limpio.EndsWith('}')) return limpio;

        var inicio = limpio.IndexOf('{');
        var fin = limpio.LastIndexOf('}');
        return inicio >= 0 && fin > inicio ? limpio[inicio..(fin + 1)] : null;
    }

    public static string? LeerTexto(string json, string campo)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String)
                return valor.GetString();
        }
        catch (JsonException)
        {
        }
        return null;
    }

    public async Task<(bool disponible, string mensaje)> EstadoAsync(CancellationToken ct = default)
    {
        if (!Configurado)
            return (false, $"{Nombre} no esta configurado.");

        try
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/models");
            peticion.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", string.IsNullOrWhiteSpace(ApiKey) ? "ollama" : ApiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            using var respuesta = await _http.SendAsync(peticion, cts.Token);
            return respuesta.IsSuccessStatusCode
                ? (true, $"{Nombre} listo para asignar plazas (modelo {Modelo})")
                : (false, $"{Nombre} respondio HTTP {(int)respuesta.StatusCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"{Nombre} no responde ({ex.GetType().Name}). Se usara la asignacion heuristica local.");
        }
    }

    private static string Recortar(string texto) => texto.Length <= 300 ? texto : texto[..300] + "...";
}
