namespace UrbanPark.Api.Modelos;

/// <summary>Usuario del sistema. Los roles se guardan separados por coma: "ADMIN,OPERADOR".</summary>
public sealed class Usuario
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Roles { get; set; } = "CLIENTE";
    public bool Enabled { get; set; } = true;
    public string? GoogleSub { get; set; }
    public DateTime CreadoEn { get; set; }

    /// <summary>Secreto TOTP del autenticador de Google (NULL si nunca se activó).</summary>
    public string? TotpSecret { get; set; }

    /// <summary>True cuando el usuario ya confirmó un código: el login pedirá el de 6 dígitos.</summary>
    public bool TotpActivo { get; set; }

    public string[] ListaRoles => Roles.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Rol efectivo unico y jerarquico: ADMIN &gt; OPERADOR &gt; CLIENTE.</summary>
    public string RolPrincipal => Reglas.RolPrincipal(ListaRoles);
}

/// <summary>
/// Parqueadero (sede) del grupo Urban Park. Cada sede tiene su propia dirección y
/// coordenadas, y agrupa sus plazas: el cliente elige primero la sede en el mapa.
/// </summary>
public sealed class Sede
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Direccion { get; set; } = "";
    public string Sector { get; set; } = "";
    public double Latitud { get; set; }
    public double Longitud { get; set; }
    public string Telefono { get; set; } = "";
    public string Horario { get; set; } = "24 horas";
    public bool Techado { get; set; }
    public bool Vigilancia { get; set; }
    public bool AceptaReserva { get; set; } = true;
    public double TarifaBase { get; set; }
    public string Servicios { get; set; } = "";
    public bool Activa { get; set; } = true;
}

/// <summary>Plaza de estacionamiento. Siempre pertenece a una sede.</summary>
public sealed class Espacio
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Zona { get; set; } = "";
    public string TipoVehiculo { get; set; } = "AUTO";
    public bool Ocupado { get; set; }
    public double TarifaHora { get; set; }
    public int? Nivel { get; set; }
    public string? Descripcion { get; set; }
    public long? SedeId { get; set; }
    public string? SedeNombre { get; set; }
    public string? SedeDireccion { get; set; }
}

/// <summary>Vehiculo registrado por un conductor.</summary>
public sealed class Vehiculo
{
    public long Id { get; set; }
    public string Placa { get; set; } = "";
    public string Tipo { get; set; } = "AUTO";
    public string? Marca { get; set; }
    public string? Color { get; set; }
    public string Propietario { get; set; } = "";
    public int Descuento { get; set; }
    public string? PropietarioEmail { get; set; }
    public string? PropietarioUsername { get; set; }
    public bool Activo { get; set; } = true;
}

/// <summary>Huella o rostro enrolado para abrir el QR de salida.</summary>
public sealed class Biometria
{
    public long Id { get; set; }
    public string Usuario { get; set; } = "";
    public long? VehiculoId { get; set; }
    public string Placa { get; set; } = "";
    public string Tipo { get; set; } = "HUELLA";
    public string? Dispositivo { get; set; }
    public string TokenHash { get; set; } = "";
    public byte[]? FotoRostro { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Ticket de entrada/salida.</summary>
public sealed class Ticket
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

    /// <summary>"AHORA" (entra ya) o "RESERVA" (guardado para una fecha y hora).</summary>
    public string Modo { get; set; } = "AHORA";

    /// <summary>Hora de salida prevista cuando el ticket es una reserva.</summary>
    public DateTime? SalidaPrevista { get; set; }

    public bool EsReserva => string.Equals(Modo, "RESERVA", StringComparison.OrdinalIgnoreCase);
}
