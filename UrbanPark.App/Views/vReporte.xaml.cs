using System.Globalization;
using System.Text;
using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Pantalla «Reporte IA» (solo ADMIN). Replica la especificación 3.6:
/// rango de fechas por fecha de ingreso del ticket y render claro del reporte.
/// La presentación usa el sistema de diseño (Resources/Styles) desde XAML y desde el código.
/// </summary>
public partial class vReporte : ContentPage
{
    private static readonly string[] EncabezadosTabla = ["Plaza", "Placa", "Cliente", "Zona", "Por cobrar"];
    private static readonly double[] AnchoTabla = [0.8, 1.1, 1.3, 1.0, 1.2];

    private bool generando;

    public vReporte()
    {
        InitializeComponent();

        FechasPorDefecto();
        MostrarInicial();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!Sesion.EsAdmin)
        {
            await Ui.Error(this, "No tienes acceso a esta pantalla con tu perfil");
            await Navigation.PopAsync();
        }
    }

    // ================================================================ fechas
    private void FechasPorDefecto()
    {
        dtpHasta.Date = DateTime.Today;
        dtpDesde.Date = DateTime.Today.AddDays(-6);
    }

    private void OnHoyClicked(object? sender, EventArgs e)
    {
        dtpDesde.Date = DateTime.Today;
        dtpHasta.Date = DateTime.Today;
    }

    private void OnMesClicked(object? sender, EventArgs e)
    {
        var hoy = DateTime.Today;
        dtpDesde.Date = new DateTime(hoy.Year, hoy.Month, 1);
        dtpHasta.Date = hoy;
    }

    private void OnUltimos7Clicked(object? sender, EventArgs e) => FechasPorDefecto();

    private static string Iso(DateTime fecha) => fecha.ToString("yyyy-MM-dd");

    // ================================================================ generar
    private async void OnGenerarClicked(object? sender, EventArgs e)
    {
        if (generando) return;

        // En .NET 10 la propiedad Date del DatePicker es DateTime?: si no hay fecha, es null.
        var desdeElegido = dtpDesde.Date;
        var hastaElegido = dtpHasta.Date;

        if (desdeElegido is null || hastaElegido is null)
        {
            await Ui.Aviso(this, "Indica fecha desde y hasta");
            return;
        }

        var fechaDesde = desdeElegido.Value.Date;
        var fechaHasta = hastaElegido.Value.Date;

        if (fechaHasta < fechaDesde)
        {
            await Ui.Aviso(this, "La fecha hasta no puede ser anterior a desde");
            return;
        }

        var desde = Iso(fechaDesde);
        var hasta = Iso(fechaHasta);

        generando = true;
        btnGenerar.IsEnabled = false;
        btnHoy.IsEnabled = false;
        btnMes.IsEnabled = false;
        btnUltimos7.IsEnabled = false;

        PrepararCaja();
        var motor = "Ollama";
        try
        {
            var estado = await ApiCliente.EstadoIaAsync();
            if (!string.IsNullOrWhiteSpace(estado.Modelo))
                motor = $"Ollama {estado.Modelo}";
        }
        catch (Exception)
        {
            // si el estado no responde, el aviso igual nombra a Ollama
        }
        contenedorReporte.Add(Tarjeta($"Generando reporte del {desde} al {hasta} con {motor}…", 15));
        lblPie.Text = "Analizando recaudación, ocupación y tickets del periodo.";

        try
        {
            var reporte = await ApiCliente.ReporteAsync(desde, hasta);
            PintarReporte(reporte, desde, hasta);
        }
        catch (Exception ex)
        {
            PintarError(ex is ApiExcepcion ? ex.Message : "Ocurrió un error: " + ex.Message);
        }
        finally
        {
            generando = false;
            btnGenerar.IsEnabled = true;
            btnHoy.IsEnabled = true;
            btnMes.IsEnabled = true;
            btnUltimos7.IsEnabled = true;
        }
    }

    private async void OnDescargarClicked(object? sender, EventArgs e)
    {
        btnDescargar.IsEnabled = false;
        try
        {
            var desdeElegido = dtpDesde.Date;
            var hastaElegido = dtpHasta.Date;
            if (desdeElegido is null || hastaElegido is null)
            {
                await Ui.Aviso(this, "Indica fecha desde y hasta");
                return;
            }

            var desde = desdeElegido.Value.Date;
            var hasta = hastaElegido.Value.Date;
            if (hasta < desde)
            {
                await Ui.Aviso(this, "La fecha hasta no puede ser anterior a desde");
                return;
            }

            var tickets = (await ApiCliente.ListarTicketsAsync() ?? [])
                .Where(t => t.Entrada.Date >= desde && t.Entrada.Date <= hasta)
                .OrderBy(t => t.Entrada)
                .ToList();
            if (tickets.Count == 0)
            {
                await Ui.Aviso(this, $"No hay tickets entre {desde:dd/MM/yyyy} y {hasta:dd/MM/yyyy}.");
                return;
            }

            var cultura = CultureInfo.GetCultureInfo("es-EC");
            var csv = new StringBuilder();
            csv.AppendLine("codigo;placa;plaza;zona;estado;entrada;salida;tarifa_hora;valor_bruto;valor_pagado;valor_descuento;cuadra");

            double brutoTotal = 0, pagadoTotal = 0, descuentoTotal = 0;
            foreach (var ticket in tickets)
            {
                var pagado = ticket.Estado == "CERRADO" ? ticket.MontoTotal ?? 0 : 0;
                var descuento = ticket.MontoDescuento;
                var bruto = Math.Round(pagado + descuento, 2, MidpointRounding.AwayFromZero);
                if (ticket.Estado != "CERRADO")
                {
                    bruto = 0;
                    descuento = 0;
                    pagado = 0;
                }

                brutoTotal += bruto;
                pagadoTotal += pagado;
                descuentoTotal += descuento;
                var cuadra = ticket.Estado != "CERRADO" || Math.Abs(bruto - (pagado + descuento)) < 0.02 ? "SI" : "NO";

                csv.Append(Csv(ticket.Codigo)).Append(';')
                    .Append(Csv(ticket.Placa)).Append(';')
                    .Append(Csv(ticket.EspacioCodigo ?? ticket.EspacioId.ToString())).Append(';')
                    .Append(Csv(ticket.Zona)).Append(';')
                    .Append(Csv(ticket.Estado)).Append(';')
                    .Append(ticket.Entrada.ToString("yyyy-MM-dd HH:mm", cultura)).Append(';')
                    .Append(ticket.Salida?.ToString("yyyy-MM-dd HH:mm", cultura) ?? "").Append(';')
                    .Append((ticket.TarifaHora ?? 0).ToString("0.00", cultura)).Append(';')
                    .Append(bruto.ToString("0.00", cultura)).Append(';')
                    .Append(pagado.ToString("0.00", cultura)).Append(';')
                    .Append(descuento.ToString("0.00", cultura)).Append(';')
                    .AppendLine(cuadra);
            }

            csv.Append("TOTAL;;;;;;;;")
                .Append(brutoTotal.ToString("0.00", cultura)).Append(';')
                .Append(pagadoTotal.ToString("0.00", cultura)).Append(';')
                .Append(descuentoTotal.ToString("0.00", cultura)).Append(';')
                .AppendLine(Math.Abs(brutoTotal - (pagadoTotal + descuentoTotal)) < 0.05 ? "SI" : "NO");

            var carpeta = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(carpeta);
            var ruta = System.IO.Path.Combine(carpeta, $"urbanpark-base-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.csv");
            await File.WriteAllTextAsync(ruta, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            lblPie.Text = "Ollama está armando el Excel con gráficas…";
            var modelo = "llama3.2:1b";
            ReporteDto? reporte = null;
            try
            {
                var estado = await ApiCliente.EstadoIaAsync();
                if (!string.IsNullOrWhiteSpace(estado.Modelo))
                    modelo = estado.Modelo;
                lblPie.Text = $"Ollama {modelo} está armando el Excel con gráficas…";
                reporte = await ApiCliente.ReporteAsync(Iso(desde), Iso(hasta));
            }
            catch (Exception)
            {
                lblPie.Text = "Ollama no respondió. El Excel lleva las gráficas calculadas con los tickets.";
            }

            var rutaExcel = System.IO.Path.Combine(carpeta, $"urbanpark-decisiones-{desde:yyyyMMdd}-{hasta:yyyyMMdd}.xlsx");
            ExcelDecisiones.Guardar(rutaExcel, tickets, reporte, modelo, desde, hasta);
            await Launcher.Default.OpenAsync(new OpenFileRequest("Decisiones Urban Park", new ReadOnlyFile(rutaExcel)));
            var avisoIa = reporte is null
                ? "Ollama no alcanzó a redactar el resumen; las gráficas y las señales sí están."
                : $"La lectura la hizo Ollama {modelo}.";
            await Ui.Exito(this,
                $"Excel del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy} guardado en Descargas.\n" +
                $"{tickets.Count} tickets, gráficas de horas, zonas, permanencia y dinero.\n{avisoIa}\nTambién quedó el CSV de la base.");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnDescargar.IsEnabled = true;
        }
    }

    private static string Csv(string? valor)
    {
        var texto = valor ?? "";
        return texto.Contains(';') || texto.Contains('"') ? "\"" + texto.Replace("\"", "\"\"") + "\"" : texto;
    }

    // ================================================================ estados de la caja
    private void PrepararCaja()
    {
        contenedorReporte.Clear();
        lblPie.Text = "";
        panelReporte.Style = Ui.EstiloDe("Tarjeta");
        panelReporte.Stroke = Ui.ColorDe("SuperficieBorde");
        panelReporte.BackgroundColor = Ui.ColorDe("Superficie");
    }

    private void MostrarInicial()
    {
        PrepararCaja();
        contenedorReporte.Add(Parrafo("Selecciona fechas y pulsa «Generar reporte».",
            13, cursiva: true, suave: true));
    }

    private void PintarError(string mensaje)
    {
        PrepararCaja();
        panelReporte.Stroke = Ui.ColorDe("Peligro");

        contenedorReporte.Add(Parrafo("No se pudo generar el reporte", 18, fuerte: true, color: Ui.ColorDe("Peligro")));
        contenedorReporte.Add(Parrafo(mensaje, 13));
        contenedorReporte.Add(Parrafo("Revisa que el API esté encendido y que el rango de fechas sea válido.",
            12, suave: true, color: Ui.ColorDe("Advertencia")));
        lblPie.Text = "Sin datos: la consulta falló.";
    }

    // ================================================================ reporte
    private void PintarReporte(ReporteDto r, string desde, string hasta)
    {
        PrepararCaja();

        // ---------- encabezado ----------
        var periodo = desde == hasta ? $"Hoy / día {desde}" : $"{desde} → {hasta}";

        contenedorReporte.Add(TarjetaEncabezado(r, periodo));

        // ---------- secciones ----------
        var secciones = new VerticalStackLayout { Spacing = 14 };

        // ---------- 1. plazas actuales ----------
        secciones.Add(TarjetaSeccion("1. Plazas actuales",
            "Lo que hay ahora mismo en el estacionamiento.",
            TablaMetricas(
                ("TOTAL", "Total", r.PlazasTotal.ToString(), "Acento"),
                ("OCUPADAS", "Ocupadas", r.PlazasOcupadas.ToString(), "Peligro"),
                ("LIBRES", "Libres", r.PlazasLibres.ToString(), "Exito"),
                ("OCUPACIÓN", "Ocupación", $"{r.PlazasPorcentaje}%", "Acento"))));

        // ---------- 2. quien esta estacionado ----------
        secciones.Add(TarjetaSeccion("2. Quién está estacionado ahora",
            "Número de plaza ocupada por cada cliente.",
            TablaOcupadas(r.Metricas.PlazasOcupadasDetalle)));

        // ---------- 3. dinero del periodo ----------
        secciones.Add(TarjetaSeccion("3. Dinero del periodo",
            "Tickets entre las fechas seleccionadas.",
            TablaMetricas(
                ("TICKETS DEL PERIODO", "Tickets del periodo", r.TotalTickets.ToString(), "TextoClaro"),
                ("CERRADOS (COBRADOS)", "Cerrados (cobrados)", r.Cerrados.ToString(), "TextoClaro"),
                ("RECAUDADO", "Pagado", Ui.Moneda(r.Recaudado), "Acento"),
                ("DESCUENTO", "Descuento", Ui.Moneda(r.DescuentoAplicado), "Exito"),
                ("POR COBRAR AHORA", "Por cobrar ahora", Ui.Moneda(r.PorCobrarEstimado), "Advertencia"))));

        secciones.Add(TarjetaSeccion("4. Pagado y descuento por día",
            "Línea amarilla: lo pagado. Línea blanca: el descuento, día a día.",
            GraficaDias(r.Metricas.PorDia)));

        secciones.Add(TarjetaSeccion("5. Tipo de persona",
            "Porcentaje de cada perfil en los tickets del periodo.",
            GraficaPersonas(r.Metricas.PlazasMasUsadas)));

        // ---------- 4. resumen IA ----------
        secciones.Add(TarjetaSeccion("6. Resumen IA",
            "Lectura de Ollama sobre ocupación, dinero, cuellos de botella y desbalances.",
            ResumenIa(string.IsNullOrWhiteSpace(r.ResumenIa) ? "Sin resumen." : r.ResumenIa, r.ProveedorIa)));

        // ---------- 5. puntos clave ----------
        secciones.Add(TarjetaSeccion("7. Puntos clave",
            "Hallazgos detectados por la IA.",
            ListaPuntos(r.Hallazgos.Count == 0
                ? ["Sin hallazgos en el periodo."]
                : [.. r.Hallazgos])));

        // ---------- 6. actividad por zona ----------
        secciones.Add(TarjetaSeccion("8. Actividad por zona",
            "Tickets registrados en cada zona del parqueadero.",
            ListaPuntos(r.Metricas.PorZona.Count == 0
                ? ["Sin actividad en el periodo."]
                : [.. r.Metricas.PorZona.Select(zona => $"{zona.Key}: {zona.Value} ticket(s)")])));

        contenedorReporte.Add(secciones);

        // ---------- pie ----------
        var generado = string.IsNullOrWhiteSpace(r.Metricas.GeneradoEn) ? "—" : r.Metricas.GeneradoEn;
        lblPie.Text = $"Generado: {generado} · {r.ProveedorIa}";
    }

    // ================================================================ tarjetas del reporte
    private static View TarjetaEncabezado(ReporteDto r, string periodo)
    {
        var contenido = new VerticalStackLayout { Spacing = 10 };
        contenido.Add(Parrafo("Reporte del parqueadero", 20, fuerte: true, color: Ui.ColorDe("Acento")));
        contenido.Add(Parrafo($"Periodo de tickets: {periodo}", 13, fuerte: true));
        contenido.Add(Sello(r.CuadranConPlazas));

        return TarjetaContenido("Urban Park · reporte del administrador", contenido);
    }

    private static View Tarjeta(string mensaje, double tamano) =>
        TarjetaContenido("Reporte", Parrafo(mensaje, tamano, fuerte: true));

    /// <summary>Tarjeta contenedora de una sección: título numerado + bajada + contenido.</summary>
    private static View TarjetaSeccion(string titulo, string bajada, View contenido)
    {
        var pila = new VerticalStackLayout { Spacing = 4 };
        pila.Add(new Label { Text = titulo, Style = Ui.EstiloDe("TituloSeccionClaro") });
        pila.Add(Parrafo(bajada, 11, cursiva: true, suave: true));

        return TarjetaContenido(null, pila, contenido);
    }

    /// <summary>Armazón común: encabezado opcional, divisor y cuerpo, dentro de una Tarjeta oscura.</summary>
    private static View TarjetaContenido(string? titulo, params View[] cuerpo)
    {
        var pila = new VerticalStackLayout { Spacing = 10 };

        if (!string.IsNullOrEmpty(titulo))
        {
            pila.Add(new Label { Text = titulo, Style = Ui.EstiloDe("EncabezadoTablaClaro") });
            pila.Add(Divisor(new Thickness(0, 2, 0, 4)));
        }

        foreach (var vista in cuerpo)
            pila.Add(vista);

        return new Border { Style = Ui.EstiloDe("Tarjeta"), Content = pila };
    }

    /// <summary>Resumen de la IA en un contenedor destacado con borde izquierdo de acento.</summary>
    private static View GraficaDias(IReadOnlyList<DiaMontoDto>? dias)
    {
        var datos = dias ?? [];
        if (datos.Count == 0)
            return Parrafo("No hay cobros en el periodo.", 13, suave: true);

        var mapa = new Dictionary<DateTime, (double Pagado, double Descuento)>();
        foreach (var dia in datos)
        {
            if (!DateTime.TryParse(dia.Fecha, out var fecha)) continue;
            mapa[fecha.Date] = (dia.Pagado, dia.Descuento);
        }

        if (mapa.Count == 0)
            return Parrafo("No hay cobros en el periodo.", 13, suave: true);

        var inicio = mapa.Keys.Min();
        var fin = mapa.Keys.Max();
        var serie = new List<(DateTime Fecha, double Pagado, double Descuento)>();
        for (var cursor = inicio; cursor <= fin; cursor = cursor.AddDays(1))
        {
            mapa.TryGetValue(cursor, out var valor);
            serie.Add((cursor, valor.Pagado, valor.Descuento));
        }

        var leyenda = new HorizontalStackLayout { Spacing = 16, Margin = new Thickness(0, 0, 0, 8) };
        leyenda.Add(Leyenda("Pagado", "Acento"));
        leyenda.Add(Leyenda("Descuento", "Blanco"));

        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                leyenda,
                new GraphicsView
                {
                    HeightRequest = 230,
                    Drawable = new DibujoLineas { Serie = serie }
                }
            }
        };
    }

    private sealed class DibujoLineas : IDrawable
    {
        public required List<(DateTime Fecha, double Pagado, double Descuento)> Serie { get; init; }

        public void Draw(ICanvas canvas, RectF area)
        {
            const float izquierda = 46f;
            const float abajo = 26f;
            const float arriba = 10f;
            const float derecha = 8f;
            var plotW = Math.Max(1, area.Width - izquierda - derecha);
            var plotH = Math.Max(1, area.Height - arriba - abajo);
            var maximo = Serie.Max(s => Math.Max(s.Pagado, s.Descuento));
            if (maximo <= 0) maximo = 1;

            canvas.FontSize = 10;
            canvas.FontColor = Color.FromArgb("#9AA4B2");
            canvas.StrokeSize = 1;
            canvas.StrokeColor = Color.FromArgb("#28323F");
            for (var marca = 0; marca <= 3; marca++)
            {
                var y = arriba + plotH - plotH * marca / 3f;
                canvas.DrawLine(izquierda, y, area.Width - derecha, y);
                canvas.DrawString((maximo * marca / 3d).ToString("0"), 0, y - 7, izquierda - 4, 14,
                    HorizontalAlignment.Right, VerticalAlignment.Center);
            }

            float X(int indice) => Serie.Count <= 1
                ? izquierda + plotW / 2f
                : izquierda + plotW * indice / (Serie.Count - 1);
            float Y(double valor) => arriba + plotH - (float)(plotH * valor / maximo);

            void Trazo(Func<(DateTime Fecha, double Pagado, double Descuento), double> valor, Color color)
            {
                var camino = new PathF();
                for (var i = 0; i < Serie.Count; i++)
                {
                    var x = X(i);
                    var y = Y(valor(Serie[i]));
                    if (i == 0) camino.MoveTo(x, y);
                    else camino.LineTo(x, y);
                }

                canvas.StrokeColor = color;
                canvas.StrokeSize = 2.4f;
                canvas.DrawPath(camino);
            }

            Trazo(s => s.Pagado, Color.FromArgb("#F2C51A"));
            Trazo(s => s.Descuento, Colors.White);

            var meses = Serie
                .Select((punto, indice) => (punto.Fecha, indice))
                .Where(x => x.Fecha.Day == 1 || x.indice == 0)
                .ToList();
            foreach (var (fecha, indice) in meses)
            {
                canvas.DrawString(fecha.ToString("MMM"), X(indice) - 16, area.Height - 16, 32, 14,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }

    private static View GraficaPersonas(IReadOnlyList<PlazaUsoDto>? personas)
    {
        var datos = (personas ?? []).Where(p => p.Usos > 0).ToList();
        if (datos.Count == 0)
            return Parrafo("No hay tickets en el periodo.", 13, suave: true);

        var total = datos.Sum(p => p.Usos);
        var colores = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Hombre"] = "#F2C51A",
            ["Mujer"] = "#F472B6",
            ["Tercera edad"] = "#38BDF8",
            ["Capacidades especiales"] = "#34D399",
            ["Embarazada"] = "#A78BFA",
            ["Estudiante"] = "#FB923C",
            ["Otro"] = "#94A3B8"
        };

        var lista = new VerticalStackLayout { Spacing = 12 };
        foreach (var persona in datos)
        {
            var pct = total == 0 ? 0 : persona.Usos * 100.0 / total;
            var hex = colores.TryGetValue(persona.Plaza, out var color) ? color : "#94A3B8";
            var lleno = Math.Max(pct, 1);
            var resto = Math.Max(100 - pct, 0);

            var barra = new Grid { HeightRequest = 14 };
            barra.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(lleno, GridUnitType.Star)));
            barra.Add(new BoxView
            {
                Color = Color.FromArgb(hex),
                CornerRadius = 6
            }, 0, 0);
            if (resto > 0)
            {
                barra.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(resto, GridUnitType.Star)));
                barra.Add(new BoxView
                {
                    Color = Color.FromArgb("#0A0E13"),
                    CornerRadius = 6
                }, 1, 0);
            }

            var fila = new Grid
            {
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)],
                ColumnSpacing = 8
            };
            fila.Add(new Label
            {
                Text = persona.Plaza,
                FontSize = 13,
                TextColor = Ui.ColorDe("TextoClaro"),
                LineBreakMode = LineBreakMode.TailTruncation,
                VerticalOptions = LayoutOptions.Center
            }, 0, 0);
            fila.Add(new Label
            {
                Text = pct.ToString("0") + "%",
                FontFamily = "OpenSansSemibold",
                FontSize = 13,
                TextColor = Color.FromArgb(hex),
                VerticalOptions = LayoutOptions.Center
            }, 1, 0);

            lista.Add(new VerticalStackLayout
            {
                Spacing = 4,
                Children = { fila, barra }
            });
        }

        return lista;
    }

    private static View Leyenda(string texto, string token) => new HorizontalStackLayout
    {
        Spacing = 6,
        Children =
        {
            new BoxView
            {
                Color = Ui.ColorDe(token),
                WidthRequest = 12,
                HeightRequest = 12,
                CornerRadius = 3,
                VerticalOptions = LayoutOptions.Center
            },
            new Label
            {
                Text = texto,
                FontSize = 12,
                TextColor = Ui.ColorDe("TextoClaro"),
                VerticalOptions = LayoutOptions.Center
            }
        }
    };

    private static View BarraVertical(double valor, double maximo, double alto, string token)
    {
        var altura = valor <= 0 ? 0 : Math.Max(4, alto * valor / maximo);
        return new BoxView
        {
            Color = Ui.ColorDe(token),
            WidthRequest = 16,
            HeightRequest = altura,
            CornerRadius = 4,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End
        };
    }

    private static View ResumenIa(string texto, string proveedor)
    {
        var pila = new VerticalStackLayout { Spacing = 4 };
        pila.Add(new Label
        {
            Text = string.IsNullOrWhiteSpace(proveedor) ? "RESUMEN DE LA IA" : $"RESUMEN · {proveedor}",
            Style = Ui.EstiloDe("EtiquetaCampo"),
            TextColor = Ui.ColorDe("TextoFuerte")
        });
        pila.Add(Parrafo(texto, 14, color: Ui.ColorDe("TextoFuerte")));

        return new Border
        {
            Style = Ui.EstiloDe("TarjetaClara"),
            Padding = new Thickness(8, 6),
            Stroke = Ui.ColorDe("Acento"),
            StrokeThickness = 3,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0, 14, 14, 0) },
            Content = pila
        };
    }

    /// <summary>Lista de puntos clave: guion/icono delante de cada línea.</summary>
    private static View ListaPuntos(IReadOnlyList<string> puntos)
    {
        var pila = new VerticalStackLayout { Spacing = 8 };

        foreach (var punto in puntos)
        {
            var fila = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star)
                ],
                ColumnSpacing = 8
            };

            var vineta = Parrafo("—", 12, fuerte: true, color: Ui.ColorDe("Acento"));
            var linea = Parrafo(punto, 12);
            Grid.SetColumn(linea, 1);

            fila.Add(vineta);
            fila.Add(linea);
            pila.Add(fila);
        }

        return pila;
    }

    // ================================================================ sello de estado
    private static View Sello(bool cuadran)
    {
        var texto = cuadran ? "Estado: en orden" : "Estado: revisar";

        var borde = new Border
        {
            Style = Ui.EstiloDe(cuadran ? "BadgeOk" : "BadgeAviso"),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = texto,
                Style = Ui.EstiloDe(cuadran ? "BadgeOkTexto" : "BadgeAvisoTexto")
            }
        };

        return borde;
    }

    // ================================================================ tabla de plazas ocupadas
    private static View TablaOcupadas(List<PlazaOcupadaDetalleDto> detalle)
    {
        if (detalle.Count == 0)
            return TarjetaContenido(null, new Label
            {
                Text = "Ninguna plaza ocupada ahora",
                Style = Ui.EstiloDe("EstadoVacio")
            });

        var filas = new List<View[]> { FilaEncabezado() };

        foreach (var plaza in detalle)
        {
            var cliente = string.IsNullOrWhiteSpace(plaza.Cliente) || plaza.Cliente.Trim() == "-"
                ? "—"
                : plaza.Cliente;

            filas.Add([
                Celda(string.IsNullOrWhiteSpace(plaza.Plaza) ? "—" : plaza.Plaza, fuerte: true, color: Ui.ColorDe("Acento")),
                Celda(string.IsNullOrWhiteSpace(plaza.Placa) ? "-" : plaza.Placa),
                Celda(cliente),
                Celda(string.IsNullOrWhiteSpace(plaza.Zona) ? "—" : plaza.Zona),
                Celda(Ui.Moneda(plaza.PorCobrar), color: Ui.ColorDe("Advertencia"))
            ]);
        }

        return ConstruirTabla(filas);
    }

    private static View[] FilaEncabezado()
    {
        var celdas = new List<View>();
        for (var i = 0; i < EncabezadosTabla.Length; i++)
            celdas.Add(Encabezado(EncabezadosTabla[i]));
        return [.. celdas];
    }

    /// <summary>Cabecera con EncabezadoTablaClaro y filas separadas por Divisor.</summary>
    private static View ConstruirTabla(List<View[]> filas)
    {
        var pila = new VerticalStackLayout { Spacing = 0 };

        for (var f = 0; f < filas.Count; f++)
        {
            if (f > 0)
                pila.Add(Divisor(new Thickness(0, 8)));
            else
                pila.Add(Divisor(new Thickness(0, 0, 0, 5)));

            pila.Add(Fila(filas[f]));
        }

        return TarjetaContenido(null, pila);
    }

    private static View Fila(View[] celdas)
    {
        var fila = new Grid
        {
            ColumnSpacing = 4,
            ColumnDefinitions = [.. AnchoTabla.Select(a => new ColumnDefinition(new GridLength(a, GridUnitType.Star)))]
        };

        for (var c = 0; c < celdas.Length; c++)
        {
            Grid.SetColumn(celdas[c], c);
            fila.Add(celdas[c]);
        }

        return fila;
    }

    private static Label Encabezado(string texto) => new()
    {
        Text = texto,
        Style = Ui.EstiloDe("EncabezadoTablaClaro")
    };

    private static Label Celda(string texto, bool fuerte = false, bool cursiva = false, Color? color = null) => new()
    {
        Text = texto,
        FontSize = 12,
        FontAttributes = cursiva
            ? FontAttributes.Italic
            : fuerte ? FontAttributes.Bold : FontAttributes.None,
        TextColor = color ?? Ui.ColorDe("TextoClaro"),
        LineBreakMode = LineBreakMode.WordWrap,
        VerticalOptions = LayoutOptions.Start
    };

    // ================================================================ metricas de 4 casillas
    private static View TablaMetricas(params (string Encabezado, string Etiqueta, string Valor, string Token)[] metricas)
    {
        var tabla = new Grid
        {
            RowSpacing = 10,
            ColumnSpacing = 10,
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Star)
            ]
        };

        var indice = 0;
        foreach (var metrica in metricas)
        {
            var fila = indice / 2;
            var columna = indice % 2;

            if (columna == 0)
                tabla.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var panel = Metrica(metrica.Encabezado, metrica.Etiqueta, metrica.Valor, metrica.Token);
            Grid.SetRow(panel, fila);
            Grid.SetColumn(panel, columna);
            tabla.Add(panel);

            indice++;
        }

        return tabla;
    }

    private static Border Metrica(string encabezado, string etiqueta, string valor, string token)
    {
        var valorLabel = new Label
        {
            Text = valor,
            Style = Ui.EstiloDe("MetricaValor"),
            TextColor = Ui.ColorDe(token)
        };

        var pila = new VerticalStackLayout { Spacing = 4 };
        pila.Add(new Label { Text = encabezado, Style = Ui.EstiloDe("MetricaEtiqueta") });
        pila.Add(valorLabel);
        pila.Add(Parrafo(etiqueta, 11, suave: true));

        return new Border { Style = Ui.EstiloDe("TarjetaMetrica"), Content = pila };
    }

    // ================================================================ ayudantes de pintado
    private static Label Parrafo(string texto, double tamano, bool fuerte = false, bool cursiva = false, bool suave = false,
        Color? color = null) => new()
        {
            Text = texto,
            FontSize = tamano,
            FontAttributes = cursiva
                ? FontAttributes.Italic
                : fuerte ? FontAttributes.Bold : FontAttributes.None,
            TextColor = color ?? Ui.ColorDe(suave ? "TextoClaroSuave" : "TextoClaro"),
            LineBreakMode = LineBreakMode.WordWrap
        };

    private static BoxView Divisor(Thickness? margen = null) => new()
    {
        Style = Ui.EstiloDe("Divisor"),
        Margin = margen ?? new Thickness(0)
    };
}
