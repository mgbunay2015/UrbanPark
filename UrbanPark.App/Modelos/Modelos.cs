using System.Text.Json;
using System.Text.Json.Serialization;

namespace UrbanPark.App.Modelos;

// ------------------------------------------------------------------ sesion
public sealed class SesionDto
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public long ExpiresAt { get; set; }
    public string Username { get; set; } = "";
    public string[] Roles { get; set; } = [];
    public UsuarioDto? Usuario { get; set; }

    /// <summary>Token con el que este equipo queda vinculado (entrar solo con el código).</summary>
    public string? DispositivoToken { get; set; }
}

public sealed class UsuarioDto
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public bool Enabled { get; set; }
    public string[] Roles { get; set; } = [];

    /// <summary>True si la cuenta tiene el autenticador de Google activo.</summary>
    public bool TotpActivo { get; set; }

    public string NombreCompleto =>
        string.Join(" ", new[] { FirstName, LastName }.Where(p => !string.IsNullOrWhiteSpace(p)));
}

// ------------------------------------------------------------------ espacios
public sealed class EspacioDto
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Zona { get; set; } = "";
    public string TipoVehiculo { get; set; } = "";
    public bool Ocupado { get; set; }
    public double TarifaHora { get; set; }
    public int? Nivel { get; set; }
    public string? Descripcion { get; set; }
    public long? SedeId { get; set; }
    public string? SedeNombre { get; set; }
    public string? SedeDireccion { get; set; }
}

// ------------------------------------------------------------------ sedes (parqueaderos)
public sealed class SedeDto
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Sector { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public string Telefono { get; set; } = "";
    public string Horario { get; set; } = "";
    public bool Techado { get; set; }
    public bool Vigilancia { get; set; }
    public bool AceptaReserva { get; set; }
    public double TarifaBase { get; set; }
    public string Servicios { get; set; } = "";
    public int PlazasTotal { get; set; }
    public int PlazasLibres { get; set; }
    public int PlazasOcupadas { get; set; }
    public int OcupacionPorcentaje { get; set; }
    public double? DistanciaKm { get; set; }
    public int? TiempoCaminandoMin { get; set; }
    public int PlazasLibresVehiculo { get; set; }

    /// <summary>Texto corto de la distancia: "480 m" o "1,4 km".</summary>
    public string DistanciaTexto => DistanciaKm is null
        ? ""
        : DistanciaKm.Value < 1
            ? $"{Math.Round(DistanciaKm.Value * 1000)} m"
            : $"{DistanciaKm.Value.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("es-EC"))} km";

    public string CaminandoTexto => TiempoCaminandoMin is null ? "" : $"{TiempoCaminandoMin} min a pie";

    public string LibreTexto => PlazasLibres == 0
        ? "Lleno"
        : $"{PlazasLibres} {(PlazasLibres == 1 ? "plaza libre" : "plazas libres")}";
}

public sealed class SedeZonaDto
{
    public string Zona { get; set; } = "";
    public int? Nivel { get; set; }
    public int Total { get; set; }
    public int Libres { get; set; }
    public int Ocupados { get; set; }
    public double TarifaDesde { get; set; }
    public double TarifaHasta { get; set; }
}

public sealed class SedeDetalleDto
{
    public SedeDto? Sede { get; set; }
    public List<SedeZonaDto> Zonas { get; set; } = [];
}

public sealed class SedeCandidataDto
{
    public long SedeId { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double DistanciaKm { get; set; }
    public int TiempoCaminandoMin { get; set; }
    public double TarifaBase { get; set; }
    public int PlazasLibres { get; set; }
    public int PlazasTotal { get; set; }
    public int Puntaje { get; set; }
    public string Motivo { get; set; } = "";
}

public sealed class RecomendacionSedeDto
{
    public long SedeId { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Sector { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public double DistanciaKm { get; set; }
    public int TiempoCaminandoMin { get; set; }
    public double TarifaBase { get; set; }
    public int PlazasLibres { get; set; }
    public int PlazasTotal { get; set; }
    public int Puntaje { get; set; }
    public string Motivo { get; set; } = "";
    public string ExplicacionIa { get; set; } = "";
    public string ProveedorIa { get; set; } = "";
    public List<SedeCandidataDto> Alternativas { get; set; } = [];
}

// ------------------------------------------------------------------ pase del puesto (QR)
public sealed class PasePuestoDto
{
    public long TicketId { get; set; }
    public string TicketCodigo { get; set; } = "";
    public string Placa { get; set; } = "";
    public long SedeId { get; set; }
    public string SedeCodigo { get; set; } = "";
    public string SedeNombre { get; set; } = "";
    public string SedeDireccion { get; set; } = "";
    public string SedeSector { get; set; } = "";
    public double SedeLatitud { get; set; }
    public double SedeLongitud { get; set; }
    public long EspacioId { get; set; }
    public string EspacioCodigo { get; set; } = "";
    public string Zona { get; set; } = "";
    public int? Nivel { get; set; }
    public string TipoVehiculo { get; set; } = "";
    public double TarifaHora { get; set; }
    public DateTime Entrada { get; set; }
    public int MinutosTranscurridos { get; set; }
    public int HorasCobradas { get; set; }
    public int Descuento { get; set; }
    public double MontoBruto { get; set; }
    public double MontoAhora { get; set; }
    public double MontoPorHora { get; set; }
    public string Estado { get; set; } = "";
    public string Usuario { get; set; } = "";
    public string QrPayload { get; set; } = "";
    public string Modo { get; set; } = "AHORA";
    public DateTime? SalidaPrevista { get; set; }
    public int HorasPrevistas { get; set; }
    public double MontoPrevisto { get; set; }

    public bool EsReserva => string.Equals(Modo, "RESERVA", StringComparison.OrdinalIgnoreCase);
}

public sealed class OcupacionZonaDto
{
    public int Total { get; set; }
    public int Ocupados { get; set; }
    public int Libres { get; set; }
    public int Porcentaje { get; set; }
}

public sealed class OcupacionDto
{
    public int Total { get; set; }
    public int Ocupados { get; set; }
    public int Libres { get; set; }
    public int PorcentajeGlobal { get; set; }
    public Dictionary<string, OcupacionZonaDto> Zonas { get; set; } = [];
}

// ------------------------------------------------------------------ vehiculos
public sealed class VehiculoDto
{
    public long Id { get; set; }
    public string Placa { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string? Marca { get; set; }
    public string? Color { get; set; }
    public string Propietario { get; set; } = "";
    public int Descuento { get; set; }
    public string? PropietarioEmail { get; set; }
    public string? PropietarioUsername { get; set; }
    public bool Activo { get; set; } = true;
}

public sealed class BiometriaDto
{
    public long Id { get; set; }
    public string Usuario { get; set; } = "";
    public long? VehiculoId { get; set; }
    public string Placa { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string? Dispositivo { get; set; }
    public bool TieneFoto { get; set; }
    public DateTime CreadoEn { get; set; }
}

// ------------------------------------------------------------------ tickets
public sealed class TicketDto
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Placa { get; set; } = "";
    public long EspacioId { get; set; }
    public string? EspacioCodigo { get; set; }
    public string? Zona { get; set; }
    public DateTime Entrada { get; set; }
    public DateTime? Salida { get; set; }
    public string Estado { get; set; } = "ABIERTO";
    public double? TarifaHora { get; set; }
    public double? MontoTotal { get; set; }
    public double MontoDescuento { get; set; }
    public string? Usuario { get; set; }
    public bool LiberacionPendiente { get; set; }
    public string Modo { get; set; } = "AHORA";
    public DateTime? SalidaPrevista { get; set; }

    public bool Abierto => Estado is "ABIERTO" or "RESERVADO";

    public bool EsReserva => string.Equals(Modo, "RESERVA", StringComparison.OrdinalIgnoreCase);
}

// ------------------------------------------------------------------ IA
public sealed class RecomendacionDto
{
    public string ZonaRecomendada { get; set; } = "";
    public long EspacioId { get; set; }
    public string EspacioCodigo { get; set; } = "";
    public double TarifaHora { get; set; }
    public int? Nivel { get; set; }
    public string Motivo { get; set; } = "";
    public string ExplicacionIa { get; set; } = "";
    public string ProveedorIa { get; set; } = "";
    public List<string> Alternativas { get; set; } = [];
}

public sealed class AsignacionDto
{
    public long EspacioId { get; set; }
    public string EspacioCodigo { get; set; } = "";
    public string Zona { get; set; } = "";
    public double TarifaHora { get; set; }
    public int? Nivel { get; set; }
    public string Placa { get; set; } = "";
    public long TicketId { get; set; }
    public string TicketCodigo { get; set; } = "";
    public string Motivo { get; set; } = "";
    public string ExplicacionIa { get; set; } = "";
    public string ProveedorIa { get; set; } = "";
    public bool Asignado { get; set; }
}

public sealed class IaEstadoDto
{
    public bool Disponible { get; set; }
    public string BaseUrl { get; set; } = "";
    public string Modelo { get; set; } = "";
    public string Mensaje { get; set; } = "";
    public string Proveedor { get; set; } = "";
}

public sealed class ChatRespuestaDto
{
    public string Respuesta { get; set; } = "";
    public string Proveedor { get; set; } = "";
    public string Modelo { get; set; } = "";
    public bool UsaIa { get; set; }
}

public sealed class PlazaOcupadaDetalleDto
{
    public string Plaza { get; set; } = "";
    public string Placa { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Zona { get; set; } = "";
    public double PorCobrar { get; set; }
}

public sealed class DiaMontoDto
{
    public string Fecha { get; set; } = "";
    public double Pagado { get; set; }
    public double Descuento { get; set; }
}

public sealed class PlazaUsoDto
{
    public string Plaza { get; set; } = "";
    public int Usos { get; set; }
}

public sealed class MetricasReporteDto
{
    public string GeneradoEn { get; set; } = "";
    public Dictionary<string, int> PorZona { get; set; } = [];
    public List<PlazaOcupadaDetalleDto> PlazasOcupadasDetalle { get; set; } = [];
    public List<DiaMontoDto> PorDia { get; set; } = [];
    public List<PlazaUsoDto> PlazasMasUsadas { get; set; } = [];

    /// <summary>Tickets del periodo por sucursal.</summary>
    public Dictionary<string, int> PorSede { get; set; } = [];
}

public sealed class ReporteDto
{
    public int TotalTickets { get; set; }
    public int Abiertos { get; set; }
    public int Cerrados { get; set; }
    public double Recaudado { get; set; }
    public double DescuentoAplicado { get; set; }
    public double PorCobrarEstimado { get; set; }
    public string FechaDesde { get; set; } = "";
    public string FechaHasta { get; set; } = "";
    public int PlazasTotal { get; set; }
    public int PlazasOcupadas { get; set; }
    public int PlazasLibres { get; set; }
    public int PlazasPorcentaje { get; set; }
    public int TicketsAbiertosAhora { get; set; }
    public bool CuadranConPlazas { get; set; }
    public string ResumenIa { get; set; } = "";
    public string ProveedorIa { get; set; } = "";
    public List<string> Hallazgos { get; set; } = [];

    /// <summary>Nombres de las sucursales incluidas en el reporte.</summary>
    public List<string> Sucursales { get; set; } = [];

    public MetricasReporteDto Metricas { get; set; } = new();
}

// ------------------------------------------------------------------ demo / resiliencia
public sealed class DemoEstadoDto
{
    public string Servicio { get; set; } = "";
    public bool Running { get; set; }
    public string? ContainerId { get; set; }
    public string Mensaje { get; set; } = "";
}

public sealed class DemoAccionDto
{
    public string Action { get; set; } = "";
    public string Servicio { get; set; } = "";
    public string? ContainerId { get; set; }
    public string Mensaje { get; set; } = "";
    public bool Ok { get; set; }
}

public sealed class OcupacionRemotaDto
{
    public bool Fallback { get; set; }
    public string Origen { get; set; } = "";
    public string? Mensaje { get; set; }
    public string? Resilience { get; set; }
    public int Total { get; set; }
    public int Ocupados { get; set; }
    public int Libres { get; set; }
    public int? PorcentajeGlobal { get; set; }
    public long? Ms { get; set; }
    public string? EstadoCircuito { get; set; }
    public Dictionary<string, OcupacionZonaDto>? Zonas { get; set; }
}

public sealed class SincronizacionDto
{
    public int Pendientes { get; set; }
    public int PendientesIniciales { get; set; }
    public int Liberadas { get; set; }
    public int Fallidas { get; set; }
    public int SinCambio { get; set; }
    public int PendientesRestantes { get; set; }
    public string Mensaje { get; set; } = "";
}

public sealed class ReconciliacionDto
{
    public int TicketsAbiertos { get; set; }
    public int PlazasConTicketAbierto { get; set; }
    public int PlazasOcupadasTrasSync { get; set; }
    public int AjustesOcupar { get; set; }
    public int AjustesLiberar { get; set; }
    public int DuplicadosCerrados { get; set; }
    public bool Cuadran { get; set; }
    public string Mensaje { get; set; } = "";
    public List<string> Detalle { get; set; } = [];
}

// ------------------------------------------------------------------ error del API
public sealed class ErrorApi
{
    public int Status { get; set; }
    public string? Error { get; set; }
    public string? Message { get; set; }
    public string? Path { get; set; }
}

/// <summary>Excepción con el mensaje ya traducido para mostrar al usuario.</summary>
public sealed class ApiExcepcion : Exception
{
    public int Status { get; }

    /// <summary>True cuando el API pide el código del autenticador de Google para entrar.</summary>
    public bool RequiereTotp { get; init; }

    public ApiExcepcion(int status, string mensaje) : base(mensaje) => Status = status;
}

public sealed class TotpEstadoDto
{
    public bool Activo { get; set; }
    public string Mensaje { get; set; } = "";
}

public sealed class TotpPrepararDto
{
    public string Secreto { get; set; } = "";
    public string OtpAuthUri { get; set; } = "";
    public string Mensaje { get; set; } = "";
}

public static class JsonUrbanPark
{
    public static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
