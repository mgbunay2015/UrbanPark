using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Emite y valida los JWT de la aplicacion (reemplaza a Keycloak).
/// Mantiene los mismos claims que usaba el sistema original:
///   preferred_username  -> identidad del usuario
///   role                -> autoridades ROLE_* (ASP.NET Core)
///   realm_access.roles  -> compatibilidad con el token de Keycloak
/// </summary>
public sealed class TokenService
{
    public const string TipoAcceso = "access";
    public const string TipoRefresh = "refresh";

    private readonly IConfiguration _cfg;

    public TokenService(IConfiguration cfg) => _cfg = cfg;

    public int MinutosAcceso => int.TryParse(_cfg["Jwt:AccessTokenMinutes"], out var m) && m > 0 ? m : 300;
    public int DiasRefresh => int.TryParse(_cfg["Jwt:RefreshTokenDays"], out var d) && d > 0 ? d : 7;

    private string ClaveTexto => _cfg["Jwt:Key"] is { Length: >= 32 } k
        ? k
        : throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres.");

    private SymmetricSecurityKey Clave() => new(Encoding.UTF8.GetBytes(ClaveTexto));

    public TokenValidationParameters ParametrosValidacion(bool validarVida = true) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = _cfg["Jwt:Issuer"],
        ValidateAudience = true,
        ValidAudience = _cfg["Jwt:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = Clave(),
        ValidateLifetime = validarVida,
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "preferred_username",
        RoleClaimType = "role"
    };

    private string Construir(IEnumerable<Claim> claims, DateTime expira)
    {
        var token = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expira,
            signingCredentials: new SigningCredentials(Clave(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string token, long expiraUnix) CrearAcceso(string username, IEnumerable<string> roles)
    {
        var lista = roles.ToArray();
        var expira = DateTime.UtcNow.AddMinutes(MinutosAcceso);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, username),
            new("preferred_username", username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new("token_use", TipoAcceso)
        };
        claims.AddRange(lista.Select(r => new Claim("role", r)));
        claims.Add(new Claim("realm_access", JsonSerializer.Serialize(new { roles = lista }), JsonClaimValueTypes.Json));

        var expiraUnix = new DateTimeOffset(expira, TimeSpan.Zero).ToUnixTimeSeconds();
        return (Construir(claims, expira), expiraUnix);
    }

    public string CrearRefresh(string username) => Construir(new[]
    {
        new Claim(JwtRegisteredClaimNames.Sub, username),
        new Claim("preferred_username", username),
        new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        new Claim("token_use", TipoRefresh)
    }, DateTime.UtcNow.AddDays(DiasRefresh));

    /// <summary>Valida un token (de acceso o de refresco) y devuelve su principal, o null si es invalido.</summary>
    public ClaimsPrincipal? Validar(string? token, string usoEsperado)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, ParametrosValidacion(), out _);
            var uso = principal.FindFirst("token_use")?.Value;
            return uso == usoEsperado ? principal : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
