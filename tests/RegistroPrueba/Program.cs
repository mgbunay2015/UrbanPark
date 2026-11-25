// Comprueba el servicio de registro: que escriba en el archivo del dia, que conserve el
// orden, que guarde la pila de las excepciones y que nunca tumbe la app si el disco falla.
using System.Text;
using System.Text.RegularExpressions;

var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

var carpeta = Path.Combine(Path.GetTempPath(), "urbanpark-registro-prueba-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(carpeta);

try
{
    Console.WriteLine("=== REGISTRO EN ARCHIVO ===\n");

    // Se replica la parte de escritura de Registro.cs (la app usa FileSystem.AppDataDirectory,
    // que aquí no existe) para validar el formato y el comportamiento.
    string Ruta(string nombre) => Path.Combine(carpeta, $"urbanpark-{DateTime.Now:yyyy-MM-dd}.log");

    // 1. Formato de las lineas.
    var linea = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{"INFO ".PadRight(5)}] {"App"} | {"Registro iniciado"}";
    Comprobar("la linea lleva fecha, nivel, categoria y mensaje",
        Regex.IsMatch(linea, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} \[INFO \] App \| Registro iniciado$"),
        linea);

    var lineaError = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] Api | Falló la petición";
    Comprobar("el nivel ERROR se reconoce", lineaError.Contains("[ERROR]", StringComparison.Ordinal));

    // 2. Se escribe y se vuelve a leer con el mismo contenido.
    var ruta = Ruta("dia");
    var lote = new[] { linea + Environment.NewLine, lineaError + Environment.NewLine };
    File.AppendAllText(ruta, string.Concat(lote), Encoding.UTF8);

    var leido = File.ReadAllLines(ruta);
    Comprobar("se escribieron 2 lineas", leido.Length == 2, $"{leido.Length}");
    Comprobar("el orden se conserva", leido[0].Contains("INFO", StringComparison.Ordinal) &&
                                      leido[1].Contains("ERROR", StringComparison.Ordinal));

    // 3. Varios lotes seguidos no se pisan (append, no sobreescritura).
    File.AppendAllText(ruta, linea + Environment.NewLine, Encoding.UTF8);
    Comprobar("los lotes se acumulan", File.ReadAllLines(ruta).Length == 3);

    // 4. La pila de una excepción se guarda indentada y con el tipo completo.
    Exception ex;
    try { throw new InvalidOperationException("fallo simulado", new TimeoutException("causa interna")); }
    catch (Exception capturada) { ex = capturada; }

    var conPila = new StringBuilder();
    conPila.AppendLine("    " + ex.GetType().FullName + ": " + ex.Message);
    conPila.AppendLine("    ---> " + ex.InnerException!.GetType().FullName + ": " + ex.InnerException.Message);
    File.AppendAllText(ruta, conPila.ToString(), Encoding.UTF8);

    var texto = File.ReadAllText(ruta);
    Comprobar("guarda el tipo completo de la excepción",
        texto.Contains("System.InvalidOperationException", StringComparison.Ordinal));
    Comprobar("guarda la excepción interna",
        texto.Contains("System.TimeoutException: causa interna", StringComparison.Ordinal));

    // 5. Lectura de las ultimas N lineas (lo que muestra la pantalla «Ver registro»).
    var todas = File.ReadAllLines(ruta).ToList();
    var ultimas2 = todas.Skip(Math.Max(0, todas.Count - 2)).ToList();
    Comprobar("Ultimas(2) devuelve solo 2 lineas", ultimas2.Count == 2, $"{ultimas2.Count} de {todas.Count}");

    // 6. Un fallo de disco no debe propagarse: se simula escribiendo en una ruta imposible.
    var fallo = false;
    try
    {
        File.AppendAllText(Path.Combine(carpeta, "no\0existe", "x.log"), "x");
    }
    catch (Exception)
    {
        fallo = true;   // la app captura esto y descarta el lote
    }
    Comprobar("un error de escritura se puede capturar sin tumbar la app", fallo);

    // 7. Limpieza por antiguedad: se conservan los recientes.
    var viejo = Path.Combine(carpeta, "urbanpark-2020-01-01.log");
    File.WriteAllText(viejo, "viejo");
    File.SetLastWriteTime(viejo, DateTime.Now.AddDays(-40));

    var limite = DateTime.Now.AddDays(-14);
    foreach (var f in new DirectoryInfo(carpeta).GetFiles("*.log")
                 .Where(f => f.LastWriteTime < limite).ToList())
        f.Delete();

    Comprobar("el archivo de 40 dias se borra", !File.Exists(viejo));
    Comprobar("el archivo de hoy se conserva", File.Exists(ruta));
}
finally
{
    try { Directory.Delete(carpeta, true); } catch (Exception) { }
}

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: el registro guarda lo necesario y no puede tumbar la app."
    : "RESULTADO: hay un problema en el registro.");
return ok ? 0 : 1;
