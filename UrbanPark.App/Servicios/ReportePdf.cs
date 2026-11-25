using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QColors = QuestPDF.Helpers.Colors;
using QPages = QuestPDF.Helpers.PageSizes;
using UrbanPark.App.Modelos;
using QContainer = QuestPDF.Infrastructure.IContainer;

namespace UrbanPark.App.Servicios;

/// <summary>PDF del reporte: una hoja por parqueadero.</summary>
public static class ReportePdf
{
    static ReportePdf()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static void Guardar(string ruta, IReadOnlyList<(string Parqueadero, ReporteDto Reporte)> paginas,
        DateTime desde, DateTime hasta)
    {
        if (paginas.Count == 0)
            throw new InvalidOperationException("No hay parqueaderos para exportar.");

        var periodo = desde.Date == hasta.Date
            ? $"Hoy / día {desde:dd/MM/yyyy}"
            : $"{desde:dd/MM/yyyy} → {hasta:dd/MM/yyyy}";

        Document.Create(doc =>
        {
            foreach (var (parqueadero, r) in paginas)
                doc.Page(page => PaginaParqueadero(page, parqueadero, r, periodo));
        }).GeneratePdf(ruta);
    }

    private static void PaginaParqueadero(PageDescriptor page, string parqueadero, ReporteDto r, string periodo)
    {
        var nombre = parqueadero.Replace("Urban Park ", "", StringComparison.OrdinalIgnoreCase).Trim();
        var estado = r.CuadranConPlazas ? "En orden" : "Revisar";
        var resumen = string.IsNullOrWhiteSpace(r.ResumenIa) ? "Sin resumen IA." : Recortar(r.ResumenIa, 520);
        var hallazgos = r.Hallazgos.Count == 0
            ? "Sin hallazgos en el periodo."
            : "• " + string.Join("\n• ", r.Hallazgos.Take(5).Select(h => Recortar(h, 120)));

        page.Size(QPages.Letter);
        page.Margin(28);
        page.DefaultTextStyle(x => x.FontSize(9));

        page.Header().Column(col =>
        {
            col.Spacing(2);
            col.Item().Row(row =>
            {
                row.RelativeItem().Text("Urban Park").FontSize(16).Bold().FontColor("#D2A80F");
                row.AutoItem().Text(estado).FontSize(9).SemiBold()
                    .FontColor(r.CuadranConPlazas ? QColors.Green.Darken2 : QColors.Orange.Darken2);
            });
            col.Item().Text(nombre).FontSize(13).SemiBold();
            col.Item().Text($"Periodo: {periodo}").FontSize(10);
            col.Item().Text(r.ProveedorIa).FontSize(8).FontColor(QColors.Grey.Medium);
            col.Item().PaddingTop(4).LineHorizontal(0.8f).LineColor(QColors.Grey.Lighten2);
        });

        page.Content().PaddingVertical(6).Column(col =>
        {
            col.Spacing(8);

            col.Item().Row(row =>
            {
                row.RelativeItem().Element(c => SeccionCompacta(c, "Plazas ahora", tabla =>
                {
                    Fila(tabla, "Total", r.PlazasTotal.ToString());
                    Fila(tabla, "Ocupadas", r.PlazasOcupadas.ToString());
                    Fila(tabla, "Libres", r.PlazasLibres.ToString());
                    Fila(tabla, "Ocupación", $"{r.PlazasPorcentaje}%");
                }));
                row.ConstantItem(10);
                row.RelativeItem().Element(c => SeccionCompacta(c, "Dinero del periodo", tabla =>
                {
                    Fila(tabla, "Tickets", r.TotalTickets.ToString());
                    Fila(tabla, "Cerrados", r.Cerrados.ToString());
                    Fila(tabla, "Recaudado", Ui.Moneda(r.Recaudado));
                    Fila(tabla, "Descuento", Ui.Moneda(r.DescuentoAplicado));
                    Fila(tabla, "Por cobrar", Ui.Moneda(r.PorCobrarEstimado));
                }));
            });

            if (r.Metricas.PorZona.Count > 0)
            {
                col.Item().Element(c => SeccionCompacta(c, "Actividad por zona", tabla =>
                {
                    foreach (var (zona, cantidad) in r.Metricas.PorZona.OrderByDescending(kv => kv.Value).Take(8))
                        Fila(tabla, zona, $"{cantidad}");
                }));
            }

            if (r.Metricas.PlazasMasUsadas.Any(p => p.Usos > 0))
            {
                var total = r.Metricas.PlazasMasUsadas.Sum(p => p.Usos);
                col.Item().Element(c => SeccionCompacta(c, "Tipo de persona", tabla =>
                {
                    foreach (var p in r.Metricas.PlazasMasUsadas.Where(x => x.Usos > 0).Take(6))
                    {
                        var pct = total == 0 ? 0 : p.Usos * 100.0 / total;
                        Fila(tabla, p.Plaza, $"{pct:0}%");
                    }
                }));
            }

            var ocupados = r.Metricas.PlazasOcupadasDetalle;
            if (ocupados.Count > 0)
            {
                col.Item().Element(c => SeccionCompacta(c, "Estacionados ahora (muestra)", tabla =>
                {
                    foreach (var p in ocupados.Take(5))
                        Fila(tabla, $"{p.Plaza} · {p.Placa}", Ui.Moneda(p.PorCobrar));
                }));
            }

            col.Item().Element(c => SeccionTextoCompacta(c, "Resumen IA", resumen));
            col.Item().Element(c => SeccionTextoCompacta(c, "Puntos clave", hallazgos));
        });

        page.Footer().AlignCenter().Text(text =>
        {
            text.DefaultTextStyle(x => x.FontSize(7).FontColor(QColors.Grey.Medium));
            text.Span($"{nombre} · Urban Park · Pág. ");
            text.CurrentPageNumber();
            text.Span(" / ");
            text.TotalPages();
        });
    }

    private static void SeccionCompacta(QContainer contenedor, string titulo, Action<TableDescriptor> filas)
    {
        contenedor.Column(col =>
        {
            col.Item().Text(titulo).FontSize(10).SemiBold().FontColor("#243244");
            col.Item().PaddingTop(2).Table(tabla =>
            {
                tabla.ColumnsDefinition(cols =>
                {
                    cols.RelativeColumn(3);
                    cols.RelativeColumn(2);
                });
                filas(tabla);
            });
        });
    }

    private static void Fila(TableDescriptor tabla, string etiqueta, string valor)
    {
        tabla.Cell().BorderBottom(0.4f).BorderColor(QColors.Grey.Lighten3).PaddingVertical(2)
            .Text(etiqueta).FontSize(8);
        tabla.Cell().BorderBottom(0.4f).BorderColor(QColors.Grey.Lighten3).PaddingVertical(2)
            .AlignRight().Text(valor).FontSize(8).SemiBold();
    }

    private static void SeccionTextoCompacta(QContainer contenedor, string titulo, string cuerpo)
    {
        contenedor.Column(col =>
        {
            col.Item().Text(titulo).FontSize(10).SemiBold().FontColor("#243244");
            col.Item().PaddingTop(2).Background(QColors.Grey.Lighten4).Padding(6)
                .Text(cuerpo).FontSize(7.5f).LineHeight(1.25f);
        });
    }

    private static string Recortar(string texto, int maximo) =>
        string.IsNullOrWhiteSpace(texto) ? "" : texto.Length <= maximo ? texto : texto[..maximo].TrimEnd() + "…";
}
