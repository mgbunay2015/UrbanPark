using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Tickets del parqueadero. Lista, filtra y cobra. La plaza se asigna desde el mapa con IA.
/// </summary>
public partial class vTickets : ContentPage
{
    private List<TicketDto> _tickets = [];
    private List<VehiculoDto> _vehiculos = [];
    private List<EspacioDto> _espacios = [];

    /// <summary>Filtro activo: TODOS / ABIERTO / CERRADO. Se aplica en memoria.</summary>
    private string _filtro = "ABIERTO";

    public vTickets()
    {
        InitializeComponent();
        AplicarColorFiltros();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarAsync();
    }

    // ================================================================ carga
    private async Task CargarAsync()
    {
        spinnerTickets.IsVisible = spinnerTickets.IsRunning = true;
        try
        {
            var tareaTickets = ApiCliente.ListarTicketsAsync();
            var tareaVehiculos = ApiCliente.ListarVehiculosAsync();
            var tareaEspacios = ApiCliente.ListarEspaciosAsync();

            await Task.WhenAll(tareaTickets, tareaVehiculos, tareaEspacios);

            _tickets = await tareaTickets ?? [];
            _vehiculos = await tareaVehiculos ?? [];
            _espacios = await tareaEspacios ?? [];

            lblTitulo.Text = Sesion.EsCliente ? "Mis tickets" : "Tickets entrada / salida";
            Title = "Tickets";
            PintarSyncHint();
            PintarResumen();
            PintarLista();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinnerTickets.IsRunning = false;
            spinnerTickets.IsVisible = false;
        }
    }

    // ================================================================ aviso de sincronizacion
    private void PintarSyncHint()
    {
        var pendientes = _tickets.Count(t => t.LiberacionPendiente);

        if (pendientes == 0)
        {
            lblSyncHint.Text = "";
            lblSyncHint.IsVisible = false;
            panelSyncHint.IsVisible = false;
            return;
        }

        lblSyncHint.Text =
            $"{pendientes} pago(s) cobrado(s) con plaza aún por liberar. Pulsa «Sincronizar plazas» cuando el servicio esté UP.";
        lblSyncHint.IsVisible = true;
        panelSyncHint.IsVisible = true;
    }

    // ================================================================ resumen
    private void PintarResumen()
    {
        var total = _tickets.Count;
        var cerrados = _tickets.Count(t => !t.Abierto);
        var abiertos = _tickets.Count(t => t.Abierto);
        var pagado = _tickets.Where(t => !t.Abierto).Sum(t => t.MontoTotal ?? 0);
        var pendiente = _tickets.Where(t => t.Abierto).Sum(t => Ui.MontoEstimado(t.Entrada, t.TarifaHora, DescuentoDe(t.Placa)));

        panelResumen.IsVisible = total > 0;

        lblResumenTitulo.Text = Sesion.EsCliente ? "Tu resumen de consumo" : "Resumen de tickets";
        lblResumenSubtitulo.Text = Sesion.EsCliente
            ? "Solo tus tickets: usados, pagados y pendientes."
            : "Totales según los tickets visibles en esta sesión.";

        lblUsados.Text = total.ToString();
        lblPieUsados.Text = $"{cerrados} cerrados · {abiertos} abiertos";
        lblPagado.Text = Ui.Moneda(pagado);
        lblPendiente.Text = Ui.Moneda(pendiente);
        lblPiePendiente.Text = abiertos == 0 ? "Sin cargos pendientes" : "Estimado de tickets abiertos";

        btnCobrarTodos.IsVisible = abiertos >= 2;
        btnCobrarTodos.Text = abiertos >= 2
            ? $"Cobrar {abiertos} vehículos y salir · {Ui.Moneda(pendiente)}"
            : "Cobrar todos y salir";
    }

    // ================================================================ filtros
    private void OnFiltroTodosClicked(object? sender, EventArgs e) => CambiarFiltro("TODOS");
    private void OnFiltroAbiertosClicked(object? sender, EventArgs e) => CambiarFiltro("ABIERTO");
    private void OnFiltroCerradosClicked(object? sender, EventArgs e) => CambiarFiltro("CERRADO");

    private void CambiarFiltro(string filtro)
    {
        _filtro = filtro;
        AplicarColorFiltros();
        PintarLista();          // solo repinta: no se vuelve a pedir datos al servidor
    }

    private void AplicarColorFiltros()
    {
        PintarChip(btnFiltroTodos, _filtro == "TODOS");
        PintarChip(btnFiltroAbiertos, _filtro == "ABIERTO");
        PintarChip(btnFiltroCerrados, _filtro == "CERRADO");
    }

    private void PintarChip(Button boton, bool activo)
    {
        boton.Style = EstiloChip(activo ? "FiltroChipActivo" : "FiltroChip");
    }

    /// <summary>El estilo del chip vive en los recursos de la página (o del sistema de diseño).</summary>
    private Style? EstiloChip(string clave) =>
        Resources.TryGetValue(clave, out var valor) && valor is Style estilo
            ? estilo
            : Ui.EstiloDe(clave);

    // ================================================================ lista
    private void PintarLista()
    {
        contenedorTickets.Clear();

        var visibles = _tickets
            .Where(t => _filtro == "TODOS" || t.Estado == _filtro)
            .OrderBy(t => t.Abierto ? 0 : 1)          // ABIERTOS primero
            .ThenByDescending(t => t.Entrada)         // y por entrada descendente
            .ToList();

        if (visibles.Count == 0)
        {
            contenedorTickets.Add(new Label
            {
                Text = TextoVacio(),
                Style = Ui.EstiloDe("EstadoVacio")
            });
            return;
        }

        foreach (var ticket in visibles)
            contenedorTickets.Add(FilaTicket(ticket));
    }

    private string TextoVacio()
    {
        if (Sesion.EsCliente && _filtro == "ABIERTO")
            return "No tienes tickets abiertos. Abre uno con tu placa.";
        if (Sesion.EsCliente && _filtro == "CERRADO")
            return "No tienes tickets cerrados.";
        if (Sesion.EsCliente)
            return "No tienes tickets propios para mostrar. Abre uno con tu placa.";

        return _filtro switch
        {
            "ABIERTO" => "No hay tickets abiertos para mostrar.",
            "CERRADO" => "No hay tickets cerrados para mostrar.",
            _ => "No hay tickets para mostrar."
        };
    }

    private View FilaTicket(TicketDto ticket)
    {
        var abierto = ticket.Abierto;
        var tipo = TipoDeTicket(ticket);

        var contenido = new VerticalStackLayout { Spacing = 6 };

        // ---- cabecera: codigo + placa + plaza, con el icono del tipo ----
        var cabecera = new Label
        {
            TextColor = Ui.ColorDe("TextoFuerte"),
            LineBreakMode = LineBreakMode.WordWrap,
            VerticalOptions = LayoutOptions.Center
        };
        var formateado = new FormattedString();
        formateado.Spans.Add(new Span
        {
            Text = ticket.Codigo,
            FontFamily = "OpenSansSemibold",
            FontSize = 13
        });
        formateado.Spans.Add(new Span
        {
            Text = $"  · {ticket.Placa} · plaza {ticket.EspacioCodigo ?? ticket.EspacioId.ToString()}",
            FontSize = 11
        });
        formateado.Spans.Add(new Span
        {
            Text = $"   {Ui.IconoTipo(tipo)}{tipo}",
            FontSize = 10,
            TextColor = Ui.ColorDe("TextoSuave")
        });
        cabecera.FormattedText = formateado;

        // ---- badges de estado ----
        var badges = new HorizontalStackLayout { Spacing = 6 };
        badges.Add(new Border
        {
            Style = abierto ? Ui.EstiloDe("BadgeOk") : Ui.EstiloDe("Badge"),
            Content = new Label
            {
                Text = ticket.Estado,
                Style = abierto ? Ui.EstiloDe("BadgeOkTexto") : Ui.EstiloDe("BadgeTexto")
            }
        });

        if (ticket.LiberacionPendiente)
            badges.Add(new Border
            {
                Style = Ui.EstiloDe("BadgeAviso"),
                Content = new Label
                {
                    Text = "Plaza pendiente sync",
                    Style = Ui.EstiloDe("BadgeAvisoTexto")
                }
            });

        var cabeceraGrid = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 10
        };
        cabeceraGrid.Add(cabecera, 0);
        cabeceraGrid.Add(badges, 1);
        contenido.Add(cabeceraGrid);

        // ---- detalle ----
        contenido.Add(DetalleTicket(ticket));

        // ---- accion solo en ABIERTOS ----
        if (abierto)
        {
            var estimado = Ui.MontoEstimado(ticket.Entrada, ticket.TarifaHora, DescuentoDe(ticket.Placa));
            var btnCerrar = new Button
            {
                Text = "Cerrar / cobrar",
                Style = Ui.EstiloDe("BotonPrimario")
            };
            var enFila = ticket;
            btnCerrar.Clicked += async (_, _) => await CerrarAsync(enFila, estimado);
            contenido.Add(btnCerrar);
        }

        return new Border
        {
            Style = Ui.EstiloDe("TarjetaClara"),
            Margin = new Thickness(0, 0, 0, 6),
            Stroke = Ui.ColorDe(abierto ? "Primario" : "BordeClaro"),
            StrokeThickness = abierto ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = contenido
        };
    }

    private Label DetalleTicket(TicketDto ticket)
    {
        var detalle = new Label
        {
            FontSize = 12,
            TextColor = Ui.ColorDe("TextoFuerte"),
            LineBreakMode = LineBreakMode.WordWrap
        };
        var texto = new FormattedString();

        if (ticket.Abierto)
        {
            var estimado = Ui.MontoEstimado(ticket.Entrada, ticket.TarifaHora, DescuentoDe(ticket.Placa));

            texto.Spans.Add(new Span { Text = "Ingreso: ", FontAttributes = FontAttributes.Bold });
            texto.Spans.Add(new Span { Text = Ui.Fecha(ticket.Entrada) + "\n" });
            texto.Spans.Add(new Span { Text = "Valor por cobrar (ahora): ", FontAttributes = FontAttributes.Bold });
            texto.Spans.Add(new Span { Text = $"{Ui.Moneda(estimado)} · se actualiza al cerrar · " });
            var descuento = DescuentoDe(ticket.Placa);
            if (descuento > 0)
                texto.Spans.Add(new Span { Text = $"{descuento}% de descuento · " });
            texto.Spans.Add(new Span
            {
                Text = Ui.Tarifa(ticket.TarifaHora),
                TextColor = Ui.ColorDe("TextoMedio")
            });
        }
        else
        {
            var monto = ticket.MontoTotal ?? MontoCerrado(ticket);

            texto.Spans.Add(new Span { Text = "Ingreso: ", FontAttributes = FontAttributes.Bold });
            texto.Spans.Add(new Span { Text = Ui.Fecha(ticket.Entrada) + "\n" });
            texto.Spans.Add(new Span { Text = "Salida: ", FontAttributes = FontAttributes.Bold });
            texto.Spans.Add(new Span { Text = Ui.Fecha(ticket.Salida) + "\n" });
            texto.Spans.Add(new Span { Text = "Valor cobrado: ", FontAttributes = FontAttributes.Bold });
            texto.Spans.Add(new Span { Text = $"{Ui.Moneda(monto)}  " });
            texto.Spans.Add(new Span
            {
                Text = $"({Ui.Moneda(ticket.TarifaHora ?? 0)}/h)",
                TextColor = Ui.ColorDe("TextoMedio")
            });
        }

        detalle.FormattedText = texto;
        return detalle;
    }

    /// <summary>Misma fórmula del backend cuando el ticket cerrado no trae montoTotal.</summary>
    private static double MontoCerrado(TicketDto ticket)
    {
        var salida = ticket.Salida ?? DateTime.Now;
        var minutos = Math.Max(1L, (long)(salida - ticket.Entrada).TotalMinutes);
        var horas = Math.Ceiling(minutos / 60.0);
        return Math.Round(horas * (ticket.TarifaHora ?? 0), 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>tipoDeTicket(): vehículo por placa → plaza por codigo/id → AUTO.</summary>
    private string TipoDeTicket(TicketDto ticket)
    {
        var vehiculo = _vehiculos.FirstOrDefault(v =>
            string.Equals(v.Placa, ticket.Placa, StringComparison.OrdinalIgnoreCase));

        if (vehiculo is not null && !string.IsNullOrWhiteSpace(vehiculo.Tipo))
            return vehiculo.Tipo;

        var espacio = _espacios.FirstOrDefault(e =>
            e.Id == ticket.EspacioId ||
            (!string.IsNullOrWhiteSpace(ticket.EspacioCodigo) && e.Codigo == ticket.EspacioCodigo));

        if (espacio is not null && !string.IsNullOrWhiteSpace(espacio.TipoVehiculo))
            return espacio.TipoVehiculo;

        return "AUTO";
    }

    private int DescuentoDe(string placa)
    {
        var vehiculo = _vehiculos.FirstOrDefault(v =>
            string.Equals(v.Placa, placa, StringComparison.OrdinalIgnoreCase));
        if (vehiculo is null) return 0;
        return Math.Max(vehiculo.Descuento, Ui.DescuentoDe(vehiculo.Propietario));
    }

    // ================================================================ cerrar / cobrar
    private List<TicketDto> TicketsAbiertos() => _tickets.Where(t => t.Abierto).ToList();

    private async void OnCobrarTodosClicked(object? sender, EventArgs e) =>
        await CerrarVariosAsync(TicketsAbiertos());

    private async Task CerrarAsync(TicketDto ticket, double estimado)
    {
        var otros = TicketsAbiertos().Where(t => t.Id != ticket.Id).ToList();
        if (otros.Count > 0)
        {
            var todos = TicketsAbiertos();
            var total = todos.Sum(t => Ui.MontoEstimado(t.Entrada, t.TarifaHora, DescuentoDe(t.Placa)));
            if (await Ui.Confirmar(this, "Varios vehículos",
                    $"Tienes {todos.Count} tickets abiertos. ¿Cobrarlos todos ({Ui.Moneda(total)}) y mostrar un QR por vehículo?"))
            {
                await CerrarVariosAsync(todos, false);
                return;
            }
        }

        var confirmado = await Ui.Confirmar(this, "Cerrar y cobrar",
            $"¿Cerrar el ticket {ticket.Codigo} de {ticket.Placa}? Valor estimado a cobrar: {Ui.Moneda(estimado)}");

        if (!confirmado)
        {
            await Ui.Informar(this, "Cobro cancelado");
            return;
        }

        try
        {
            await CobrarUnoAsync(ticket);
            await Navigation.PushAsync(new vSalida());
            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    private async Task CerrarVariosAsync(List<TicketDto> tickets, bool confirmar = true)
    {
        if (tickets.Count == 0) return;

        var total = tickets.Sum(t => Ui.MontoEstimado(t.Entrada, t.TarifaHora, DescuentoDe(t.Placa)));
        if (confirmar && tickets.Count > 1 &&
            !await Ui.Confirmar(this, "Cobrar todos y salir",
                $"Se cerrarán {tickets.Count} tickets ({string.Join(", ", tickets.Select(t => t.Placa))}) por {Ui.Moneda(total)}. Un QR por vehículo, 10 minutos para salir."))
            return;

        var fallos = new List<string>();
        foreach (var ticket in tickets.ToList())
        {
            try
            {
                await CobrarUnoAsync(ticket);
            }
            catch (Exception ex)
            {
                fallos.Add($"{ticket.Placa}: {ex.Message}");
            }
        }

        if (fallos.Count > 0)
            await Ui.Aviso(this, "Algunos no se cobraron:\n" + string.Join("\n", fallos), "Cobro parcial");
        else
            await Ui.Exito(this, $"{PaseSalida.Activos.Count} QR(s) listos para salir.");

        await Navigation.PushAsync(new vSalida());
        await CargarAsync();
    }

    private async Task CobrarUnoAsync(TicketDto ticket)
    {
        var estimado = Ui.MontoEstimado(ticket.Entrada, ticket.TarifaHora, DescuentoDe(ticket.Placa));
        var cerrado = await ApiCliente.CerrarTicketAsync(ticket.Id);
        var monto = cerrado?.MontoTotal ?? estimado;

        PaseSalida.Activar(
            cerrado?.Codigo ?? ticket.Codigo,
            cerrado?.Placa ?? ticket.Placa,
            cerrado?.EspacioCodigo ?? ticket.EspacioCodigo ?? ticket.EspacioId.ToString(),
            monto,
            Sesion.Username);
    }

    // ================================================================ sincronizar plazas
    private async void OnSincronizarClicked(object? sender, EventArgs e)
    {
        btnSincronizar.IsEnabled = false;
        spinnerTickets.IsVisible = spinnerTickets.IsRunning = true;
        try
        {
            var respuesta = await ApiCliente.SincronizarLiberacionesAsync();
            var mensaje = string.IsNullOrWhiteSpace(respuesta?.Mensaje)
                ? "No hay liberaciones pendientes"
                : respuesta!.Mensaje;

            if (respuesta is not null && respuesta.Liberadas > 0)
                await Ui.Exito(this, mensaje);
            else
                await Ui.Informar(this, mensaje);

            // siempre recarga tickets y plazas
            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinnerTickets.IsRunning = false;
            spinnerTickets.IsVisible = false;
            btnSincronizar.IsEnabled = true;
        }
    }
}
