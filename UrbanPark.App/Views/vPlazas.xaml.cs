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
        };
    }

    /// <summary>Plazas de un parqueadero concreto: al tocar un puesto libre se reserva y sale el QR.</summary>
    public vPlazas(SedeDto sede) : this(null, null)
    {
        _sede = sede;
        panelSede.IsVisible = true;
        panelVehiculo.IsVisible = true;
        lblSedeNombre.Text = sede.Nombre;
        lblSedeDireccion.Text = $"📍 {sede.Direccion} · {sede.Horario}";
        Title = sede.Nombre;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _ubicarPlaza = true;
        await CargarAsync();
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
        if (_placaReserva is not null && activos.Any(v => v.Placa == _placaReserva)) return;

        // Prefiere un vehículo que todavía no esté estacionado y del tipo que se está mirando.
        var estacionadas = MisTicketsAbiertos().Select(t => t.Placa).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _placaReserva = activos.FirstOrDefault(v => !estacionadas.Contains(v.Placa) &&
                                                    (_tipo is null || string.Equals(v.Tipo, _tipo, StringComparison.OrdinalIgnoreCase)))?.Placa
                        ?? activos.FirstOrDefault(v => !estacionadas.Contains(v.Placa))?.Placa
                        ?? activos.FirstOrDefault()?.Placa;
    }

    private void PintarVehiculos()
    {
        if (_sedeEfectiva is null) return;

        contenedorVehiculos.Children.Clear();
        var activos = _vehiculos.Where(v => v.Activo).ToList();

        if (activos.Count == 0)
        {
            contenedorVehiculos.Add(new Label
            {
                Text = "No tienes vehículos activos: agrégalos en la pestaña Autos.",
                FontSize = 11,
                TextColor = Rojo
            });
            return;
        }

        foreach (var vehiculo in activos)
        {
            var elegido = vehiculo.Placa == _placaReserva;
            var chip = new Border
            {
                BackgroundColor = elegido ? Amarillo : SuperficieAlta,
                Stroke = elegido ? Amarillo : Borde,
                StrokeThickness = 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                Padding = new Thickness(9, 4),
                Margin = new Thickness(0, 0, 6, 4),
                Content = new Label
                {
                    Text = $"{Ui.IconoTipo(vehiculo.Tipo)} {vehiculo.Placa}",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    TextColor = elegido ? Tinta : Claro,
                    LineBreakMode = LineBreakMode.NoWrap
                }
            };
            FlexLayout.SetShrink(chip, 0);
            FlexLayout.SetGrow(chip, 0);

            var placa = vehiculo.Placa;
            var toque = new TapGestureRecognizer();
            toque.Tapped += (_, _) =>
            {
                _placaReserva = placa;
                PintarVehiculos();
                PintarSeleccion();
            };
            chip.GestureRecognizers.Add(toque);
            contenedorVehiculos.Children.Add(chip);
        }
    }

    private VehiculoDto? VehiculoReserva() =>
        _vehiculos.FirstOrDefault(v => string.Equals(v.Placa, _placaReserva, StringComparison.OrdinalIgnoreCase));

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
                (!string.IsNullOrWhiteSpace(t.Usuario) && t.Usuario == Sesion.Username) ||
                (placas.Count > 0 && placas.Contains(t.Placa)) ||
                (placas.Count == 0 && Sesion.EsCliente &&
                 (string.IsNullOrWhiteSpace(t.Usuario) || t.Usuario == Sesion.Username)))
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
        const int columnas = 3;
        var filas = (plazas.Count + columnas - 1) / columnas;
        var grilla = new Grid { ColumnSpacing = 6, RowSpacing = 6 };
        for (var c = 0; c < columnas; c++)
            grilla.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        for (var f = 0; f < filas; f++)
            grilla.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        for (var i = 0; i < plazas.Count; i++)
            grilla.Add(TarjetaPlaza(plazas[i], abiertos, mios), i % columnas, i / columnas);

        contenedorPlazas.Add(grilla);
        _anchoPintado = Width;
    }

    private void PintarSeleccion()
    {
        if (_sedeEfectiva is not null)
        {
            // En una sede el toque reserva al instante: el botón solo explica cómo funciona.
            var vehiculo = VehiculoReserva();
            var placa = vehiculo?.Placa ?? _placaReserva ?? "sin vehículo";
            lblSeleccion.Text = vehiculo is null
                ? "Agrega un vehículo en Autos para reservar"
                : $"{placa} · toca un puesto libre y tu QR sale al instante";
            btnContinuar.Text = "Ver mis tickets";
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

    private static string Foto(EspacioDto plaza, bool ocupada, bool esMia)
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

    /// <summary>Tarjeta compacta para la grilla de 3 columnas: foto arriba y datos abajo.</summary>
    private View TarjetaPlaza(EspacioDto plaza, List<TicketDto> abiertos, List<TicketDto> mios)
    {
        var ticketMio = mios.FirstOrDefault(t => EsDePlaza(t, plaza));
        var ticketOtro = abiertos.FirstOrDefault(t => t != ticketMio && EsDePlaza(t, plaza) && !mios.Contains(t));

        var esMia = ticketMio is not null;
        var ocupada = plaza.Ocupado || ticketOtro is not null;
        var elegida = _seleccionada?.Id == plaza.Id;
        var bloquearNuevo = TodosEstacionados();

        var (estado, colorEstado) = esMia ? ("Tuyo", Amarillo)
            : ocupada ? ("Ocupada", AzulOcupada)
            : elegida ? ("Elegida", Amarillo)
            : ("Libre", Verde);

        var extra = esMia ? (ticketMio!.Placa ?? "")
            : ocupada ? ticketOtro?.Placa ?? ""
            : $"{Ui.IconoTipo(plaza.TipoVehiculo)} {Ui.Tarifa(plaza.TarifaHora)}";

        var anchoCol = Width > 0 ? Math.Max(90, (Width - 32) / 3.0) : 110;
        var altoFoto = Math.Clamp(anchoCol * 0.72, 56, 84);

        var foto = new Grid
        {
            HeightRequest = altoFoto,
            Children =
            {
                new Image
                {
                    Source = Foto(plaza, ocupada, esMia),
                    Aspect = Aspect.AspectFit,
                    HeightRequest = altoFoto,
                    HorizontalOptions = LayoutOptions.Fill
                }
            }
        };

        if (esMia)
        {
            foto.Add(new Border
            {
                BackgroundColor = Amarillo,
                StrokeThickness = 0,
                StrokeShape = new RoundRectangle { CornerRadius = 6 },
                Padding = new Thickness(5, 1),
                Margin = new Thickness(4),
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Start,
                Content = new Label
                {
                    Text = "Tuyo",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 8,
                    TextColor = Tinta
                }
            });
        }

        var datos = new VerticalStackLayout
        {
            Spacing = 1,
            Padding = new Thickness(6, 4, 6, 6),
            Children =
            {
                new Label
                {
                    Text = plaza.Codigo,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 13,
                    TextColor = esMia ? Tinta : Claro,
                    LineBreakMode = LineBreakMode.NoWrap
                },
                new HorizontalStackLayout
                {
                    Spacing = 4,
                    Children =
                    {
                        new BoxView
                        {
                            Color = esMia ? Tinta : colorEstado,
                            WidthRequest = 6,
                            HeightRequest = 6,
                            CornerRadius = 3,
                            VerticalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = estado,
                            FontSize = 9,
                            TextColor = esMia ? Tinta : colorEstado
                        }
                    }
                },
                new Label
                {
                    Text = extra,
                    FontSize = 9,
                    TextColor = esMia ? Color.FromArgb("#3D3307") : Gris,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    IsVisible = !string.IsNullOrWhiteSpace(extra)
                }
            }
        };

        var tarjeta = new Border
        {
            BackgroundColor = esMia ? Amarillo : Superficie,
            Stroke = esMia ? Color.FromArgb("#D4A50F") : elegida ? Amarillo : Borde,
            StrokeThickness = elegida ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 0,
            Opacity = bloquearNuevo && !esMia ? 0.4 : 1,
            Content = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Fondo,
                        StrokeThickness = 0,
                        StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(8, 8, 0, 0) },
                        Padding = 0,
                        Content = foto
                    },
                    datos
                }
            }
        };

        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) =>
        {
            if (!ocupada && !esMia && bloquearNuevo)
            {
                var puestos = string.Join(", ", mios.Select(t => t.EspacioCodigo ?? t.EspacioId.ToString()));
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
        tarjeta.GestureRecognizers.Add(toque);
        return tarjeta;
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
