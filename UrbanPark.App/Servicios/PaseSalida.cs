using System.Globalization;
using System.Text.Json;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Pase de salida que se habilita al cobrar un ticket (vigente 10 minutos desde el pago).
/// Solo se conserva el último que cerró el usuario: uno nuevo sustituye los anteriores.
/// </summary>
public static class PaseSalida
{
    public const int Minutos = 10;
    private const string Clave = "urbanpark-pases-salida";
    private const string ClaveVieja = "urbanpark-pase-salida";

    /// <summary>Último pase pagado en esta sesión (el único que se muestra en Salida).</summary>
    public static Pase? Actual => UltimoDeSesion();

    public static IReadOnlyList<Pase> Activos
    {
        get
        {
            var ultimo = UltimoDeSesion();
            return ultimo is null ? [] : [ultimo];
        }
    }

    /// <summary>Se dispara cuando se activa, vence o se borra un pase.</summary>
    public static event EventHandler? Cambio;

    public static void Activar(string codigo, string placa, string plaza, double monto, string usuario) =>
        Activar(codigo, placa, plaza, monto, usuario, null);

    /// <summary>
    /// Activa el pase de salida guardando además el ticket completo, para que la pantalla de
    /// salida pueda mostrar el mismo comprobante detallado que el QR del puesto.
    /// </summary>
    public static void Activar(string codigo, string placa, string plaza, double monto, string usuario,
        PasePuestoDto? datos)
    {
        var ahora = DateTimeOffset.Now;
        // Un solo QR vigente: el cobro más reciente reemplaza cualquier pase anterior.
        var lista = new List<Pase>();

        // Conserva el detalle del ticket: es lo que hace que la salida muestre el mismo diseño.
        var modelo = datos ?? BuscarEnBaseLocal(codigo);

        lista.Add(new Pase
        {
            Id = modelo?.TicketId ?? 0,
            Codigo = codigo,
            Placa = placa,
            Plaza = plaza,
            Monto = monto,
            Usuario = usuario,
            PagadoEn = ahora,
            ExpiraEn = ahora.AddMinutes(Minutos),
            Sede = modelo?.SedeNombre ?? "",
            TarifaHora = modelo?.TarifaHora ?? 0,
            Entrada = modelo?.Entrada ?? DateTime.Now,
            Descuento = modelo?.Descuento ?? 0,
            Perfil = modelo?.PerfilConductor ?? "",
            Marca = modelo?.Marca ?? "",
            Color = modelo?.Color ?? "",
            Datos = modelo
        });
        Guardar(lista);
        Registro.Info("PaseSalida",
            $"Pase de salida para «{usuario}» · {placa} ({codigo}) hasta {ahora.AddMinutes(Minutos):HH:mm:ss}.");
        Cambio?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Si un admin cobró el ticket en caja, el cliente recupera aquí su QR (vigente 10 min desde el pago).
    /// </summary>
    public static async Task SincronizarQrsPagadosAsync()
    {
        if (!Sesion.Activa || string.IsNullOrWhiteSpace(Sesion.Username)) return;
        // Admin y operador cobran en nombre de otros; el QR lo ve el dueño del ticket en su equipo.
        if (Sesion.EsOperador && !Sesion.EsCliente) return;

        try
        {
            var tickets = await ApiCliente.ListarTicketsAsync() ?? [];
            var ahora = DateTime.Now;
            var recientes = tickets.Where(t =>
                !t.Abierto &&
                t.Salida is not null &&
                (ahora - t.Salida.Value).TotalMinutes <= Minutos &&
                PerteneceAlUsuario(t.Usuario)).ToList();

            if (recientes.Count == 0) return;

            // Solo el ticket cerrado más reciente: coincide con «un QR, el último que pagó».
            var ticket = recientes.MaxBy(t => t.Salida);
            if (ticket is null) return;

            var vigentes = Vigentes();
            if (vigentes.Any(p =>
                    string.Equals(p.Codigo, ticket.Codigo, StringComparison.OrdinalIgnoreCase)))
                return;

            PasePuestoDto? detalle = null;
            try { detalle = await ApiCliente.PaseAsync(ticket.Id); }
            catch (Exception ex)
            {
                Registro.Aviso("PaseSalida",
                    $"No se pudo cargar el pase del ticket {ticket.Codigo}: {ex.Message}");
            }

            var monto = ticket.MontoTotal ?? detalle?.MontoAhora ?? 0;
            Activar(
                ticket.Codigo,
                ticket.Placa,
                ticket.EspacioCodigo ?? ticket.EspacioId.ToString(),
                monto,
                ticket.Usuario ?? Sesion.Username,
                detalle);
        }
        catch (Exception ex)
        {
            Registro.Aviso("PaseSalida", $"No se pudieron sincronizar los QR pagados: {ex.Message}");
        }
    }

    /// <summary>Pase guardado en SQLite al abrir el ticket (respaldo si no se pasa el detalle).</summary>
    private static PasePuestoDto? BuscarEnBaseLocal(string codigo)
    {
        try
        {
            var pase = AlmacenLocal.PaseSinRed();
            if (pase is null) return null;
            return string.Equals(pase.TicketCodigo, codigo, StringComparison.OrdinalIgnoreCase)
                ? pase
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static void Limpiar()
    {
        var clave = ClaveDeSesion();
        if (!Preferences.ContainsKey(clave) && !Preferences.ContainsKey(Clave) && !Preferences.ContainsKey(ClaveVieja))
            return;
        Preferences.Remove(clave);
        if (string.IsNullOrWhiteSpace(Sesion.Username))
        {
            Preferences.Remove(Clave);
            Preferences.Remove(ClaveVieja);
        }

        Cambio?.Invoke(null, EventArgs.Empty);
    }

    public static string TextoRestante(Pase pase)
    {
        var resta = pase.ExpiraEn - DateTimeOffset.Now;
        if (resta < TimeSpan.Zero) resta = TimeSpan.Zero;
        return $"{(int)resta.TotalMinutes:00}:{resta.Seconds:00}";
    }

    private static List<Pase> Vigentes()
    {
        var lista = Leer();
        var vigentes = lista.Where(p => p.ExpiraEn > DateTimeOffset.Now).ToList();
        vigentes = UnicoMasReciente(vigentes);

        if (vigentes.Count != lista.Count)
        {
            if (vigentes.Count == 0) Preferences.Remove(ClaveDeSesion());
            else Guardar(vigentes);
        }

        return vigentes;
    }

    private static Pase? UltimoDeSesion() =>
        UnicoMasReciente(Vigentes().Where(PerteneceASesion).ToList()).FirstOrDefault();

    /// <summary>De una lista de pases, solo el pagado más recientemente (migración de datos viejos).</summary>
    private static List<Pase> UnicoMasReciente(List<Pase> pases) =>
        pases.Count <= 1 ? pases : pases.MaxBy(p => p.PagadoEn) is { } uno ? [uno] : [];

    private static string ClaveDeSesion()
    {
        var usuario = Sesion.Username.Trim();
        return string.IsNullOrEmpty(usuario)
            ? Clave
            : $"{Clave}.{usuario.ToLowerInvariant()}";
    }

    private static bool PerteneceAlUsuario(string? usuarioTicket)
    {
        if (string.IsNullOrWhiteSpace(Sesion.Username)) return true;
        if (string.IsNullOrWhiteSpace(usuarioTicket)) return Sesion.EsCliente;
        return string.Equals(usuarioTicket.Trim(), Sesion.Username, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PerteneceASesion(Pase pase) => PerteneceAlUsuario(pase.Usuario);

    private static List<Pase> Leer()
    {
        var json = Preferences.Get(ClaveDeSesion(), "");
        if (string.IsNullOrWhiteSpace(json) && !string.IsNullOrWhiteSpace(Sesion.Username))
            json = Preferences.Get(Clave, "");
        if (string.IsNullOrWhiteSpace(json))
            json = Preferences.Get(ClaveVieja, "");
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            List<Pase> lista;
            var recorte = json.TrimStart();
            if (recorte.StartsWith('['))
                lista = JsonSerializer.Deserialize<List<Pase>>(json, JsonUrbanPark.Opciones) ?? [];
            else
            {
                var uno = JsonSerializer.Deserialize<Pase>(json, JsonUrbanPark.Opciones);
                lista = uno is null ? [] : [uno];
            }

            return lista.Where(PerteneceASesion).ToList();
        }
        catch (Exception ex)
        {
            // Cualquier JSON inservible (corrupto o de una versión anterior sin los campos
            // nuevos) se descarta: el pase se puede volver a generar, la app no debe caerse.
            Registro.Error("PaseSalida", "El pase guardado estaba corrupto; se descartó.", ex);
            Preferences.Remove(Clave);
            Preferences.Remove(ClaveVieja);
            return [];
        }
    }

    private static void Guardar(List<Pase> pases)
    {
        try
        {
            Preferences.Set(ClaveDeSesion(), JsonSerializer.Serialize(pases, JsonUrbanPark.Opciones));
        }
        catch (Exception ex)
        {
            // Si el JSON con el detalle completo excede el límite de Preferences, se guarda
            // solo lo indispensable: el QR de salida debe funcionar igual.
            Registro.Aviso("PaseSalida", $"No se pudo guardar el pase con detalle ({ex.Message}); se guarda el resumen.");
            var simple = pases.Select(p => new Pase
            {
                Id = p.Id,
                Codigo = p.Codigo,
                Placa = p.Placa,
                Plaza = p.Plaza,
                Monto = p.Monto,
                Usuario = p.Usuario,
                PagadoEn = p.PagadoEn,
                ExpiraEn = p.ExpiraEn,
                Sede = p.Sede,
                TarifaHora = p.TarifaHora,
                Entrada = p.Entrada,
                Descuento = p.Descuento,
                Perfil = p.Perfil,
                Marca = p.Marca,
                Color = p.Color,
                Datos = null
            }).ToList();

            try { Preferences.Set(ClaveDeSesion(), JsonSerializer.Serialize(simple, JsonUrbanPark.Opciones)); }
            catch (Exception) { /* sin persistencia, el pase vive en memoria */ }
        }

        Preferences.Remove(ClaveVieja);
    }

    public sealed class Pase
    {
        /// <summary>Id del ticket; sirve como clave del pase en pantalla.</summary>
        public long Id { get; set; }

        public string Codigo { get; set; } = "";
        public string Placa { get; set; } = "";
        public string Plaza { get; set; } = "";
        public double Monto { get; set; }
        public string Usuario { get; set; } = "";
        public DateTimeOffset PagadoEn { get; set; }
        public DateTimeOffset ExpiraEn { get; set; }

        // Detalle del ticket, para mostrar el mismo comprobante que en la entrada.
        public string Sede { get; set; } = "";
        public double TarifaHora { get; set; }
        public DateTime Entrada { get; set; }
        public int Descuento { get; set; }
        public string Perfil { get; set; } = "";
        public string Marca { get; set; } = "";
        public string Color { get; set; } = "";

        /// <summary>Pase completo guardado al cobrar (incluye QR, zona, nivel y sector).</summary>
        public PasePuestoDto? Datos { get; set; }

        public string Payload =>
            string.Join('|',
                "URBANPARK",
                "SALIDA",
                Codigo,
                Placa,
                Plaza,
                Monto.ToString("0.00", CultureInfo.InvariantCulture),
                ExpiraEn.ToString("O", CultureInfo.InvariantCulture),
                Usuario);
    }
}
