using System.Security.Cryptography;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Segundo factor con el autenticador de Google (TOTP, RFC 6238 sobre HMAC-SHA1).
/// El usuario escanea un QR con Google Authenticator y, al entrar, escribe los 6 dígitos
/// que muestra la app. No hace falta ninguna cuenta de Google Cloud: el secreto vive en
/// la base de datos del sistema.
/// </summary>
public static class ServicioTotp
{
    private const string Alfabeto = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public const int Digitos = 6;
    public const int Periodo = 30;
    public const string Emisor = "Urban Park";

    /// <summary>Secreto nuevo de 160 bits (el tamaño recomendado por el RFC).</summary>
    public static string NuevoSecreto() => Base32(GenerarBytes(20));

    private static byte[] GenerarBytes(int cantidad)
    {
        var bytes = new byte[cantidad];
        RandomNumberGenerator.Fill(bytes);
        return bytes;
    }

    /// <summary>URI que entiende Google Authenticator (se dibuja como QR en la app).</summary>
    public static string UriProvisioning(string usuario, string secreto)
    {
        var etiqueta = Uri.EscapeDataString($"{Emisor}:{usuario}");
        var emisor = Uri.EscapeDataString(Emisor);
        return $"otpauth://totp/{etiqueta}?secret={secreto}&issuer={emisor}&algorithm=SHA1&digits={Digitos}&period={Periodo}";
    }

    /// <summary>Código de 6 dígitos para un instante concreto.</summary>
    public static string Codigo(string secreto, DateTime momento)
    {
        var clave = Base32ADecodificar(secreto);
        var pasos = (long)(momento.ToUniversalTime() - DateTime.UnixEpoch).TotalSeconds / Periodo;
        return CodigoHotp(clave, pasos);
    }

    /// <summary>
    /// Valida el código aceptando un paso de margen (reloj del celular adelantado o atrasado).
    /// Compara en tiempo constante para no filtrar información.
    /// </summary>
    public static bool Verificar(string? secreto, string? codigo, DateTime? momento = null, int ventana = 1)
    {
        if (string.IsNullOrWhiteSpace(secreto) || string.IsNullOrWhiteSpace(codigo)) return false;

        var limpio = new string(codigo.Where(char.IsDigit).ToArray());
        if (limpio.Length != Digitos) return false;

        var ahora = momento ?? DateTime.UtcNow;
        for (var desfase = -ventana; desfase <= ventana; desfase++)
        {
            var esperado = Codigo(secreto, ahora.AddSeconds(desfase * Periodo));
            if (CryptographicOperations.FixedTimeEquals(
                    System.Text.Encoding.UTF8.GetBytes(esperado),
                    System.Text.Encoding.UTF8.GetBytes(limpio)))
                return true;
        }

        return false;
    }

    private static string CodigoHotp(byte[] clave, long contador)
    {
        var pasos = BitConverter.GetBytes(contador);
        if (BitConverter.IsLittleEndian) Array.Reverse(pasos);

        using var hmac = new HMACSHA1(clave);
        var hash = hmac.ComputeHash(pasos);

        var offset = hash[^1] & 0x0F;
        var binario = ((hash[offset] & 0x7F) << 24) |
                      ((hash[offset + 1] & 0xFF) << 16) |
                      ((hash[offset + 2] & 0xFF) << 8) |
                      (hash[offset + 3] & 0xFF);

        var codigo = binario % (int)Math.Pow(10, Digitos);
        return codigo.ToString(new string('0', Digitos));
    }

    // ---------------------------------------------------------------- base32 (RFC 4648)
    public static string Base32(byte[] datos)
    {
        var salida = new System.Text.StringBuilder((datos.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in datos)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                salida.Append(Alfabeto[(buffer >> bits) & 0x1F]);
            }
        }

        if (bits > 0)
            salida.Append(Alfabeto[(buffer << (5 - bits)) & 0x1F]);

        return salida.ToString();
    }

    public static byte[] Base32ADecodificar(string? texto)
    {
        var limpio = (texto ?? "")
            .Trim()
            .Replace(" ", "")
            .Replace("-", "")
            .TrimEnd('=')
            .ToUpperInvariant();

        var salida = new List<byte>(limpio.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in limpio)
        {
            var valor = Alfabeto.IndexOf(c);
            if (valor < 0) continue;   // ignora cualquier carácter raro
            buffer = (buffer << 5) | valor;
            bits += 5;
            if (bits < 8) continue;
            bits -= 8;
            salida.Add((byte)((buffer >> bits) & 0xFF));
        }

        return [.. salida];
    }
}
