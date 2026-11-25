using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Elección de plaza: pestañas por zona, filtro por tipo de vehículo y una tarjeta por
/// plaza con su foto (libre, ocupada o tu vehículo). Una plaza libre se selecciona y
/// "Continuar" lleva a la IA; una ocupada o la propia actúan al tocarlas.
/// Si el cliente aún tiene un vehículo sin plaza, todas las zonas siguen activas.
/// Solo se bloquean cuando todos sus vehículos ya están estacionados.
///
/// Con una sede (la que eligió en el mapa) la pantalla trabaja solo con las plazas de ese
/// parqueadero y, al tocar un puesto libre, abre el ticket y muestra el QR del puesto.
/// </summary>
public partial class vPlazas : ContentPage
{
    private static readonly Color Amarillo = Color.FromArgb("#F2C51A");
    private static readonly Color Fondo = Color.FromArgb("#0A0E13");
    private static readonly Color Superficie = Color.FromArgb("#141B24");
    private static readonly Color SuperficieAlta = Color.FromArgb("#1C2530");
    private static readonly Color Borde = Color.FromArgb("#28323F");
    private static readonly Color Gris = Color.FromArgb("#7C8A9C");
    private static readonly Color Claro = Color.FromArgb("#F8FAFC");
    private static readonly Color Tinta = Color.FromArgb("#0F172A");
    private static readonly Color Verde = Color.FromArgb("#34D399");
    private static readonly Color Rojo = Color.FromArgb("#F87171");
    private static readonly Color AzulOcupada = Color.FromArgb("#3B82F6");

    private List<EspacioDto> _espacios = [];
    private List<TicketDto> _tickets = [];
    private List<VehiculoDto> _vehiculos = [];
    private OcupacionDto? _ocupacion;
    private string? _zona;
    private string? _tipo;
    private EspacioDto? _seleccionada;
    private double _anchoPintado;
    private bool _ubicarPlaza;
    private readonly SedeDto? _sede;
    private SedeDto? _sedeEfectiva;
    private List<SedeDto> _sedes = [];
    private string? _placaReserva;
    private bool _reservando;
    private bool _iaBuscando;

    /// <summary>True cuando el usuario desplegó la ficha completa del vehículo elegido.</summary>
    private bool _fichaAbierta = true;

    /// <summary>Solo el dibujo del estacionamiento: sin filtros, fichas ni pie de página.</summary>
    private bool _soloPlano;

    /// <summary>El usuario pulsó «Ver Detalle»: no volver a ocultarlos hasta salir de la pantalla.</summary>
    private bool _usuarioPrefiereControles;

    /// <summary>Tras alta o baja en Autos, al volver a esta pantalla se prioriza esta placa.</summary>
    public static void NotificarVehiculosActualizados(string? placaPreferida = null)
    {
        if (!string.IsNullOrWhiteSpace(placaPreferida))
            vVehiculos.PlacaPendiente = placaPreferida.Trim().ToUpperInvariant();
    }

    public static void IrATabMapa()
    {
        var pagina = Application.Current?.Windows.Count > 0
            ? Application.Current.Windows[0].Page
            : null;

        if (pagina is not Shell shell || shell.Items.Count == 0) return;
        shell.CurrentItem = shell.Items[0];
    }

    public vPlazas() : this(null, null) { }

    public vPlazas(string? tipo, string? zona)
    {
        InitializeComponent();
        _tipo = string.IsNullOrWhiteSpace(tipo) ? null : tipo.ToUpperInvariant();
        _zona = zona;
        PintarSesion();
        lblRol.Text = string.IsNullOrWhiteSpace(Sesion.RolPrincipal) ? "—" : Sesion.RolPrincipal;
        SizeChanged += (_, _) =>
        {
            if (Width <= 0) return;
            var estrecho = Width < 480;
            if (FindByName("cajaPlazas") is VerticalStackLayout caja)
                caja.Padding = estrecho ? new Thickness(8, 6, 8, 6) : new Thickness(10, 8, 10, 8);
            if (_espacios.Count > 0 && Math.Abs(Width - _anchoPintado) > 12)
                PintarPlazas();
            PintarPlaca();
        };
    }

    /// <summary>Plazas de un parqueadero concreto: al tocar un puesto libre se reserva y sale el QR.</summary>
    public vPlazas(SedeDto sede) : this(null, null)
    {
        _sede = sede;
        // Se llega aquí con la sede ya elegida: la ficha del vehículo arranca plegada y las
        // barras que solo sirven para elegir (marca, título, ocupación global) se ocultan,
        // para que el plano quede a la vista sin desplazar media pantalla.
        _fichaAbierta = false;
        panelSede.IsVisible = true;
        barraMarca.IsVisible = false;
        lblTituloPagina.IsVisible = false;
        panelSesion.IsVisible = false;
        lblSedeNombre.Text = sede.Nombre;
        lblSedeDireccion.Text = $"📍 {sede.Direccion} · {sede.Horario}";
        Title = sede.Nombre;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ubicarPlaza = true;
        _usuarioPrefiereControles = false;
        if (!string.IsNullOrWhiteSpace(vVehiculos.PlacaPendiente))
        {
            _placaReserva = vVehiculos.PlacaPendiente.Trim().ToUpperInvariant();
            vVehiculos.PlacaPendiente = null;
        }

        await CargarAsync();

        // Al venir de una sede concreta, el plano es lo que el usuario quiere ver: se baja
        // el scroll hasta las bahías en vez de dejarlo en la cabecera.
        if (_sede is not null)
            await DesplazarAlPlanoAsync();
    }

    /// <summary>
    /// Lleva el scroll hasta el dibujo del plano. Se hace en dos pasos porque el alto real de
    /// las tarjetas de arriba solo se conoce después de que el layout termina de medirse.
    /// </summary>
    private async Task DesplazarAlPlanoAsync()
    {
        await Task.Delay(60);
        try
        {
            VisualElement destino = marcoPlano;
            await scrollPlazas.ScrollToAsync(destino, ScrollToPosition.Start, false);
        }
        catch
        {
            // Si el layout aún no está listo, se deja el scroll donde está: no es un error fatal.
        }
    }

    private void OnSoloPlanoClicked(object? sender, EventArgs e)
    {
        _soloPlano = !_soloPlano;
        if (!_soloPlano)
            _usuarioPrefiereControles = true;
        AplicarVistaSoloPlano();
    }

    /// <summary>
    /// Conmuta entre la vista completa y solo el marco del plano. El borde amarillo del dibujo
    /// queda a pantalla casi completa para leer bahías y señalética sin distracciones.
    /// </summary>
    private void AplicarVistaSoloPlano()
    {
        panelControles.IsVisible = !_soloPlano;
        panelPie.IsVisible = !_soloPlano;

        btnSoloPlano.Text = _soloPlano ? "Ver Detalle" : "Solo plano";
        btnSoloPlano.BackgroundColor = _soloPlano ? Amarillo : Fondo;
        btnSoloPlano.TextColor = _soloPlano ? Tinta : Amarillo;

        marcoPlano.StrokeThickness = _soloPlano ? 2 : 1;
        marcoPlanoCabecera.IsVisible = !_soloPlano;

        if (cajaPlazas is null) return;

        if (_soloPlano)
            cajaPlazas.Padding = new Thickness(4, 4, 4, 4);
        else
        {
            var estrecho = Width > 0 && Width < 480;
            cajaPlazas.Padding = estrecho ? new Thickness(8, 6, 8, 6) : new Thickness(10, 8, 10, 8);
            RestaurarCabeceraContexto();
        }

        if (_soloPlano)
            _ = DesplazarAlPlanoAsync();
    }

    /// <summary>
    /// Si ya hay ticket abierto con puesto, arranca como en la captura: solo el plano y la
    /// bahía amarilla del vehículo, sin filtros ni fichas encima.
    /// </summary>
    private void SugerirVistaPlanoAsignado()
    {
        if (_usuarioPrefiereControles || !Sesion.EsCliente) return;
        if (MisTicketsAbiertos().Count == 0) return;

        _soloPlano = true;
        AplicarVistaSoloPlano();
    }

    /// <summary>
    /// Al salir del modo plano, recupera qué cabeceras deben verse según llegamos con sede o no.
    /// </summary>
    private void RestaurarCabeceraContexto()
    {
        if (_sede is null) return;
        barraMarca.IsVisible = false;
        lblTituloPagina.IsVisible = false;
        panelSesion.IsVisible = false;
    }

    private async void OnActualizarClicked(object? sender, EventArgs e) => await CargarAsync();

    private async void OnContinuarClicked(object? sender, EventArgs e)
    {
        if (_sede is not null)
        {
            await Navigation.PushAsync(new vTickets());
            return;
        }

        if (_seleccionada is not null)
        {
            await PulsarPlazaAsync(_seleccionada, false, false, null, null);
            return;
        }

        if (TodosEstacionados())
            await Navigation.PushAsync(new vTickets());
    }

    // ================================================================ carga
    private async Task CargarAsync()
    {
        spinnerPlazas.IsVisible = spinnerPlazas.IsRunning = true;
        btnActualizar.IsEnabled = false;
        try
        {
            // Sin sede (por ejemplo al entrar desde el menú antiguo) se elige la más cercana:
            // la pantalla siempre trabaja sobre un solo parqueadero.
            if (_sede is null && _sedes.Count == 0)
                _sedes = await SedesSeguroAsync();

            _sedeEfectiva ??= _sede ?? _sedes.FirstOrDefault();
            var sedeId = _sedeEfectiva?.Id;

            var tareaEspacios = sedeId is null
                ? ApiCliente.ListarEspaciosAsync()
                : ApiCliente.EspaciosDeSedeAsync(sedeId.Value);
            var tareaTickets = TicketsSeguroAsync();
            var tareaVehiculos = VehiculosSeguroAsync();

            await Task.WhenAll(tareaEspacios, tareaTickets, tareaVehiculos);

            _espacios = await tareaEspacios ?? [];
            _ocupacion = EspacioRepositorioLocal.Calcular(_espacios);
            _tickets = await tareaTickets;
            _vehiculos = await tareaVehiculos;
            _seleccionada = null;
            ElegirPlacaReserva();

            PintarSede();
            PintarSedes();
            PintarResumen();
            PintarAviso();
            PintarVehiculos();
            Pintar();
            PintarSesion();
            SugerirVistaPlanoAsignado();
        }
        catch (Exception ex)
        {
            PintarSesion();
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinnerPlazas.IsRunning = false;
            spinnerPlazas.IsVisible = false;
            btnActualizar.IsEnabled = true;
        }
    }

    private static async Task<List<SedeDto>> SedesSeguroAsync()
    {
        try
        {
            var (lat, lng, _) = await Ubicacion.ObtenerAsync();
            return await ApiCliente.SedesAsync(lat, lng) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Encabezado con el parqueadero que se está viendo.</summary>
    private void PintarSede()
    {
        if (_sedeEfectiva is null)
        {
            panelSede.IsVisible = false;
            return;
        }

        panelSede.IsVisible = true;
        lblSedeNombre.Text = _sedeEfectiva.Nombre;
        lblSedeDireccion.Text = $"📍 {_sedeEfectiva.Direccion} · {_sedeEfectiva.Horario}";
        // "Cambiar" solo tiene sentido si se entró desde el mapa (aquí ya hay selector de sedes).
        lblCambiarSede.IsVisible = _sede is not null;
        panelVehiculo.IsVisible = true;
        btnAsignarIa.IsVisible = _sedeEfectiva is not null && !Sesion.EsOperador;
    }

    /// <summary>Selector de parqueadero: solo aparece si se entró sin una sede elegida.</summary>
    private void PintarSedes()
    {
        if (_sede is not null || contenedorSedes is null) return;

        contenedorSedes.Children.Clear();
        contenedorSedes.IsVisible = true;

        foreach (var sede in _sedes)
        {
            var elegida = _sedeEfectiva?.Id == sede.Id;
            var chip = new Border
            {
                BackgroundColor = elegida ? Amarillo : SuperficieAlta,
                Stroke = elegida ? Amarillo : Borde,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(9, 4),
                Margin = new Thickness(0, 0, 6, 4),
                Content = new Label
                {
                    Text = $"{sede.Codigo} · {sede.PlazasLibres} libres",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    TextColor = elegida ? Tinta : Claro,
                    LineBreakMode = LineBreakMode.NoWrap
                }
            };
            FlexLayout.SetShrink(chip, 0);
            FlexLayout.SetGrow(chip, 0);

            var destino = sede;
            var toque = new TapGestureRecognizer();
            toque.Tapped += async (_, _) =>
            {
                if (_sedeEfectiva?.Id == destino.Id) return;
                _sedeEfectiva = destino;
                _zona = null;
                _tipo = null;
                _ubicarPlaza = true;
                await CargarAsync();
            };
            chip.GestureRecognizers.Add(toque);
            contenedorSedes.Children.Add(chip);
        }
    }

    /// <summary>Ocupación de la sede calculada con las mismas plazas que se muestran.</summary>
    private static class EspacioRepositorioLocal
    {
        public static OcupacionDto Calcular(List<EspacioDto> plazas)
        {
            var total = plazas.Count;
            var ocupados = plazas.Count(p => p.Ocupado);

            var zonas = new Dictionary<string, OcupacionZonaDto>();
            foreach (var grupo in plazas.GroupBy(p => p.Zona ?? ""))
            {
                var t = grupo.Count();
                var o = grupo.Count(p => p.Ocupado);
                zonas[grupo.Key] = new OcupacionZonaDto
                {
                    Total = t,
                    Ocupados = o,
                    Libres = t - o,
                    Porcentaje = t == 0 ? 0 : (int)Math.Round(o * 100.0 / t)
                };
            }

            return new OcupacionDto
            {
                Total = total,
                Ocupados = ocupados,
                Libres = total - ocupados,
                PorcentajeGlobal = total == 0 ? 0 : (int)Math.Round(ocupados * 100.0 / total),
                Zonas = zonas
            };
        }
    }

    // ================================================================ vehículo de la reserva
    /// <summary>Con qué placa se reserva: la elegida o el primer vehículo activo del cliente.</summary>
    private void ElegirPlacaReserva()
    {
        if (_sedeEfectiva is null) return;

        var activos = _vehiculos.Where(v => v.Activo).ToList();
        var mios = MisTicketsAbiertos();

        // Con puesto ya asignado, el chip y la ficha coinciden con la placa del ticket.
        if (mios.Count > 0)
        {
            var placaPuesto = mios
                .OrderByDescending(t => t.Entrada)
                .Select(t => t.Placa)
                .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

            var reservandoOtra = _placaReserva is not null &&
                                 activos.Any(v => string.Equals(v.Placa, _placaReserva, StringComparison.OrdinalIgnoreCase)) &&
                                 mios.All(t => !string.Equals(t.Placa, _placaReserva, StringComparison.OrdinalIgnoreCase));

            if (!reservandoOtra &&
                !string.IsNullOrWhiteSpace(placaPuesto) &&
                activos.Any(v => string.Equals(v.Placa, placaPuesto, StringComparison.OrdinalIgnoreCase)))
            {
                _placaReserva = placaPuesto;
                return;
            }
        }

        if (_placaReserva is not null && activos.Any(v => v.Placa == _placaReserva)) return;

        // Prefiere un vehículo que todavía no esté estacionado y del tipo que se está mirando.
        var estacionadas = mios.Select(t => t.Placa).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _placaReserva = activos.FirstOrDefault(v => !estacionadas.Contains(v.Placa) &&
                                                    (_tipo is null || string.Equals(v.Tipo, _tipo, StringComparison.OrdinalIgnoreCase)))?.Placa
                        ?? activos.FirstOrDefault(v => !estacionadas.Contains(v.Placa))?.Placa
                        ?? activos.FirstOrDefault()?.Placa;
    }

    private void PintarVehiculos()
    {
        if (_sedeEfectiva is null)
        {
            panelVehiculo.IsVisible = false;
            btnAsignarIa.IsVisible = false;
            return;
        }

        panelVehiculo.IsVisible = true;
        btnAsignarIa.IsVisible = !Sesion.EsOperador;
        contenedorVehiculos.Children.Clear();

        var activos = _vehiculos
            .Where(v => v.Activo)
            .OrderBy(v => v.Placa, StringComparer.OrdinalIgnoreCase)
            .ToList();

        lblVehiculosTitulo.Text = activos.Count switch
        {
            0 => "SIN VEHÍCULOS ACTIVOS",
            1 => "1 VEHÍCULO ACTIVO",
            _ => $"{activos.Count} VEHÍCULOS · DESLIZA"
        };

        if (activos.Count == 0)
        {
            contenedorVehiculos.Children.Add(new Label
            {
                Text = "Regístralos en Autos",
                FontSize = 10,
                TextColor = Rojo
            });
            PintarPlaca();
            return;
        }

        // Selector horizontal: cada chip lleva icono, placa y la flecha de despliegue. La flecha
        // ya no indica «desplegar la ficha», sino que el elegido es el que se muestra en grande.
        foreach (var vehiculo in activos)
        {
            var elegido = string.Equals(vehiculo.Placa, _placaReserva, StringComparison.OrdinalIgnoreCase);

            var fila = new Grid
            {
                ColumnDefinitions = [new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Auto)],
                ColumnSpacing = 3
            };
            fila.Add(new Label
            {
                Text = Ui.IconoTipo(vehiculo.Tipo),
                FontSize = 9,
                VerticalOptions = LayoutOptions.Center
            }, 0);
            fila.Add(new Label
            {
                Text = PlacaEcuador.Formatear(vehiculo.Placa),
                FontFamily = "OpenSansSemibold",
                FontSize = 8.5,
                TextColor = elegido ? Tinta : Claro,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.NoWrap
            }, 1);
            fila.Add(new Label
            {
                Text = elegido ? (_fichaAbierta ? "▾" : "▸") : "",
                FontSize = 7.5,
                TextColor = elegido ? Tinta : Gris,
                VerticalOptions = LayoutOptions.Center
            }, 2);

            var chip = new Border
            {
                BackgroundColor = elegido ? Amarillo : SuperficieAlta,
                Stroke = elegido ? Amarillo : Borde,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 7 },
                Padding = new Thickness(6, 3),
                HorizontalOptions = LayoutOptions.Start,
                Content = fila
            };

            var placa = vehiculo.Placa;
            var toque = new TapGestureRecognizer();
            toque.Tapped += (_, _) =>
            {
                // Tocar otro vehículo despliega su ficha; tocar el elegido la pliega/despliega.
                if (string.Equals(_placaReserva, placa, StringComparison.OrdinalIgnoreCase))
                    _fichaAbierta = !_fichaAbierta;
                else
                {
                    _placaReserva = placa;
                    _fichaAbierta = true;
                }

                PintarVehiculos();
                PintarSeleccion();
                Pintar();
            };
            chip.GestureRecognizers.Add(toque);
            contenedorVehiculos.Children.Add(chip);
        }

        PintarPlaca();
    }

    /// <summary>Segunda fila: la placa al centro y, si la ficha está abierta, los datos al lado.</summary>
    private void PintarPlaca()
    {
        contenedorPlaca.Children.Clear();
        contenedorDatosVehiculo.Children.Clear();

        var vehiculo = VehiculoReserva();
        if (vehiculo is null)
        {
            contenedorPlaca.Children.Add(new Label
            {
                Text = "Sin placa",
                FontSize = 10,
                TextColor = Gris
            });
            return;
        }

        // Placa compacta: es una referencia, no el protagonista de la tarjeta. Con la ficha
        // abierta se deja sitio a los datos; plegada crece un poco, pero sin pasar de 116 px.
        var total = Width > 0 ? Width - 40 : 300;
        var ancho = Math.Clamp(_fichaAbierta ? total - 190 : total - 150, 92, 116);
        contenedorPlaca.Children.Add(PlacaEcuador.Crear(vehiculo.Placa, vehiculo.Tipo, ancho, resaltada: _fichaAbierta));

        if (_fichaAbierta)
        {
            var estacionado = MisTicketsAbiertos().Any(t =>
                string.Equals(t.Placa, vehiculo.Placa, StringComparison.OrdinalIgnoreCase));
            contenedorDatosVehiculo.Children.Add(PlacaEcuador.CrearFichaCompleta(vehiculo, estacionado));
        }
    }

    private VehiculoDto? VehiculoReserva() =>
        _vehiculos.FirstOrDefault(v => string.Equals(v.Placa, _placaReserva, StringComparison.OrdinalIgnoreCase));

    private static string PerfilParaIa(VehiculoDto vehiculo)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(vehiculo.Propietario))
            partes.Add(vehiculo.Propietario.Trim());
        if (!string.IsNullOrWhiteSpace(vehiculo.Marca))
            partes.Add("marca " + vehiculo.Marca.Trim());
        if (!string.IsNullOrWhiteSpace(vehiculo.Color))
            partes.Add("color " + vehiculo.Color.Trim());
        partes.Add("tipo " + (string.IsNullOrWhiteSpace(vehiculo.Tipo) ? "AUTO" : vehiculo.Tipo.Trim()));
        return string.Join(", ", partes);
    }

    private static bool EsEmbarazadaPerfil(string perfil) => perfil.ToLowerInvariant().Contains("embaraz");

    private static bool EsDiscapacidadPerfil(string perfil)
    {
        var p = perfil.ToLowerInvariant();
        return p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial");
    }

    private static bool PlazaComun(string tipo) => tipo is not "DISCAPACIDAD" and not "EMBARAZADA";

    private static int PrioridadPerfil(EspacioDto plaza, string perfil)
    {
        var p = perfil.ToLowerInvariant();
        var tipo = (plaza.TipoVehiculo ?? "").ToUpperInvariant();
        var zona = (plaza.Zona ?? "").ToUpperInvariant();
        var nivel = plaza.Nivel ?? 9;

        if (EsDiscapacidadPerfil(perfil))
            return tipo == "DISCAPACIDAD" ? 0 : 10;
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor"))
            return PlazaComun(tipo) && zona.Contains("ZONA A") && nivel == 1 ? 0 : 8;
        if (EsEmbarazadaPerfil(perfil))
            return tipo == "EMBARAZADA" ? 0 : 8;
        if (p.Contains("estudiante") || p.Contains("estudiate"))
            return PlazaComun(tipo) && zona.Contains("ZONA C") ? 0 : 8;
        if (p.Contains("mujer"))
            return PlazaComun(tipo) && zona.Contains("ZONA D") ? 0 : 8;
        if (p.Contains("hombre"))
            return PlazaComun(tipo) && (zona.Contains("ZONA B") || zona.Contains("ZONA C")) ? 0 : 8;
        return 0;
    }

    private EspacioDto? ElegirMejorPlazaLocal(VehiculoDto vehiculo)
    {
        var libres = _espacios.Where(p => !p.Ocupado && PorQueNoCumple(p, vehiculo) is null).ToList();
        if (libres.Count == 0) return null;
        var perfil = vehiculo.Propietario ?? "";
        return libres
            .OrderBy(p => PrioridadPerfil(p, perfil))
            .ThenBy(p => p.TarifaHora)
            .ThenBy(p => p.Nivel ?? 9)
            .ThenBy(p => p.Codigo, StringComparer.Ordinal)
            .First();
    }

    private EspacioDto? ResolverPlazaEnSede(RecomendacionDto r) =>
        _espacios.FirstOrDefault(p => p.Id == r.EspacioId)
        ?? _espacios.FirstOrDefault(p => string.Equals(p.Codigo, r.EspacioCodigo, StringComparison.OrdinalIgnoreCase));

    private string? MensajeTicketAbiertoLocal(string placa)
    {
        var abierto = _tickets.FirstOrDefault(t =>
            t.Abierto && string.Equals(t.Placa, placa, StringComparison.OrdinalIgnoreCase));
        if (abierto is null) return null;
        var plaza = _espacios.FirstOrDefault(p => p.Id == abierto.EspacioId);
        if (plaza is not null && !plaza.Ocupado) return null;
        var donde = string.IsNullOrWhiteSpace(abierto.EspacioCodigo) ? "" : $" en {abierto.EspacioCodigo}";
        return $"La placa {placa} ya tiene un ticket abierto{donde}. Ciérralo en Tickets antes de elegir otro puesto.";
    }

    private void MarcarPlazaRecomendada(EspacioDto plaza)
    {
        _seleccionada = plaza;
        _zona = plaza.Zona;
        Pintar();
    }

    private async void OnAsignarIaClicked(object? sender, EventArgs e)
    {
        if (_iaBuscando || Sesion.EsOperador) return;

        var vehiculo = VehiculoReserva();
        if (vehiculo is null || !vehiculo.Activo)
        {
            await Ui.Aviso(this, "Elige un vehículo activo arriba o regístralo en la pestaña Autos.");
            return;
        }

        if (TodosEstacionados())
        {
            await Ui.Informar(this, "Todos tus vehículos ya están estacionados. Cierra un ticket para liberar otro puesto.");
            return;
        }

        var avisoTicket = MensajeTicketAbiertoLocal(vehiculo.Placa);
        if (avisoTicket is not null)
        {
            await Ui.Aviso(this, avisoTicket, "Ticket abierto");
            return;
        }

        var tipo = string.IsNullOrWhiteSpace(vehiculo.Tipo) ? "AUTO" : vehiculo.Tipo;
        var preferencia = PerfilParaIa(vehiculo);

        _iaBuscando = true;
        btnAsignarIa.IsEnabled = false;
        btnAsignarIa.Text = "🤖  Buscando…";
        try
        {
            // El motor de IA puede tardar o no estar levantado: se le da un tope corto
            // y, si no contesta, se elige en el acto con la misma heurística del servidor.
            var r = await RecomendarConTopeAsync(tipo, preferencia, vehiculo.Placa, TimeSpan.FromSeconds(6));

            EspacioDto? plaza = r is null ? null : ResolverPlazaEnSede(r);
            var respondioIa = plaza is not null;
            plaza ??= ElegirMejorPlazaLocal(vehiculo);

            if (plaza is null)
            {
                await Ui.Aviso(this, "No hay puestos libres que coincidan con tu vehículo en este parqueadero.");
                return;
            }

            MarcarPlazaRecomendada(plaza);
            var texto = $"Puesto {plaza.Codigo} · {Ui.Tarifa(plaza.TarifaHora)}";
            if (respondioIa && !string.IsNullOrWhiteSpace(r!.Motivo))
                texto += $"\n{r.Motivo.Trim()}";
            else
                texto += "\nElegido al instante según tu perfil y el tipo de vehículo.";
            texto += "\n\nToca el puesto resaltado para entrar o reservar.";
            await Ui.Aviso(this, texto, "Mejor puesto");
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            _iaBuscando = false;
            btnAsignarIa.IsEnabled = true;
            btnAsignarIa.Text = "🤖  Asignar por IA";
        }
    }

    /// <summary>Consulta la IA sin dejar el botón esperando: si tarda más que el tope, devuelve null.</summary>
    private static async Task<RecomendacionDto?> RecomendarConTopeAsync(string tipo, string preferencia,
        string placa, TimeSpan tope)
    {
        try
        {
            var tarea = ApiCliente.RecomendarAsync(tipo, preferencia, placa);
            var ganadora = await Task.WhenAny(tarea, Task.Delay(tope));
            return ganadora == tarea ? await tarea : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Al tocar un puesto libre el cliente elige cómo quiere entrar:
    ///   · «Entrar ahora»  → se abre el ticket y el QR aparece al instante.
    ///   · «Reservar»      → elige fecha, hora de ingreso y hora de salida, y también sale el QR.
    /// </summary>
    private async Task ReservarAsync(EspacioDto plaza)
    {
        if (_reservando) return;
        if (string.IsNullOrWhiteSpace(_placaReserva))
        {
            await Ui.Informar(this, "Agrega un vehículo en la pestaña Autos para reservar el puesto.");
            return;
        }

        // Las mismas reglas del sistema original para los puestos preferenciales y el tipo.
        var vehiculo = VehiculoReserva();
        var motivo = vehiculo is null ? null : PorQueNoCumple(plaza, vehiculo);
        if (motivo is not null)
        {
            await Ui.Aviso(this, $"El puesto {plaza.Codigo} no se puede reservar con {_placaReserva}: {motivo}. " +
                                 "Toca un puesto libre que corresponda.", "Puesto no disponible");
            return;
        }

        var opcion = await DisplayActionSheetAsync(
            $"Puesto {plaza.Codigo} · {Ui.Tarifa(plaza.TarifaHora)}",
            "Cancelar", null,
            "🚗 Entrar ahora (QR inmediato)",
            "📅 Reservar fecha y hora");

        if (string.IsNullOrWhiteSpace(opcion) || opcion == "Cancelar") return;

        if (opcion.StartsWith("📅") || opcion.Contains("Reservar", StringComparison.Ordinal))
        {
            var sede = _sedeEfectiva;
            if (sede is null)
            {
                await Ui.Informar(this, "Vuelve a cargar el parqueadero y elige el puesto otra vez.");
                return;
            }

            var descuento = vehiculo is null
                ? 0
                : Math.Max(vehiculo.Descuento, Ui.DescuentoDe(vehiculo.Propietario));
            await Navigation.PushAsync(new vReserva(sede, plaza, _placaReserva!, descuento));
            return;
        }

        _reservando = true;
        try
        {
            var pase = await ApiCliente.EstacionarAsync(_placaReserva!, plaza.Id);
            AlmacenLocal.GuardarPase(pase, Sesion.Username);
            _seleccionada = null;
            await CargarAsync();
            await Navigation.PushAsync(new vQrPuesto(pase));
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
            await CargarAsync();
        }
        finally
        {
            _reservando = false;
        }
    }

    /// <summary>Null si el vehículo puede ocupar ese puesto; si no, el motivo para avisar.</summary>
    private static string? PorQueNoCumple(EspacioDto plaza, VehiculoDto vehiculo)
    {
        if (plaza.Ocupado) return "ya está ocupada";
        if (!vehiculo.Activo) return "el vehículo está de baja";

        var tipoVehiculo = string.IsNullOrWhiteSpace(vehiculo.Tipo) ? "AUTO" : vehiculo.Tipo.Trim().ToUpperInvariant();
        var tipoPuesto = (plaza.TipoVehiculo ?? "AUTO").Trim().ToUpperInvariant();
        var perfil = (vehiculo.Propietario ?? "").ToLowerInvariant();

        var esDiscapacidad = perfil.Contains("capacidades") || perfil.Contains("discapacit") || perfil.Contains("especial");
        var esEmbarazada = perfil.Contains("embaraz");

        if (tipoPuesto == "DISCAPACIDAD")
            return esDiscapacidad ? null : "es un puesto preferencial para capacidades especiales";
        if (tipoPuesto == "EMBARAZADA")
            return esEmbarazada
                ? (tipoVehiculo == "MOTO" ? "el puesto de embarazada no es para moto" : null)
                : "es un puesto preferencial para embarazadas";
        if (esDiscapacidad) return "tu perfil usa los puestos de discapacidad";
        if (esEmbarazada) return "tu perfil usa los puestos de embarazada";
        if (tipoPuesto != tipoVehiculo) return $"es para {tipoPuesto} y el vehículo es {tipoVehiculo}";
        return null;
    }

    private async void OnCambiarSedeTapped(object? sender, TappedEventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }


    private void PintarSesion()
    {
        var baseTexto = Sesion.Activa ? $"Sesión: {Sesion.Username}" : "Sin sesión";
        if (AlmacenLocal.Pendientes > 0)
            baseTexto += $" · {AlmacenLocal.Pendientes} por sincronizar";
        else if (ApiCliente.SinConexion)
            baseTexto += " · sin conexión";
        lblSesion.Text = baseTexto;
    }

    /// <summary>Los avisos no deben tumbar la pantalla si el endpoint falla.</summary>
    private static async Task<List<TicketDto>> TicketsSeguroAsync()
    {
        try
        {
            return await ApiCliente.TicketsAbiertosAsync() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static async Task<List<VehiculoDto>> VehiculosSeguroAsync()
    {
        try
        {
            return await ApiCliente.ListarVehiculosAsync() ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }



    //  asiganr puesto de IA MB

    private void Pintar()
    {
        ElegirZona();
        PintarZonas();
        PintarTipos();
        PintarPlazas();
    }

    // ================================================================ resumen
    private void PintarResumen()
    {
        if (_ocupacion is null)
        {
            lblResumen.Text = "Sin datos de ocupación";
            barraOcupacion.Progress = 0;
            return;
        }

        lblResumen.Text = $"{_ocupacion.Libres} libres de {_ocupacion.Total} · {_ocupacion.PorcentajeGlobal}% ocupado";
        var porcentaje = _ocupacion.PorcentajeGlobal;
        barraOcupacion.Progress = Math.Clamp(porcentaje > 1 ? porcentaje / 100.0 : porcentaje, 0, 1);
    }

    // ================================================================ aviso
    private List<VehiculoDto> VehiculosActivos() =>
        _vehiculos.Where(v => v.Activo).ToList();

    private List<TicketDto> MisTicketsAbiertos()
    {
        var abiertos = _tickets.Where(t => t.Abierto).ToList();
        if (abiertos.Count == 0) return [];

        var placas = VehiculosActivos()
            .Select(v => v.Placa)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return abiertos
            .Where(t =>
                Sesion.EsMio(t.UsuarioId, t.Usuario) ||
                (placas.Count > 0 && placas.Contains(t.Placa)) ||
                (placas.Count == 0 && Sesion.EsCliente && string.IsNullOrWhiteSpace(t.Usuario)))
            .DistinctBy(t => t.Id)
            .ToList();
    }

    private TicketDto? MiTicketAbierto() => MisTicketsAbiertos().FirstOrDefault();

    private List<EspacioDto> PlazasMias() =>
        MisTicketsAbiertos()
            .Select(t => _espacios.FirstOrDefault(p => EsDePlaza(t, p)))
            .Where(p => p is not null)
            .Cast<EspacioDto>()
            .ToList();

    private EspacioDto? PlazaMia() => PlazasMias().FirstOrDefault();

    /// <summary>
    /// Las demás zonas se bloquean solo cuando todos los vehículos del cliente
    /// ya tienen plaza. Si le queda alguno libre, puede estacionar en cualquier zona.
    /// </summary>
    private bool TodosEstacionados()
    {
        var tickets = MisTicketsAbiertos();
        if (tickets.Count == 0) return false;

        var vehiculos = VehiculosActivos();
        if (vehiculos.Count == 0) return true;

        var placas = tickets.Select(t => t.Placa).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return vehiculos.All(v => placas.Contains(v.Placa));
    }

    private HashSet<string> ZonasMias() =>
        PlazasMias()
            .Select(p => p.Zona ?? "")
            .Concat(MisTicketsAbiertos().Select(t => t.Zona ?? ""))
            .Where(z => !string.IsNullOrWhiteSpace(z))
            .ToHashSet(StringComparer.Ordinal);

    private void PintarAviso()
    {
        var abiertos = _tickets.Where(t => t.Abierto).ToList();
        var mios = MisTicketsAbiertos();
        var plazasMias = PlazasMias();

        panelAviso.BackgroundColor = mios.Count == 0 ? SuperficieAlta : Amarillo;
        lblAviso.TextColor = mios.Count == 0 ? Claro : Tinta;
        lblAvisoDetalle.TextColor = mios.Count == 0 ? Gris : Color.FromArgb("#3D3307");

        if (mios.Count > 0)
        {
            var pendientes = Math.Max(0, VehiculosActivos().Count - mios.Count);
            var extra = pendientes > 0
                ? $"Aún puedes estacionar {pendientes} vehículo{(pendientes == 1 ? "" : "s")} más."
                : "Toca tu plaza o \"Ver ticket\" para pagar.";

            if (mios.Count == 1)
            {
                var mio = mios[0];
                var plazaMia = plazasMias.FirstOrDefault();
                var codigo = mio.EspacioCodigo ?? mio.EspacioId.ToString();
                var zona = plazaMia is null ? "" : $"{NombreCorto(plazaMia.Zona)} · Nivel {Ui.Nivel(plazaMia.Nivel)} · ";
                lblAviso.Text = $"📍 Tu vehículo está en {zona}Plaza {codigo}";
                lblAvisoDetalle.Text = $"Placa {mio.Placa} · ticket {mio.Codigo} · {extra}";
            }
            else
            {
                var puestos = string.Join(", ", mios.Select(t => t.EspacioCodigo ?? t.EspacioId.ToString()));
                lblAviso.Text = $"📍 Tienes {mios.Count} vehículos estacionados: {puestos}";
                lblAvisoDetalle.Text = string.Join(" · ", mios.Select(t => t.Placa)) + " · " + extra;
            }

            lblAviso.FontSize = 12;
            panelAviso.IsVisible = true;
            return;
        }

        lblAviso.FontSize = 11;
        if (Sesion.EsOperador && abiertos.Count > 0)
        {
            lblAviso.Text = $"{abiertos.Count} plaza(s) ocupada(s) por clientes";
            lblAvisoDetalle.Text = string.Join(" · ",
                abiertos.Select(t => $"{t.EspacioCodigo ?? t.EspacioId.ToString()} ({t.Placa})"));
            panelAviso.IsVisible = true;
            return;
        }

        lblAviso.Text = "";
        lblAvisoDetalle.Text = "";
        panelAviso.IsVisible = false;
    }

    // ================================================================ zonas
    private List<string> Zonas() => _espacios
        .Select(p => p.Zona ?? "")
        .Distinct()
        .OrderBy(z => z, StringComparer.Ordinal)
        .ToList();

    private void ElegirZona()
    {
        var zonas = Zonas();
        if (_ubicarPlaza)
        {
            _ubicarPlaza = false;
            var zonaMia = ZonaDeMiPlaza();
            if (!string.IsNullOrWhiteSpace(zonaMia) && zonas.Contains(zonaMia))
            {
                _zona = zonaMia;
                _tipo = null;
                return;
            }
        }

        if (TodosEstacionados())
        {
            var mias = ZonasMias();
            if (_zona is not null && mias.Contains(_zona)) return;
            _zona = mias.FirstOrDefault() ?? zonas.FirstOrDefault();
            return;
        }

        if (_zona is not null && zonas.Contains(_zona)) return;

        _zona = zonas.FirstOrDefault(z => DeZona(z).Any(p => !p.Ocupado))
                ?? zonas.FirstOrDefault();
    }

    /// <summary>Zona del puesto que el cliente ya tiene, el ticket más reciente.</summary>
    private string? ZonaDeMiPlaza()
    {
        var ticket = MisTicketsAbiertos().OrderByDescending(t => t.Entrada).FirstOrDefault();
        if (ticket is null) return null;

        var plaza = _espacios.FirstOrDefault(p => EsDePlaza(ticket, p));
        var zona = plaza?.Zona ?? ticket.Zona;
        return string.IsNullOrWhiteSpace(zona) ? null : zona;
    }

    private IEnumerable<EspacioDto> DeZona(string? zona)
    {
        var mias = PlazasMias();
        return _espacios
            .Where(p => (p.Zona ?? "") == zona)
            .Where(p => _tipo is null || TipoDe(p) == _tipo || mias.Any(m => m.Id == p.Id));
    }

    /// <summary>Pestañas subrayadas; con ticket abierto solo la zona propia queda activa.</summary>
    private void PintarZonas()
    {
        contenedorZonas.Children.Clear();
        var zonasMias = ZonasMias();
        var bloquear = TodosEstacionados();
        var zonas = Zonas();

        foreach (var zona in zonas)
        {
            var elegida = zona == _zona;
            var esMia = zonasMias.Contains(zona);
            var bloqueada = bloquear && !esMia;
            var libres = DeZona(zona).Count(p => !p.Ocupado);

            var pestana = new VerticalStackLayout
            {
                Spacing = 1,
                MinimumWidthRequest = 64,
                Margin = new Thickness(0, 0, 8, 4),
                Opacity = bloqueada ? 0.35 : 1,
                Children =
                {
                    new Label
                    {
                        Text = (bloqueada ? "🔒 " : "") + NombreCorto(zona),
                        FontFamily = elegida ? "OpenSansSemibold" : null,
                        FontSize = 12,
                        TextColor = elegida ? Amarillo : Gris,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = esMia ? "📍 Tu auto"
                            : bloqueada ? "Bloqueada"
                            : libres == 0 ? "Lleno" : $"{libres} libres",
                        FontSize = 9,
                        TextColor = elegida ? Amarillo : libres == 0 ? Rojo : Gris,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new BoxView
                    {
                        HeightRequest = 3,
                        CornerRadius = 2,
                        Color = elegida ? Amarillo : Borde
                    }
                }
            };

            var toque = new TapGestureRecognizer();
            toque.Tapped += async (_, _) =>
            {
                if (bloqueada)
                {
                    var puestos = string.Join(", ", PlazasMias().Select(p => p.Codigo));
                    await Ui.Informar(this,
                        $"Todos tus vehículos ya están estacionados ({puestos}). " +
                        "Las demás zonas se habilitan cuando cierres un ticket.");
                    return;
                }

                _zona = zona;
                _seleccionada = null;
                Pintar();
            };
            pestana.GestureRecognizers.Add(toque);
            FlexLayout.SetShrink(pestana, 0);
            FlexLayout.SetGrow(pestana, 0);
            contenedorZonas.Children.Add(pestana);
        }
    }

    private static string NombreCorto(string zona) => zona.Split(" - ")[0].Trim();

    private static string Lugar(string zona)
    {
        var partes = zona.Split(" - ");
        return partes.Length > 1 ? partes[1].Trim() : "";
    }

    // ================================================================ tipos
    private void PintarTipos()
    {
        contenedorTipos.Clear();
        contenedorTipos.Add(ChipTipo(null, "Todos"));
        foreach (var tipo in _espacios.Select(TipoDe).Distinct().OrderBy(OrdenTipo))
            contenedorTipos.Add(ChipTipo(tipo, $"{Ui.IconoTipo(tipo)} {NombreTipo(tipo)}"));
    }

    private Border ChipTipo(string? tipo, string texto)
    {
        var elegido = _tipo == tipo;
        var chip = new Border
        {
            BackgroundColor = elegido ? Amarillo : SuperficieAlta,
            Stroke = elegido ? Amarillo : Borde,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 0, 6, 4),
            Content = new Label
            {
                Text = texto,
                FontSize = 11,
                FontFamily = "OpenSansSemibold",
                TextColor = elegido ? Tinta : Claro,
                LineBreakMode = LineBreakMode.NoWrap
            }
        };
        FlexLayout.SetShrink(chip, 0);
        FlexLayout.SetGrow(chip, 0);

        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) =>
        {
            _tipo = tipo;
            _seleccionada = null;
            Pintar();
        };
        chip.GestureRecognizers.Add(toque);
        return chip;
    }

    private static string TipoDe(EspacioDto p) => (p.TipoVehiculo ?? "AUTO").ToUpperInvariant();

    private static int OrdenTipo(string tipo) => tipo switch
    {
        "EMBARAZADA" => 0,
        "DISCAPACIDAD" => 1,
        "AUTO" => 2,
        "CAMIONETA" => 3,
        "MOTO" => 4,
        _ => 5
    };

    /// <summary>Embarazada y discapacidad van al inicio de la grilla.</summary>
    private static int OrdenPuesto(string tipo) => tipo switch
    {
        "EMBARAZADA" => 0,
        "DISCAPACIDAD" => 1,
        _ => 2
    };

    private static string NombreTipo(string tipo) => tipo switch
    {
        "AUTO" => "Autos",
        "CAMIONETA" => "Camionetas",
        "MOTO" => "Motos",
        "DISCAPACIDAD" => "Discapacidad",
        "EMBARAZADA" => "Embarazada",
        _ => tipo
    };

    // ================================================================ plazas
    private void PintarPlazas()
    {
        contenedorPlazas.Clear();

        var idsMias = PlazasMias().Select(p => p.Id).ToHashSet();
        var plazas = DeZona(_zona)
            .OrderBy(p => idsMias.Contains(p.Id) ? 0 : 1)
            .ThenBy(p => OrdenPuesto(TipoDe(p)))
            .ThenBy(p => p.Codigo, StringComparer.Ordinal)
            .ToList();

        var muestra = _espacios.FirstOrDefault(p => p.Zona == _zona);
        lblZonaDetalle.Text = muestra is null ? "" : $"{Lugar(muestra.Zona)} · Nivel {Ui.Nivel(muestra.Nivel)} · ⬆ Entrada";

        var libres = plazas.Count(p => !p.Ocupado);
        var mias = PlazasMias();
        lblLibres.Text = TodosEstacionados() && mias.Count > 0
            ? (mias.Count == 1 ? $"TU PLAZA: {mias[0].Codigo}" : $"TUS PLAZAS: {string.Join(", ", mias.Select(p => p.Codigo))}")
            : mias.Count > 0
                ? $"{mias.Count} estacionado{(mias.Count == 1 ? "" : "s")} · {libres} {(libres == 1 ? "libre" : "libres")}"
                : $"{libres} {(libres == 1 ? "PLAZA LIBRE" : "PLAZAS LIBRES")}";
        PintarSeleccion();

        if (plazas.Count == 0)
        {
            contenedorPlazas.Add(new Label
            {
                Text = "No hay plazas de este tipo en esta zona.",
                FontSize = 11,
                TextColor = Gris,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 8)
            });
            return;
        }

        var abiertos = _tickets.Where(t => t.Abierto).ToList();
        var mios = MisTicketsAbiertos();
        PintarPlano(plazas, abiertos, mios);
        _anchoPintado = Width;
    }

    private void PintarSeleccion()
    {
        if (_sedeEfectiva is not null)
        {
            var vehiculo = VehiculoReserva();
            var placa = vehiculo?.Placa ?? _placaReserva ?? "sin vehículo";
            if (_seleccionada is not null)
                lblSeleccion.Text = $"🤖 {placa} · {_seleccionada.Codigo} · {Ui.Tarifa(_seleccionada.TarifaHora)} · toca para reservar";
            else
                lblSeleccion.Text = vehiculo is null
                    ? "Agrega un vehículo en Autos para reservar"
                    : $"{placa} · toca un puesto libre o usa Asignar por IA";
            btnContinuar.Text = Sesion.EsCliente ? "Ver mis tickets" : "Ver tickets abiertos";
            btnContinuar.IsEnabled = true;
            btnContinuar.BackgroundColor = Borde;
            btnContinuar.TextColor = Claro;
            return;
        }

        if (_seleccionada is not null)
        {
            btnContinuar.Text = "Continuar";
            btnContinuar.IsEnabled = true;
            btnContinuar.BackgroundColor = Amarillo;
            btnContinuar.TextColor = Tinta;
            lblSeleccion.Text = $"Plaza {_seleccionada.Codigo} · {Ui.Tarifa(_seleccionada.TarifaHora)}";
            return;
        }

        if (TodosEstacionados())
        {
            var plazaMia = PlazaMia();
            btnContinuar.Text = "Ver ticket";
            btnContinuar.IsEnabled = true;
            btnContinuar.BackgroundColor = Amarillo;
            btnContinuar.TextColor = Tinta;
            lblSeleccion.Text = plazaMia is null
                ? "Todos tus vehículos están estacionados"
                : $"{NombreCorto(plazaMia.Zona)} · Nivel {Ui.Nivel(plazaMia.Nivel)} · {Lugar(plazaMia.Zona)}";
            return;
        }

        btnContinuar.Text = "Continuar";
        btnContinuar.IsEnabled = false;
        btnContinuar.BackgroundColor = Borde;
        btnContinuar.TextColor = Gris;
        lblSeleccion.Text = MisTicketsAbiertos().Count > 0
            ? "Toca una plaza libre para el otro vehículo"
            : "Toca una plaza libre";
    }


    /// <summary>
    /// Acción al tocar una plaza: avisa si ya no puede estacionar, reserva, selecciona o abre
    /// el ticket. Compartida por la tarjeta grande y por la bahía del plano.
    /// </summary>
    private EventHandler<TappedEventArgs> Toque(EspacioDto plaza, bool ocupada, bool esMia, bool elegida,
        bool bloquearNuevo, TicketDto? ticketMio, TicketDto? ticketOtro, List<TicketDto> miosAbiertos)
    {
        return async (_, _) =>
        {
            if (!ocupada && !esMia && bloquearNuevo)
            {
                var puestos = string.Join(", ", miosAbiertos.Select(t => t.EspacioCodigo ?? t.EspacioId.ToString()));
                await Ui.Informar(this,
                    $"Todos tus vehículos ya están estacionados ({puestos}). Cierra un ticket para elegir otra plaza.");
                return;
            }

            if (!ocupada && !esMia)
            {
                if (_sedeEfectiva is not null)
                {
                    await ReservarAsync(plaza);
                    return;
                }

                _seleccionada = elegida ? null : plaza;
                PintarPlazas();
                return;
            }

            await PulsarPlazaAsync(plaza, esMia, ocupada, ticketMio, ticketOtro);
        };
    }

    // ================================================================ plano del parqueadero
    /// <summary>
    /// Dibuja la zona como un plano visto desde arriba, igual que la señalización del local:
    /// un bloque largo arriba, una isla central, un bloque largo abajo, un bloque corto a la
    /// izquierda, dos carriles de circulación y la calle con la salida.
    ///
    /// No cambia el modelo ni la lógica: recibe las mismas plazas de la zona y cada bahía se
    /// dibuja con <see cref="BahiaPlano"/>, así que seleccionar, reservar y liberar siguen igual.
    /// </summary>
    private void PintarPlano(List<EspacioDto> plazas, List<TicketDto> abiertos, List<TicketDto> mios)
    {
        // Orden estable y predecible: primero las preferenciales, luego por código.
        var ordenadas = plazas
            .OrderBy(p => OrdenPuesto(TipoDe(p)))
            .ThenBy(p => p.Codigo, StringComparer.Ordinal)
            .ToList();

        var bloques = RepartirEnBloques(ordenadas);

        // Datos de la lámina: nivel que se ve y pisos que realmente tiene la sede.
        var niveles = NivelesSede();
        var nivelActual = ordenadas.Count > 0 ? ordenadas[0].Nivel : null;
        var variosNiveles = niveles.Count > 1;

        var lamina = new VerticalStackLayout { Spacing = 7 };

        // 1) Cajetín: identifica el dibujo como en un proyecto arquitectónico.
        lamina.Add(Cajetin(nivelActual, ordenadas.Count, variosNiveles ? niveles : null));

        // 2) Ingreso vehicular: siempre arriba, con la flecha de entrada.
        lamina.Add(IngresoVehicular());

        // 3) Planta: solo el dibujo. Columnas, carriles, cebras y salida abajo.
        lamina.Add(Planta(bloques, abiertos, mios));

        // 4) Salida vehicular: siempre abajo, cerrando el recorrido.
        lamina.Add(SalidaVehicular());

        // 5) Signografía: un botón pequeño que abre la leyenda en un popup.
        lamina.Add(BotonLeyenda(variosNiveles, nivelActual, niveles));

        contenedorPlazas.Add(lamina);
    }

    /// <summary>
    /// Ingreso vehicular: banda superior con la señal «P», el portón y la flecha que indica que
    /// el auto entra bajando por el carril de subida.
    /// </summary>
    private static View IngresoVehicular()
    {
        // Rótulo del portón: la señal «P» y el texto de cómo se entra.
        var rotulo = new Grid { ColumnSpacing = 6 };
        rotulo.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        rotulo.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        rotulo.Add(new Border
        {
            BackgroundColor = Verde,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            Padding = new Thickness(6, 1),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "P",
                FontFamily = "OpenSansSemibold",
                FontSize = 11,
                TextColor = Tinta
            }
        }, 0, 0);
        rotulo.Add(new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "INGRESO VEHICULAR",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 8,
                    CharacterSpacing = 0.8,
                    TextColor = Verde
                },
                new Label
                {
                    Text = "Portón arriba · avanza de norte a sur",
                    FontSize = 6,
                    TextColor = Color.FromArgb("#6F8496")
                }
            }
        }, 1, 0);

        var banda = new Grid { RowSpacing = 3 };
        banda.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        banda.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        banda.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        banda.Add(rotulo, 0, 0);
        banda.Add(BandaFlechas(Verde, 9), 0, 1);
        // Cebra de llegada: marca el umbral del portón, como en la señalética de la calle.
        banda.Add(CebraHorizontal(Color.FromArgb("#B7D8C4")), 0, 2);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#0E1F16"),
            Stroke = Color.FromArgb("#1F6B3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(7, 4),
            Content = banda
        };
    }

    /// <summary>
    /// Flecha de carril pintada en el piso, como la señalética horizontal real: cuerpo sólido
    /// blanco con la punta triangular. Se dibuja con un <see cref="Polygon"/> para que no
    /// dependa de glifos de fuente, que se ven distintos en cada plataforma.
    /// </summary>
    private static View FlechaPiso(bool arriba, double largo = 26, Color? color = null)
    {
        var tinta = color ?? Color.FromArgb("#E8EFF7");
        var ancho = largo * 0.52;

        // Contorno en coordenadas 0..10: vástago estrecho y cabeza triangular.
        PointCollection forma;
        if (arriba)
        {
            forma =
            [
                new Point(5, 0),          // punta
                new Point(10, 3.6),       // hombro derecho
                new Point(6.6, 3.6),
                new Point(6.6, 10),       // base del vástago
                new Point(3.4, 10),
                new Point(3.4, 3.6),
                new Point(0, 3.6)         // hombro izquierdo
            ];
        }
        else
        {
            forma =
            [
                new Point(5, 10),
                new Point(10, 6.4),
                new Point(6.6, 6.4),
                new Point(6.6, 0),
                new Point(3.4, 0),
                new Point(3.4, 6.4),
                new Point(0, 6.4)
            ];
        }

        return new Polygon
        {
            Points = forma,
            Fill = new SolidColorBrush(tinta),
            Stroke = new SolidColorBrush(tinta),
            StrokeThickness = 0,
            WidthRequest = ancho,
            HeightRequest = largo,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
    }
    /// <summary>
    /// Cebra horizontal para las bandas de ingreso y salida: bloques verticales repartidos a
    /// todo lo ancho, como el paso peatonal que cruza la calle en la referencia.
    /// </summary>
    private static View CebraHorizontal(Color color, int cantidad = 14)
    {
        var franja = new Grid
        {
            RowSpacing = 3,
            HorizontalOptions = LayoutOptions.Fill
        };
        franja.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        franja.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        // Línea de tope continua: donde el auto se detiene antes del cruce.
        franja.Add(new BoxView
        {
            Color = color,
            HeightRequest = 1.5,
            HorizontalOptions = LayoutOptions.Fill
        }, 0, 0);

        var bloques = new Grid
        {
            ColumnSpacing = 4,
            HeightRequest = 8,
            HorizontalOptions = LayoutOptions.Fill
        };

        for (var i = 0; i < cantidad; i++)
        {
            bloques.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            bloques.Add(new BoxView
            {
                Color = color,
                HeightRequest = 8,
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            }, i, 0);
        }

        franja.Add(bloques, 0, 1);
        return franja;
    }

    /// <summary>
    /// Fila de flechas pintadas en el piso, a todo lo ancho de la banda: se usa en las bandas
    /// de ingreso y salida. Antes eran glifos «⬇» de fuente; ahora son flechas sólidas, que se
    /// leen como señalética horizontal y no cambian de forma según la plataforma.
    /// </summary>
    private static View BandaFlechas(Color color, int cantidad)
    {
        var banda = new Grid { ColumnSpacing = 0, HorizontalOptions = LayoutOptions.Fill };
        for (var i = 0; i < cantidad; i++)
        {
            banda.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            banda.Add(FlechaPiso(arriba: false, largo: 20, color: color), i, 0);
        }

        return banda;
    }

    /// <summary>
    /// Salida vehicular: banda inferior con la flecha de salida y el acceso peatonal a la calle.
    /// </summary>
    private static View SalidaVehicular()
    {
        var rotulo = new Grid { ColumnSpacing = 6 };
        rotulo.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        rotulo.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        rotulo.Add(new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "SALIDA",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 8,
                    CharacterSpacing = 0.8,
                    TextColor = Amarillo
                },
                new Label
                {
                    Text = "Barrera abajo · acceso peatonal a la calle",
                    FontSize = 6,
                    TextColor = Color.FromArgb("#8A7420")
                }
            }
        }, 0, 0);
        rotulo.Add(new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                new Label { Text = "🚶", FontSize = 11, TextColor = Amarillo },
                new Label { Text = "🚶", FontSize = 11, TextColor = Amarillo }
            }
        }, 1, 0);

        var banda = new Grid { RowSpacing = 3 };
        banda.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        banda.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        banda.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        banda.Add(rotulo, 0, 0);
        banda.Add(BandaFlechas(Amarillo, 9), 0, 1);
        // Cebra de salida: marca el umbral de la barrera, igual que el paso peatonal.
        banda.Add(CebraHorizontal(Color.FromArgb("#D8C68A")), 0, 2);

        return new Border
        {
            BackgroundColor = Color.FromArgb("#2A2410"),
            Stroke = Amarillo,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(7, 4),
            Content = banda
        };
    }

    /// <summary>
    /// Botón pequeño con la leyenda: en vez de ocupar media pantalla con el recuadro de
    /// significados, queda como un botón discreto que abre el detalle en un popup.
    /// </summary>
    private View BotonLeyenda(bool variosNiveles, int? nivelActual, List<int> niveles)
    {
        var boton = new Button
        {
            Text = "ⓘ  Significado de los símbolos",
            BackgroundColor = Superficie,
            TextColor = Gris,
            FontFamily = "OpenSansSemibold",
            FontSize = 9,
            CornerRadius = 4,
            HeightRequest = 34,
            Padding = new Thickness(8, 0)
        };

        // En WinUI el gesto táctil no dispara sobre un Button: se usa su evento Clicked.
        boton.Clicked += (_, _) => MostrarLeyenda(variosNiveles, nivelActual, niveles);
        return boton;
    }

    /// <summary>
    /// Abre la leyenda como capa superpuesta dentro de la misma página: se ve sobre el plano,
    /// oscurece el fondo y se cierra con la ✕ sin salir de la vista.
    /// </summary>
    private void MostrarLeyenda(bool variosNiveles, int? nivelActual, List<int> niveles)
    {
        cuerpoLeyenda.Clear();
        cuerpoLeyenda.Add(LeyendaPlano(variosNiveles, nivelActual, niveles));
        capaLeyenda.IsVisible = true;
    }

    private void OnCerrarLeyendaClicked(object? sender, EventArgs e) => capaLeyenda.IsVisible = false;

    /// <summary>
    /// Cajetín de la lámina: nombre del dibujo, nivel y número de bahías, como el rótulo que
    /// lleva toda planta técnica. Va fuera del dibujo para no tapar ni una plaza.
    /// </summary>
    private static View Cajetin(int? nivelActual, int bahias, List<int>? niveles)
    {
        var izquierda = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = "PLANTA DE PARQUEO",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 8,
                    CharacterSpacing = 1.2,
                    TextColor = Claro
                },
                new Label
                {
                    Text = bahias == 1 ? "1 bahía · vista en planta" : $"{bahias} bahías · vista en planta",
                    FontSize = 6,
                    TextColor = Gris
                }
            }
        };

        var derecha = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = nivelActual is null ? "NIVEL —" : $"NIVEL {Ui.Nivel(nivelActual)}",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 8,
                    CharacterSpacing = 0.6,
                    TextColor = Amarillo,
                    HorizontalTextAlignment = TextAlignment.End
                },
                new Label
                {
                    Text = niveles is null ? "un solo nivel" : $"{niveles.Count} niveles",
                    FontSize = 6,
                    TextColor = Gris,
                    HorizontalTextAlignment = TextAlignment.End
                }
            }
        };

        var fila = new Grid { ColumnSpacing = 6 };
        fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        fila.Add(izquierda, 0, 0);
        fila.Add(derecha, 1, 0);

        var contenido = new VerticalStackLayout { Spacing = 3 };
        contenido.Add(fila);
        contenido.Add(new BoxView { Color = Amarillo, HeightRequest = 1, Opacity = 0.5 });

        return new Border
        {
            BackgroundColor = Superficie,
            Stroke = Borde,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(7, 4),
            Content = contenido
        };
    }

    /// <summary>
    /// La planta propiamente dicha: bloques de bahías, carriles con paso peatonal y la calle
    /// del frente. Se dibuja sobre un marco oscuro con la orientación al norte, y no lleva
    /// leyendas dentro: todo lo explicativo baja al recuadro de signografía.
    /// </summary>
    private View Planta(BloquesPlano bloques, List<TicketDto> abiertos, List<TicketDto> mios)
    {
        var dibujo = new VerticalStackLayout { Spacing = 5 };

        dibujo.Add(EncabezadoPlanta());

        // Zona pequeña: tres tiras verticales, sin linderos laterales.
        if (bloques.Izquierda.Count == 0)
        {
            var compacta = new Grid { ColumnSpacing = 5 };
            compacta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            compacta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            compacta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            compacta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            compacta.Add(TiraVertical(bloques.IslaArriba, abiertos, mios, "FILA 1"), 0, 0);
            compacta.Add(CarrilVertical(pasoPeatonal: true), 1, 0);
            compacta.Add(TiraVertical(bloques.IslaAbajo, abiertos, mios, "FILA 2"), 2, 0);
            compacta.Add(CarrilVertical(pasoPeatonal: true), 3, 0);

            dibujo.Add(compacta);
            return Enmarcar(dibujo);
        }

        // Arreglo de la referencia: lindero | carril | isla doble | carril | lindero.
        var planta = new Grid { ColumnSpacing = 5 };
        planta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); // tira oeste
        planta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); // carril de subida
        planta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); // isla central
        planta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star)); // carril de bajada
        planta.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto)); // tira este

        planta.ColumnDefinitions[0].Width = new GridLength(AnchoBahia);
        planta.ColumnDefinitions[2].Width = new GridLength(AnchoBahia);
        planta.ColumnDefinitions[4].Width = new GridLength(AnchoBahia);

        planta.Add(BloqueVertical(bloques.Izquierda, abiertos, mios, "LINDERO OESTE"), 0, 0);
        planta.Add(CarrilVertical(pasoPeatonal: true), 1, 0);
        planta.Add(IslaCentral(bloques.IslaArriba, bloques.IslaAbajo, abiertos, mios), 2, 0);
        planta.Add(CarrilVertical(pasoPeatonal: true), 3, 0);
        planta.Add(BloqueVertical(bloques.Derecha, abiertos, mios, "LINDERO ESTE"), 4, 0);

        dibujo.Add(planta);

        return Enmarcar(dibujo);
    }

    /// <summary>Marco del dibujo: fondo de plano y borde, igual para todos los arreglos.</summary>
    private static View Enmarcar(View contenido) => new Border
    {
        BackgroundColor = Color.FromArgb("#080C11"),
        Stroke = Borde,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 4 },
        Padding = new Thickness(6, 5),
        Content = contenido
    };

    /// <summary>
    /// Encabezado del dibujo: orientación al norte, sentido de circulación y la señal «P».
    /// Son tres marcas pequeñas, para que el ojo llegue primero a las bahías.
    /// </summary>
    private static View EncabezadoPlanta()
    {
        var fila = new Grid { ColumnSpacing = 5 };
        fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        // Rosa de los vientos simplificada: el norte queda arriba del dibujo.
        var norte = new HorizontalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center };
        norte.Add(new Label
        {
            Text = "▲",
            FontSize = 7,
            TextColor = Color.FromArgb("#64748B"),
            VerticalOptions = LayoutOptions.Center
        });
        norte.Add(new Label
        {
            Text = "N",
            FontFamily = "OpenSansSemibold",
            FontSize = 7,
            TextColor = Color.FromArgb("#64748B"),
            VerticalOptions = LayoutOptions.Center
        });
        fila.Add(norte, 0, 0);

        fila.Add(new Label
        {
            Text = "DOBLE VÍA  ▲  ▼",
            FontSize = 6,
            CharacterSpacing = 0.6,
            TextColor = Color.FromArgb("#4ADE80"),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center
        }, 1, 0);

        fila.Add(new Border
        {
            BackgroundColor = Color.FromArgb("#10251A"),
            Stroke = Color.FromArgb("#1F6B3A"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 2 },
            Padding = new Thickness(5, 0),
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "P",
                FontFamily = "OpenSansSemibold",
                FontSize = 9,
                TextColor = Color.FromArgb("#4ADE80")
            }
        }, 2, 0);

        return fila;
    }

    /// <summary>
    /// Recuadro de signografía al pie de la lámina: cada símbolo del plano, dibujado igual que
    /// en el dibujo, con su significado. Es la única zona con texto explicativo, para que el
    /// mapa quede limpio.
    /// </summary>
    private static View LeyendaPlano(bool variosNiveles, int? nivelActual, List<int> niveles)
    {
        var contenido = new VerticalStackLayout { Spacing = 5 };

        var titulo = new Grid { ColumnSpacing = 6 };
        titulo.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        titulo.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        titulo.Add(new Label
        {
            Text = "SIGNIFICADO DE LOS SÍMBOLOS",
            FontFamily = "OpenSansSemibold",
            FontSize = 6,
            CharacterSpacing = 1,
            TextColor = Gris,
            VerticalOptions = LayoutOptions.Center
        }, 0, 0);

        titulo.Add(new Label
        {
            Text = variosNiveles
                ? $"N-{niveles.Count}  ·  {string.Join(" · ", niveles.Select(n => $"N{n}"))}"
                : "1 nivel",
            FontSize = 6,
            TextColor = Color.FromArgb("#4B5A6B"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center
        }, 1, 0);

        contenido.Add(titulo);
        contenido.Add(new BoxView { Color = Borde, HeightRequest = 1 });

        // Cada fila usa el mismo símbolo que aparece en la planta, no un emoji distinto.
        var filas = new VerticalStackLayout { Spacing = 4 };

        if (variosNiveles)
        {
            filas.Add(FilaLeyenda(
                SimboloRampa(Color.FromArgb("#60A5FA")),
                "Rampa vehicular",
                $"Comunica {string.Join(" y ", niveles.Select(n => $"N{n}"))} en auto"));
        }

        filas.Add(FilaLeyenda(
            SimboloGradas(Color.FromArgb("#A78BFA")),
            "Gradas",
            variosNiveles ? $"Bajada a pie desde N{nivelActual ?? 0}" : "Salida peatonal"));

        filas.Add(FilaLeyenda(
            SimboloAscensor(Color.FromArgb("#22D3EE")),
            "Ascensor",
            "Silla de ruedas, coches de bebé y equipaje"));

        filas.Add(FilaLeyenda(
            CebraMini(),
            "Paso peatonal",
            "Cruce pintado de lado a lado en la doble vía"));

        filas.Add(FilaLeyenda(
            FlechaMini(),
            "Sentido de circulación",
            "Doble vía: una señal por cada sentido en el carril"));

        filas.Add(FilaLeyenda(
            BahiaMini(Verde),
            "Bahía libre",
            "Borde verde: disponible para tu vehículo"));

        filas.Add(FilaLeyenda(
            BahiaMini(AzulOcupada),
            "Bahía ocupada",
            "Azul: otro vehículo está estacionado"));

        filas.Add(FilaLeyenda(
            BahiaMini(Amarillo),
            "Tu bahía",
            "Amarillo: tu vehículo está aquí"));

        contenido.Add(filas);

        return new Border
        {
            BackgroundColor = Superficie,
            Stroke = Borde,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 4 },
            Padding = new Thickness(7, 5),
            Content = contenido
        };
    }

    /// <summary>Miniatura de cebra para la leyenda: las mismas franjas que el plano.</summary>
    private static View CebraMini()
    {
        var cebra = new HorizontalStackLayout { Spacing = 1.5, VerticalOptions = LayoutOptions.Center };
        for (var i = 0; i < 4; i++)
            cebra.Add(new BoxView
            {
                Color = Color.FromArgb("#E8EFF7"),
                WidthRequest = 3,
                HeightRequest = 10
            });
        return cebra;
    }

    /// <summary>Miniatura de flecha de circulación para la leyenda: la misma del plano.</summary>
    private static View FlechaMini() => FlechaPiso(arriba: true, largo: 14);

    /// <summary>Miniatura de bahía con el color de estado que corresponde.</summary>
    private static View BahiaMini(Color color) => new Border
    {
        WidthRequest = 14,
        HeightRequest = 11,
        BackgroundColor = color.WithAlpha(0.15f),
        Stroke = color,
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 2 },
        VerticalOptions = LayoutOptions.Center
    };

    /// <summary>Una fila de la leyenda: el símbolo real, su nombre y la explicación.</summary>
    private static View FilaLeyenda(View simbolo, string nombre, string detalle)
    {
        var fila = new Grid { ColumnSpacing = 6 };
        fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        fila.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        // Caja fija a la izquierda para que todos los símbolos queden alineados.
        var marco = new Grid
        {
            WidthRequest = 30,
            HeightRequest = 18,
            VerticalOptions = LayoutOptions.Center
        };
        marco.Add(simbolo);
        fila.Add(marco, 0, 0);

        var textos = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = nombre,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 7,
                    TextColor = Claro
                },
                new Label
                {
                    Text = detalle,
                    FontSize = 6,
                    TextColor = Color.FromArgb("#6F8496")
                }
            }
        };
        fila.Add(textos, 1, 0);

        return fila;
    }

    private sealed record BloquesPlano(
        List<EspacioDto> Izquierda,
        List<EspacioDto> IslaArriba,
        List<EspacioDto> IslaAbajo,
        List<EspacioDto> Derecha);

    /// <summary>
    /// Reparte las plazas en columnas verticales, como el plano de referencia: una tira contra
    /// cada lindero y la isla central de doble frente en medio.
    ///
    /// Con 30 plazas queda 9 + (6 arriba / 6 abajo) + 9. Las tiras verticales caben en el ancho
    /// de un teléfono, cosa que una fila de 7 bahías no permitiría.
    /// </summary>
    private static BloquesPlano RepartirEnBloques(List<EspacioDto> plazas)
    {
        var total = plazas.Count;
        if (total == 0)
            return new BloquesPlano([], [], [], []);

        // Zona muy pequeña: una sola tira doble en el centro.
        if (total < 10)
        {
            var mitad = (total + 1) / 2;
            return new BloquesPlano([], plazas.Take(mitad).ToList(), plazas.Skip(mitad).ToList(), []);
        }

        // ~30 % por lindero; el resto forma la isla central de doble frente.
        var lateral = Math.Max(3, (int)Math.Round(total * 0.30));
        var isla = total - lateral * 2;
        var islaArriba = (isla + 1) / 2;

        var i = 0;
        List<EspacioDto> Tomar(int n)
        {
            var trozo = plazas.Skip(i).Take(n).ToList();
            i += trozo.Count;
            return trozo;
        }

        return new BloquesPlano(
            Tomar(lateral),
            Tomar(islaArriba),
            Tomar(isla - islaArriba),
            Tomar(lateral));
    }

    /// <summary>Tira de bahías contra un lindero, con el área verde arriba y el jardín abajo.</summary>
    private View BloqueVertical(List<EspacioDto> plazas, List<TicketDto> abiertos, List<TicketDto> mios,
        string rotulo)
    {
        var columna = new VerticalStackLayout
        {
            Spacing = 0,
            WidthRequest = AnchoBahia,
            HorizontalOptions = LayoutOptions.Start
        };
        columna.Add(AreaVerde());

        foreach (var plaza in plazas)
            columna.Add(BahiaPlano(plaza, abiertos, mios));

        var jardinAlEste = rotulo.Contains("ESTE", StringComparison.OrdinalIgnoreCase);
        columna.Add(PieJardinConBicis(jardinAlEste));

        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                Rotulo(rotulo),
                columna
            }
        };
    }

    /// <summary>
    /// Isla central de doble frente: una tira de bahías mirando al carril de subida, la espina
    /// divisoria y otra tira mirando al carril de bajada. Duplica el aforo sin ensanchar el
    /// local, igual que el plano de referencia.
    /// </summary>
    private View IslaCentral(List<EspacioDto> arriba, List<EspacioDto> abajo,
        List<TicketDto> abiertos, List<TicketDto> mios)
    {
        var isla = new VerticalStackLayout
        {
            Spacing = 0,
            WidthRequest = AnchoBahia
        };

        isla.Add(Rotulo("ISLA CENTRAL"));

        foreach (var plaza in arriba)
            isla.Add(BahiaPlano(plaza, abiertos, mios));

        // Espina: el muro que comparten los dos frentes de la isla.
        isla.Add(new BoxView
        {
            Color = Color.FromArgb("#2A3644"),
            HeightRequest = 2,
            Margin = new Thickness(0, 1)
        });

        foreach (var plaza in abajo)
            isla.Add(BahiaPlano(plaza, abiertos, mios));

        return isla;
    }

    /// <summary>
    /// Tira vertical de bahías para el arreglo compacto (zonas con pocas plazas): una columna
    /// estrecha, sin linderos laterales.
    /// </summary>
    private View TiraVertical(List<EspacioDto> plazas, List<TicketDto> abiertos, List<TicketDto> mios,
        string rotulo)
    {
        var columna = new VerticalStackLayout
        {
            Spacing = 0,
            WidthRequest = AnchoBahia,
            HorizontalOptions = LayoutOptions.Center
        };

        foreach (var plaza in plazas)
            columna.Add(BahiaPlano(plaza, abiertos, mios));

        return new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                Rotulo(rotulo),
                columna,
                Cota(plazas.Count)
            }
        };
    }

    /// <summary>
    /// Ilustración de la bahía según su estado, igual que en el modelo original: la plaza vacía
    /// se ve con su isométrico (auto, moto, camioneta, discapacidad o embarazada) y la ocupada
    /// cambia al dibujo del vehículo estacionado. La propia usa la variante amarilla.
    /// </summary>
    private static string Ilustracion(EspacioDto plaza, bool ocupada, bool esMia)
    {
        var tipo = TipoDe(plaza);

        if (esMia)
            return tipo switch
            {
                "MOTO" => "moto_ocupada.png",
                "DISCAPACIDAD" => "auto_mio_discapacidad.png",
                "EMBARAZADA" => "auto_mio_embarazada.png",
                _ => "auto_mio.png"
            };

        if (ocupada)
            return tipo switch
            {
                "MOTO" => "moto_ocupada.png",
                "DISCAPACIDAD" => "auto_ocupado_discapacidad.png",
                "EMBARAZADA" => "auto_ocupado_embarazada.png",
                _ => "auto_ocupado.png"
            };

        return tipo switch
        {
            "DISCAPACIDAD" => "plaza_discapacidad.png",
            "EMBARAZADA" => "plaza_embarazada.png",
            "MOTO" => "plaza_moto.png",
            "CAMIONETA" => "plaza_camioneta.png",
            _ => "plaza_auto.png"
        };
    }

    /// <summary>
    /// Bahía dibujada como en un plano: rectángulo contiguo al de sus vecinas, con el isométrico
    /// de la plaza y el código debajo. El relleno y el borde codifican el estado.
    /// </summary>
    private View BahiaPlano(EspacioDto plaza, List<TicketDto> abiertos, List<TicketDto> mios)
    {
        var ticketMio = mios.FirstOrDefault(t => EsDePlaza(t, plaza));
        var ticketOtro = abiertos.FirstOrDefault(t => t != ticketMio && EsDePlaza(t, plaza) && !mios.Contains(t));

        var esMia = ticketMio is not null;
        var ocupada = plaza.Ocupado || ticketOtro is not null;
        var elegida = _seleccionada?.Id == plaza.Id;
        var bloquearNuevo = TodosEstacionados();

        var color = esMia || elegida ? Amarillo : ocupada ? AzulOcupada : Verde;

        // Isométrico de la plaza: el mismo modelo libre/ocupado de siempre.
        var contenido = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(0, 1),
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Image
                {
                    Source = Ilustracion(plaza, ocupada, esMia),
                    Aspect = Aspect.AspectFit,
                    HeightRequest = 27,
                    HorizontalOptions = LayoutOptions.Center,
                    Opacity = ocupada && !esMia ? 0.75 : 1
                },
                new Label
                {
                    Text = CodigoCorto(plaza.Codigo),
                    FontFamily = "OpenSansSemibold",
                    FontSize = 6,
                    TextColor = esMia || elegida ? Tinta : color,
                    HorizontalTextAlignment = TextAlignment.Center,
                    LineBreakMode = LineBreakMode.NoWrap
                }
            }
        };

        var bahia = new Border
        {
            BackgroundColor = esMia || elegida ? Amarillo : Color.FromArgb("#0D141C"),
            Stroke = color.WithAlpha(esMia || elegida ? 1f : 0.45f),
            StrokeThickness = elegida ? 1.6 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 0 },
            Padding = new Thickness(1, 1),
            WidthRequest = AnchoBahia,
            HeightRequest = AltoBahia,
            Content = contenido
        };

        // Clic: la bahía se comporta igual que la tarjeta grande.
        var toque = new TapGestureRecognizer();
        toque.Tapped += Toque(plaza, ocupada, esMia, elegida, bloquearNuevo, ticketMio, ticketOtro, mios);
        bahia.GestureRecognizers.Add(toque);

        if (esMia && ticketMio is not null)
        {
            var puntero = new PointerGestureRecognizer();
            puntero.PointerEntered += (_, _) => MostrarUbicacionBahia(plaza, ticketMio);
            puntero.PointerExited += (_, _) => OcultarUbicacionBahia();
            bahia.GestureRecognizers.Add(puntero);
            ToolTipProperties.SetText(bahia, TextoTooltipUbicacionBahia(plaza, ticketMio));
        }

        return bahia;
    }

    private void MostrarUbicacionBahia(EspacioDto plaza, TicketDto ticket)
    {
        lblUbicacionBahiaTitulo.Text = $"📍 Plaza {plaza.Codigo} — tu puesto";
        lblUbicacionBahiaDetalle.Text = ArmarTextoUbicacionBahia(plaza, ticket);
        capaUbicacionBahia.IsVisible = true;
    }

    private void OcultarUbicacionBahia() => capaUbicacionBahia.IsVisible = false;

    private string ArmarTextoUbicacionBahia(EspacioDto plaza, TicketDto ticket)
    {
        var sedeNom = _sedeEfectiva?.Nombre ?? plaza.SedeNombre ?? ticket.Sede ?? "Urban Park";
        var dir = _sedeEfectiva?.Direccion ?? plaza.SedeDireccion;
        var zona = plaza.Zona ?? ticket.Zona ?? "";
        var lineaLugar = string.IsNullOrWhiteSpace(zona)
            ? ""
            : $"{NombreCorto(zona)} · Nivel {Ui.Nivel(plaza.Nivel)} · {Lugar(zona)}";

        var lineas = new List<string> { sedeNom.Trim() };
        if (!string.IsNullOrWhiteSpace(dir))
            lineas.Add(dir.Trim());
        if (!string.IsNullOrWhiteSpace(lineaLugar))
            lineas.Add(lineaLugar.Trim());
        lineas.Add($"Placa {ticket.Placa} · Ticket {ticket.Codigo}");
        lineas.Add($"Entrada {ticket.Entrada:dd/MM/yyyy HH:mm}");
        if (!string.IsNullOrWhiteSpace(plaza.Descripcion))
            lineas.Add(plaza.Descripcion.Trim());

        return string.Join(Environment.NewLine, lineas);
    }

    private static string TextoTooltipUbicacionBahia(EspacioDto plaza, TicketDto ticket)
    {
        var zona = plaza.Zona ?? ticket.Zona ?? "";
        var lugar = string.IsNullOrWhiteSpace(zona)
            ? $"Plaza {plaza.Codigo}"
            : $"{plaza.Codigo} · {NombreCorto(zona)} · Nivel {Ui.Nivel(plaza.Nivel)}";
        return $"{lugar} · Placa {ticket.Placa}";
    }

    /// <summary>Ancho de una bahía en el plano: estrecho, para que quepan todos los bloques.</summary>
    private const double AnchoBahia = 46;

    /// <summary>Alto de una bahía: la proporción 2,5 × 5 m de una plaza real, a escala del plano.</summary>
    private const double AltoBahia = 42;

    /// <summary>
    /// Carril vertical de doble vía: cada sentido lleva su flecha, eje discontinuo al centro y
    /// cebras que cruzan todo el ancho de la vía (de lado a lado).
    /// </summary>
    private static View CarrilVertical(bool pasoPeatonal)
    {
        const int colBordeL = 0;
        const int colSube = 1;
        const int colEje = 2;
        const int colBaja = 3;
        const int colBordeR = 4;

        var pista = new Grid { ColumnSpacing = 0, RowSpacing = 0, VerticalOptions = LayoutOptions.Fill };
        pista.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        pista.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        pista.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        pista.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        pista.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

        var asfalto = new BoxView { Color = Color.FromArgb("#11171F") };
        pista.Add(asfalto, colBordeL, 0);
        pista.SetColumnSpan(asfalto, 5);

        var bordeIzq = new BoxView
        {
            Color = Color.FromArgb("#C9D6E4"),
            WidthRequest = 1.5,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill,
            Opacity = 0.8
        };
        var bordeDer = new BoxView
        {
            Color = Color.FromArgb("#C9D6E4"),
            WidthRequest = 1.5,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill,
            Opacity = 0.8
        };
        pista.Add(bordeIzq, colBordeL, 0);
        pista.Add(bordeDer, colBordeR, 0);

        var fila = 0;

        pista.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        pista.Add(new Label
        {
            Text = "▲ SUBE",
            FontSize = 5,
            CharacterSpacing = 0.6,
            TextColor = Color.FromArgb("#9FB3C8"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 4, 0, 2)
        }, colSube, fila);
        pista.Add(new Label
        {
            Text = "BAJA ▼",
            FontSize = 5,
            CharacterSpacing = 0.6,
            TextColor = Color.FromArgb("#9FB3C8"),
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 4, 0, 2)
        }, colBaja, fila);
        fila++;

        const int tramos = 8;
        for (var i = 0; i < tramos; i++)
        {
            if (pasoPeatonal && i == 4)
            {
                pista.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var cebra = CebraPaso();
                pista.Add(cebra, colSube, fila);
                pista.SetColumnSpan(cebra, 3);
                fila++;
            }

            pista.RowDefinitions.Add(new RowDefinition(GridLength.Star));
            pista.Add(FlechaPiso(arriba: true, largo: 28), colSube, fila);
            pista.Add(FlechaPiso(arriba: false, largo: 28), colBaja, fila);
            fila++;
        }

        if (pasoPeatonal)
        {
            pista.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cebra = CebraPaso();
            pista.Add(cebra, colSube, fila);
            pista.SetColumnSpan(cebra, 3);
            fila++;
        }

        var eje = new VerticalStackLayout
        {
            Spacing = 4,
            WidthRequest = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Fill
        };
        for (var i = 0; i < 40; i++)
            eje.Add(new BoxView
            {
                Color = Color.FromArgb("#B9C6D4"),
                WidthRequest = 1.5,
                HeightRequest = 6,
                Opacity = 0.55
            });
        pista.Add(eje, colEje, 1);
        pista.SetRowSpan(eje, Math.Max(1, fila - 1));

        pista.SetRowSpan(asfalto, fila);
        pista.SetRowSpan(bordeIzq, fila);
        pista.SetRowSpan(bordeDer, fila);

        return new Border
        {
            Stroke = Color.FromArgb("#1E2A36"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 2 },
            Padding = 0,
            Content = pista
        };
    }

    /// <summary>
    /// Franja de cebra: bloques blancos anchos que cubren todo el ancho del carril, como el
    /// paso peatonal pintado en el asfalto. Antes eran cuatro barritas finas y no se leía.
    /// </summary>
    private static View CebraPaso()
    {
        var franja = new Grid
        {
            BackgroundColor = Color.FromArgb("#11171F"),
            HeightRequest = 24,
            ColumnSpacing = 4,
            Padding = new Thickness(2, 0),
            HorizontalOptions = LayoutOptions.Fill
        };

        const int bloques = 7;
        for (var i = 0; i < bloques; i++)
        {
            franja.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            franja.Add(new BoxView
            {
                Color = Color.FromArgb("#E8EFF7"),
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Fill
            }, i, 0);
        }

        return franja;
    }

    /// <summary>
    /// Bicicleta pintada en el piso: el pictograma de la ciclovía. Se dibuja como trazo
    /// vectorial (dos ruedas y el cuadro) para que se lea igual en Windows y en Android,
    /// sin depender de un emoji que cambia de forma en cada plataforma.
    /// </summary>
    private static View BiciPiso(double alto = 13, Color? color = null)
    {
        const string cuadro =
            "M 1,17 A 5,5 0 1 0 11,17 A 5,5 0 1 0 1,17 " +   // rueda trasera
            "M 17,17 A 5,5 0 1 0 27,17 A 5,5 0 1 0 17,17 " +  // rueda delantera
            "M 6,17 L 11,8 L 14,17 Z " +                      // triángulo trasero
            "M 14,17 L 21,9 L 6,17 " +                        // tubo diagonal
            "M 21,9 L 22,17 " +                               // horquilla
            "M 9,7 L 13,7 " +                                 // silla
            "M 19,7 L 24,7 M 21,9 L 21,7";                    // manubrio

        var tinta = color ?? Color.FromArgb("#E8EFF7");
        var geometria = (Geometry)new PathGeometryConverter().ConvertFromInvariantString(cuadro)!;

        return new Microsoft.Maui.Controls.Shapes.Path
        {
            Data = geometria,
            Stroke = new SolidColorBrush(tinta),
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.Uniform,
            WidthRequest = alto * 1.15,
            HeightRequest = alto,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
    }

    /// <summary>
    /// Pie del lindero: el jardín y, al lado, el hueco con bicicletas pintadas en el piso.
    /// </summary>
    private static View PieJardinConBicis(bool jardinAlEste)
    {
        var pie = new Grid
        {
            ColumnSpacing = 2,
            HeightRequest = 44
        };
        pie.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        pie.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.15, GridUnitType.Star)));

        if (jardinAlEste)
        {
            pie.Add(ZonaBicisJardin(), 0, 0);
            pie.Add(Jardin(), 1, 0);
        }
        else
        {
            pie.Add(Jardin(), 0, 0);
            pie.Add(ZonaBicisJardin(), 1, 0);
        }

        return pie;
    }

    /// <summary>Espacio junto al jardín: punteado y pictogramas de bici en el piso.</summary>
    private static View ZonaBicisJardin()
    {
        var zona = new Border
        {
            BackgroundColor = Color.FromArgb("#0E1419"),
            Stroke = Color.FromArgb("#2A3644"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            Padding = new Thickness(2, 2),
            InputTransparent = true
        };

        var fila = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        foreach (var _ in new[] { 1, 2, 3 })
            fila.Add(BiciPiso(11));

        zona.Content = fila;
        return zona;
    }

    private static Label Rotulo(string texto) => new()
    {
        Text = texto,
        FontSize = 7,
        CharacterSpacing = 0.8,
        TextColor = Gris,
        Margin = new Thickness(1, 0, 0, 0)
    };


    /// <summary>Referencia visual del lindero, como en el plano del local.</summary>
    private static View AreaVerde() => new Border
    {
        BackgroundColor = Color.FromArgb("#14361F"),
        Stroke = Color.FromArgb("#1F6B3A"),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 3 },
        Padding = new Thickness(2, 6),
        Content = new Label
        {
            Text = "🌿\nÁREA\nVERDE",
            FontSize = 6,
            CharacterSpacing = 0.4,
            TextColor = Color.FromArgb("#4ADE80"),
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap
        }
    };

    private static View Jardin() => new Border
    {
        BackgroundColor = Color.FromArgb("#14361F"),
        Stroke = Color.FromArgb("#1F6B3A"),
        StrokeThickness = 1,
        StrokeShape = new RoundRectangle { CornerRadius = 3 },
        Padding = new Thickness(2, 3),
        Content = new Label
        {
            Text = "JARDÍN",
            FontSize = 6,
            CharacterSpacing = 0.4,
            TextColor = Color.FromArgb("#4ADE80"),
            HorizontalTextAlignment = TextAlignment.Center
        }
    };

    /// <summary>
    /// Niveles de la sede según los datos de sus plazas (sin inventar pisos que no existen).
    /// </summary>
    private List<int> NivelesSede() => _espacios
        .Where(p => p.Nivel is not null)
        .Select(p => p.Nivel!.Value)
        .Distinct()
        .OrderBy(n => n)
        .ToList();

    /// <summary>
    /// Cota del bloque: la longitud dibujada, como en un plano. Se deriva del número de bahías,
    /// no es un dato inventado.
    /// </summary>
    private static Label Cota(int bahias)
    {
        var metros = bahias * 2.5;
        return new Label
        {
            Text = $"‹────  {metros:0.#} m  ────›",
            FontSize = 5,
            CharacterSpacing = 0.3,
            TextColor = Color.FromArgb("#3E4C5C"),
            HorizontalTextAlignment = TextAlignment.Center
        };
    }

    /// <summary>Símbolo de rampa en planta: franja inclinada con chevrones de ascenso.</summary>
    private static View SimboloRampa(Color color)
    {
        var franja = new Grid { WidthRequest = 26, HeightRequest = 16 };
        franja.Add(new BoxView
        {
            Color = color.WithAlpha(0.18f),
            CornerRadius = 2,
            HeightRequest = 16
        });

        var chevrones = new HorizontalStackLayout
        {
            Spacing = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        for (var i = 0; i < 3; i++)
            chevrones.Add(new Label
            {
                Text = "❯",
                FontSize = 7,
                TextColor = color,
                VerticalOptions = LayoutOptions.Center
            });
        franja.Add(chevrones);
        return franja;
    }

    /// <summary>Símbolo de gradas en planta: peldaños escalonados.</summary>
    private static View SimboloGradas(Color color)
    {
        var peldaños = new VerticalStackLayout
        {
            WidthRequest = 26,
            HeightRequest = 16,
            Spacing = 1.5,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        for (var i = 0; i < 4; i++)
            peldaños.Add(new BoxView
            {
                Color = color.WithAlpha(0.35f + i * 0.15f),
                WidthRequest = 22 - i * 4,
                HeightRequest = 1.6,
                HorizontalOptions = LayoutOptions.Center
            });
        return peldaños;
    }

    /// <summary>Símbolo de ascensor en planta: caja del montacargas con puertas.</summary>
    private static View SimboloAscensor(Color color)
    {
        var caja = new Grid { WidthRequest = 20, HeightRequest = 16 };
        caja.Add(new Border
        {
            Stroke = color,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 2 },
            HeightRequest = 16,
            BackgroundColor = color.WithAlpha(0.14f)
        });

        // Cruz diagonal clásica del plano de ascensor.
        caja.Add(new BoxView
        {
            Color = color.WithAlpha(0.45f),
            WidthRequest = 1,
            HeightRequest = 18,
            Rotation = 38,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        });
        caja.Add(new BoxView
        {
            Color = color.WithAlpha(0.45f),
            WidthRequest = 1,
            HeightRequest = 18,
            Rotation = -38,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        });

        // Puertas.
        caja.Add(new BoxView
        {
            Color = color,
            WidthRequest = 6,
            HeightRequest = 2,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End
        });
        return caja;
    }


    /// <summary>PLA-A-16 → A-16: en el plano el prefijo de la sede se sobreentiende.</summary>
    private static string CodigoCorto(string codigo)
    {
        var partes = (codigo ?? "").Split('-');
        return partes.Length >= 3 ? $"{partes[^2]}-{partes[^1]}" : codigo ?? "";
    }

    private async Task PulsarPlazaAsync(EspacioDto plaza, bool esMia, bool ocupada, TicketDto? ticketMio, TicketDto? ticketOtro)
    {
        if (!ocupada)
        {
            if (Sesion.RolPrincipal == "OPERADOR")
            {
                await Ui.Informar(this, $"Plaza {plaza.Codigo} libre. La asignación la hace el cliente con IA.");
                return;
            }

            await Navigation.PushAsync(new vIa(plaza));
            return;
        }

        if (esMia)
        {
            await Navigation.PushAsync(new vTickets());
            return;
        }

        if (ticketOtro is not null || !Sesion.EsOperador)
        {
            var detalle = ticketOtro is null
                ? $"La plaza {plaza.Codigo} está ocupada."
                : $"La plaza {plaza.Codigo} está ocupada · {ticketOtro.Placa}.";
            await Ui.Aviso(this, detalle, "Plaza ocupada");
            return;
        }

        await LiberarAsync(plaza.Id, plaza.Codigo);
    }

    private static bool EsDePlaza(TicketDto ticket, EspacioDto plaza) =>
        ticket.EspacioId == plaza.Id ||
        (!string.IsNullOrWhiteSpace(ticket.EspacioCodigo) && ticket.EspacioCodigo == plaza.Codigo);

    // ================================================================ liberar
    private async Task LiberarAsync(long id, string codigo)
    {
        if (!await Ui.Confirmar(this, "Liberar plaza",
                $"¿Marcar {codigo} como libre? Úsalo si no hay ticket abierto (plaza atascada)."))
            return;

        try
        {
            await ApiCliente.LiberarAsync(id);
            await Ui.Exito(this, $"Plaza {codigo} liberada");
            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }
}
