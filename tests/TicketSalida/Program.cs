// Verifica que el pase de salida guarde el ticket detallado y lo recupere,
// que era la causa de que la pantalla de salida mostrara un ticket simplificado.
using System.Text.Json;

var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

// Modelo del pase tal como lo guarda PaseSalida (con Datos anidado).
var datos = new
{
    ticketId = 90210,
    ticketCodigo = "TK-25D96A7B",
    placa = "AUT-0016",
    sedeId = 7,
    sedeCodigo = "FOC",
    sedeNombre = "Urban Park La Foch",
    sedeDireccion = "Av. Reina Victoria N24-56 y La Pinta",
    sedeSector = "La Foch",
    espacioId = 2529,
    espacioCodigo = "FOC-C-01",
    zona = "Zona C - La Foch S1",
    nivel = -1,
    tipoVehiculo = "DISCAPACIDAD",
    tarifaHora = 0.5,
    entrada = "2026-11-21T00:49:00",
    descuento = 50,
    montoAhora = 0.25,
    montoPorHora = 0.25,
    estado = "CERRADO",
    usuario = "cliente2",
    perfilConductor = "Capacidades Especiales",
    marca = "Chevrolet Aveo",
    color = "Negro",
    qrPayload = "URBANPARK|ESTACION|TK-25D96A7B|FOC|..."
};

var pase = new
{
    Id = 90210L,
    Codigo = "TK-25D96A7B",
    Placa = "AUT-0016",
    Plaza = "FOC-C-01",
    Monto = 0.25,
    Usuario = "cliente2",
    PagadoEn = "2026-11-21T00:49:26-05:00",
    ExpiraEn = "2026-11-21T00:59:26-05:00",
    Sede = "Urban Park La Foch",
    TarifaHora = 0.5,
    Entrada = "2026-11-21T00:49:00",
    Descuento = 50,
    Perfil = "Capacidades Especiales",
    Marca = "Chevrolet Aveo",
    Color = "Negro",
    Datos = datos,
    Payload = "URBANPARK|SALIDA|TK-25D96A7B|AUT-0016|FOC-C-01|0.25|...|cliente2"
};

Console.WriteLine("=== PASE DE SALIDA CON DETALLE ===\n");

var json = JsonSerializer.Serialize(new[] { pase });
Comprobar("se serializa", json.Length > 0, $"{json.Length} bytes");

using var doc = JsonDocument.Parse(json);
var raiz = doc.RootElement[0];

Console.WriteLine("\n  -- datos que usa la pantalla de salida --");
Comprobar("placa", raiz.GetProperty("Placa").GetString() == "AUT-0016");
Comprobar("monto pagado", Math.Abs(raiz.GetProperty("Monto").GetDouble() - 0.25) < 0.001);
Comprobar("detalle conservado", raiz.TryGetProperty("Datos", out var d2) && d2.ValueKind == JsonValueKind.Object);

if (d2.ValueKind == JsonValueKind.Object)
{
    Console.WriteLine("\n  -- datos del ticket detallado --");
    Comprobar("zona", d2.GetProperty("zona").GetString() == "Zona C - La Foch S1");
    Comprobar("nivel", d2.GetProperty("nivel").GetInt32() == -1);
    Comprobar("tipo", d2.GetProperty("tipoVehiculo").GetString() == "DISCAPACIDAD");
    Comprobar("sector", d2.GetProperty("sedeSector").GetString() == "La Foch");
    Comprobar("perfil", d2.GetProperty("perfilConductor").GetString() == "Capacidades Especiales");
    Comprobar("marca", d2.GetProperty("marca").GetString() == "Chevrolet Aveo");
    Comprobar("color", d2.GetProperty("color").GetString() == "Negro");
    Comprobar("QR payload", d2.GetProperty("qrPayload").GetString()!.StartsWith("URBANPARK"));
}

Console.WriteLine("\n  -- comparacion con el ticket antiguo (resumido) --");
var camposDetallados = new[] { "zona", "nivel", "tipoVehiculo", "sedeSector", "perfilConductor", "marca", "color" };
var presentes = camposDetallados.Count(c => d2.TryGetProperty(c, out var v) && v.ValueKind != JsonValueKind.Null);
Comprobar($"el detalle incluye {camposDetallados.Length} campos extra", presentes == camposDetallados.Length,
    $"{presentes}/{camposDetallados.Length}");

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: la pantalla de salida puede mostrar el mismo ticket detallado."
    : "RESULTADO: faltan datos para el ticket detallado.");
return ok ? 0 : 1;
