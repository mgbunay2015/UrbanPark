using SQLite;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

// =====================================================================================
// Tablas de la base local (SQLite). Guardan una copia de lo esencial para que la app
// siga funcionando sin internet: el pase con su QR, los vehículos, las sedes y plazas,
// los tickets abiertos y los cambios que quedan por enviar al servidor.
// =====================================================================================

/// <summary>Clave/valor: sirve para marcas de sincronización y copias pequeñas.</summary>
[Table("ajustes")]
public sealed class AjusteLocal
{
    [PrimaryKey, Column("clave")]
    public string Clave { get; set; } = "";

    [Column("valor")]
    public string? Valor { get; set; }

    [Column("actualizado")]
    public DateTime Actualizado { get; set; } = DateTime.Now;
}

/// <summary>Pase del puesto: es lo que permite pintar el QR sin conexión.</summary>
[Table("pases")]
public sealed class PaseLocal
{
    [PrimaryKey, Column("ticket_id")]
    public long TicketId { get; set; }

    [Column("usuario"), Indexed]
    public string Usuario { get; set; } = "";

    /// <summary>Id del duenno de la sesion que guardo la fila (0 en equipos con datos anteriores).</summary>
    [Column("usuario_id"), Indexed]
    public long UsuarioId { get; set; }

    [Column("estado")]
    public string Estado { get; set; } = "";

    [Column("json")]
    public string Json { get; set; } = "";

    [Column("guardado_en")]
    public DateTime GuardadoEn { get; set; } = DateTime.Now;
}

/// <summary>Vehículos del cliente (marca, color, placa y descuento) para reservar sin red.</summary>
[Table("vehiculos")]
public sealed class VehiculoLocal
{
    [PrimaryKey, Column("placa")]
    public string Placa { get; set; } = "";

    [Column("usuario"), Indexed]
    public string Usuario { get; set; } = "";

    /// <summary>Id del usuario duenno del vehiculo (0 en filas guardadas por versiones anteriores).</summary>
    [Column("usuario_id"), Indexed]
    public long UsuarioId { get; set; }

    [Column("json")]
    public string Json { get; set; } = "";

    [Column("activo")]
    public bool Activo { get; set; } = true;

    [Column("guardado_en")]
    public DateTime GuardadoEn { get; set; } = DateTime.Now;
}

/// <summary>Parqueaderos con dirección y coordenadas (para "Cómo llegar" sin datos).</summary>
[Table("sedes")]
public sealed class SedeLocal
{
    [PrimaryKey, Column("sede_id")]
    public long SedeId { get; set; }

    [Column("json")]
    public string Json { get; set; } = "";

    [Column("guardado_en")]
    public DateTime GuardadoEn { get; set; } = DateTime.Now;
}

/// <summary>Plazas de cada sede: permiten mostrar la grilla y elegir puesto.</summary>
[Table("espacios")]
public sealed class EspacioLocal
{
    [PrimaryKey, Column("espacio_id")]
    public long EspacioId { get; set; }

    [Column("sede_id"), Indexed]
    public long SedeId { get; set; }

    [Column("json")]
    public string Json { get; set; } = "";

    [Column("guardado_en")]
    public DateTime GuardadoEn { get; set; } = DateTime.Now;
}

/// <summary>Tickets abiertos del usuario, para la lista y el resumen sin conexión.</summary>
[Table("tickets")]
public sealed class TicketLocal
{
    [PrimaryKey, Column("ticket_id")]
    public long TicketId { get; set; }

    [Column("usuario"), Indexed]
    public string Usuario { get; set; } = "";

    /// <summary>Id del usuario duenno del ticket en la copia local (0 en versiones anteriores).</summary>
    [Column("usuario_id"), Indexed]
    public long UsuarioId { get; set; }

    [Column("abierto")]
    public bool Abierto { get; set; }

    [Column("json")]
    public string Json { get; set; } = "";

    [Column("guardado_en")]
    public DateTime GuardadoEn { get; set; } = DateTime.Now;
}

/// <summary>Cambio hecho sin internet que se enviará al servidor cuando vuelva la red.</summary>
[Table("pendientes")]
public sealed class PendienteLocal
{
    [PrimaryKey, Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Column("metodo")]
    public string Metodo { get; set; } = "POST";

    [Column("ruta")]
    public string Ruta { get; set; } = "";

    [Column("cuerpo")]
    public string? Cuerpo { get; set; }

    [Column("usuario"), Indexed]
    public string Usuario { get; set; } = "";

    /// <summary>Id del usuario que hizo el cambio (0 si se encolo sin sesion con id).</summary>
    [Column("usuario_id"), Indexed]
    public long UsuarioId { get; set; }

    [Column("intentos")]
    public int Intentos { get; set; }

    [Column("ultimo_error")]
    public string? UltimoError { get; set; }

    [Column("creado_en")]
    public DateTime CreadoEn { get; set; } = DateTime.Now;
}
