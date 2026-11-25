// Comprueba que los datos persistidos inservibles (JSON corrupto o de una version
// anterior) ya no tumban la app: antes, una JsonException sin control cerraba el
// proceso con 0xFFFFFFFF al cobrar un ticket o al leer un pase de salida.
using System.Text.Json;
using System.Text.Json.Serialization;

var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

// Mismas opciones que JsonUrbanPark.Opciones en la app.
var opciones = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

// Misma forma que PaseSalida.Pase.
var pase = new Pase
{
    Id = 90210,
    Codigo = "TK-25D96A7B",
    Placa = "AUT-0016",
    Plaza = "FOC-C-01",
    Monto = 0.25,
    Usuario = "cliente2",
    PagadoEn = DateTimeOffset.Now,
    ExpiraEn = DateTimeOffset.Now.AddMinutes(10)
};

Console.WriteLine("=== PASES DE SALIDA RESISTENTES A JSON INVALIDO ===\n");

// 1. Ida y vuelta normal.
var json = JsonSerializer.Serialize(new[] { pase }, opciones);
var vuelta = JsonSerializer.Deserialize<List<Pase>>(json, opciones);
Comprobar("ida y vuelta normal", vuelta is { Count: 1 } && vuelta[0].Placa == "AUT-0016");

// 2. El caso que rompia: JSON truncado o corrupto.
var corruptos = new[]
{
    "{ esto no es json",
    "[{\"Placa\":\"AUT-0016\",\"Monto\":",
    "null",
    "",
    "}{",
    "[{\"PagadoEn\":\"no-es-una-fecha\"}]"
};

var tolerados = 0;
foreach (var texto in corruptos)
{
    try
    {
        if (string.IsNullOrWhiteSpace(texto)) { tolerados++; continue; }

        if (TextoEmpiezaConLista(texto))
            JsonSerializer.Deserialize<List<Pase>>(texto, opciones);
        else
            JsonSerializer.Deserialize<Pase>(texto, opciones);
        tolerados++;
    }
    catch (JsonException)
    {
        // Esperado: la app captura esta excepcion y limpia la clave guardada.
        tolerados++;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FALLA  excepcion inesperada con «{Recortar(texto)}»: {ex.GetType().Name}");
        ok = false;
    }
}
Comprobar($"{corruptos.Length} JSON invalidos capturados sin matar el proceso", tolerados == corruptos.Length);

// 3. Un pase sin los campos nuevos (formato anterior) se sigue leyendo.
var antiguo = "[{\"Id\":1,\"Codigo\":\"TK-OLD\",\"Placa\":\"ABC-1234\",\"Monto\":1.5}]";
var leidoAntiguo = JsonSerializer.Deserialize<List<Pase>>(antiguo, opciones);
Comprobar("pase de una version anterior se lee", leidoAntiguo is { Count: 1 } && leidoAntiguo[0].Placa == "ABC-1234");

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: los datos guardados invalidos ya no cierran la app."
    : "RESULTADO: queda un camino donde el JSON invalido escapa.");
return ok ? 0 : 1;

static bool TextoEmpiezaConLista(string texto) => texto.TrimStart().StartsWith('[');

static string Recortar(string texto) => texto.Length <= 24 ? texto : texto[..24] + "…";

internal sealed class Pase
{
    public long Id { get; set; }
    public string Codigo { get; set; } = "";
    public string Placa { get; set; } = "";
    public string Plaza { get; set; } = "";
    public double Monto { get; set; }
    public string Usuario { get; set; } = "";
    public DateTimeOffset PagadoEn { get; set; }
    public DateTimeOffset ExpiraEn { get; set; }
    public string Sede { get; set; } = "";
    public double TarifaHora { get; set; }
    public int Descuento { get; set; }
    public string Perfil { get; set; } = "";
    public string Marca { get; set; } = "";
    public string Color { get; set; } = "";
    public object? Datos { get; set; }
}
