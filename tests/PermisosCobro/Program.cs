// Fija la regla: el cliente solo cobra SUS tickets; ADMIN y OPERADOR cobran los de cualquiera.
// Es el caso de uso real: el cliente se queda sin internet y no puede cobrar, así que el
// administrador cierra su ticket desde la caja.
var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

Console.WriteLine("=== QUIEN PUEDE CERRAR UN TICKET ===\n");

// Misma jerarquía que Sesion.RolPrincipal y Reglas.RolPrincipal.
string RolPrincipal(string[] roles)
{
    var set = roles.Select(r => r.Trim().ToUpperInvariant()).ToHashSet();
    if (set.Contains("ADMIN")) return "ADMIN";
    if (set.Contains("OPERADOR")) return "OPERADOR";
    if (set.Contains("CLIENTE")) return "CLIENTE";
    return "";
}

bool EsCliente(string[] roles) => RolPrincipal(roles) == "CLIENTE";

// El servidor decide con EsClienteSolo; la app refuerza con Sesion.EsOperador (ADMIN u OPERADOR).
bool PuedeCerrarDeOtro(string[] roles) => !EsCliente(roles);

// Se puede cobrar ese ticket: el cliente solo los suyos.
bool PuedeCobrar(string[] roles, string usuarioSesion, string usuarioTicket)
{
    var ajeno = !string.Equals(usuarioSesion, usuarioTicket, StringComparison.OrdinalIgnoreCase);
    if (!ajeno) return true;                 // es su propio ticket
    return PuedeCerrarDeOtro(roles);          // de otro: solo operador/admin
}

var admin = new[] { "ADMIN" };
var operador = new[] { "OPERADOR" };
var cliente = new[] { "CLIENTE" };

// --- ADMIN -------------------------------------------------------------------------
Comprobar("admin cobra el ticket de cliente2", PuedeCobrar(admin, "admin1", "cliente2"));
Comprobar("admin cobra el ticket de cliente1", PuedeCobrar(admin, "admin1", "cliente1"));
Comprobar("admin cobra el suyo", PuedeCobrar(admin, "admin1", "admin1"));
Comprobar("el admin ve tickets de todos", PuedeCerrarDeOtro(admin));

// --- OPERADOR ----------------------------------------------------------------------
Comprobar("operador cobra el ticket de un cliente", PuedeCobrar(operador, "operador1", "cliente2"));
Comprobar("el operador ve tickets de todos", PuedeCerrarDeOtro(operador));

// --- CLIENTE -----------------------------------------------------------------------
Comprobar("cliente cobra el suyo", PuedeCobrar(cliente, "cliente2", "cliente2"));
Comprobar("cliente NO cobra el de otro", !PuedeCobrar(cliente, "cliente2", "cliente1"),
    "solo el administrador puede");
Comprobar("cliente NO ve tickets ajenos", EsCliente(cliente));

// --- Casos límite ------------------------------------------------------------------
Comprobar("mayúsculas no cambian la regla", PuedeCobrar(admin, "ADMIN1", "cliente2"));
Comprobar("ticket sin usuario no se puede atribuir", PuedeCobrar(cliente, "cliente2", "") == false);

// --- La jerarquía de roles manda (un usuario con varios roles) ----------------------
var clienteYAdmin = new[] { "CLIENTE", "ADMIN" };
Comprobar("con rol ADMIN gana sobre CLIENTE", PuedeCobrar(clienteYAdmin, "alguien", "cliente2"));
Comprobar("el rol efectivo es ADMIN", RolPrincipal(clienteYAdmin) == "ADMIN");

// --- El cobro sigue exigiendo conexión, también para el admin ----------------------
bool CobraConConexion(string[] roles, string sesion, string dueno, bool enLinea) =>
    PuedeCobrar(roles, sesion, dueno) && enLinea;

Comprobar("admin sin internet tampoco cobra", !CobraConConexion(admin, "admin1", "cliente2", false));
Comprobar("admin con internet sí cobra el ajeno", CobraConConexion(admin, "admin1", "cliente2", true));
Comprobar("cliente con internet solo cobra el suyo",
    CobraConConexion(cliente, "cliente2", "cliente2", true) &&
    !CobraConConexion(cliente, "cliente2", "cliente1", true));

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: solo ADMIN/OPERADOR pueden cerrar tickets de otros; el cliente, solo los suyos."
    : "RESULTADO: la regla de permisos no es correcta.");
return ok ? 0 : 1;
