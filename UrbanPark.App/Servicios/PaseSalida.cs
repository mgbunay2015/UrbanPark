using System.Globalization;
using System.Text.Json;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Pases de salida que se habilitan al cobrar un ticket.
/// Cada uno dura como máximo 10 minutos desde su pago.
/// Pueden coexistir varios (un QR por vehículo).
/// </summary>
public static class PaseSalida
{
    public const int Minutos = 10;
    private const string Clave = "urbanpark-pases-salida";
    private const string ClaveVieja = "urbanpark-pase-salida";

    /// <summary>Pase que vence antes. Sirve para la cuenta de la pestaña.</summary>
    public static Pase? Actual => Vigentes().MinBy(p => p.ExpiraEn);

    public static IReadOnlyList<Pase> Activos => Vigentes();

    /// <summary>Se dispara cuando se activa, vence o se borra un pase.</summary>
    public static event EventHandler? Cambio;

    public static void Activar(string codigo, string placa, string plaza, double monto, string usuario)
    {
        var ahora = DateTimeOffset.Now;
        var lista = Vigentes();
        lista.RemoveAll(p =>
            string.Equals(p.Codigo, codigo, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Placa, placa, StringComparison.OrdinalIgnoreCase));
        lista.Add(new Pase
        {
            Codigo = codigo,
            Placa = placa,
            Plaza = plaza,
            Monto = monto,
            Usuario = usuario,
            PagadoEn = ahora,
            ExpiraEn = ahora.AddMinutes(Minutos)
        });
        Guardar(lista);
        Cambio?.Invoke(null, EventArgs.Empty);
    }

    public static void Limpiar()
    {
        if (!Preferences.ContainsKey(Clave) && !Preferences.ContainsKey(ClaveVieja)) return;
        Preferences.Remove(Clave);
        Preferences.Remove(ClaveVieja);
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
        if (vigentes.Count != lista.Count)
        {
            if (vigentes.Count == 0) Preferences.Remove(Clave);
            else Guardar(vigentes);
        }

        return vigentes;
    }

    private static List<Pase> Leer()
    {
        var json = Preferences.Get(Clave, "");
        if (string.IsNullOrWhiteSpace(json))
            json = Preferences.Get(ClaveVieja, "");
        if (string.IsNullOrWhiteSpace(json)) return [];

        try
        {
            var recorte = json.TrimStart();
            if (recorte.StartsWith('['))
                return JsonSerializer.Deserialize<List<Pase>>(json) ?? [];

            var uno = JsonSerializer.Deserialize<Pase>(json);
            return uno is null ? [] : [uno];
        }
        catch (JsonException)
        {
            Preferences.Remove(Clave);
            return [];
        }
    }

    private static void Guardar(List<Pase> pases)
    {
        Preferences.Set(Clave, JsonSerializer.Serialize(pases));
        Preferences.Remove(ClaveVieja);
    }

    public sealed class Pase
    {
        public string Codigo { get; set; } = "";
        public string Placa { get; set; } = "";
        public string Plaza { get; set; } = "";
        public double Monto { get; set; }
        public string Usuario { get; set; } = "";
        public DateTimeOffset PagadoEn { get; set; }
        public DateTimeOffset ExpiraEn { get; set; }

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
