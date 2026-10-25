using NPOI.SS.UserModel;
using NPOI.SS.UserModel.Charts;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Excel de la base del periodo más gráficas y señales para el administrador:
/// hora pico, desbalance entre zonas, permanencia alta y peso del descuento.
/// </summary>
public static class ExcelDecisiones
{
    public static void Guardar(string ruta, IReadOnlyList<TicketDto> tickets, ReporteDto? reporte,
        string modelo, DateTime desde, DateTime hasta)
    {
        using var libro = new XSSFWorkbook();
        var negrita = libro.CreateCellStyle();
        var fuente = libro.CreateFont();
        fuente.IsBold = true;
        negrita.SetFont(fuente);

        var alerta = libro.CreateCellStyle();
        alerta.FillForegroundColor = IndexedColors.LightYellow.Index;
        alerta.FillPattern = FillPattern.SolidForeground;

        Base(libro, negrita, tickets);
        Graficas(libro, negrita, tickets, reporte);
        Decisiones(libro, negrita, alerta, tickets, reporte, modelo, desde, hasta);

        using var archivo = File.Create(ruta);
        libro.Write(archivo);
    }

    private static void Base(XSSFWorkbook libro, ICellStyle negrita, IReadOnlyList<TicketDto> tickets)
    {
        var hoja = libro.CreateSheet("Base");
        var encabezados = new[]
        {
            "codigo", "placa", "plaza", "zona", "estado", "entrada", "salida",
            "tarifa_hora", "valor_bruto", "valor_pagado", "valor_descuento", "minutos", "cuadra"
        };
        var fila = hoja.CreateRow(0);
        for (var c = 0; c < encabezados.Length; c++)
        {
            var celda = fila.CreateCell(c);
            celda.SetCellValue(encabezados[c]);
            celda.CellStyle = negrita;
        }

        double brutoTotal = 0, pagadoTotal = 0, descuentoTotal = 0;
        for (var i = 0; i < tickets.Count; i++)
        {
            var ticket = tickets[i];
            var (bruto, pagado, descuento) = Montos(ticket);
            brutoTotal += bruto;
            pagadoTotal += pagado;
            descuentoTotal += descuento;
            var minutos = Minutos(ticket);
            var cuadra = ticket.Estado != "CERRADO" || Math.Abs(bruto - (pagado + descuento)) < 0.02 ? "SI" : "NO";

            var r = hoja.CreateRow(i + 1);
            r.CreateCell(0).SetCellValue(ticket.Codigo);
            r.CreateCell(1).SetCellValue(ticket.Placa);
            r.CreateCell(2).SetCellValue(ticket.EspacioCodigo ?? ticket.EspacioId.ToString());
            r.CreateCell(3).SetCellValue(ticket.Zona ?? "");
            r.CreateCell(4).SetCellValue(ticket.Estado);
            r.CreateCell(5).SetCellValue(ticket.Entrada.ToString("yyyy-MM-dd HH:mm"));
            r.CreateCell(6).SetCellValue(ticket.Salida?.ToString("yyyy-MM-dd HH:mm") ?? "");
            r.CreateCell(7).SetCellValue(ticket.TarifaHora ?? 0);
            r.CreateCell(8).SetCellValue(bruto);
            r.CreateCell(9).SetCellValue(pagado);
            r.CreateCell(10).SetCellValue(descuento);
            if (minutos is not null) r.CreateCell(11).SetCellValue(minutos.Value);
            r.CreateCell(12).SetCellValue(cuadra);
        }

        var total = hoja.CreateRow(tickets.Count + 1);
        var celdaTotal = total.CreateCell(0);
        celdaTotal.SetCellValue("TOTAL");
        celdaTotal.CellStyle = negrita;
        total.CreateCell(8).SetCellValue(Math.Round(brutoTotal, 2));
        total.CreateCell(9).SetCellValue(Math.Round(pagadoTotal, 2));
        total.CreateCell(10).SetCellValue(Math.Round(descuentoTotal, 2));
        for (var c = 0; c < encabezados.Length; c++)
            hoja.SetColumnWidth(c, 16 * 256);
    }

    private static void Graficas(XSSFWorkbook libro, ICellStyle negrita, IReadOnlyList<TicketDto> tickets, ReporteDto? reporte)
    {
        var hoja = libro.CreateSheet("Graficas");
        var dibujo = (XSSFDrawing)hoja.CreateDrawingPatriarch();

        var horas = Enumerable.Range(0, 24)
            .Select(h => (Etiqueta: $"{h:00}:00", Valor: (double)tickets.Count(t => t.Entrada.Hour == h)))
            .ToArray();
        Tabla(hoja, negrita, 0, "Hora", "Entradas", horas);
        Grafico(hoja, dibujo, "Entradas por hora", 0, horas.Length, 3, 0, 14, 16, columna: true);

        var porZona = tickets
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Zona) ? "Sin zona" : t.Zona!)
            .OrderByDescending(g => g.Count())
            .Select(g => (g.Key, (double)g.Count()))
            .ToArray();
        if (porZona.Length > 0)
        {
            Tabla(hoja, negrita, 18, "Zona", "Tickets", porZona);
            Grafico(hoja, dibujo, "Tickets por zona", 18, porZona.Length, 3, 18, 14, 34, columna: false);
        }

        var permanencia = tickets
            .Select(t => (Zona: string.IsNullOrWhiteSpace(t.Zona) ? "Sin zona" : t.Zona!, Minutos: Minutos(t)))
            .Where(t => t.Minutos is not null)
            .GroupBy(t => t.Zona)
            .Select(g => (g.Key, Math.Round(g.Average(t => t.Minutos!.Value), 0)))
            .OrderByDescending(x => x.Item2)
            .ToArray();
        if (permanencia.Length > 0)
        {
            Tabla(hoja, negrita, 36, "Zona", "Minutos promedio", permanencia);
            Grafico(hoja, dibujo, "Permanencia promedio por zona", 36, permanencia.Length, 3, 36, 14, 52, columna: false);
        }

        var porDia = (reporte?.Metricas.PorDia ?? [])
            .Select(d => (d.Fecha, d.Pagado, d.Descuento))
            .ToArray();
        if (porDia.Length > 0)
        {
            var titulo = hoja.CreateRow(54);
            Titulo(titulo, 0, "Dia", negrita);
            Titulo(titulo, 1, "Pagado", negrita);
            Titulo(titulo, 2, "Descuento", negrita);
            for (var i = 0; i < porDia.Length; i++)
            {
                var r = hoja.GetRow(55 + i) ?? hoja.CreateRow(55 + i);
                r.CreateCell(0).SetCellValue(porDia[i].Fecha);
                r.CreateCell(1).SetCellValue(porDia[i].Pagado);
                r.CreateCell(2).SetCellValue(porDia[i].Descuento);
            }
            GraficoDoble(hoja, dibujo, "Pagado y descuento por dia", 54, porDia.Length, 4, 54, 15, 70);
        }

        var personas = (reporte?.Metricas.PlazasMasUsadas ?? [])
            .Select(p => (p.Plaza, (double)p.Usos))
            .ToArray();
        if (personas.Length > 0)
        {
            Tabla(hoja, negrita, 72, "Tipo de persona", "Tickets", personas);
            Grafico(hoja, dibujo, "Tipo de persona", 72, personas.Length, 3, 72, 12, 88, columna: true);
        }

        hoja.SetColumnWidth(0, 28 * 256);
        hoja.SetColumnWidth(1, 18 * 256);
        hoja.SetColumnWidth(2, 16 * 256);
    }

    private static void Decisiones(XSSFWorkbook libro, ICellStyle negrita, ICellStyle alerta,
        IReadOnlyList<TicketDto> tickets, ReporteDto? reporte, string modelo, DateTime desde, DateTime hasta)
    {
        var hoja = libro.CreateSheet("Decisiones");
        var fila = 0;
        Escribir(hoja, fila++, 0, $"Decisiones del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}", negrita);
        Escribir(hoja, fila++, 0, reporte is null
            ? $"Motor: Ollama {modelo} no respondió. Las señales salen de los tickets."
            : $"Motor: {reporte.ProveedorIa} · modelo {modelo}");
        fila++;
        Escribir(hoja, fila++, 0, "Lectura de la IA", negrita);
        Escribir(hoja, fila++, 0, string.IsNullOrWhiteSpace(reporte?.ResumenIa)
            ? "Sin resumen de la IA."
            : reporte!.ResumenIa);
        fila++;

        Escribir(hoja, fila++, 0, "Señales para decidir", negrita);
        var cab = hoja.CreateRow(fila++);
        Titulo(cab, 0, "Senal", negrita);
        Titulo(cab, 1, "Valor", negrita);
        Titulo(cab, 2, "Que mirar", negrita);

        foreach (var senal in Senales(tickets, reporte))
        {
            var r = hoja.CreateRow(fila++);
            r.CreateCell(0).SetCellValue(senal.Nombre);
            r.CreateCell(1).SetCellValue(senal.Valor);
            var lectura = r.CreateCell(2);
            lectura.SetCellValue(senal.Lectura);
            if (senal.Alerta)
            {
                r.GetCell(0).CellStyle = alerta;
                r.GetCell(1).CellStyle = alerta;
                lectura.CellStyle = alerta;
            }
        }

        if (reporte?.Hallazgos.Count > 0)
        {
            fila++;
            Escribir(hoja, fila++, 0, "Hallazgos", negrita);
            foreach (var hallazgo in reporte.Hallazgos)
                Escribir(hoja, fila++, 0, hallazgo);
        }

        hoja.SetColumnWidth(0, 28 * 256);
        hoja.SetColumnWidth(1, 36 * 256);
        hoja.SetColumnWidth(2, 88 * 256);
    }

    private static IEnumerable<(string Nombre, string Valor, string Lectura, bool Alerta)> Senales(
        IReadOnlyList<TicketDto> tickets, ReporteDto? reporte)
    {
        var total = Math.Max(tickets.Count, 1);
        var porHora = tickets.GroupBy(t => t.Entrada.Hour).OrderByDescending(g => g.Count()).FirstOrDefault();
        if (porHora is not null)
        {
            var pct = 100.0 * porHora.Count() / total;
            var alerta = pct >= 25;
            yield return ("Hora pico",
                $"{porHora.Key:00}:00 · {porHora.Count()} entradas ({pct:0}%)",
                alerta
                    ? "Cuello de botella: esa hora concentra las llegadas. Conviene abrir más plazas o adelantar el ingreso."
                    : "Las llegadas están repartidas. Esa es solo la hora más cargada.",
                alerta);
        }

        var zonas = tickets
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Zona) ? "Sin zona" : t.Zona!)
            .Select(g => (Zona: g.Key, N: g.Count()))
            .OrderByDescending(z => z.N)
            .ToList();
        if (zonas.Count >= 2)
        {
            var media = zonas.Average(z => z.N);
            var alerta = zonas[0].N >= media * 2;
            yield return ("Zonas",
                $"{zonas[0].Zona}: {zonas[0].N} · {zonas[^1].Zona}: {zonas[^1].N}",
                alerta
                    ? "Desbalance: una zona carga mucho más que las otras. Revisa si la asignación está empujando todo hacia el mismo lado."
                    : "El movimiento entre zonas está relativamente parejo.",
                alerta);
        }

        var estadias = tickets
            .Select(t => (Zona: string.IsNullOrWhiteSpace(t.Zona) ? "Sin zona" : t.Zona!, Minutos: Minutos(t)))
            .Where(t => t.Minutos is not null)
            .ToList();
        if (estadias.Count > 0)
        {
            var global = estadias.Average(t => t.Minutos!.Value);
            var porZona = estadias.GroupBy(t => t.Zona)
                .Select(g => (Zona: g.Key, N: g.Count(), Media: g.Average(t => t.Minutos!.Value)))
                .OrderByDescending(z => z.Media)
                .First();
            var alerta = porZona.N >= 2 && porZona.Media >= global * 1.5 && global > 0;
            yield return ("Permanencia",
                $"{porZona.Zona}: {porZona.Media:0} min · parqueadero: {global:0} min",
                alerta
                    ? "Cuello de botella de salida: esa zona retiene los vehículos mucho más que el resto."
                    : "La permanencia no muestra una zona atascada.",
                alerta);
        }

        var plazas = tickets
            .GroupBy(t => t.EspacioCodigo ?? t.EspacioId.ToString())
            .OrderByDescending(g => g.Count())
            .ToList();
        if (plazas.Count >= 4)
        {
            var pct = 100.0 * plazas[0].Count() / total;
            var alerta = pct >= 15;
            yield return ("Plaza más usada",
                $"{plazas[0].Key}: {plazas[0].Count()} usos ({pct:0}%)",
                alerta
                    ? "Desbalance de uso: una plaza se repite mucho. Puede estar mejor ubicada o mal repartida la asignación."
                    : "Ninguna plaza se lleva una parte excesiva de los usos.",
                alerta);
        }

        var cerrados = tickets.Where(t => t.Estado == "CERRADO").ToList();
        var pagado = cerrados.Sum(t => t.MontoTotal ?? 0);
        var descuento = cerrados.Sum(t => t.MontoDescuento);
        var baseDinero = pagado + descuento;
        if (baseDinero > 0)
        {
            var pct = 100.0 * descuento / baseDinero;
            var alerta = pct >= 20;
            yield return ("Descuento",
                $"{pct:0}% del valor bruto",
                alerta
                    ? "El descuento se está comiendo una parte grande del ingreso. Revisa reglas y perfiles."
                    : "El descuento no desbalancea el ingreso del periodo.",
                alerta);
        }

        var abiertos = tickets.Count(t => t.Estado != "CERRADO");
        if (tickets.Count > 0)
        {
            var pct = 100.0 * abiertos / tickets.Count;
            var alerta = pct >= 20;
            yield return ("Tickets abiertos del periodo",
                $"{abiertos} de {tickets.Count} ({pct:0}%)",
                alerta
                    ? "Hay muchos tickets sin cerrar. Esas plazas siguen ocupadas y el cobro queda pendiente."
                    : "La mayoría de los tickets del periodo ya está cerrada.",
                alerta);
        }

        if (reporte is not null)
        {
            yield return ("Ocupación ahora",
                $"{reporte.PlazasOcupadas} de {reporte.PlazasTotal} ({reporte.PlazasPorcentaje}%)",
                reporte.PlazasPorcentaje >= 85
                    ? "El parqueadero está cerca de llenarse. Conviene reservar margen en la zona con más libres."
                    : "Todavía hay margen de plazas libres.",
                reporte.PlazasPorcentaje >= 85);
        }
    }

    private static void Tabla(ISheet hoja, ICellStyle negrita, int fila, string colA, string colB,
        IReadOnlyList<(string Etiqueta, double Valor)> filas)
    {
        var cab = hoja.GetRow(fila) ?? hoja.CreateRow(fila);
        Titulo(cab, 0, colA, negrita);
        Titulo(cab, 1, colB, negrita);
        for (var i = 0; i < filas.Count; i++)
        {
            var r = hoja.GetRow(fila + 1 + i) ?? hoja.CreateRow(fila + 1 + i);
            r.CreateCell(0).SetCellValue(filas[i].Etiqueta);
            r.CreateCell(1).SetCellValue(filas[i].Valor);
        }
    }

    private static void Grafico(ISheet hoja, XSSFDrawing dibujo, string titulo, int filaDatos, int cantidad,
        int col1, int fila1, int col2, int fila2, bool columna)
    {
        if (cantidad <= 0) return;
        var ancla = dibujo.CreateAnchor(0, 0, 0, 0, col1, fila1, col2, fila2);
        var grafico = (XSSFChart)dibujo.CreateChart(ancla);
        grafico.SetTitle(titulo);
        var leyenda = grafico.GetOrCreateLegend();
        leyenda.Position = LegendPosition.Bottom;

        var categorias = DataSources.FromStringCellRange(hoja, new CellRangeAddress(filaDatos + 1, filaDatos + cantidad, 0, 0));
        var valores = DataSources.FromNumericCellRange(hoja, new CellRangeAddress(filaDatos + 1, filaDatos + cantidad, 1, 1));
        if (columna)
        {
            var datos = grafico.ChartDataFactory.CreateColumnChartData<string, double>();
            var ejeX = grafico.ChartAxisFactory.CreateCategoryAxis(AxisPosition.Bottom);
            var ejeY = grafico.ChartAxisFactory.CreateValueAxis(AxisPosition.Left);
            datos.AddSeries(categorias, valores).SetTitle(titulo);
            grafico.Plot(datos, ejeX, ejeY);
        }
        else
        {
            var datos = grafico.ChartDataFactory.CreateBarChartData<string, double>();
            var ejeX = grafico.ChartAxisFactory.CreateCategoryAxis(AxisPosition.Left);
            var ejeY = grafico.ChartAxisFactory.CreateValueAxis(AxisPosition.Bottom);
            datos.AddSeries(categorias, valores).SetTitle(titulo);
            grafico.Plot(datos, ejeX, ejeY);
        }
    }

    private static void GraficoDoble(ISheet hoja, XSSFDrawing dibujo, string titulo, int filaDatos, int cantidad,
        int col1, int fila1, int col2, int fila2)
    {
        if (cantidad <= 0) return;
        var ancla = dibujo.CreateAnchor(0, 0, 0, 0, col1, fila1, col2, fila2);
        var grafico = (XSSFChart)dibujo.CreateChart(ancla);
        grafico.SetTitle(titulo);
        var leyenda = grafico.GetOrCreateLegend();
        leyenda.Position = LegendPosition.Bottom;
        var datos = grafico.ChartDataFactory.CreateLineChartData<string, double>();
        var ejeX = grafico.ChartAxisFactory.CreateCategoryAxis(AxisPosition.Bottom);
        var ejeY = grafico.ChartAxisFactory.CreateValueAxis(AxisPosition.Left);
        var categorias = DataSources.FromStringCellRange(hoja, new CellRangeAddress(filaDatos + 1, filaDatos + cantidad, 0, 0));
        datos.AddSeries(categorias, DataSources.FromNumericCellRange(hoja, new CellRangeAddress(filaDatos + 1, filaDatos + cantidad, 1, 1))).SetTitle("Pagado");
        datos.AddSeries(categorias, DataSources.FromNumericCellRange(hoja, new CellRangeAddress(filaDatos + 1, filaDatos + cantidad, 2, 2))).SetTitle("Descuento");
        grafico.Plot(datos, ejeX, ejeY);
    }

    private static void Titulo(IRow fila, int columna, string texto, ICellStyle negrita)
    {
        var celda = fila.CreateCell(columna);
        celda.SetCellValue(texto);
        celda.CellStyle = negrita;
    }

    private static void Escribir(ISheet hoja, int fila, int columna, string texto, ICellStyle? estilo = null)
    {
        var r = hoja.GetRow(fila) ?? hoja.CreateRow(fila);
        var celda = r.CreateCell(columna);
        celda.SetCellValue(texto);
        if (estilo is not null) celda.CellStyle = estilo;
    }

    private static (double Bruto, double Pagado, double Descuento) Montos(TicketDto ticket)
    {
        if (ticket.Estado != "CERRADO") return (0, 0, 0);
        var pagado = ticket.MontoTotal ?? 0;
        var descuento = ticket.MontoDescuento;
        var bruto = Math.Round(pagado + descuento, 2, MidpointRounding.AwayFromZero);
        return (bruto, pagado, descuento);
    }

    private static double? Minutos(TicketDto ticket)
    {
        if (ticket.Salida is null || ticket.Estado != "CERRADO") return null;
        var minutos = (ticket.Salida.Value - ticket.Entrada).TotalMinutes;
        return minutos < 0 ? null : Math.Round(minutos, 0);
    }
}
