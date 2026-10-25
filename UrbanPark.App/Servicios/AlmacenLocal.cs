using System.Text.Json;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Base pequeña en el equipo: última copia de lo consultado y cola de cambios
/// hechos sin internet, para enviarlos cuando vuelva la conexión.
/// </summary>
public static class AlmacenLocal
{
    private static readonly object Candado = new();
    private static readonly JsonSerializerOptions Opciones = JsonUrbanPark.Opciones;

    public sealed class Operacion
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Metodo { get; set; } = "POST";
        public string Ruta { get; set; } = "";
        public string? Cuerpo { get; set; }
        public string Usuario { get; set; } = "";
        public DateTime CreadoEn { get; set; } = DateTime.Now;
    }

    private sealed class Archivo
    {
        public Dictionary<string, string> Cache { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<Operacion> Cola { get; set; } = [];
    }

    public static int Pendientes
    {
        get { lock (Candado) return Leer().Cola.Count; }
    }

    public static void GuardarCache(string clave, string json)
    {
        lock (Candado)
        {
            var datos = Leer();
            datos.Cache[clave] = json;
            Escribir(datos);
        }
    }

    public static bool TryGet(string clave, out string json)
    {
        lock (Candado)
        {
            return Leer().Cache.TryGetValue(clave, out json!);
        }
    }

    public static void Encolar(string metodo, string ruta, string? cuerpo, string usuario)
    {
        lock (Candado)
        {
            var datos = Leer();
            datos.Cola.Add(new Operacion
            {
                Metodo = metodo,
                Ruta = ruta,
                Cuerpo = cuerpo,
                Usuario = usuario
            });
            Escribir(datos);
        }
    }

    public static List<Operacion> PendientesDe(string usuario)
    {
        lock (Candado)
        {
            return Leer().Cola
                .Where(op => string.Equals(op.Usuario, usuario, StringComparison.OrdinalIgnoreCase))
                .OrderBy(op => op.CreadoEn)
                .ToList();
        }
    }

    public static void Quitar(string id)
    {
        lock (Candado)
        {
            var datos = Leer();
            datos.Cola.RemoveAll(op => op.Id == id);
            Escribir(datos);
        }
    }

    private static string RutaArchivo()
    {
        Directory.CreateDirectory(FileSystem.AppDataDirectory);
        return Path.Combine(FileSystem.AppDataDirectory, "urbanpark-local.json");
    }

    private static Archivo Leer()
    {
        try
        {
            var ruta = RutaArchivo();
            if (!File.Exists(ruta)) return new Archivo();
            var json = File.ReadAllText(ruta);
            return JsonSerializer.Deserialize<Archivo>(json, Opciones) ?? new Archivo();
        }
        catch (Exception)
        {
            return new Archivo();
        }
    }

    private static void Escribir(Archivo datos)
    {
        var json = JsonSerializer.Serialize(datos, Opciones);
        File.WriteAllText(RutaArchivo(), json);
    }
}
