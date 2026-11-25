// Verifica que el cobro NO se pueda guardar para después cuando no hay conexión.
// Antes, /api/tickets/{id}/cerrar era un POST normal: sin red se encolaba en SQLite y el
// ticket quedaba "cobrado" sin haberse cobrado, con la plaza aún ocupada.
var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

Console.WriteLine("=== EL COBRO EXIGE CONEXION ===\n");

// Misma regla que ApiCliente.EsCobro
bool EsCobro(string ruta) =>
    ruta.Contains("/cerrar", StringComparison.OrdinalIgnoreCase)
    || ruta.Contains("/cobrar", StringComparison.OrdinalIgnoreCase)
    || ruta.Contains("/cancelar", StringComparison.OrdinalIgnoreCase)
    || ruta.Contains("sincronizar-liberaciones", StringComparison.OrdinalIgnoreCase);

// Misma regla que ApiCliente.SeEncola
bool SeEncola(string metodo, string ruta) =>
    !metodo.Equals("GET", StringComparison.OrdinalIgnoreCase)
    && !ruta.Contains("/api/auth/", StringComparison.OrdinalIgnoreCase)
    && !ruta.Contains("/api/ia/", StringComparison.OrdinalIgnoreCase)
    && !EsCobro(ruta);

// --- Lo que NO se debe encolar nunca -----------------------------------------------
var noEncolables = new[]
{
    "/api/tickets/123/cerrar",
    "/api/tickets/cerrar-varios",
    "/api/estacionar/45/cancelar",
    "/api/espacios/sincronizar-liberaciones"
};

foreach (var ruta in noEncolables)
{
    Comprobar($"POST {ruta} NO se encola", !SeEncola("POST", ruta));
    Comprobar($"  {ruta} se reconoce como cobro", EsCobro(ruta));
}

// --- Lo que sí puede esperar a tener red ------------------------------------------
var encolables = new[]
{
    "/api/vehiculos",
    "/api/estacionar",
    "/api/perfil/me",
    "/api/estacionar/45/activar"
};

foreach (var ruta in encolables)
{
    Comprobar($"POST {ruta} sí se encola", SeEncola("POST", ruta));
    Comprobar($"  {ruta} no se trata como cobro", !EsCobro(ruta));
}

// --- Las lecturas nunca se encolan -------------------------------------------------
Comprobar("GET /api/tickets no se encola", !SeEncola("GET", "/api/tickets"));
Comprobar("GET /api/tickets/1/cerrar tampoco (es lectura)", !SeEncola("GET", "/api/tickets/1/cerrar"));

// --- Los endpoints de autenticación siguen excluidos -------------------------------
Comprobar("POST /api/auth/login no se encola", !SeEncola("POST", "/api/auth/login"));
Comprobar("POST /api/ia/recomendar no se encola", !SeEncola("POST", "/api/ia/recomendar"));

// --- Comportamiento esperado al intentar cobrar sin red ----------------------------
(bool Puede, string Motivo) PuedeCobrar(bool internet, bool servidor)
{
    if (!internet)
        return (false, "Sin internet: no se puede cobrar. El cobro libera la plaza y se registra en el servidor.");
    if (!servidor)
        return (false, "No se pudo conectar con el servidor: el cobro no se puede guardar para después.");
    return (true, "");
}

var sinRed = PuedeCobrar(false, false);
Comprobar("sin internet no se puede cobrar", !sinRed.Puede);
Comprobar("el motivo explica por qué", sinRed.Motivo.Contains("no se puede cobrar", StringComparison.Ordinal));

var sinServidor = PuedeCobrar(true, false);
Comprobar("con internet pero sin servidor tampoco", !sinServidor.Puede);

var todoBien = PuedeCobrar(true, true);
Comprobar("con internet y servidor sí se puede", todoBien.Puede && todoBien.Motivo.Length == 0);

// --- El botón debe reflejar el estado ----------------------------------------------
string TextoBoton(bool puede) => puede ? "Cerrar / cobrar" : "Sin conexión · no se puede cobrar";
Comprobar("sin conexión el botón lo dice", TextoBoton(false).Contains("Sin conexión", StringComparison.Ordinal));
Comprobar("con conexión el botón es el normal", TextoBoton(true) == "Cerrar / cobrar");

// Misma regla que la vista: el botón se activa solo si se puede cobrar.
bool BotonActivo(bool internet, bool servidor) => PuedeCobrar(internet, servidor).Puede;
Comprobar("el botón está desactivado sin internet", !BotonActivo(false, true));
Comprobar("el botón está desactivado sin servidor", !BotonActivo(true, false));
Comprobar("el botón se activa con todo en orden", BotonActivo(true, true));

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: sin conexión el cobro queda bloqueado y nunca se guarda en cola."
    : "RESULTADO: el cobro todavía podría completarse sin conexión.");
return ok ? 0 : 1;
