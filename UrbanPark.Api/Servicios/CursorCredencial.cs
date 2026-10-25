using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace UrbanPark.Api.Servicios;

/// <summary>
/// Lee la clave del worker de Cursor que la extensión guarda en el almacén
/// cifrado de esta PC. El nombre (cursor-agent-worker-ext-...) no es la clave:
/// el valor no se copia al proyecto ni se escribe en el log.
/// </summary>
internal static class CursorCredencial
{
    private const string ClaveSecreto =
        """secret://{"extensionId":"anysphere.cursor-agent-worker","key":"cursor-agent-worker.apiKey"}""";

    public static string? Leer(string? configurada)
    {
        if (PareceClave(configurada))
            return configurada!.Trim();

        var entorno = Environment.GetEnvironmentVariable("CURSOR_API_KEY");
        if (PareceClave(entorno))
            return entorno!.Trim();

        return DesdeCursor();
    }

    private static bool PareceClave(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) &&
        (valor.StartsWith("crsr_", StringComparison.Ordinal) ||
         valor.StartsWith("cursor_", StringComparison.Ordinal) ||
         valor.StartsWith("key_", StringComparison.Ordinal));

    private static string? DesdeCursor()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var estadoLocal = Path.Combine(appData, "Cursor", "Local State");
            var rutaBase = Path.Combine(appData, "Cursor", "User", "globalStorage", "state.vscdb");
            if (!File.Exists(estadoLocal) || !File.Exists(rutaBase))
                return null;

            using var local = JsonDocument.Parse(File.ReadAllText(estadoLocal));
            var cifrada = Convert.FromBase64String(
                local.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString()!);
            var aes = ProtectedData.Unprotect(cifrada[5..], null, DataProtectionScope.CurrentUser);

            using var con = new SqliteConnection($"Data Source={rutaBase};Mode=ReadOnly");
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "select value from ItemTable where key = $k";
            cmd.Parameters.AddWithValue("$k", ClaveSecreto);
            if (cmd.ExecuteScalar() is not string crudo)
                return null;

            using var valor = JsonDocument.Parse(crudo);
            var buf = valor.RootElement.GetProperty("data").EnumerateArray()
                .Select(n => (byte)n.GetInt32()).ToArray();
            if (!buf.AsSpan(0, 3).SequenceEqual("v10"u8))
                return null;

            var nonce = buf[3..15];
            var cuerpo = buf[15..^16];
            var etiqueta = buf[^16..];
            var plano = new byte[cuerpo.Length];
            using var gcm = new AesGcm(aes, 16);
            gcm.Decrypt(nonce, cuerpo, etiqueta, plano);
            var texto = Encoding.UTF8.GetString(plano);
            return PareceClave(texto) ? texto : null;
        }
        catch (Exception ex) when (ex is IOException or CryptographicException or JsonException
                                       or SqliteException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }
}
