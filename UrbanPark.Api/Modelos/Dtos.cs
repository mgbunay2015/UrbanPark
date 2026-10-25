namespace UrbanPark.Api.Modelos;

// ============================ Autenticacion ============================
public sealed record RegisterRequest(string? Username, string? Email, string? FirstName, string? LastName, string? Password, string? Role);
public sealed record LoginRequest(string? Username, string? Password, string? CodigoTotp = null);
public sealed record LoginBiometriaRequest(string? Username, string? TokenHash);
public sealed record GoogleLoginRequest(string? IdToken);
public sealed record RefreshRequest(string? RefreshToken);
public sealed record RegisterResponse(string Id, string Username, string Role, string Message);
public sealed record UsuarioDto(string Id, string Username, string Email, string FirstName, string LastName, bool Enabled, string[] Roles, bool TotpActivo = false);
public sealed record SesionResponse(string AccessToken, string RefreshToken, long ExpiresAt, string Username, string[] Roles, UsuarioDto Usuario, string? DispositivoToken = null);

// ============================ Perfil ============================
public sealed record UpdatePerfilDto(string? Email, string? FirstName, string? LastName);
public sealed record ChangePasswordDto(string? CurrentPassword, string? NewPassword);

// ============================ Admin de usuarios ============================
public sealed record CreateUserDto(string? Username, string? Email, string? FirstName, string? LastName, string? Password, string? Role);
public sealed record UpdateUserDto(string? Email, string? FirstName, string? LastName, bool? Enabled);
public sealed record UpdateRolesDto(string? Role);
public sealed record ResetPasswordDto(string? Password);
public sealed record MensajeResponse(string Message);

// ============================ Sedes (parqueaderos) ============================
public sealed record SedeDto(long Id, string Codigo, string Nombre, string Direccion, string Sector,
    double Latitud, double Longitud, string Telefono, string Horario, bool Techado, bool Vigilancia,
    bool AceptaReserva, double TarifaBase, string Servicios, int PlazasTotal, int PlazasLibres,
    int PlazasOcupadas, int OcupacionPorcentaje, double? DistanciaKm, int? TiempoCaminandoMin,
    int PlazasLibresVehiculo);

public sealed record SedeZonaDto(string Zona, int? Nivel, int Total, int Libres, int Ocupados,
    double TarifaDesde, double TarifaHasta);

public sealed record SedeDetalleDto(SedeDto Sede, List<SedeZonaDto> Zonas);

public sealed record RecomendarSedeRequest(string? TipoVehiculo, double? Latitud, double? Longitud,
    string? Preferencia, string? Placa, double? RadioKm, bool? SoloTechado, double? PrecioMaximo);

public sealed record SedeCandidataDto(long SedeId, string Codigo, string Nombre, string Direccion,
    double Latitud, double Longitud, double DistanciaKm, int TiempoCaminandoMin, double TarifaBase,
    int PlazasLibres, int PlazasTotal, int Puntaje, string Motivo);

public sealed record RecomendacionSedeResponse(long SedeId, string Codigo, string Nombre, string Direccion,
    string Sector, double Latitud, double Longitud, double DistanciaKm, int TiempoCaminandoMin,
    double TarifaBase, int PlazasLibres, int PlazasTotal, int Puntaje, string Motivo, string ExplicacionIa,
    string ProveedorIa, List<SedeCandidataDto> Alternativas);

// ============================ Estacionar (QR del puesto) ============================
/// <summary>
/// Modo "AHORA" entra en el acto; modo "RESERVA" guarda el puesto para una fecha y hora.
/// En los dos casos la respuesta trae el pase con el QR.
/// </summary>
public sealed record EstacionarRequest(string? Placa, long? EspacioId, string? Modo = null,
    DateTime? InicioReserva = null, DateTime? FinReserva = null);

public sealed record PasePuestoDto(long TicketId, string TicketCodigo, string Placa,
    long SedeId, string SedeCodigo, string SedeNombre, string SedeDireccion, string SedeSector,
    double SedeLatitud, double SedeLongitud,
    long EspacioId, string EspacioCodigo, string Zona, int? Nivel, string TipoVehiculo, double TarifaHora,
    DateTime Entrada, int MinutosTranscurridos, int HorasCobradas, int Descuento, double MontoBruto,
    double MontoAhora, double MontoPorHora, string Estado, string Usuario, string QrPayload,
    string Modo = "AHORA", DateTime? SalidaPrevista = null, int HorasPrevistas = 0, double MontoPrevisto = 0);

// ============================ Espacios ============================
public sealed record EspacioDto(long Id, string Codigo, string Zona, string TipoVehiculo, bool Ocupado, double TarifaHora, int? Nivel, string? Descripcion,
    long? SedeId = null, string? SedeNombre = null, string? SedeDireccion = null, string? SedeCodigo = null);
public sealed record GuardarEspacioDto(string? Codigo, string? Zona, string? TipoVehiculo, double? TarifaHora, int? Nivel, string? Descripcion, long? SedeId = null);
public sealed record OcupacionZonaDto(int Total, int Ocupados, int Libres, int Porcentaje);
public sealed record OcupacionDto(int Total, int Ocupados, int Libres, int PorcentajeGlobal, Dictionary<string, OcupacionZonaDto> Zonas);

// ============================ Vehiculos ============================
public sealed record VehiculoDto(long Id, string Placa, string Tipo, string? Marca, string? Color, string Propietario, string? PropietarioEmail, string? PropietarioUsername, int Descuento, bool Activo = true);
public sealed record ActivoVehiculoDto(bool Activo);
public sealed record GuardarVehiculoDto(string? Placa, string? Tipo, string? Marca, string? Color, string? Propietario, string? PropietarioEmail, string? PropietarioUsername);
public sealed record GuardarBiometriaDto(long? VehiculoId, string? Placa, string? Tipo, string? TokenHash, string? FotoBase64, string? Dispositivo);
public sealed record BiometriaDto(long Id, string Usuario, long? VehiculoId, string Placa, string Tipo, string? Dispositivo, bool TieneFoto, DateTime CreadoEn);

// ============================ Tickets ============================
public sealed record TicketDto(long Id, string Codigo, string Placa, long EspacioId, string? EspacioCodigo, string? Zona,
    DateTime Entrada, DateTime? Salida, string Estado, double? TarifaHora, double? MontoTotal, string? Usuario, bool LiberacionPendiente,
    double MontoDescuento = 0, string Modo = "AHORA", DateTime? SalidaPrevista = null);

public sealed record AbrirTicketRequest(string? Placa, long? EspacioId);
public sealed record OcupacionRemotaDto(bool Fallback, string Origen, string Mensaje, int Total, int Ocupados, int Libres, string? Resilience);

public sealed record ReconciliarResponse(int TicketsAbiertos, int PlazasConTicketAbierto, int PlazasOcupadasTrasSync,
    int PlazasLibresTrasSync, int PlazasTotal, int PorcentajeGlobal, OcupacionDto? Ocupacion,
    int AjustesOcupar, int AjustesLiberar, int DuplicadosCerrados, int SinCambio, bool Cuadran,
    List<string> Detalle, string Mensaje);

public sealed record SincronizarResponse(int PendientesIniciales, int Liberadas, int Fallidas, int SinCambio,
    int PendientesRestantes, string Mensaje, List<string> Detalle);

public sealed record DemoEstadoResponse(string Servicio, bool Running, string? ContainerId, string Mensaje);
public sealed record DemoAccionResponse(string Action, string Servicio, string? ContainerId, string Mensaje, bool Ok);

// ============================ IA ============================
public sealed record RecomendarRequest(string? TipoVehiculo, string? Preferencia, string? Placa = null);
public sealed record RecomendacionResponse(string ZonaRecomendada, long EspacioId, string EspacioCodigo, double TarifaHora,
    int? Nivel, string Motivo, string ExplicacionIa, string ProveedorIa, List<string> Alternativas);
public sealed record AsignarRequest(string? Placa, string? TipoVehiculo, string? Preferencia);
public sealed record AsignacionResponse(long EspacioId, string EspacioCodigo, string Zona, double TarifaHora, int? Nivel,
    string Placa, long TicketId, string TicketCodigo, string Motivo, string ExplicacionIa, string ProveedorIa, bool Asignado);
public sealed record ReporteRequest(string? FechaDesde, string? FechaHasta);
public sealed record PlazaOcupadaDetalle(string Plaza, string Placa, string Cliente, string Zona, double PorCobrar);
public sealed record DiaMonto(string Fecha, double Pagado, double Descuento);
public sealed record PlazaUso(string Plaza, int Usos);
public sealed record MetricasReporte(string GeneradoEn, Dictionary<string, int> PorZona, List<PlazaOcupadaDetalle> PlazasOcupadasDetalle,
    List<DiaMonto> PorDia, List<PlazaUso> PlazasMasUsadas);
public sealed record ReporteTicketsResponse(int TotalTickets, int Abiertos, int Cerrados, double Recaudado, double PorCobrarEstimado,
    string FechaDesde, string FechaHasta, int PlazasTotal, int PlazasOcupadas, int PlazasLibres, int PlazasPorcentaje,
    int TicketsAbiertosAhora, bool CuadranConPlazas, string ResumenIa, string ProveedorIa, List<string> Hallazgos, MetricasReporte Metricas,
    double DescuentoAplicado = 0);
public sealed record IaEstadoResponse(bool Disponible, string BaseUrl, string Modelo, string Mensaje, string Proveedor);

// ============================ Chat con la IA ============================
public sealed record ChatMensaje(string? Rol, string? Contenido);
public sealed record ChatRequest(string? Mensaje, List<ChatMensaje>? Historial);
public sealed record ChatResponse(string Respuesta, string Proveedor, string Modelo, bool UsaIa);

// ============================ Autenticador de Google (TOTP) ============================
public sealed record TotpPrepararResponse(string Secreto, string OtpAuthUri, string Mensaje);
public sealed record TotpCodigoRequest(string? Codigo);
public sealed record TotpEstadoResponse(bool Activo, string Mensaje);
public sealed record LoginCodigoRequest(string? Dispositivo, string? Codigo);
public sealed record LoginDispositivoRequest(string? Dispositivo);
public sealed record TotpRequeridoResponse(string Username, bool Activo, string Mensaje);
// ============================ Error ============================
public sealed record ErrorResponse(DateTime Timestamp, int Status, string Error, string Message, string Path);
