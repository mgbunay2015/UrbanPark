// Verifica el flujo de pago de punta a punta contra el API real:
// abrir ticket -> cerrar (pagar) -> el ticket sale de los abiertos -> el pase queda válido.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var api = "http://127.0.0.1:8090";
var ok = true;

void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

Console.WriteLine("=== FLUJO DE PAGO ===\n");

// 1) sesión
var login = await http.PostAsync($"{api}/api/auth/login", new StringContent(
    JsonSerializer.Serialize(new { username = "cliente2", password = "cliente123" }),
    Encoding.UTF8, "application/json"));
if (!login.IsSuccessStatusCode)
{
    Comprobar("API disponible", false, $"HTTP {(int)login.StatusCode}");
    return 1;
}
string token;
using (var d = JsonDocument.Parse(await login.Content.ReadAsStringAsync()))
    token = d.RootElement.GetProperty("accessToken").GetString()!;
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
Comprobar("sesión iniciada", true);

// 2) limpiar tickets abiertos previos
var abiertosResp = await http.GetAsync($"{api}/api/tickets/abiertos");
var abiertos = new List<long>();
using (var d = JsonDocument.Parse(await abiertosResp.Content.ReadAsStringAsync()))
    foreach (var t in d.RootElement.EnumerateArray())
        abiertos.Add(t.GetProperty("id").GetInt64());

foreach (var id in abiertos)
    await http.PostAsync($"{api}/api/tickets/{id}/cerrar", null);

Console.WriteLine($"  (se cerraron {abiertos.Count} tickets previos)\n");

// 3) plaza libre
var esp = await http.GetAsync($"{api}/api/espacios?sedeId=2");
long plazaId = 0;
string plazaCodigo = "";
using (var d = JsonDocument.Parse(await esp.Content.ReadAsStringAsync()))
{
    foreach (var e in d.RootElement.EnumerateArray())
    {
        if (e.GetProperty("tipoVehiculo").GetString() == "AUTO" && !e.GetProperty("ocupado").GetBoolean())
        {
            plazaId = e.GetProperty("id").GetInt64();
            plazaCodigo = e.GetProperty("codigo").GetString()!;
            break;
        }
    }
}
Comprobar("plaza libre encontrada", plazaId > 0, plazaCodigo);
if (plazaId == 0) return 1;

// 4) abrir ticket (equivale a «Entrar ahora»)
var abrir = await http.PostAsync($"{api}/api/estacionar", new StringContent(
    JsonSerializer.Serialize(new { placa = "AUT-0016", espacioId = plazaId, modo = "AHORA" }),
    Encoding.UTF8, "application/json"));
Comprobar("ticket abierto", abrir.IsSuccessStatusCode, $"HTTP {(int)abrir.StatusCode}");

long ticketId = 0;
string codigo = "";
using (var d = JsonDocument.Parse(await abrir.Content.ReadAsStringAsync()))
{
    ticketId = d.RootElement.GetProperty("ticketId").GetInt64();
    codigo = d.RootElement.GetProperty("ticketCodigo").GetString()!;
}
Comprobar("ticket con código", ticketId > 0 && codigo.Length > 0, $"{codigo}");

// 5) el ticket aparece como abierto
var antes = await http.GetAsync($"{api}/api/tickets/abiertos");
var hayAbierto = false;
using (var d = JsonDocument.Parse(await antes.Content.ReadAsStringAsync()))
    foreach (var t in d.RootElement.EnumerateArray())
        if (t.GetProperty("id").GetInt64() == ticketId) hayAbierto = true;
Comprobar("aparece en abiertos", hayAbierto);

// 6) cerrar / cobrar
var cerrar = await http.PostAsync($"{api}/api/tickets/{ticketId}/cerrar", null);
Comprobar("cobro correcto", cerrar.IsSuccessStatusCode, $"HTTP {(int)cerrar.StatusCode}");

double monto = 0;
string estado = "";
using (var d = JsonDocument.Parse(await cerrar.Content.ReadAsStringAsync()))
{
    if (d.RootElement.TryGetProperty("montoTotal", out var m) && m.ValueKind == JsonValueKind.Number)
        monto = m.GetDouble();
    estado = d.RootElement.GetProperty("estado").GetString() ?? "";
}
Comprobar("ticket queda CERRADO", estado.Equals("CERRADO", StringComparison.OrdinalIgnoreCase), estado);
Comprobar("monto calculado", monto > 0, $"${monto:0.00}");

// 7) ya no está entre los abiertos (esto es lo que quitaba la pestaña QR)
var despues = await http.GetAsync($"{api}/api/tickets/abiertos");
var sigueAbierto = false;
using (var d = JsonDocument.Parse(await despues.Content.ReadAsStringAsync()))
    foreach (var t in d.RootElement.EnumerateArray())
        if (t.GetProperty("id").GetInt64() == ticketId) sigueAbierto = true;
Comprobar("sale de abiertos tras pagar", !sigueAbierto);

// 8) la plaza quedó libre
var esp2 = await http.GetAsync($"{api}/api/espacios?sedeId=2");
bool libre = false;
using (var d = JsonDocument.Parse(await esp2.Content.ReadAsStringAsync()))
    foreach (var e in d.RootElement.EnumerateArray())
        if (e.GetProperty("id").GetInt64() == plazaId)
            libre = !e.GetProperty("ocupado").GetBoolean();
Comprobar("plaza liberada", libre);

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: el flujo de pago responde correctamente."
    : "RESULTADO: hay fallos.");
return ok ? 0 : 1;
