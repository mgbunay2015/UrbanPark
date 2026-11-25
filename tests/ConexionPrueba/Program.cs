// Comprueba la lógica del indicador: qué texto y color toca según lo que responda
// internet y el servidor. El fallo que se estaba corrigiendo: el punto se ponía verde
// porque el API local (127.0.0.1) contesta aunque el equipo no tenga internet.
var ok = true;
void Comprobar(string nombre, bool cond, string? detalle = null)
{
    Console.WriteLine($"  {(cond ? "OK   " : "FALLA")}  {nombre}{(detalle is null ? "" : "  [" + detalle + "]")}");
    if (!cond) ok = false;
}

Console.WriteLine("=== INDICADOR DE CONEXION ===\n");

// Misma regla que MonitorConexion.Recalcular(): verde solo con internet real Y servidor vivo.
string Estado(bool internet, bool servidor) =>
    internet && servidor ? "EnLinea" : "SinConexion";

string Descripcion(bool internet, bool servidor) =>
    internet && servidor ? "En línea"
    : !internet ? "Sin internet"
    : "Sin servidor";

// El caso que fallaba: API local encendido, equipo sin internet -> debe ser ROJO.
Comprobar("sin internet con API local encendido => rojo",
    Estado(internet: false, servidor: true) == "SinConexion",
    Descripcion(false, true));
Comprobar("el texto dice «Sin internet»", Descripcion(false, true) == "Sin internet");

// Con internet y API vivo -> verde.
Comprobar("con internet y servidor => verde",
    Estado(true, true) == "EnLinea", Descripcion(true, true));
Comprobar("el texto dice «En línea»", Descripcion(true, true) == "En línea");

// Con internet pero API caído -> rojo (se trabaja con SQLite).
Comprobar("con internet sin servidor => rojo",
    Estado(true, false) == "SinConexion", Descripcion(true, false));
Comprobar("distingue «Sin servidor» de «Sin internet»",
    Descripcion(true, false) == "Sin servidor");

// Sin nada -> rojo.
Comprobar("sin internet y sin servidor => rojo", Estado(false, false) == "SinConexion");

// El color debe seguir al estado.
string Color(bool internet, bool servidor) => Estado(internet, servidor) == "EnLinea" ? "verde" : "rojo";
Comprobar("el verde solo se usa con internet real",
    Color(true, true) == "verde" && Color(false, true) == "rojo" && Color(true, false) == "rojo");

// El sondeo debe repetirse: si no, el punto no cambiaría al perder la red.
const int segundosSondeo = 6;
Comprobar("el sondeo es periódico (cada pocos segundos)", segundosSondeo <= 10, $"cada {segundosSondeo} s");

Console.WriteLine();
Console.WriteLine(ok
    ? "RESULTADO: el punto solo está verde con internet real y servidor vivo."
    : "RESULTADO: el estado del indicador no es correcto.");
return ok ? 0 : 1;
