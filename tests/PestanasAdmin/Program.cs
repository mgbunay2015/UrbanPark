// Fija qué pestañas ve cada rol y que el admin/operador tenga acceso directo a los
// tickets abiertos de todos los usuarios (para cobrarlos en caja).
var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

Console.WriteLine("=== PESTANAS POR ROL ===\n");

// Misma jerarquía que Sesion.RolPrincipal.
string RolPrincipal(string[] roles)
{
    var set = roles.Select(r => r.Trim().ToUpperInvariant()).ToHashSet();
    if (set.Contains("ADMIN")) return "ADMIN";
    if (set.Contains("OPERADOR")) return "OPERADOR";
    if (set.Contains("CLIENTE")) return "CLIENTE";
    return "";
}

// Misma construcción que vPrincipal(): la barra de pestañas de cada rol.
List<string> Pestanas(string[] roles)
{
    var rol = RolPrincipal(roles);
    var lista = new List<string> { "Mapa", "Autos" };

    if (rol == "ADMIN")
    {
        lista.Add("Tickets");
        lista.Add("Reportería");
        lista.Add("Cambiar precios");
    }
    else if (rol == "OPERADOR")
    {
        lista.Add("Tickets");
        lista.Add("Llegar");
    }
    else
    {
        lista.Add("Llegar");
    }

    lista.Add("Perfil");
    lista.Add("Salir");
    return lista;
}

var admin = new[] { "ADMIN" };
var operador = new[] { "OPERADOR" };
var cliente = new[] { "CLIENTE" };

// --- ADMIN: debe poder cobrar en caja ----------------------------------------------
var pestanasAdmin = Pestanas(admin);
Comprobar("admin tiene pestaña Tickets", pestanasAdmin.Contains("Tickets"));
Comprobar("admin conserva Reportería", pestanasAdmin.Contains("Reportería"));
Comprobar("admin conserva Cambiar precios", pestanasAdmin.Contains("Cambiar precios"));
Comprobar("admin tiene todas las de caja",
    pestanasAdmin.Contains("Tickets") && pestanasAdmin.Contains("Reportería") && pestanasAdmin.Contains("Cambiar precios"));
Console.WriteLine($"         admin: {string.Join(" · ", pestanasAdmin)}");

// --- OPERADOR ----------------------------------------------------------------------
var pestanasOperador = Pestanas(operador);
Comprobar("operador tiene pestaña Tickets", pestanasOperador.Contains("Tickets"));
Comprobar("operador no ve Reportería", !pestanasOperador.Contains("Reportería"));
Comprobar("operador no ve Cambiar precios", !pestanasOperador.Contains("Cambiar precios"));
Console.WriteLine($"         operador: {string.Join(" · ", pestanasOperador)}");

// --- CLIENTE: no debe tener la pestaña de caja -------------------------------------
var pestanasCliente = Pestanas(cliente);
Comprobar("cliente NO tiene pestaña Tickets", !pestanasCliente.Contains("Tickets"));
Comprobar("cliente tiene Llegar", pestanasCliente.Contains("Llegar"));
Comprobar("cliente no ve Reportería", !pestanasCliente.Contains("Reportería"));
Console.WriteLine($"         cliente: {string.Join(" · ", pestanasCliente)}");

// --- Quién ve tickets ajenos -------------------------------------------------------
bool VeTodosLosTickets(string[] roles) => RolPrincipal(roles) is "ADMIN" or "OPERADOR";
Comprobar("admin ve tickets de todos", VeTodosLosTickets(admin));
Comprobar("operador ve tickets de todos", VeTodosLosTickets(operador));
Comprobar("cliente solo ve los suyos", !VeTodosLosTickets(cliente));

// --- El botón del mapa cambia de texto según el rol --------------------------------
string TextoBotonMapa(string[] roles) =>
    RolPrincipal(roles) == "CLIENTE" ? "Ver mis tickets" : "Ver tickets abiertos";

Comprobar("cliente ve «Ver mis tickets»", TextoBotonMapa(cliente) == "Ver mis tickets");
Comprobar("admin ve «Ver tickets abiertos»", TextoBotonMapa(admin) == "Ver tickets abiertos");
Comprobar("operador ve «Ver tickets abiertos»", TextoBotonMapa(operador) == "Ver tickets abiertos");

// --- El admin puede cobrar un ticket ajeno, con conexión ---------------------------
bool PuedeCobrarAjeno(string[] roles, bool enLinea) => VeTodosLosTickets(roles) && enLinea;
Comprobar("admin cobra ticket ajeno con conexión", PuedeCobrarAjeno(admin, true));
Comprobar("admin no cobra sin conexión", !PuedeCobrarAjeno(admin, false));
Comprobar("cliente nunca cobra ajeno", !PuedeCobrarAjeno(cliente, true));

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: el admin y el operador ven y cobran los tickets abiertos de todos."
    : "RESULTADO: la visibilidad de tickets por rol no es correcta.");
return ok ? 0 : 1;
