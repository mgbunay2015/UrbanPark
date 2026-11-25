using System.Globalization;
using System.Text;
using UrbanPark.Api.Modelos;
using UrbanPark.Api.Repositorios;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Catalogo de parqueaderos (sedes) y recomendacion de cual conviene.
/// El puntaje es deterministico (distancia, plazas libres, precio y servicios) y la IA
/// (DeepSeek/Ollama o el respaldo heurístico) solo redacta el motivo en lenguaje natural.
/// Asi la recomendacion funciona igual con o sin conexion a la IA.
/// </summary>
public sealed class ServicioSedes
{
    /// <summary>Velocidad media a pie en km/h; se usa para estimar minutos caminando.</summary>
    private const double VelocidadCaminando = 4.8;

    private readonly SedeRepositorio _sedes;
    private readonly EspacioRepositorio _espacios;
    private readonly DeepSeekService _ia;
    private readonly ILogger<ServicioSedes> _log;

    public ServicioSedes(SedeRepositorio sedes, EspacioRepositorio espacios, DeepSeekService ia,
        ILogger<ServicioSedes> log)
    {
        _sedes = sedes;
        _espacios = espacios;
        _ia = ia;
        _log = log;
    }

    // ================================================================ catalogo
    public async Task<List<SedeDto>> ListarAsync(double? latitud, double? longitud, string? tipoVehiculo,
        string? preferencia = null)
    {
        var sedes = await _sedes.ListarAsync();
        var conteos = await _sedes.ConteosAsync();
        var tipo = TipoRelevante(tipoVehiculo, preferencia);

        var lista = new List<SedeDto>(sedes.Count);
        foreach (var sede in sedes)
        {
            if (!sede.Activa) continue;
            conteos.TryGetValue(sede.Id, out var conteo);
            lista.Add(ADto(sede, conteo, latitud, longitud, tipo));
        }

        return OrdenarPorCercania(lista);
    }

    public async Task<SedeDetalleDto?> DetalleAsync(long id, double? latitud, double? longitud)
    {
        var sede = await _sedes.PorIdAsync(id);
        if (sede is null) return null;

        var conteos = await _sedes.ConteosAsync();
        conteos.TryGetValue(sede.Id, out var conteo);
        var zonas = await _espacios.ZonasDeSedeAsync(sede.Id);
        return new SedeDetalleDto(ADto(sede, conteo, latitud, longitud, null), zonas);
    }

    private static List<SedeDto> OrdenarPorCercania(List<SedeDto> lista) =>
        lista.OrderBy(s => s.DistanciaKm ?? double.MaxValue)
             .ThenByDescending(s => s.PlazasLibres)
             .ThenBy(s => s.Nombre, StringComparer.Ordinal)
             .ToList();

    private static SedeDto ADto(Sede sede, ConteoSede? conteo, double? latitud, double? longitud, string? tipo)
    {
        var total = conteo?.Total ?? 0;
        var ocupados = conteo?.Ocupados ?? 0;
        var libres = conteo?.Libres ?? 0;
        var porcentaje = total == 0 ? 0 : (int)Math.Round(ocupados * 100.0 / total);
        var libresDelTipo = LibresDelTipo(conteo, tipo);

        double? distancia = null;
        int? minutos = null;
        if (latitud is not null && longitud is not null)
        {
            distancia = Math.Round(DistanciaKm(latitud.Value, longitud.Value, sede.Latitud, sede.Longitud), 2);
            minutos = MinutosCaminando(distancia.Value);
        }

        return new SedeDto(sede.Id, sede.Codigo, sede.Nombre, sede.Direccion, sede.Sector,
            sede.Latitud, sede.Longitud, sede.Telefono, sede.Horario, sede.Techado, sede.Vigilancia,
            sede.AceptaReserva, sede.TarifaBase, sede.Servicios, total, libres, ocupados, porcentaje,
            distancia, minutos, libresDelTipo);
    }

    /// <summary>Tipo de plaza que debe haber libre según el vehículo y el perfil del conductor.</summary>
    private static string? TipoRelevante(string? tipoVehiculo, string? preferencia)
    {
        var perfil = (preferencia ?? "").ToLowerInvariant();
        if (perfil.Contains("capacidades") || perfil.Contains("discapacit") || perfil.Contains("especial"))
            return "DISCAPACIDAD";
        if (perfil.Contains("embaraz"))
            return "EMBARAZADA";
        return string.IsNullOrWhiteSpace(tipoVehiculo) ? null : tipoVehiculo.Trim().ToUpperInvariant();
    }

    private static int LibresDelTipo(ConteoSede? conteo, string? tipo)
    {
        if (conteo is null) return 0;
        return (tipo ?? "").ToUpperInvariant() switch
        {
            "AUTO" => conteo.LibresAuto,
            "MOTO" => conteo.LibresMoto,
            "CAMIONETA" => conteo.LibresCamioneta,
            "DISCAPACIDAD" => conteo.LibresDiscapacidad,
            "EMBARAZADA" => conteo.LibresEmbarazada,
            _ => conteo.Libres
        };
    }

    // ================================================================ recomendacion
    public async Task<(RecomendacionSedeResponse? Respuesta, string? Error)> RecomendarAsync(
        RecomendarSedeRequest peticion)
    {
        var tipo = string.IsNullOrWhiteSpace(peticion.TipoVehiculo) ? "AUTO" : peticion.TipoVehiculo!.Trim().ToUpperInvariant();
        var preferencia = string.IsNullOrWhiteSpace(peticion.Preferencia) ? "sin preferencia" : peticion.Preferencia!.Trim();

        var sedes = await _sedes.ListarAsync();
        var conteos = await _sedes.ConteosAsync();
        var tipoRelevante = TipoRelevante(tipo, preferencia);

        var candidatas = new List<(Sede Sede, ConteoSede? Conteo, SedeDto Dto, int Puntaje, double Distancia)>();

        foreach (var sede in sedes)
        {
            if (!sede.Activa) continue;
            if (peticion.SoloTechado == true && !sede.Techado) continue;
            if (peticion.PrecioMaximo is > 0 && sede.TarifaBase > peticion.PrecioMaximo.Value) continue;

            conteos.TryGetValue(sede.Id, out var conteo);
            var libresTipo = LibresDelTipo(conteo, tipoRelevante);

            // Sin plazas de su tipo no sirve: la recomendacion nunca manda a un parqueadero lleno.
            if (libresTipo <= 0) continue;

            var dto = ADto(sede, conteo, peticion.Latitud, peticion.Longitud, tipoRelevante);
            var distancia = dto.DistanciaKm ?? 0;
            if (peticion.RadioKm is > 0 && dto.DistanciaKm is not null && distancia > peticion.RadioKm.Value)
                continue;

            var puntaje = Puntaje(dto, libresTipo);
            candidatas.Add((sede, conteo, dto, puntaje, distancia));
        }

        if (candidatas.Count == 0)
            return (null, $"No hay parqueaderos con plazas libres para un vehículo {tipo} con esas condiciones. " +
                          "Prueba sin filtros o elige otra sede en el mapa.");

        var ordenadas = candidatas
            .OrderByDescending(c => c.Puntaje)
            .ThenBy(c => c.Distancia)
            .ThenBy(c => c.Sede.TarifaBase)
            .ToList();

        var mejor = ordenadas[0];
        var motivo = MotivoDe(mejor.Dto);
        var explicacion = ExplicacionLocal(mejor.Dto, mejor.Sede, ordenadas[1..].Take(2).Select(c => c.Dto).ToList());
        var proveedor = "local/heurístico";

        if (_ia.Configurado)
        {
            try
            {
                var narracion = await NarrarAsync(mejor.Dto, ordenadas.Skip(1).Take(2).Select(c => c.Dto).ToList(),
                    tipo, preferencia);

                // Un modelo pequeño tiende a repetir la instrucción ("máximo 12 palabras:") o a
                // devolver el JSON de ejemplo. Solo se acepta una frase limpia; si no, se queda
                // el motivo calculado con los datos reales.
                var motivoIa = LimpiarMotivo(narracion.Motivo);
                var textoIa = LimpiarExplicacion(narracion.Texto);

                if (motivoIa is not null) motivo = motivoIa;
                if (textoIa is not null)
                {
                    explicacion = textoIa;
                    proveedor = _ia.Proveedor;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning("La IA no redactó la recomendación de sede ({Motivo}); se usa el texto local.",
                    ex.Message);
            }
        }

        var alternativas = ordenadas.Skip(1).Take(3)
            .Select(c => new SedeCandidataDto(c.Sede.Id, c.Sede.Codigo, c.Sede.Nombre, c.Sede.Direccion,
                c.Sede.Latitud, c.Sede.Longitud, c.Dto.DistanciaKm ?? 0, c.Dto.TiempoCaminandoMin ?? 0,
                c.Sede.TarifaBase, LibresDelTipo(c.Conteo, tipoRelevante), c.Dto.PlazasTotal, c.Puntaje,
                MotivoDe(c.Dto)))
            .ToList();

        var respuesta = new RecomendacionSedeResponse(mejor.Sede.Id, mejor.Sede.Codigo, mejor.Sede.Nombre,
            mejor.Sede.Direccion, mejor.Sede.Sector, mejor.Sede.Latitud, mejor.Sede.Longitud,
            mejor.Dto.DistanciaKm ?? 0, mejor.Dto.TiempoCaminandoMin ?? 0, mejor.Sede.TarifaBase,
            LibresDelTipo(mejor.Conteo, tipoRelevante), mejor.Dto.PlazasTotal, mejor.Puntaje,
            motivo, explicacion, proveedor, alternativas);

        return (respuesta, null);
    }

    /// <summary>
    /// Puntaje 0..100: cercania (45), plazas libres (25), precio (20) y servicios (10).
    /// Es determinista para que la recomendacion sea reproducible y auditable.
    /// </summary>
    private static int Puntaje(SedeDto dto, int libresDelTipo)
    {
        var distancia = dto.DistanciaKm ?? 0;
        var porDistancia = 45.0 - (distancia * 9.0);
        var porDisponibilidad = Math.Min(25.0, libresDelTipo * 1.5);
        var porPrecio = Math.Max(0.0, 20.0 - (dto.TarifaBase * 4.0));
        var porServicios = (dto.Techado ? 5 : 0) + (dto.Vigilancia ? 3 : 0) + (dto.Servicios.Contains("Wi-Fi") ? 2 : 0);

        var total = porDistancia + porDisponibilidad + porPrecio + porServicios;
        return (int)Math.Round(Math.Clamp(total, 0, 100));
    }

    private static string MotivoDe(SedeDto dto)
    {
        var partes = new List<string>();
        if (dto.DistanciaKm is not null)
            partes.Add($"a {DistanciaTexto(dto.DistanciaKm.Value)} ({dto.TiempoCaminandoMin} min a pie)");
        else
            partes.Add($"{dto.Sector}");

        partes.Add(dto.PlazasLibres == 0 ? "sin plazas libres" : $"{dto.PlazasLibres} plazas libres");
        partes.Add($"{Moneda(dto.TarifaBase)}/h");

        return string.Join(" · ", partes);
    }

    /// <summary>Explicacion sin IA: se usa siempre como respaldo y como base del prompt.</summary>
    private static string ExplicacionLocal(SedeDto mejor, Sede sede, List<SedeDto> alternativas)
    {
        var texto = new StringBuilder();
        texto.Append($"Te conviene {sede.Nombre} ({sede.Direccion}): ");

        if (mejor.DistanciaKm is not null)
            texto.Append($"está a {DistanciaTexto(mejor.DistanciaKm.Value)} de donde estás, unos {mejor.TiempoCaminandoMin} minutos a pie. ");
        else
            texto.Append("está en la zona que elegiste. ");

        texto.Append($"Tiene {mejor.PlazasLibres} de {mejor.PlazasTotal} plazas libres y cobra {Moneda(sede.TarifaBase)} la hora. ");

        var ventajas = new List<string>();
        if (sede.Techado) ventajas.Add("es techado");
        if (sede.Vigilancia) ventajas.Add("tiene vigilancia");
        if (!string.IsNullOrWhiteSpace(sede.Servicios)) ventajas.Add(sede.Servicios.Replace(" · ", ", "));
        if (ventajas.Count > 0) texto.Append($"Además {string.Join(", ", ventajas)}. ");

        if (alternativas.Count > 0)
            texto.Append("Otra opción cercana: " +
                          string.Join("; ", alternativas.Select(a => $"{a.Nombre} ({MotivoDe(a)})")) + ".");

        return texto.ToString().Trim();
    }

    private async Task<(string? Motivo, string? Texto)> NarrarAsync(SedeDto mejor, List<SedeDto> alternativas,
        string tipo, string preferencia)
    {
        var lista = new StringBuilder();
        lista.AppendLine($"1) {mejor.Nombre} | {mejor.Direccion} | {DistanciaTexto(mejor.DistanciaKm ?? 0)} | " +
                         $"{mejor.TiempoCaminandoMin} min a pie | {mejor.PlazasLibres}/{mejor.PlazasTotal} libres | " +
                         $"{Moneda(mejor.TarifaBase)}/h | techado={Si(mejor.Techado)} | vigilancia={Si(mejor.Vigilancia)}");
        for (var i = 0; i < alternativas.Count; i++)
        {
            var a = alternativas[i];
            lista.AppendLine($"{i + 2}) {a.Nombre} | {a.Direccion} | {DistanciaTexto(a.DistanciaKm ?? 0)} | " +
                             $"{a.TiempoCaminandoMin} min a pie | {a.PlazasLibres}/{a.PlazasTotal} libres | {Moneda(a.TarifaBase)}/h");
        }

        var prompt = $$"""
            Eres el asistente de estacionamiento de Urban Park en Quito.
            Un conductor con vehículo tipo {{tipo}} y perfil "{{preferencia}}" debe elegir entre estos parqueaderos:
            {{lista}}
            Ya calculamos la mejor opción: {{mejor.Nombre}}.
            Responde SOLO con un JSON válido, sin markdown:
            {"sedeId": {{mejor.Id}}, "motivo": "Cerca, con plazas libres y buen precio", "explicacion": "3 o 4 frases en español, tuteando al conductor, explicando por qué le conviene y mencionando una alternativa"}
            """;

        var texto = await _ia.GenerarAsync(prompt, json: true, maxTokens: 400);
        var objeto = DeepSeekService.ExtraerObjetoJson(texto);
        if (objeto is null) return (null, null);

        return (DeepSeekService.LeerTexto(objeto, "motivo"), DeepSeekService.LeerTexto(objeto, "explicacion"));
    }

    private static string Si(bool valor) => valor ? "sí" : "no";

    /// <summary>Descarta el motivo si el modelo repitió la instrucción o devolvió basura.</summary>
    private static string? LimpiarMotivo(string? motivo)
    {
        var texto = (motivo ?? "").Trim().Trim('"', '\'', '.', ' ');
        if (texto.Length < 10 || texto.Length > 110) return null;

        var minusculas = texto.ToLowerInvariant();
        if (minusculas.Contains("máximo") || minusculas.Contains("maximo") ||
            minusculas.Contains("palabras") || minusculas.Contains("json") ||
            minusculas.Contains("sedeid") || minusculas.StartsWith("motivo"))
            return null;

        return char.ToUpperInvariant(texto[0]) + texto[1..];
    }

    /// <summary>Descarta la explicación si vino vacía, en JSON o demasiado corta para ser útil.</summary>
    private static string? LimpiarExplicacion(string? explicacion)
    {
        var texto = (explicacion ?? "").Trim().Trim('"');
        if (texto.Length < 40 || texto.Length > 900) return null;
        if (texto.Contains('{') || texto.Contains('}') || texto.Contains("sedeId")) return null;
        return texto;
    }

    // ================================================================ geo
    /// <summary>Distancia en km entre dos coordenadas (formula de Haversine).</summary>
    public static double DistanciaKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double radioTierra = 6371.0;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return radioTierra * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double Rad(double grados) => grados * Math.PI / 180.0;

    public static int MinutosCaminando(double km) => Math.Max(1, (int)Math.Ceiling(km / VelocidadCaminando * 60.0));

    public static string DistanciaTexto(double km) =>
        km < 1 ? $"{Math.Round(km * 1000)} m" : $"{km.ToString("0.0", CultureInfo.GetCultureInfo("es-EC"))} km";

    private static string Moneda(double valor) => "$" + valor.ToString("0.00", CultureInfo.GetCultureInfo("es-EC"));
}
