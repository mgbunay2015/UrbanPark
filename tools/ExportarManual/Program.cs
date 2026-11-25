using System.Diagnostics;
using System.Text;
using Markdig;

var raiz = EncontrarRaizProyecto();
var nombreMd = args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]) ? args[0].Trim() : "MANUAL_PROGRAMADOR.md";
var nombrePdf = args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]) ? args[1].Trim() : Path.GetFileNameWithoutExtension(nombreMd) + ".pdf";
var tituloDoc = args.Length > 2 && !string.IsNullOrWhiteSpace(args[2]) ? args[2].Trim() : "Manual del programador";
var subtitulo = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3])
    ? args[3].Trim()
    : ".NET MAUI · ASP.NET Core · MySQL · DeepSeek";

var md = Path.Combine(raiz, "docs", nombreMd);
var pdfDestino = Path.Combine(raiz, "docs", nombrePdf);
var cssOrigen = Path.Combine(raiz, "docs", "manual-pdf.css");
var nombreHtml = Path.GetFileNameWithoutExtension(nombreMd) + ".html";

if (!File.Exists(md))
{
    Console.Error.WriteLine($"No existe: {md}");
    return 1;
}

var trabajo = Path.Combine(raiz, "tools", "ExportarManual", "salida",
    DateTime.Now.ToString("yyyyMMdd_HHmmss"));
Directory.CreateDirectory(trabajo);
var html = Path.Combine(trabajo, nombreHtml);
var pdf = Path.Combine(trabajo, nombrePdf);
CopiarCss(cssOrigen, Path.Combine(trabajo, "manual-pdf.css"));

var markdown = await File.ReadAllTextAsync(md);
var cuerpo = Markdown.ToHtml(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build());

var generado = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
var plantilla = "<!DOCTYPE html>\n<html lang=\"es\">\n<head>\n"
    + "  <meta charset=\"utf-8\" />\n"
    + $"  <title>{tituloDoc} — Urban Park</title>\n"
    + "  <link rel=\"stylesheet\" href=\"manual-pdf.css\" />\n"
    + "</head>\n<body>\n"
    + "  <header class=\"portada\">\n"
    + "    <p class=\"marca\">Urban Park</p>\n"
    + $"    <h1>{tituloDoc}</h1>\n"
    + $"    <p class=\"sub\">{subtitulo}</p>\n"
    + $"    <p class=\"sub\">Versión generada: {generado}</p>\n"
    + "  </header>\n"
    + "  <main class=\"contenido\">\n"
    + cuerpo
    + "\n  </main>\n</body>\n</html>\n";

await File.WriteAllTextAsync(html, plantilla, Encoding.UTF8);

if (!IntentarPdfConEdge(html, pdf))
{
    Console.WriteLine($"HTML listo: {html}");
    Console.WriteLine("No se encontró Edge/Chrome para PDF. Abre el HTML y usa Imprimir → Guardar como PDF.");
    return 2;
}

try
{
    File.Copy(pdf, pdfDestino, true);
    Console.WriteLine($"PDF generado: {pdfDestino}");
}
catch (Exception ex)
{
    Console.WriteLine($"PDF en carpeta temporal: {pdf}");
    Console.WriteLine($"(No se pudo copiar a docs: {ex.Message})");
}

return 0;

static void CopiarCss(string origen, string destino)
{
    if (!File.Exists(origen)) return;
    File.Copy(origen, destino, true);
}

static string EncontrarRaizProyecto()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir))
    {
        if (File.Exists(Path.Combine(dir, "UrbanPark.slnx")) || Directory.Exists(Path.Combine(dir, "UrbanPark.Api")))
            return dir;
        dir = Directory.GetParent(dir)?.FullName ?? "";
    }

    return Directory.GetCurrentDirectory();
}

static bool IntentarPdfConEdge(string html, string pdf)
{
    string[] rutas =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            @"Microsoft\Edge\Application\msedge.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Microsoft\Edge\Application\msedge.exe"),
        @"C:\Program Files\Google\Chrome\Application\chrome.exe"
    ];

    var uri = new Uri(html).AbsoluteUri;
    foreach (var exe in rutas)
    {
        if (!File.Exists(exe)) continue;
        try
        {
            if (File.Exists(pdf)) File.Delete(pdf);
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments =
                    $"--headless=new --disable-gpu --no-pdf-header-footer --print-to-pdf=\"{pdf}\" \"{uri}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
            p?.WaitForExit(90_000);
            if (!File.Exists(pdf) || new FileInfo(pdf).Length < 1000)
                Thread.Sleep(3000);
            if (File.Exists(pdf) && new FileInfo(pdf).Length > 10_000)
                return true;
        }
        catch
        {
            // siguiente navegador
        }
    }

    return false;
}