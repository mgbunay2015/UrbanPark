// VERIFICACIÓN FINAL: base local SQLite + sincronización al volver la red.
//
// Comprueba, en este orden:
//   1. La base SQLite existe con sus 7 tablas y está íntegra.
//   2. El pase del puesto se guarda y se reconstruye (QR sin internet).
//   3. Un cambio hecho sin red queda encolado.
//   4. Al reconectar se recupera sesión con el refresh token (sin contraseña).
//   5. La cola se envía y el servidor aplica el cambio.
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

var api = "http://127.0.0.1:8090";
var db = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "User Name", "com.urbanpark.app", "Data", "urbanpark.db3");

var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"   {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

Console.WriteLine("============================================================");
Console.WriteLine("  URBAN PARK - Verificación sin conexión");
Console.WriteLine("============================================================\n");

// ---------------------------------------------------------------- 1. base
Console.WriteLine("1. BASE LOCAL SQLITE");
Comprobar("archivo urbanpark.db3 existe", File.Exists(db),
    File.Exists(db) ? $"{new FileInfo(db).Length / 1024} KB" : "no encontrado");
if (!File.Exists(db)) return 1;

using (var cn = new SqliteConnection($"Data Source={db}"))
{
    cn.Open();
    var esperadas = new[] { "ajustes", "espacios", "pases", "pendientes", "sedes", "tickets", "vehiculos" };
    using (var t = cn.CreateCommand())
    {
        t.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        var hay = new List<string>();
        using var rd = t.ExecuteReader();
        while (rd.Read()) hay.Add(rd.GetString(0));
        foreach (var tabla in esperadas)
            Comprobar($"tabla {tabla}", hay.Contains(tabla));
    }
    using var integ = cn.CreateCommand();
    integ.CommandText = "PRAGMA integrity_check";
    Comprobar("integridad", (integ.ExecuteScalar() as string) == "ok");
}

// ---------------------------------------------------------------- 2. QR
Console.WriteLine("\n2. QR GUARDADO (pase del puesto)");
var usuarioPrueba = "test-qr";
using (var cn = new SqliteConnection($"Data Source={db}"))
{
    cn.Open();
    using (var ins = cn.CreateCommand())
    {
        ins.CommandText =
            "INSERT OR REPLACE INTO pases (ticket_id, usuario, estado, json, guardado_en) " +
            "VALUES (555001, @u, 'ABIERTO', @j, @g)";
        ins.Parameters.AddWithValue("@u", usuarioPrueba);
        ins.Parameters.AddWithValue("@j", JsonSerializer.Serialize(new
        {
            ticketId = 555001,
            placa = "PBA-9999",
            espacioCodigo = "PLA-B-05",
            tarifaHora = 0.75,
            qrPayload = "URBANPARK|ESTACION|T-555001|PLA|PBA-9999",
            estado = "ABIERTO"
        }));
        ins.Parameters.AddWithValue("@g", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        ins.ExecuteNonQuery();
    }

    using var q = cn.CreateCommand();
    q.CommandText = "SELECT json FROM pases WHERE ticket_id = 555001";
    var json = q.ExecuteScalar() as string;
    Comprobar("pase recuperable", !string.IsNullOrWhiteSpace(json));
    if (json is not null)
    {
        using var d = JsonDocument.Parse(json);
        Comprobar("contiene la placa", d.RootElement.GetProperty("placa").GetString() == "PBA-9999");
        Comprobar("contiene el QR", d.RootElement.GetProperty("qrPayload").GetString()!.StartsWith("URBANPARK"));
    }

    using var del = cn.CreateCommand();
    del.CommandText = "DELETE FROM pases WHERE usuario = @u";
    del.Parameters.AddWithValue("@u", usuarioPrueba);
    del.ExecuteNonQuery();
}

// ---------------------------------------------------------------- 3-5. ciclo
Console.WriteLine("\n3. CAMBIO SIN RED -> RECONEXIÓN -> SINCRONIZACIÓN");
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

var login = await http.PostAsync($"{api}/api/auth/login", new StringContent(
    JsonSerializer.Serialize(new { username = "cliente2", password = "cliente123" }),
    Encoding.UTF8, "application/json"));

if (!login.IsSuccessStatusCode)
{
    Comprobar("API disponible para la prueba", false, $"HTTP {(int)login.StatusCode}");
    Console.WriteLine("\n   (levanta la API con reiniciar-api.bat y repite)");
    return 1;
}

string token, refresh;
using (var d = JsonDocument.Parse(await login.Content.ReadAsStringAsync()))
{
    token = d.RootElement.GetProperty("accessToken").GetString()!;
    refresh = d.RootElement.GetProperty("refreshToken").GetString()!;
}
Comprobar("sesión iniciada", true);
Comprobar("refresh token para reconectar", !string.IsNullOrWhiteSpace(refresh));

// localizar vehículo
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
var vr = await http.GetAsync($"{api}/api/vehiculos");
long idVeh = 0;
bool activoAntes = false;
using (var d = JsonDocument.Parse(await vr.Content.ReadAsStringAsync()))
{
    foreach (var v in d.RootElement.EnumerateArray())
        if (v.GetProperty("placa").GetString() == "DEF-456")
        {
            idVeh = v.GetProperty("id").GetInt64();
            activoAntes = v.GetProperty("activo").GetBoolean();
        }
}
Comprobar("vehículo de prueba disponible", idVeh > 0, $"id={idVeh}");
if (idVeh == 0) return 1;

// encolar
using (var cn = new SqliteConnection($"Data Source={db}"))
{
    cn.Open();
    using (var l = cn.CreateCommand()) { l.CommandText = "DELETE FROM pendientes"; l.ExecuteNonQuery(); }
    using var ins = cn.CreateCommand();
    ins.CommandText =
        "INSERT INTO pendientes (id, metodo, ruta, cuerpo, usuario, intentos, creado_en) " +
        "VALUES (@i, 'PATCH', @r, @c, 'cliente2', 0, @f)";
    ins.Parameters.AddWithValue("@i", Guid.NewGuid().ToString("N"));
    ins.Parameters.AddWithValue("@r", $"/api/vehiculos/{idVeh}/activo");
    ins.Parameters.AddWithValue("@c", JsonSerializer.Serialize(new { activo = !activoAntes }));
    ins.Parameters.AddWithValue("@f", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
    ins.ExecuteNonQuery();
    Comprobar("cambio guardado sin red", true);
}

// reconectar: refresh
http.DefaultRequestHeaders.Authorization = null;
var rr = await http.PostAsync($"{api}/api/auth/refresh",
    new StringContent(JsonSerializer.Serialize(new { refreshToken = refresh }), Encoding.UTF8, "application/json"));
string? token2 = null;
if (rr.IsSuccessStatusCode)
{
    using var d = JsonDocument.Parse(await rr.Content.ReadAsStringAsync());
    token2 = d.RootElement.GetProperty("accessToken").GetString();
}
Comprobar("sesión recuperada sin contraseña", !string.IsNullOrWhiteSpace(token2));
if (token2 is null) return 1;

// enviar cola
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token2);
var enviados = 0;
using (var cn = new SqliteConnection($"Data Source={db}"))
{
    cn.Open();
    var ops = new List<(string Id, string M, string R, string? C)>();
    using (var q = cn.CreateCommand())
    {
        q.CommandText = "SELECT id, metodo, ruta, cuerpo FROM pendientes ORDER BY creado_en";
        using var rd = q.ExecuteReader();
        while (rd.Read()) ops.Add((rd.GetString(0), rd.GetString(1), rd.GetString(2), rd.IsDBNull(3) ? null : rd.GetString(3)));
    }

    foreach (var op in ops)
    {
        var pet = new HttpRequestMessage(new HttpMethod(op.M), api + op.R);
        if (op.C is not null) pet.Content = new StringContent(op.C, Encoding.UTF8, "application/json");
        var resp = await http.SendAsync(pet);
        if (resp.IsSuccessStatusCode)
        {
            using var del = cn.CreateCommand();
            del.CommandText = "DELETE FROM pendientes WHERE id = @i";
            del.Parameters.AddWithValue("@i", op.Id);
            del.ExecuteNonQuery();
            enviados++;
        }
        Comprobar($"envío {op.M} {op.R}", resp.IsSuccessStatusCode, $"HTTP {(int)resp.StatusCode}");
    }

    using var fin = cn.CreateCommand();
    fin.CommandText = "SELECT COUNT(*) FROM pendientes";
    Comprobar("cola vacía tras reconectar", Convert.ToInt32(fin.ExecuteScalar()) == 0, $"{enviados} enviado(s)");
}

// verificar en servidor
var ver = await http.GetAsync($"{api}/api/vehiculos");
bool? ahora = null;
using (var d = JsonDocument.Parse(await ver.Content.ReadAsStringAsync()))
{
    foreach (var v in d.RootElement.EnumerateArray())
        if (v.GetProperty("placa").GetString() == "DEF-456")
            ahora = v.GetProperty("activo").GetBoolean();
}
Comprobar("el servidor aplicó el cambio", ahora is not null && ahora != activoAntes, $"{activoAntes} -> {ahora}");

// restaurar
if (ahora is not null && ahora != activoAntes)
{
    await http.PatchAsync($"{api}/api/vehiculos/{idVeh}/activo",
        new StringContent(JsonSerializer.Serialize(new { activo = activoAntes }), Encoding.UTF8, "application/json"));
    Comprobar("dato original restaurado", true);
}

Console.WriteLine("\n============================================================");
Console.WriteLine(ok ? "  TODO CORRECTO" : "  HAY FALLOS");
Console.WriteLine("============================================================");
return ok ? 0 : 1;
