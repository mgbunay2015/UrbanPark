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

    /// <summary>Cuántas filas se pintan en pantalla (evita LayoutCycle con cientos de tarjetas).</summary>
    private const int TamanoPagina = 40;
    private int _limiteVisible = TamanoPagina;
    private string _busqueda = "";

    public vTickets()
    {
        InitializeComponent();
        AplicarColorFiltros();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarAsync();

        // El punto de conexión cambia solo; el botón de cobro debe seguirlo.
        MonitorConexion.Cambio -= OnConexionCambio;
        MonitorConexion.Cambio += OnConexionCambio;
        ActualizarBloqueoCobro();
    }

    protected override void OnDisappearing()
    {
        MonitorConexion.Cambio -= OnConexionCambio;
        base.OnDisappearing();
    }

    private void OnConexionCambio(object? sender, EventArgs e) =>
        Dispatcher.Dispatch(ActualizarBloqueoCobro);

    /// <summary>
    /// Sin conexión no se puede cobrar: el cobro libera la plaza y se registra en el servidor,
    /// así que no se guarda para después. Se avisa y se desactiva el botón.
    /// </summary>
    private void ActualizarBloqueoCobro()
    {
        try
        {
            var puede = MonitorConexion.EnLinea;

            panelSinCobro.IsVisible = !puede;
            if (!puede)
            {
                lblSinCobro.Text = MonitorConexion.HayInternet
                    ? "No se puede cobrar: el servidor no responde. El cobro libera la plaza y queda " +
                      "registrado en el servidor, así que necesita conexión. Tus tickets siguen guardados."
                    : "No se puede cobrar sin internet. El cobro libera la plaza y queda registrado en el " +
                      "servidor, así que necesita conexión. Tus datos siguen guardados en este equipo.";
            }

            btnCobrarTodos.IsEnabled = puede;
            if (!puede)
            {
                btnCobrarTodos.Text = "Cobrar todos y salir · sin conexión";
                btnCobrarTodos.Opacity = 0.55;
            }
            else
            {
                btnCobrarTodos.Opacity = 1;
                PintarResumen();      // recupera el texto con el monto
            }

            // Solo repinta la lista si hay pocos ítems; con cientos de cerrados bloquea WinUI.
            var totalFiltrados = TicketsFiltrados().Count;
            if (totalFiltrados <= TamanoPagina || _filtro == "ABIERTO")
                PintarLista();
        }
        catch (Exception ex)
        {
            Registro.Aviso("vTickets", $"No se pudo actualizar el bloqueo de cobro: {ex.Message}");
        }
    }

    /// <summary>El usuario pide reintentar la conexión desde el aviso de la pantalla.</summary>
    private async void OnReintentarCobroClicked(object? sender, EventArgs e)
    {
        try
        {
            btnSincronizar.IsEnabled = false;
            var enLinea = await MonitorConexion.RevisarAhoraAsync();
            ActualizarBloqueoCobro();

            if (enLinea)
            {
                await CargarAsync();
                await Ui.Exito(this, "Conexión restablecida. Ya puedes cerrar y cobrar.");
            }
            else
            {
                await Ui.Error(this, "Sigue sin haber conexión con el servidor. Inténtalo de nuevo en unos segundos.");
            }
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnSincronizar.IsEnabled = true;
        }
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
            lblSubtitulo.Text = Sesion.EsCliente
                ? "Aquí ves solo tus tickets: los abiertos y los que ya pagaste."
                : "Cierra y cobra los tickets del parqueadero.";
            Title = "Tickets";
            txtBuscar.IsVisible = !Sesion.EsCliente;
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
        // «Sincronizar plazas» recorre TODAS las liberaciones pendientes del parqueadero:
        // es una tarea del personal, así que el cliente no ve el botón.
        btnSincronizar.IsVisible = Sesion.EsOperador;

        var pendientes = _tickets.Count(t => t.LiberacionPendiente);

        if (pendientes == 0)
        {
            lblSyncHint.Text = "";
            lblSyncHint.IsVisible = false;
            panelSyncHint.IsVisible = false;
            return;
        }

        lblSyncHint.Text = Sesion.EsCliente
            ? $"{pendientes} pago(s) con la plaza aún por liberar. Se pondrá al día sola cuando el servicio de espacios vuelva."
            : $"{pendientes} pago(s) cobrado(s) con plaza aún por liberar. Pulsa «Sincronizar plazas» cuando el servicio esté UP.";
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
            : $"Tickets de todos los usuarios · {abiertos} abierto(s) por cobrar.";

        lblUsados.Text = total.ToString();
        lblPieUsados.Text = $"{cerrados} cerrados · {abiertos} abiertos";
        lblPagado.Text = Ui.Moneda(pagado);
        lblPendiente.Text = Ui.Moneda(pendiente);
        lblPiePendiente.Text = abiertos == 0 ? "Sin cargos pendientes" : "Estimado de tickets abiertos";

        var abiertosMios = _tickets.Count(t => t.Abierto && EsTicketPropio(t));
        var pendienteMio = _tickets.Where(t => t.Abierto && EsTicketPropio(t))
            .Sum(t => Ui.MontoEstimado(t.Entrada, t.TarifaHora, DescuentoDe(t.Placa)));
        // Solo el cliente puede cobrar varios propios a la vez; admin/operador uno por uno.
        btnCobrarTodos.IsVisible = Sesion.EsCliente && abiertosMios >= 2;
        btnCobrarTodos.Text = abiertosMios >= 2
            ? $"Cobrar {abiertosMios} vehículos y salir · {Ui.Moneda(pendienteMio)}"
            : "Cobrar todos y salir";
    }

    // ================================================================ filtros
    private void OnFiltroTodosClicked(object? sender, EventArgs e) => CambiarFiltro("TODOS");
    private void OnFiltroAbiertosClicked(object? sender, EventArgs e) => CambiarFiltro("ABIERTO");
    private void OnFiltroCerradosClicked(object? sender, EventArgs e) => CambiarFiltro("CERRADO");

    private void CambiarFiltro(string filtro)
    {
        _filtro = filtro;
        _limiteVisible = TamanoPagina;
        AplicarColorFiltros();
        PintarLista();          // solo repinta: no se vuelve a pedir datos al servidor
    }

    private void OnBuscarTextChanged(object? sender, TextChangedEventArgs e)
    {
        _busqueda = (txtBuscar.Text ?? "").Trim();
        _limiteVisible = TamanoPagina;
        PintarLista();
    }

    private void OnCargarMasClicked(object? sender, EventArgs e)
    {
        _limiteVisible += TamanoPagina;
        PintarLista();
    }

    private List<TicketDto> TicketsFiltrados()
    {
        var q = _busqueda;
        return _tickets
            .Where(t => _filtro == "TODOS" || t.Estado == _filtro)
            .Where(t => string.IsNullOrEmpty(q) ||
                        (t.Placa?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (t.Codigo?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (t.Usuario?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
            .OrderBy(t => t.Abierto ? 0 : 1)
            .ThenByDescending(t => t.Entrada)
            .ToList();
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
        btnCargarMas.IsVisible = false;
        lblListaInfo.IsVisible = false;

        var visibles = TicketsFiltrados();
        var total = visibles.Count;

        if (total == 0)
        {
            contenedorTickets.Add(new Label
            {
                Text = TextoVacio(),
                Style = Ui.EstiloDe("EstadoVacio")
            });
            return;
        }

        // Los abiertos suelen ser pocos: el admin debe verlos todos para cobrar.
        var limite = _filtro == "ABIERTO" ? total : _limiteVisible;
        var porPintar = visibles.Take(limite).ToList();
        var hayMas = _filtro != "ABIERTO" && total > porPintar.Count;

        if (!Sesion.EsCliente && total > TamanoPagina)
        {
            lblListaInfo.IsVisible = true;
            lblListaInfo.Text = hayMas
                ? $"Mostrando {porPintar.Count} de {total} tickets. Usa la búsqueda o «Cargar más»."
                : $"Mostrando los {total} tickets del filtro.";
        }

        foreach (var ticket in porPintar)
            contenedorTickets.Add(FilaTicket(ticket));

        if (hayMas)
        {
            btnCargarMas.IsVisible = true;
            var restantes = total - porPintar.Count;
            var siguiente = Math.Min(TamanoPagina, restantes);
            btnCargarMas.Text = $"Cargar más ({siguiente} de {restantes} restantes)";
        }
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

        // De quién es el ticket: lo necesitan el operador y el admin, que cobran los de todos.
        // Va dentro del mismo texto y no como badge aparte: un Border más en la columna Auto
        // (junto al chip de estado) hacía que la columna del título se encogiera a cero y
        // WinUI entrara en LayoutCycleException.
        if (!Sesion.EsCliente && !string.IsNullOrWhiteSpace(ticket.Usuario))
            formateado.Spans.Add(new Span
            {
                Text = $"   · {ticket.Usuario}",
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Ui.ColorDe("TextoMedio")
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

        // El operador y el admin ven tickets de todos: hay que decir de quién es cada uno.
        // Se muestra en el texto de la cabecera (arriba), no como badge: dos Borders en la
        // columna Auto desbordaban el ancho disponible y disparaban el ciclo de layout.

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
            var puedeCobrar = MonitorConexion.EnLinea;

            // Solo el admin (y el operador) pueden cobrar tickets de otros usuarios. El
            // cliente únicamente ve los suyos, pero se refuerza aquí por si algún día
            // cambiara el filtro del servidor.
            var esAjenoTicket = !string.IsNullOrWhiteSpace(ticket.Usuario) &&
                                !string.Equals(ticket.Usuario, Sesion.Username, StringComparison.OrdinalIgnoreCase);
            var autorizado = !esAjenoTicket || Sesion.EsOperador;

            var btnCerrar = new Button
            {
                Text = !autorizado
                    ? "Solo el administrador puede cobrar este ticket"
                    : puedeCobrar ? "Cerrar / cobrar" : "Sin conexión · no se puede cobrar",
                Style = Ui.EstiloDe("BotonPrimario"),
                IsEnabled = puedeCobrar && autorizado,
                Opacity = puedeCobrar && autorizado ? 1 : 0.55
            };
            var enFila = ticket;
            btnCerrar.Clicked += async (_, _) => await CerrarAsync(enFila, estimado);
            contenido.Add(btnCerrar);

            if (!autorizado)
            {
                contenido.Add(new Label
                {
                    Text = $"Este ticket pertenece a «{ticket.Usuario}». Solo un administrador puede cerrarlo.",
                    FontSize = 10,
                    TextColor = Ui.ColorDe("TextoMedio"),
                    LineBreakMode = LineBreakMode.WordWrap
                });
            }
            else if (!puedeCobrar)
            {
                contenido.Add(new Label
                {
                    Text = "El cobro necesita conexión: libera la plaza y se registra en el servidor.",
                    FontSize = 10,
                    TextColor = Ui.ColorDe("TextoMedio"),
                    LineBreakMode = LineBreakMode.WordWrap
                });
            }
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
    private static bool EsTicketPropio(TicketDto ticket) =>
        string.IsNullOrWhiteSpace(ticket.Usuario) ||
        string.Equals(ticket.Usuario, Sesion.Username, StringComparison.OrdinalIgnoreCase);

    private List<TicketDto> TicketsAbiertos() => _tickets.Where(t => t.Abierto).ToList();

    private List<TicketDto> TicketsAbiertosPropios() =>
        TicketsAbiertos().Where(EsTicketPropio).ToList();

    private async void OnCobrarTodosClicked(object? sender, EventArgs e)
    {
        if (!Sesion.EsCliente) return;
        await CerrarVariosAsync(TicketsAbiertosPropios());
    }

    private async Task CerrarAsync(TicketDto ticket, double estimado)
    {
        // El cobro exige conexión: libera la plaza y se registra en el servidor, así que no
        // se puede dejar en cola. Se comprueba justo antes, por si se cayó la red mientras
        // el usuario leía la pantalla.
        var (puede, motivo) = await ApiCliente.PuedeCobrarAsync();
        if (!puede)
        {
            Registro.Aviso("vTickets", $"Cobro bloqueado del ticket {ticket.Codigo}: sin conexión.");
            ActualizarBloqueoCobro();
            await Ui.Error(this, motivo, "No se puede cobrar");
            return;
        }

        if (Sesion.EsCliente)
        {
            var otros = TicketsAbiertosPropios().Where(t => t.Id != ticket.Id).ToList();
            if (otros.Count > 0)
            {
                var todos = TicketsAbiertosPropios();
                var total = todos.Sum(t => Ui.MontoEstimado(t.Entrada, t.TarifaHora, DescuentoDe(t.Placa)));
                if (await Ui.Confirmar(this, "Varios vehículos",
                        $"Tienes {todos.Count} tickets abiertos. ¿Cobrarlos todos ({Ui.Moneda(total)})? Verás el QR de salida del último cobrado."))
                {
                    await CerrarVariosAsync(todos, false);
                    return;
                }
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
            var esPropio = EsTicketPropio(ticket);
            await CobrarUnoAsync(ticket);

            if (esPropio && PaseSalida.Activos.Count > 0)
                IrAPestanaSalida();
            else if (esPropio)
                await AbrirSalidaAsync();
            else
                await Ui.Exito(this,
                    $"Ticket de «{ticket.Usuario}» cobrado. El cliente verá su QR de salida en su cuenta.");

            try { await CargarAsync(); }
            catch (Exception ex) { Registro.Aviso("vTickets", $"No se pudo recargar la lista tras cobrar: {ex.Message}"); }
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    private async Task CerrarVariosAsync(List<TicketDto> tickets, bool confirmar = true)
    {
        if (tickets.Count == 0) return;

        // Igual que en el cobro individual: varios tickets mueven dinero y plazas, así que
        // necesitan conexión. Se comprueba antes de empezar para no dejar un cobro a medias.
        var (puede, motivo) = await ApiCliente.PuedeCobrarAsync();
        if (!puede)
        {
            Registro.Aviso("vTickets", "Cobro múltiple bloqueado: sin conexión.");
            ActualizarBloqueoCobro();
            await Ui.Error(this, motivo, "No se puede cobrar");
            return;
        }

        var total = tickets.Sum(t => Ui.MontoEstimado(t.Entrada, t.TarifaHora, DescuentoDe(t.Placa)));
        if (confirmar && tickets.Count > 1 &&
            !await Ui.Confirmar(this, "Cobrar todos y salir",
                $"Se cerrarán {tickets.Count} tickets ({string.Join(", ", tickets.Select(t => t.Placa))}) por {Ui.Moneda(total)}. QR de salida del último (10 minutos)."))
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
        else if (PaseSalida.Activos.Count > 0)
            await Ui.Exito(this, "QR de salida listo (10 minutos para salir).");

        if (Sesion.EsCliente && PaseSalida.Activos.Count > 0)
            IrAPestanaSalida();
        else if (Sesion.EsCliente)
            await AbrirSalidaAsync();

        // Recargar puede fallar si la página ya no está en pantalla tras el cobro.
        try { await CargarAsync(); }
        catch (Exception ex) { Registro.Aviso("vTickets", $"No se pudo recargar la lista tras cobrar: {ex.Message}"); }
    }

    /// <summary>Abre la pantalla de salida solo si hay navegación disponible.</summary>
    private async Task AbrirSalidaAsync()
    {
        try
        {
            var nav = Navigation;
            if (nav is null || Handler is null) return;
            await nav.PushAsync(new vSalida());
        }
        catch (Exception ex)
        {
            Registro.Aviso("vTickets", $"No se pudo abrir la pantalla de salida: {ex.Message}");
        }
    }

    /// <summary>
    /// Lleva al usuario a la pestaña «Salida» que creó el propio pase. Si por algún motivo
    /// no existe todavía, se abre la pantalla como página normal.
    /// </summary>
    private void IrAPestanaSalida()
    {
        if (vPrincipal.IrAPestana("Salida"))
            return;

        // La pestaña aún no está montada (el evento de cambio puede ir un instante atrasado):
        // se intenta una vez más en el siguiente ciclo y, si falla, se abre como página.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), async () =>
        {
            if (!vPrincipal.IrAPestana("Salida"))
                await AbrirSalidaAsync();
        });
    }

    private async Task CobrarUnoAsync(TicketDto ticket)
    {
        // Al cerrar el ticket el cliente deja de tener puesto: se suspende el reajuste de
        // pestañas para que no se quite la que está viendo mientras se navega al QR.
        vPrincipal.SuspenderReajuste();

        var estimado = Ui.MontoEstimado(ticket.Entrada, ticket.TarifaHora, DescuentoDe(ticket.Placa));
        Registro.Info("Cobro", $"Cerrando ticket {ticket.Codigo} de {ticket.Placa} (estimado {Ui.Moneda(estimado)}).");

        var cerrado = await ApiCliente.CerrarTicketAsync(ticket.Id);
        var monto = cerrado?.MontoTotal ?? estimado;

        // Se guarda el pase completo del ticket (el mismo que usa el QR del puesto) para que
        // la pantalla de salida muestre el comprobante detallado y no una tarjeta resumida.
        var detalle = await PaseDelTicketAsync(ticket, cerrado);

        var dueno = cerrado?.Usuario ?? ticket.Usuario ?? Sesion.Username;
        if (!EsTicketPropio(ticket))
        {
            Registro.Info("Cobro",
                $"Ticket {ticket.Codigo} cobrado en caja; el QR de salida corresponde a «{dueno}».");
            return;
        }

        PaseSalida.Activar(
            cerrado?.Codigo ?? ticket.Codigo,
            cerrado?.Placa ?? ticket.Placa,
            cerrado?.EspacioCodigo ?? ticket.EspacioCodigo ?? ticket.EspacioId.ToString(),
            monto,
            dueno,
            detalle);

        Registro.Info("Cobro", $"Ticket {cerrado?.Codigo ?? ticket.Codigo} cobrado por {Ui.Moneda(monto)} " +
                               $"(detalle {(detalle is null ? "NO disponible" : "completo")}).");
    }

    /// <summary>
    /// Recupera el pase completo del ticket recién cobrado. Primero intenta la copia local que
    /// dejó la pantalla del puesto; si no está, lo pide al API y, si tampoco responde, arma uno
    /// con los datos del listado.
    /// </summary>
    private async Task<PasePuestoDto?> PaseDelTicketAsync(TicketDto ticket, TicketDto? cerrado)
    {
        var guardado = AlmacenLocal.LeerPase(ticket.Id);
        if (guardado is not null) return guardado;

        try
        {
            var pase = await ApiCliente.PaseAsync(ticket.Id);
            if (pase is not null) return pase;
        }
        catch (Exception)
        {
            // sin conexión se arma con lo que hay
        }

        var fuente = cerrado ?? ticket;
        return new PasePuestoDto
        {
            TicketId = fuente.Id,
            TicketCodigo = fuente.Codigo,
            Placa = fuente.Placa,
            EspacioId = fuente.EspacioId,
            EspacioCodigo = fuente.EspacioCodigo ?? "",
            Zona = fuente.Zona ?? "",
            SedeNombre = fuente.Sede ?? "",
            TarifaHora = fuente.TarifaHora ?? 0,
            Entrada = fuente.Entrada,
            MontoAhora = fuente.MontoTotal ?? 0,
            Estado = fuente.Estado,
            Usuario = fuente.Usuario ?? "",
            Descuento = DescuentoDe(fuente.Placa)
        };
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
