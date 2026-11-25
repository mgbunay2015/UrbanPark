using System.Security.Cryptography;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Hash de contraseñas con PBKDF2-SHA256 (100.000 iteraciones, salt aleatorio de 16 bytes).
/// Formato almacenado: pbkdf2$&lt;iteraciones&gt;$&lt;saltBase64&gt;$&lt;hashBase64&gt;
/// No depende de paquetes externos: usa solo System.Security.Cryptography.
/// </summary>
public sealed class PasswordService
{
    private const int Iteraciones = 100_000;
    private const int LargoClave = 32;
    private const int LargoSalt = 16;

    public string Hashear(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(LargoSalt);
        var clave = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iteraciones, HashAlgorithmName.SHA256, LargoClave);
        return $"pbkdf2${Iteraciones}${Convert.ToBase64String(salt)}${Convert.ToBase64String(clave)}";
    }

    public bool Verificar(string? password, string? almacenado)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(almacenado))
            return false;

        var partes = almacenado.Split('$');
        if (partes.Length != 4 || partes[0] != "pbkdf2")
            return false;
        if (!int.TryParse(partes[1], out var iteraciones) || iteraciones <= 0)
            return false;

        byte[] salt, esperado;
        try
        {
            salt = Convert.FromBase64String(partes[2]);
            esperado = Convert.FromBase64String(partes[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var calculado = Rfc2898DeriveBytes.Pbkdf2(password, salt, iteraciones, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(calculado, esperado);
    }
}
