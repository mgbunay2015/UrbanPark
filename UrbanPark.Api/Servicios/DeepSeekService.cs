using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Motor de IA de Urban Park. Con Proveedor=cursor llama a un agente de Cursor
/// (la clave del worker cursor-agent-worker-ext-...) y le pide solo texto, sin editar el proyecto.
/// Si no hay clave o el agente no responde, quien llama usa el respaldo heurístico local.
/// El camino de Ollama (API compatible con OpenAI en el puerto 11434) se conserva por si se vuelve a activar.
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
    private readonly Lazy<string?> _claveCursor;

    public DeepSeekService(HttpClient http, IConfiguration cfg)
    {
        _http = http;
        _cfg = cfg;
        _claveCursor = new Lazy<string?>(() => CursorCredencial.Leer(ApiKey));
    }

    public bool EsCursor =>
        string.Equals(_cfg["DeepSeek:Proveedor"], "cursor", StringComparison.OrdinalIgnoreCase);

    public string NombreClave =>
        _cfg["DeepSeek:ApiKeyName"] is { Length: > 0 } n
            ? n
            : "cursor-agent-worker-ext-DESKTOP-EEA7MN7-8376-1790207795427";

    public string Modelo => EsCursor
        ? NombreClave
        : _cfg["DeepSeek:Model"] is { Length: > 0 } m ? m : "llama3.2:1b";

    public string BaseUrl => EsCursor
        ? "https://api.cursor.com"
        : (_cfg["DeepSeek:BaseUrl"] is { Length: > 0 } b ? b : "http://127.0.0.1:11434/v1").TrimEnd('/');

    public string ApiKey => _cfg["DeepSeek:ApiKey"] ?? "";
    public int TimeoutSegundos => _cfg.GetValue("DeepSeek:TimeoutSeconds", EsCursor ? 150 : 90);
    public double Temperatura => _cfg.GetValue("DeepSeek:Temperature", 0.3);

    public bool EsLocal =>
        !EsCursor &&
        (BaseUrl.Contains("11434", StringComparison.Ordinal) ||
         BaseUrl.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
         BaseUrl.Contains("localhost", StringComparison.OrdinalIgnoreCase));

    public string Nombre => EsCursor ? "Cursor" : EsLocal ? "Ollama" : "Grok";
    public string Proveedor => EsCursor ? $"cursor/{NombreClave}" : EsLocal ? $"ollama/{Modelo}" : $"grok/{Modelo}";

    public bool Configurado => EsCursor
        ? !string.IsNullOrEmpty(_claveCursor.Value)
        : EsLocal ||
          (!string.IsNullOrWhiteSpace(ApiKey) &&
           !ApiKey.StartsWith("TU_", StringComparison.OrdinalIgnoreCase) &&
           !ApiKey.Equals("ollama", StringComparison.OrdinalIgnoreCase));

    public Task<string> GenerarAsync(string prompt, bool json, int maxTokens, CancellationToken ct = default)
        => ConversarAsync(new[] { ("user", prompt) }, json, maxTokens, ct);

    public async Task<string> ConversarAsync(IEnumerable<(string rol, string contenido)> mensajes, bool json,
        int maxTokens, CancellationToken ct = default)
    {
        if (!Configurado)
            throw new InvalidOperationException($"{Nombre} no está configurado.");

        if (EsCursor)
            return await GenerarConCursorAsync(mensajes, json, ct);

        return await GenerarConChatAsync(mensajes, json, maxTokens, ct);
    }

    private async Task<string> GenerarConCursorAsync(IEnumerable<(string rol, string contenido)> mensajes, bool json,
        CancellationToken ct)
    {
        var clave = _claveCursor.Value!;
        var pedido = new StringBuilder();
        pedido.AppendLine("Eres el motor de IA de Urban Park. Responde únicamente lo pedido.");
        pedido.AppendLine("No uses herramientas, no edites archivos y no abras repositorios.");
        if (json)
            pedido.AppendLine("La respuesta debe ser un único objeto JSON, sin markdown ni texto alrededor.");
        foreach (var (rol, contenido) in mensajes)
            pedido.Append(rol).Append(": ").AppendLine(contenido);

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(TimeoutSegundos));

        using var crearDoc = await EnviarCursorAsync(HttpMethod.Post, "/v1/agents", clave, AgenteJson(pedido.ToString()), limite.Token);
        var agente = crearDoc.RootElement.GetProperty("agent").GetProperty("id").GetString()
                     ?? throw new InvalidOperationException("Cursor no devolvió el id del agente.");
        var corrida = crearDoc.RootElement.GetProperty("run").GetProperty("id").GetString()
                      ?? throw new InvalidOperationException("Cursor no devolvió el id de la corrida.");

        try
        {
            try
            {
            while (!limite.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), limite.Token);
                using var corridaDoc = await EnviarCursorAsync(
                    HttpMethod.Get, $"/v1/agents/{agente}/runs/{corrida}", clave, null, limite.Token);
                var estado = corridaDoc.RootElement.GetProperty("status").GetString();
                if (estado is "FINISHED")
                {
                    var texto = corridaDoc.RootElement.TryGetProperty("result", out var resultado)
                        ? resultado.GetString()
                        : null;
                    if (string.IsNullOrWhiteSpace(texto))
                        throw new InvalidOperationException("Cursor terminó sin texto.");
                    return texto;
                }

                if (estado is "ERROR" or "CANCELLED" or "EXPIRED")
                    throw new InvalidOperationException($"Cursor terminó en estado {estado}.");
            }

            throw new InvalidOperationException("Cursor no respondió a tiempo.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new InvalidOperationException("Cursor no respondió a tiempo.");
            }
        }
        finally
        {
            try
            {
                using var _ = await EnviarCursorAsync(HttpMethod.Post, $"/v1/agents/{agente}/archive", clave, null, CancellationToken.None);
            }
            catch (Exception)
            {
                // archivar es limpieza; la respuesta ya se obtuvo o el error original se conserva
            }
        }
    }

    private async Task<JsonDocument> EnviarCursorAsync(HttpMethod metodo, string ruta, string clave, string? cuerpo,
        CancellationToken ct)
    {
        using var peticion = new HttpRequestMessage(metodo, BaseUrl + ruta);
        peticion.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(clave + ":")));
        if (cuerpo is not null)
            peticion.Content = new StringContent(cuerpo, Encoding.UTF8, "application/json");

        using var respuesta = await _http.SendAsync(peticion, ct);
        var texto = await respuesta.Content.ReadAsStringAsync(ct);
        if (!respuesta.IsSuccessStatusCode)
            throw new InvalidOperationException($"Cursor HTTP {(int)respuesta.StatusCode}: {Recortar(texto)}");

        try
        {
            return JsonDocument.Parse(texto);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Respuesta inesperada de Cursor: {Recortar(texto)}");
        }
    }

    private static string AgenteJson(string prompt)
    {
        using var flujo = new MemoryStream();
        using (var escritor = new Utf8JsonWriter(flujo))
        {
            escritor.WriteStartObject();
            escritor.WriteString("name", "Urban Park");
            escritor.WritePropertyName("prompt");
            escritor.WriteStartObject();
            escritor.WriteString("text", prompt);
            escritor.WriteEndObject();
            escritor.WriteEndObject();
        }
        return Encoding.UTF8.GetString(flujo.ToArray());
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
            return (false, $"{Nombre} no está configurado.");

        if (EsCursor)
        {
            try
            {
                using var doc = await EnviarCursorAsync(HttpMethod.Get, "/v1/me", _claveCursor.Value!, null, ct);
                var nombre = doc.RootElement.TryGetProperty("apiKeyName", out var n) ? n.GetString() : NombreClave;
                return (true, $"Cursor listo para asignar plazas (clave {nombre})");
            }
            catch (Exception ex)
            {
                return (false, $"Cursor no responde ({ex.GetType().Name}). Se usará la asignación heurística local.");
            }
        }

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
                : (false, $"{Nombre} respondió HTTP {(int)respuesta.StatusCode}.");
        }
        catch (Exception ex)
        {
            return (false, $"{Nombre} no responde ({ex.GetType().Name}). Se usará la asignación heurística local.");
        }
    }

    private static string Recortar(string texto) => texto.Length <= 300 ? texto : texto[..300] + "…";
}
