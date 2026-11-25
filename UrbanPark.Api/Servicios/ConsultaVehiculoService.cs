using System.Net;
using System.Text.Json;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Consulta la ficha del vehículo en EcuadorAPI (<c>GET /api/v1/placas/{placa}</c>) para que el
/// cliente no tenga que escribir marca, modelo, año ni color a mano: solo digita la placa.
/// Si la cuenta no tiene créditos (402) o la placa no existe (404) se responde con un mensaje
/// claro y la app deja registrar los datos manualmente.
/// </summary>
public sealed class ConsultaVehiculoService
{
    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _cfg;
    private readonly ILogger<ConsultaVehiculoService> _log;

    public ConsultaVehiculoService(HttpClient http, IConfiguration cfg, ILogger<ConsultaVehiculoService> log)
    {
        _http = http;
        _cfg = cfg;
        _log = log;
    }

    public string ApiKey => _cfg["EcuadorApi:ApiKey"] ?? "";

    public string BaseUrl =>
        (_cfg["EcuadorApi:BaseUrl"] is { Length: > 0 } b ? b : "https://api.ecuadorapi.com").TrimEnd('/');

    public int TimeoutSegundos => _cfg.GetValue("EcuadorApi:TimeoutSeconds", 15);

    public bool Configurado => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Placa sin guiones, como la espera el proveedor (ABC1234).</summary>
    public static string Limpiar(string? placa) =>
        new string((placa ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());

    public async Task<(ConsultaVehiculoDto? Datos, string? Error)> ConsultarAsync(string placa, CancellationToken ct = default)
    {
        if (!Configurado)
            return (null, "La consulta de placas no está configurada. Registra los datos a mano.");

        var limpia = Limpiar(placa);
        if (limpia.Length < 6)
            return (null, "La placa debe tener al menos 6 caracteres.");

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(TimeoutSegundos));

        using var peticion = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v1/placas/{limpia}");
        peticion.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");
        peticion.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var respuesta = await _http.SendAsync(peticion, limite.Token);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(limite.Token);

            if (respuesta.StatusCode == HttpStatusCode.PaymentRequired)
                return (null, "La consulta de placas está en pausa: la cuenta de EcuadorAPI necesita créditos. " +
                              "Mientras tanto puedes registrar marca, modelo y color a mano.");
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return (null, "La clave de EcuadorAPI no es válida o está inactiva.");
            if (respuesta.StatusCode == HttpStatusCode.NotFound)
                return (null, $"La placa {limpia} no aparece en EcuadorAPI. Escríbela a mano si es un vehículo nuevo.");
            if (respuesta.StatusCode == HttpStatusCode.ServiceUnavailable)
                return (null, "EcuadorAPI no tiene la ficha de ese vehículo en este momento. Intenta más tarde.");
            if (!respuesta.IsSuccessStatusCode)
                return (null, $"EcuadorAPI respondió HTTP {(int)respuesta.StatusCode}.");

            using var doc = JsonDocument.Parse(cuerpo);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return (null, "EcuadorAPI no devolvió datos para esa placa.");

            return (Leer(data, limpia), null);
        }
        catch (OperationCanceledException)
        {
            return (null, $"La consulta tardó más de {TimeoutSegundos} s. Intenta de nuevo.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo consultar la placa {Placa} en EcuadorAPI", limpia);
            return (null, "No se pudo conectar con EcuadorAPI. Revisa la conexión e intenta de nuevo.");
        }
    }

    private static ConsultaVehiculoDto Leer(JsonElement data, string placa)
    {
        static string? Texto(JsonElement e, string nombre)
        {
            if (!e.TryGetProperty(nombre, out var v)) return null;
            return v.ValueKind switch
            {
                JsonValueKind.String => v.GetString(),
                JsonValueKind.Number => v.ToString(),
                _ => null
            };
        }

        var marca = Texto(data, "brand");
        var modelo = Texto(data, "model");
        var color = Texto(data, "color");
        var clase = Texto(data, "vehicle_class");
        var servicio = Texto(data, "service");

        int? anio = null;
        if (data.TryGetProperty("year", out var y))
        {
            if (y.ValueKind == JsonValueKind.Number && y.TryGetInt32(out var n)) anio = n;
            else if (y.ValueKind == JsonValueKind.String && int.TryParse(y.GetString(), out var m)) anio = m;
        }

        int? cilindraje = null;
        if (data.TryGetProperty("engine_cc", out var cc))
        {
            if (cc.ValueKind == JsonValueKind.Number && cc.TryGetInt32(out var n)) cilindraje = n;
            else if (cc.ValueKind == JsonValueKind.String && int.TryParse(cc.GetString(), out var m)) cilindraje = m;
        }

        return new ConsultaVehiculoDto(
            true, null,
            (Texto(data, "plate") ?? placa).ToUpperInvariant(),
            Limpio(marca), Limpio(modelo), anio, Limpio(color), cilindraje, Limpio(clase), Limpio(servicio),
            "ecuadorapi");
    }

    private static string? Limpio(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return null;
        var limpio = valor.Trim();
        return limpio.Length == 0 ? null : limpio.ToUpperInvariant();
    }

    // ================================================================ matrícula
    /// <summary>
    /// Matrícula registrada: <c>GET /api/v1/placas/{placa}/matricula</c>.
    /// Devuelve año de matriculación, última matrícula, caducidad y entidad de tránsito.
    /// Es una consulta aparte (se cobra por separado), así que solo se pide cuando el
    /// cliente la solicita expresamente.
    /// </summary>
    public async Task<(MatriculaVehiculoDto? Datos, string? Error)> ConsultarMatriculaAsync(
        string placa, CancellationToken ct = default)
    {
        if (!Configurado)
            return (null, "La consulta de matrículas no está configurada. Escríbela a mano.");

        var limpia = Limpiar(placa);
        if (limpia.Length < 6)
            return (null, "La placa debe tener al menos 6 caracteres.");

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(TimeoutSegundos));

        using var peticion = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v1/placas/{limpia}/matricula");
        peticion.Headers.TryAddWithoutValidation("Authorization", $"Bearer {ApiKey}");
        peticion.Headers.TryAddWithoutValidation("Accept", "application/json");

        try
        {
            using var respuesta = await _http.SendAsync(peticion, limite.Token);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(limite.Token);

            var (datos, error) = InterpretarMatricula(respuesta.StatusCode, cuerpo, limpia);
            if (datos is not null) return (datos, null);

            return (null, error);
        }
        catch (OperationCanceledException)
        {
            return (null, $"La consulta tardó más de {TimeoutSegundos} s. Intenta de nuevo.");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo consultar la matrícula de {Placa} en EcuadorAPI", limpia);
            return (null, "No se pudo conectar con EcuadorAPI. Revisa la conexión e intenta de nuevo.");
        }
    }

    private static (MatriculaVehiculoDto? Datos, string? Error) InterpretarMatricula(
        HttpStatusCode estado, string cuerpo, string placa)
    {
        if (estado == HttpStatusCode.PaymentRequired)
            return (null, "La consulta de matrículas está en pausa: la cuenta de EcuadorAPI necesita créditos.");
        if (estado is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return (null, "La clave de EcuadorAPI no es válida o está inactiva.");
        if (estado == HttpStatusCode.NotFound)
            return (null, $"La matrícula de {placa} no aparece registrada en EcuadorAPI.");
        if (estado == HttpStatusCode.ServiceUnavailable)
            return (null, "EcuadorAPI no tiene la matrícula de ese vehículo en este momento. Intenta más tarde.");
        if ((int)estado is < 200 or > 299)
            return (null, $"EcuadorAPI respondió HTTP {(int)estado}.");

        try
        {
            using var doc = JsonDocument.Parse(cuerpo);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                return (null, "EcuadorAPI no devolvió la matrícula de esa placa.");

            static string? Texto(JsonElement e, string nombre)
            {
                if (!e.TryGetProperty(nombre, out var v)) return null;
                return v.ValueKind switch
                {
                    JsonValueKind.String => v.GetString(),
                    JsonValueKind.Number => v.ToString(),
                    _ => null
                };
            }

            int? anio = null;
            if (data.TryGetProperty("registration_year", out var anioElemento))
            {
                if (anioElemento.ValueKind == JsonValueKind.Number && anioElemento.TryGetInt32(out var n)) anio = n;
                else if (anioElemento.ValueKind == JsonValueKind.String && int.TryParse(anioElemento.GetString(), out var m)) anio = m;
            }

            return (new MatriculaVehiculoDto(
                true, null,
                (Texto(data, "plate") ?? placa).ToUpperInvariant(),
                anio,
                Fecha(Texto(data, "last_registration_date")),
                Fecha(Texto(data, "registration_expiry_date")),
                Limpio(Texto(data, "registration_authority")),
                "ecuadorapi"), null);
        }
        catch (JsonException)
        {
            return (null, "EcuadorAPI devolvió una respuesta que no se pudo leer.");
        }
    }

    /// <summary>Las fechas vienen como yyyy-MM-dd; se conserva la fecha tal cual.</summary>
    private static string? Fecha(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        return DateOnly.TryParse(texto.Trim(), out var fecha)
            ? fecha.ToString("dd/MM/yyyy")
            : texto.Trim();
    }
}
