using Microsoft.AspNetCore.Mvc;
using UrbanPark.Api.Modelos;

namespace UrbanPark.Api.Controladores;

/// <summary>
/// Base de todos los controladores: identidad del token, rol efectivo y respuestas de error
/// con el mismo formato que el sistema original { timestamp, status, error, message, path }.
/// </summary>
[ApiController]
public abstract class ControladorBase : ControllerBase
{
    /// <summary>Claim preferred_username (igual que el backend original).</summary>
    protected string Username =>
        User?.FindFirst("preferred_username")?.Value
        ?? User?.Identity?.Name
        ?? "";

    protected string[] Roles => User?.FindAll("role").Select(c => c.Value).ToArray() ?? [];

    /// <summary>Rol efectivo unico y jerarquico: ADMIN &gt; OPERADOR &gt; CLIENTE.</summary>
    protected string RolPrincipal => Reglas.RolPrincipal(Roles);

    /// <summary>True solo si el rol principal es CLIENTE (mismo criterio que AuthSupport.esClienteSolo).</summary>
    protected bool EsClienteSolo => Reglas.EsClienteSolo(Roles);

    protected ObjectResult Error(int status, string mensaje) =>
        StatusCode(status, new ErrorResponse(DateTime.Now, status, Motivo(status), mensaje, Request.Path.Value ?? ""));

    protected static string Motivo(int status) => status switch
    {
        400 => "Bad Request",
        401 => "Unauthorized",
        403 => "Forbidden",
        404 => "Not Found",
        409 => "Conflict",
        502 => "Bad Gateway",
        503 => "Service Unavailable",
        _ => "Internal Server Error"
    };

    protected ObjectResult NoAutorizado(string mensaje) => Error(401, mensaje);
    protected ObjectResult Prohibido(string mensaje) => Error(403, mensaje);
    protected ObjectResult NoEncontrado(string mensaje) => Error(404, mensaje);
    protected ObjectResult Conflicto(string mensaje) => Error(409, mensaje);
    protected ObjectResult PeticionInvalida(string mensaje) => Error(400, mensaje);
    protected ObjectResult NoDisponible(string mensaje) => Error(503, mensaje);

    protected static bool EmailValido(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        email.Contains('@') &&
        email.IndexOf('@') > 0 &&
        email.LastIndexOf('.') > email.IndexOf('@') + 1;

    public static UsuarioDto ADto(Usuario u) =>
        new(u.Id.ToString(), u.Username, u.Email, u.FirstName, u.LastName, u.Enabled, Reglas.RolesDeApp(u.ListaRoles), u.TotpActivo);
}
