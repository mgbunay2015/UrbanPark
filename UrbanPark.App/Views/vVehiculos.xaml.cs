using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Vehículos / Mis vehículos (spec §3.3). Alta en esta pantalla.
/// El listado vive en vRegistrados.
/// </summary>
public partial class vVehiculos : ContentPage
{
    private static readonly (string Nombre, string Icono)[] Perfiles =
    [
        ("Hombre", "👨"),
        ("Mujer", "👩"),
        ("Tercera Edad", "👴"),
        ("Capacidades Especiales", "♿"),
        ("Embarazada", "🤰"),
        ("Estudiante", "🎓")
    ];

    private static readonly string[] Tipos = ["AUTO", "MOTO", "CAMIONETA"];

    private static readonly Dictionary<string, (string Nombre, string Icono, string[] Modelos)[]> Catalogo = new()
    {
        ["AUTO"] =
        [
            ("Chevrolet", "marca_chevrolet.png", ["Spark", "Aveo", "Sail", "Onix", "Cavalier", "Groove", "Tracker", "Equinox", "Captiva", "Tahoe"]),
            ("Fiat", "marca_fiat.png", ["Mobi", "Uno", "Argo", "Cronos", "Pulse", "Fastback", "Strada", "Toro", "500"]),
            ("Ford", "marca_ford.png", ["Ka", "Fiesta", "Focus", "EcoSport", "Escape", "Territory", "Edge", "Explorer", "Mustang"]),
            ("Honda", "marca_honda.png", ["Fit", "City", "Civic", "Accord", "WR-V", "HR-V", "BR-V", "CR-V", "Pilot"]),
            ("Hyundai", "marca_hyundai.png", ["Grand i10", "Accent", "Venue", "Elantra", "Sonata", "Creta", "Tucson", "Santa Fe", "Palisade"]),
            ("Jeep", "marca_jeep.png", ["Renegade", "Compass", "Cherokee", "Commander", "Grand Cherokee", "Wrangler", "Gladiator"]),
            ("Kia", "marca_kia.png", ["Picanto", "Soluto", "Rio", "Cerato", "Stonic", "Seltos", "Sportage", "Sorento", "Carnival"]),
            ("Mazda", "marca_mazda.png", ["2", "3", "6", "MX-5", "CX-3", "CX-30", "CX-5", "CX-9"]),
            ("Mitsubishi", "marca_mitsubishi.png", ["Mirage", "Lancer", "Xpander", "ASX", "Eclipse Cross", "Outlander", "Montero"]),
            ("Nissan", "marca_nissan.png", ["March", "Versa", "Sentra", "Altima", "Kicks", "Qashqai", "X-Trail", "Pathfinder"]),
            ("Peugeot", "marca_peugeot.png", ["208", "301", "308", "408", "2008", "3008", "5008", "Partner"]),
            ("Renault", "marca_renault.png", ["Kwid", "Logan", "Sandero", "Stepway", "Kardian", "Duster", "Captur", "Koleos", "Oroch"]),
            ("Suzuki", "marca_suzuki.png", ["Alto", "S-Presso", "Swift", "Baleno", "Dzire", "Ciaz", "Ertiga", "Vitara", "Jimny"]),
            ("Toyota", "marca_toyota.png", ["Yaris", "Corolla", "Camry", "Rush", "RAV4", "Fortuner", "Prado", "Land Cruiser"]),
            ("Volkswagen", "marca_volkswagen.png", ["Gol", "Polo", "Virtus", "Jetta", "Nivus", "T-Cross", "Taos", "Tiguan"]),
            ("Otra", "", [])
        ],
        ["MOTO"] =
        [
            ("Honda", "marca_honda.png", ["Wave", "CB 125", "XR 150", "PCX"]),
            ("Yamaha", "", ["Crypton", "FZ 150", "NMAX", "MT-03"]),
            ("Suzuki", "marca_suzuki.png", ["GN 125", "Gixxer", "Burgman", "V-Strom"]),
            ("Kawasaki", "", ["Ninja 400", "Versys", "KLX", "Z400"]),
            ("Bajaj", "", ["Boxer", "Pulsar", "Dominar", "Discover"]),
            ("AKT", "", ["NKD", "CR4", "Dynamic", "TT"]),
            ("KTM", "", ["Duke 200", "Duke 390", "Adventure", "RC"]),
            ("TVS", "", ["Apache", "Sport", "Ntorq", "Raider"]),
            ("Italika", "", ["FT 150", "DM 150", "Vort-X", "WS"]),
            ("Benelli", "", ["TNT 150", "TRK", "180S", "Leoncino"]),
            ("Otra", "", [])
        ],
        ["CAMIONETA"] =
        [
            ("Chevrolet", "marca_chevrolet.png", ["D-Max", "Colorado", "S10", "Trailblazer", "Captiva", "Tahoe", "Suburban"]),
            ("Ford", "marca_ford.png", ["Ranger", "F-150", "Bronco", "Explorer", "Everest", "Expedition"]),
            ("Honda", "marca_honda.png", ["CR-V", "Pilot", "Passport", "Ridgeline"]),
            ("Hyundai", "marca_hyundai.png", ["Creta", "Tucson", "Santa Fe", "Palisade"]),
            ("Kia", "marca_kia.png", ["Seltos", "Sportage", "Sorento", "Carnival", "Mohave"]),
            ("Mazda", "marca_mazda.png", ["CX-5", "CX-9", "CX-50", "BT-50"]),
            ("Nissan", "marca_nissan.png", ["Frontier", "Navara", "X-Trail", "Pathfinder", "Armada"]),
            ("Suzuki", "marca_suzuki.png", ["Jimny", "Grand Vitara", "XL7", "Ertiga"]),
            ("Toyota", "marca_toyota.png", ["Hilux", "Fortuner", "Land Cruiser", "Prado", "4Runner", "Tundra"]),
            ("Volkswagen", "marca_volkswagen.png", ["Amarok", "Saveiro", "Tiguan", "Teramont", "Taos"]),
            ("Otra", "", [])
        ]
    };

    private static readonly Dictionary<string, (string Nombre, string Icono, string[] Modelos)[]> CatalogoExtra = new()
    {
        ["AUTO"] =
        [
            ("Audi", "marca_audi.png", ["A1", "A3", "A4", "A6", "Q3", "Q5", "Q7"]),
            ("BMW", "marca_bmw.png", ["Serie 1", "Serie 3", "Serie 5", "X1", "X3", "X5"]),
            ("Chery", "", ["QQ", "Arrizo 5", "Tiggo 2", "Tiggo 4", "Tiggo 7", "Tiggo 8"]),
            ("Citroen", "marca_citroen.png", ["C3", "C4", "C-Elysee", "Berlingo", "C5 Aircross"]),
            ("Mercedes", "marca_mercedes.png", ["Clase A", "Clase C", "Clase E", "GLA", "GLC", "GLE"]),
            ("MG", "", ["MG3", "MG5", "ZS", "HS", "RX5"]),
            ("Seat", "marca_seat.png", ["Ibiza", "Leon", "Arona", "Ateca", "Tarraco"]),
            ("Subaru", "marca_subaru.png", ["Impreza", "XV", "Forester", "Outback", "WRX"])
        ]
    };

    private static readonly HashSet<string> PopularesAuto = new(StringComparer.OrdinalIgnoreCase)
    {
        "Chevrolet", "Chery", "Hyundai", "Kia", "Mazda", "Nissan", "Renault", "Suzuki", "Toyota", "Volkswagen"
    };

    private bool _masMarcas;

    private bool _ajustandoPlaca;

    private void OnPlacaChanged(object? sender, TextChangedEventArgs e)
    {
        if (_ajustandoPlaca) return;

        var limpio = new string((e.NewTextValue ?? "")
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .Take(8)
            .ToArray());
        var texto = limpio.Length <= 3 ? limpio : limpio[..3] + "-" + limpio[3..];
        if (texto == (txtPlaca.Text ?? "")) return;

        _ajustandoPlaca = true;
        txtPlaca.Text = texto;
        txtPlaca.CursorPosition = texto.Length;
        _ajustandoPlaca = false;

        ProgramarConsultaAutomatica(texto);
    }

    /// <summary>
    /// Al terminar de escribir la placa se pide la ficha sola (sin pulsar el botón).
    /// La espera corta evita consultar en cada tecla y no gasta créditos dos veces por la misma placa.
    /// </summary>
    private void ProgramarConsultaAutomatica(string placa)
    {
        _esperaConsulta?.Cancel();
        if (!Ui.PlacaValida(placa)) return;
        if (string.Equals(placa, _ultimaPlacaConsultada, StringComparison.OrdinalIgnoreCase)) return;

        _esperaConsulta = new CancellationTokenSource();
        var token = _esperaConsulta.Token;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(700), async () =>
        {
            if (token.IsCancellationRequested) return;
            if (!string.Equals((txtPlaca.Text ?? "").Trim(), placa, StringComparison.OrdinalIgnoreCase)) return;
            await ConsultarPlacaAsync(placa, automatica: true);
        });
    }

    private static readonly (string Nombre, string Hex)[] Colores =
    [
        ("Blanco", "#F8FAFC"),
        ("Negro", "#111827"),
        ("Gris", "#94A3B8"),
        ("Plata", "#CBD5E1"),
        ("Rojo", "#EF4444"),
        ("Azul", "#3B82F6"),
        ("Verde", "#22C55E"),
        ("Amarillo", "#F2C51A"),
        ("Naranja", "#F97316"),
        ("Café", "#92400E")
    ];

    /// <summary>Placa que vuelve desde Registrados con "Usar placa".</summary>
    public static string? PlacaPendiente;

    private string _tipo = "AUTO";
    private string? _marca;
    private string? _color;
    private string? _modelo;
    private string? _perfil;
    private string? _anioConsulta;

    /// <summary>Ficha de EcuadorAPI para la placa escrita; null si no se ha consultado.</summary>
    private ConsultaVehiculoDto? _consulta;

    private CancellationTokenSource? _esperaConsulta;
    private string? _ultimaPlacaConsultada;

    public vVehiculos()
    {
        InitializeComponent();

        lblTitulo.Text = Sesion.EsCliente ? "Mis vehículos" : "Vehículos";
        lblPista.IsVisible = Sesion.EsCliente;
        Title = "Mis Vehículos";
        PintarSelectores();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (string.IsNullOrWhiteSpace(PlacaPendiente)) return;
        txtPlaca.Text = PlacaPendiente;
        PlacaPendiente = null;
    }

    private async void OnRegistradosClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new vRegistrados());

    // ================================================================ consulta de placa (EcuadorAPI)
    /// <summary>
    /// Con la placa escrita se piden marca, modelo, año y color. Todo queda seleccionado en la
    /// pantalla; si la consulta falla se avisa y el registro manual sigue disponible.
    /// </summary>
    private async void OnConsultarPlacaClicked(object? sender, EventArgs e)
    {
        var placa = (txtPlaca.Text ?? "").Trim().ToUpperInvariant();
        if (!Ui.PlacaValida(placa))
        {
            await Ui.Error(this, "Escribe una placa válida antes de consultar. Ej: ABC-1234");
            return;
        }

        await ConsultarPlacaAsync(placa, automatica: false);
    }

    private async Task ConsultarPlacaAsync(string placa, bool automatica)
    {
        btnConsultarPlaca.IsEnabled = false;
        var textoPrevio = btnConsultarPlaca.Text;
        btnConsultarPlaca.Text = "Buscando…";
        try
        {
            var ficha = await ApiCliente.ConsultarPlacaAsync(placa);
            _consulta = ficha.Encontrado ? ficha : null;
            _ultimaPlacaConsultada = ficha.Encontrado ? placa : null;

            if (!ficha.Encontrado)
            {
                panelConsulta.IsVisible = true;
                panelMatricula.IsVisible = false;
                contenedorMatricula.Children.Clear();
                lblConsultaTitulo.Text = $"Sin datos para {PlacaEcuador.Formatear(placa)}";
                lblConsultaTitulo.TextColor = Ui.ColorDe("Advertencia");
                lblConsultaDetalle.Text = ficha.Error ?? "Escribe la marca, el modelo y el color a mano.";
                btnMatricula.IsVisible = true;
                return;
            }

            AplicarFicha(ficha);
        }
        catch (Exception ex)
        {
            panelConsulta.IsVisible = true;
            lblConsultaTitulo.Text = "No se pudo consultar";
            lblConsultaTitulo.TextColor = Ui.ColorDe("Peligro");
            lblConsultaDetalle.Text = ex is ApiExcepcion ? ex.Message : "Revisa la conexión e intenta de nuevo.";
        }
        finally
        {
            btnConsultarPlaca.IsEnabled = true;
            btnConsultarPlaca.Text = textoPrevio;
        }
    }

    private async void OnCerrarConsultaTapped(object? sender, TappedEventArgs e)
    {
        panelConsulta.IsVisible = false;
        panelMatricula.IsVisible = false;
        contenedorMatricula.Children.Clear();
        await Task.CompletedTask;
    }

    /// <summary>Matrícula registrada (año, última matrícula, caducidad y entidad).</summary>
    private async void OnMatriculaClicked(object? sender, EventArgs e)
    {
        if (panelMatricula.IsVisible)
        {
            panelMatricula.IsVisible = false;
            return;
        }

        var placa = (txtPlaca.Text ?? "").Trim().ToUpperInvariant();
        if (!Ui.PlacaValida(placa))
        {
            await Ui.Error(this, "Escribe una placa válida para consultar su matrícula. Ej: ABC-1234");
            return;
        }

        btnMatricula.IsEnabled = false;
        btnMatricula.Text = "Consultando…";
        try
        {
            var m = await ApiCliente.ConsultarMatriculaAsync(placa);
            PintarMatricula(m);
        }
        catch (Exception ex)
        {
            contenedorMatricula.Children.Clear();
            contenedorMatricula.Children.Add(LineaMatricula("No se pudo consultar",
                ex is ApiExcepcion ? ex.Message : "Revisa la conexión e intenta de nuevo.", Ui.ColorDe("Peligro")));
            panelMatricula.IsVisible = true;
        }
        finally
        {
            btnMatricula.IsEnabled = true;
            btnMatricula.Text = "📋 Ver matrícula registrada";
        }
    }

    private void PintarMatricula(MatriculaVehiculoDto m)
    {
        contenedorMatricula.Children.Clear();
        panelMatricula.IsVisible = true;

        if (!m.Encontrado)
        {
            contenedorMatricula.Children.Add(LineaMatricula("Sin matrícula",
                m.Error ?? "EcuadorAPI no devolvió la matrícula de esa placa.", Ui.ColorDe("Advertencia")));
            return;
        }

        contenedorMatricula.Children.Add(LineaMatricula("MATRÍCULA",
            PlacaEcuador.Formatear(m.Placa), Ui.ColorDe("Acento")));
        if (m.AnioMatricula is not null)
            contenedorMatricula.Children.Add(LineaMatricula("AÑO", m.AnioMatricula.Value.ToString()));
        if (!string.IsNullOrWhiteSpace(m.UltimaMatricula))
            contenedorMatricula.Children.Add(LineaMatricula("ÚLTIMA", m.UltimaMatricula!));
        if (!string.IsNullOrWhiteSpace(m.Caduca))
            contenedorMatricula.Children.Add(LineaMatricula("CADUCA", m.Caduca!));
        if (!string.IsNullOrWhiteSpace(m.Entidad))
            contenedorMatricula.Children.Add(LineaMatricula("ENTIDAD", m.Entidad!));
    }

    private static View LineaMatricula(string etiqueta, string valor, Color? color = null)
    {
        var grilla = new Grid
        {
            ColumnDefinitions = [new(new GridLength(64)), new(GridLength.Star)],
            ColumnSpacing = 6
        };
        grilla.Add(new Label
        {
            Text = etiqueta,
            FontFamily = "OpenSansSemibold",
            FontSize = 8.5,
            CharacterSpacing = 0.6,
            TextColor = Ui.ColorDe("TextoClaroSuave"),
            VerticalOptions = LayoutOptions.Start
        }, 0);
        grilla.Add(new Label
        {
            Text = valor,
            FontSize = 10,
            TextColor = color ?? Ui.ColorDe("TextoClaro"),
            LineBreakMode = LineBreakMode.WordWrap
        }, 1);
        return grilla;
    }

    /// <summary>Vuelca la ficha de EcuadorAPI en los selectores de la pantalla.</summary>
    private void AplicarFicha(ConsultaVehiculoDto ficha)
    {
        var clase = (ficha.Clase ?? "").ToUpperInvariant();
        var tipo = clase switch
        {
            var c when c.Contains("MOTOCICLETA") || c.Contains("MOTO") => "MOTO",
            var c when c.Contains("CAMIONETA") || c.Contains("CAMION") => "CAMIONETA",
            _ => _tipo
        };

        if (tipo != _tipo)
        {
            _tipo = tipo;
            _marca = null;
            _modelo = null;
            _masMarcas = false;
            bordeMarcaOtra.IsVisible = false;
            panelModelo.IsVisible = false;
        }

        // Marca: se busca en el catálogo para reutilizar el emblema; si no está, queda "Otra".
        var marca = (ficha.Marca ?? "").Trim();
        var conocida = !string.IsNullOrWhiteSpace(marca) && BuscarMarca(marca) is not null;
        if (conocida)
        {
            _marca = marca;
            _masMarcas = false;
            bordeMarcaOtra.IsVisible = false;
        }
        else if (marca.Length > 0)
        {
            _marca = "Otra";
            _masMarcas = true;
            bordeMarcaOtra.IsVisible = true;
            txtMarca.Text = marca;
        }

        // Modelo: si la marca es conocida se intenta seleccionar uno del catálogo.
        var modelo = (ficha.Modelo ?? "").Trim();
        _modelo = null;
        if (modelo.Length > 0)
        {
            if (conocida)
            {
                var catalogo = BuscarMarca(_marca!);
                var coincidencia = catalogo?.Modelos
                    .FirstOrDefault(m => modelo.Contains(m, StringComparison.OrdinalIgnoreCase) ||
                                         m.Contains(modelo, StringComparison.OrdinalIgnoreCase));
                _modelo = coincidencia ?? modelo;
            }
            else if (marca.Length > 0)
            {
                // Marca fuera del catálogo: el modelo viaja en el texto de "Otra".
                txtMarca.Text = $"{marca} {modelo}".Trim();
            }
        }

        // Color: "PLATEADO" del proveedor se aproxima al token más cercano.
        var color = NormalizarColor(ficha.Color);
        if (color is not null) _color = color;

        _anioConsulta = ficha.Anio?.ToString();

        PintarSelectores();

        panelConsulta.IsVisible = true;
        panelMatricula.IsVisible = false;
        contenedorMatricula.Children.Clear();
        lblConsultaTitulo.TextColor = Ui.ColorDe("Acento");
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(ficha.Marca)) partes.Add(ficha.Marca!);
        if (!string.IsNullOrWhiteSpace(ficha.Modelo)) partes.Add(ficha.Modelo!);
        if (ficha.Anio is not null) partes.Add(ficha.Anio.Value.ToString());
        if (!string.IsNullOrWhiteSpace(ficha.Color)) partes.Add(ficha.Color!);

        lblConsultaTitulo.Text = $"🔎 {PlacaEcuador.Formatear(ficha.Placa)}";
        var detalle = partes.Count == 0 ? "Sin datos de fábrica" : string.Join(" · ", partes);
        if (ficha.Cilindraje is { } cc) detalle += $" · {cc} cc";
        if (!string.IsNullOrWhiteSpace(ficha.Clase)) detalle += $"\n{ficha.Clase}";
        lblConsultaDetalle.Text = detalle + "\nDatos traídos de EcuadorAPI. Puedes ajustarlos antes de registrar.";
    }

    /// <summary>PLATEADO → Plata, GUINDA → Rojo, etc. Null si el color no está en la paleta.</summary>
    private static string? NormalizarColor(string? color)
    {
        var c = (color ?? "").Trim().ToUpperInvariant();
        if (c.Length == 0) return null;

        foreach (var (nombre, _) in Colores)
        {
            if (c.Contains(nombre.ToUpperInvariant(), StringComparison.Ordinal)) return nombre;
        }

        if (c.Contains("PLATEAD") || c.Contains("PLATA")) return "Plata";
        if (c.Contains("GRIS") || c.Contains("GRAFITO")) return "Gris";
        if (c.Contains("BLANC")) return "Blanco";
        if (c.Contains("NEGR")) return "Negro";
        if (c.Contains("ROJ") || c.Contains("GUINDA") || c.Contains("VINO")) return "Rojo";
        if (c.Contains("AZUL")) return "Azul";
        if (c.Contains("VERD")) return "Verde";
        if (c.Contains("AMARILL") || c.Contains("DORAD")) return "Amarillo";
        if (c.Contains("NARANJ")) return "Naranja";
        if (c.Contains("CAF") || c.Contains("MARRON") || c.Contains("MARRÓN")) return "Café";
        return null;
    }

    // ================================================================ alta
    private async void OnRegistrarClicked(object? sender, EventArgs e)
    {
        var placa = (txtPlaca.Text ?? "").Trim().ToUpperInvariant();
        var tipo = _tipo;
        var marca = MarcaElegida();
        var color = _color ?? "";
        var propietario = _perfil ?? "";

        if (placa.Length == 0)
        {
            await Ui.Error(this, "La placa es obligatoria");
            return;
        }
        if (!Ui.PlacaValida(placa))
        {
            await Ui.Error(this, "Placa inválida. Ej: ABC-1234");
            return;
        }
        if (propietario.Length == 0)
        {
            await Ui.Error(this, "Elige quién conduce: hombre, mujer, tercera edad, capacidades especiales, embarazada o estudiante");
            return;
        }
        if (string.IsNullOrWhiteSpace(_marca))
        {
            if (_masMarcas && !string.IsNullOrWhiteSpace(txtMarca.Text))
                _marca = "Otra";
            else
            {
                await Ui.Error(this, "Elige la marca");
                return;
            }
        }
        if (_marca != "Otra" && string.IsNullOrWhiteSpace(_modelo))
        {
            await Ui.Error(this, "Elige el modelo");
            return;
        }

        btnRegistrar.IsEnabled = false;
        try
        {
            var creado = await ApiCliente.CrearVehiculoAsync(placa, tipo,
                marca.Length == 0 ? null : marca,
                color.Length == 0 ? null : color,
                propietario, null);

            try
            {
                var enrolado = await BiometriaServicio.EnrolarAsync(this, creado.Id, creado.Placa);
                if (!enrolado)
                    await Ui.Aviso(this,
                        "El vehículo quedó registrado, pero sin huella no podrás abrir el QR de salida. En Registrados puedes pulsar Huella.",
                        "Biometría pendiente");
            }
            catch (Exception bio)
            {
                await Ui.MostrarExcepcion(this, bio);
            }

            txtPlaca.Text = txtMarca.Text = "";
            _tipo = "AUTO";
            _marca = null;
            _modelo = null;
            _masMarcas = false;
            _color = null;
            _perfil = null;
            _consulta = null;
            _anioConsulta = null;
            _ultimaPlacaConsultada = null;
            panelConsulta.IsVisible = false;
            bordeMarcaOtra.IsVisible = false;
            panelModelo.IsVisible = false;
            PintarSelectores();

            vPlazas.NotificarVehiculosActualizados(creado.Placa);
            await Ui.Exito(this, $"Vehículo {creado.Placa} registrado. Vuelve al mapa: ya aparece en «Elige tu plaza».");
            vPlazas.IrATabMapa();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnRegistrar.IsEnabled = true;
        }
    }

    private string MarcaElegida()
    {
        var anio = string.IsNullOrWhiteSpace(_anioConsulta) ? "" : $" {_anioConsulta}";
        if (_marca == "Otra")
            return ((txtMarca.Text ?? "").Trim() + anio).Trim();
        if (string.IsNullOrWhiteSpace(_marca) || string.IsNullOrWhiteSpace(_modelo))
            return _marca ?? "";
        return $"{_marca} {_modelo}{anio}";
    }

    private void PintarSelectores()
    {
        PintarTipos();
        PintarMarcas();
        PintarMarcasExtra();
        PintarModelos();
        PintarColores();
        PintarPerfiles();
    }

    private void PintarPerfiles()
    {
        iconosPerfil.Children.Clear();
        foreach (var (nombre, icono) in Perfiles)
        {
            var elegido = nombre;
            var activo = _perfil == elegido;
            iconosPerfil.Children.Add(new Border
            {
                BackgroundColor = Ui.ColorDe(activo ? "Acento" : "BlancoHumo"),
                Stroke = Ui.ColorDe(activo ? "AcentoOscuro" : "BordeClaro"),
                StrokeThickness = activo ? 2 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                WidthRequest = 78,
                HeightRequest = 58,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(2, 4),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = icono,
                            FontSize = 16,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = EtiquetaPerfil(elegido),
                            FontFamily = "OpenSansSemibold",
                            FontSize = 10,
                            TextColor = Ui.ColorDe("TextoFuerte"),
                            HorizontalTextAlignment = TextAlignment.Center
                        }
                    }
                },
                GestureRecognizers =
                {
                    Toque(() =>
                    {
                        _perfil = elegido;
                        PintarPerfiles();
                    })
                }
            });
        }
    }

    private void PintarTipos()
    {
        iconosTipo.Children.Clear();
        foreach (var tipo in Tipos)
        {
            var elegido = tipo;
            var activo = _tipo == elegido;
            iconosTipo.Children.Add(new Border
            {
                BackgroundColor = Ui.ColorDe(activo ? "Acento" : "BlancoHumo"),
                Stroke = Ui.ColorDe(activo ? "AcentoOscuro" : "BordeClaro"),
                StrokeThickness = activo ? 2 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                WidthRequest = 72,
                HeightRequest = 52,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(4, 4),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = Ui.IconoTipo(elegido),
                            FontSize = 16,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = elegido,
                            FontFamily = "OpenSansSemibold",
                            FontSize = 12,
                            TextColor = Ui.ColorDe("TextoFuerte"),
                            HorizontalTextAlignment = TextAlignment.Center
                        }
                    }
                },
                GestureRecognizers =
                {
                    Toque(() =>
                    {
                        _tipo = elegido;
                        _marca = null;
                        _modelo = null;
                        _masMarcas = false;
                        bordeMarcaOtra.IsVisible = false;
                        panelModelo.IsVisible = false;
                        PintarTipos();
                        PintarMarcas();
                        PintarMarcasExtra();
                        PintarModelos();
                        PintarColores();
                    })
                }
            });
        }
    }

    private void PintarMarcas()
    {
        iconosMarca.Children.Clear();
        foreach (var (nombre, icono, _) in MarcasVisibles(populares: true))
        {
            var elegida = nombre;
            var activo = _marca == elegida;
            View emblema = string.IsNullOrEmpty(icono)
                ? new Label
                {
                    Text = nombre == "Otra" ? "+" : nombre.Length <= 3 ? nombre.ToUpperInvariant() : nombre[..2].ToUpperInvariant(),
                    FontFamily = "OpenSansSemibold",
                    FontSize = 14,
                    TextColor = Ui.ColorDe("TextoFuerte"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
                : new Image
                {
                    Source = icono,
                    WidthRequest = 28,
                    HeightRequest = 22,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center
                };

            iconosMarca.Children.Add(new Border
            {
                BackgroundColor = Ui.ColorDe(activo ? "Acento" : "Blanco"),
                Stroke = Ui.ColorDe(activo ? "AcentoOscuro" : "BordeClaro"),
                StrokeThickness = activo ? 2 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                WidthRequest = 68,
                HeightRequest = 52,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(4, 4),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        emblema,
                        new Label
                        {
                            Text = elegida,
                            FontFamily = "OpenSansSemibold",
                            FontSize = 11,
                            TextColor = Ui.ColorDe("TextoFuerte"),
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.TailTruncation
                        }
                    }
                },
                GestureRecognizers =
                {
                    Toque(() =>
                    {
                        _marca = elegida;
                        _modelo = null;
                        _masMarcas = false;
                        bordeMarcaOtra.IsVisible = false;
                        panelModelo.IsVisible = true;
                        txtMarca.Text = "";
                        PintarMarcas();
                        PintarMarcasExtra();
                        PintarModelos();
                    })
                }
            });
        }

        var masActivo = _masMarcas;
        iconosMarca.Children.Add(new Border
        {
            BackgroundColor = Ui.ColorDe(masActivo ? "Acento" : "Blanco"),
            Stroke = Ui.ColorDe(masActivo ? "AcentoOscuro" : "BordeClaro"),
            StrokeThickness = masActivo ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            WidthRequest = 68,
            HeightRequest = 52,
            Margin = new Thickness(0, 0, 6, 6),
            Padding = new Thickness(4, 4),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "+",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 14,
                        TextColor = Ui.ColorDe("TextoFuerte"),
                        HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = "Otra",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 11,
                        TextColor = Ui.ColorDe("TextoFuerte"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            },
            GestureRecognizers =
            {
                Toque(() =>
                {
                    _masMarcas = !_masMarcas;
                    if (!_masMarcas)
                    {
                        _marca = null;
                        _modelo = null;
                        txtMarca.Text = "";
                    }
                    bordeMarcaOtra.IsVisible = _masMarcas;
                    panelModelo.IsVisible = false;
                    PintarMarcas();
                    PintarMarcasExtra();
                    PintarModelos();
                })
            }
        });

        iconosMarcaExtra.IsVisible = _masMarcas && _tipo == "AUTO";
    }

    private void PintarMarcasExtra()
    {
        iconosMarcaExtra.Children.Clear();
        if (!_masMarcas || _tipo != "AUTO" || !CatalogoExtra.ContainsKey("AUTO"))
            return;

        foreach (var (nombre, icono, _) in MarcasVisibles(populares: false))
        {
            var elegida = nombre;
            var activo = _marca == elegida;
            View emblema = string.IsNullOrEmpty(icono)
                ? new Label
                {
                    Text = nombre.Length <= 3 ? nombre.ToUpperInvariant() : nombre[..2].ToUpperInvariant(),
                    FontFamily = "OpenSansSemibold",
                    FontSize = 12,
                    TextColor = Ui.ColorDe("TextoFuerte"),
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
                : new Image
                {
                    Source = icono,
                    WidthRequest = 28,
                    HeightRequest = 22,
                    Aspect = Aspect.AspectFit,
                    HorizontalOptions = LayoutOptions.Center
                };

            iconosMarcaExtra.Children.Add(new Border
            {
                BackgroundColor = Ui.ColorDe(activo ? "Acento" : "Blanco"),
                Stroke = Ui.ColorDe(activo ? "AcentoOscuro" : "BordeClaro"),
                StrokeThickness = activo ? 2 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                WidthRequest = 68,
                HeightRequest = 52,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(4, 4),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        emblema,
                        new Label
                        {
                            Text = elegida,
                            FontFamily = "OpenSansSemibold",
                            FontSize = 11,
                            TextColor = Ui.ColorDe("TextoFuerte"),
                            HorizontalTextAlignment = TextAlignment.Center
                        }
                    }
                },
                GestureRecognizers =
                {
                    Toque(() =>
                    {
                        _marca = elegida;
                        _modelo = null;
                        txtMarca.Text = "";
                        panelModelo.IsVisible = true;
                        PintarMarcasExtra();
                        PintarModelos();
                    })
                }
            });
        }
    }

    private IEnumerable<(string Nombre, string Icono, string[] Modelos)> MarcasVisibles(bool populares)
    {
        var todas = Catalogo[_tipo].Where(m => m.Nombre != "Otra");
        if (CatalogoExtra.TryGetValue(_tipo, out var extra))
            todas = todas.Concat(extra);

        if (_tipo != "AUTO")
            return populares ? todas : [];

        return todas
            .Where(m => PopularesAuto.Contains(m.Nombre) == populares)
            .OrderBy(m => m.Nombre, StringComparer.CurrentCultureIgnoreCase);
    }

    private (string Nombre, string Icono, string[] Modelos)? BuscarMarca(string nombre)
    {
        var principal = Catalogo[_tipo].FirstOrDefault(m => m.Nombre == nombre);
        if (!string.IsNullOrEmpty(principal.Nombre))
            return principal;
        if (CatalogoExtra.TryGetValue(_tipo, out var extra))
        {
            var adicional = extra.FirstOrDefault(m => m.Nombre == nombre);
            if (!string.IsNullOrEmpty(adicional.Nombre))
                return adicional;
        }

        return null;
    }

    private void PintarModelos()
    {
        iconosModelo.Children.Clear();
        if (string.IsNullOrEmpty(_marca) || _marca == "Otra")
        {
            panelModelo.IsVisible = false;
            return;
        }

        var marca = BuscarMarca(_marca);
        if (marca is null)
        {
            panelModelo.IsVisible = false;
            return;
        }

        panelModelo.IsVisible = marca.Value.Modelos.Length > 0;
        foreach (var modelo in marca.Value.Modelos.OrderBy(m => m, StringComparer.CurrentCultureIgnoreCase))
        {
            var elegido = modelo;
            var activo = _modelo == elegido;
            iconosModelo.Children.Add(new Border
            {
                BackgroundColor = Ui.ColorDe(activo ? "Acento" : "BlancoHumo"),
                Stroke = Ui.ColorDe(activo ? "AcentoOscuro" : "BordeClaro"),
                StrokeThickness = activo ? 2 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 8 },
                Padding = new Thickness(8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Content = new Label
                {
                    Text = elegido,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 11,
                    TextColor = Ui.ColorDe("TextoFuerte")
                },
                GestureRecognizers =
                {
                    Toque(() =>
                    {
                        _modelo = elegido;
                        PintarModelos();
                    })
                }
            });
        }
    }

    private void PintarColores()
    {
        iconosColor.Children.Clear();
        foreach (var (nombre, hex) in Colores)
        {
            var elegido = nombre;
            var activo = _color == elegido;
            iconosColor.Children.Add(new VerticalStackLayout
            {
                Spacing = 2,
                WidthRequest = 56,
                Margin = new Thickness(0, 0, 6, 6),
                Children =
                {
                    new Border
                    {
                        BackgroundColor = Ui.ColorDe(activo ? "Acento" : "Blanco"),
                        Stroke = Ui.ColorDe(activo ? "AcentoOscuro" : "BordeClaro"),
                        StrokeThickness = activo ? 2 : 1,
                        StrokeShape = new RoundRectangle { CornerRadius = 8 },
                        Padding = new Thickness(4, 4),
                        Content = DibujoVehiculo(_tipo, hex)
                    },
                    new Label
                    {
                        Text = elegido,
                        FontSize = 11,
                        FontFamily = activo ? "OpenSansSemibold" : "OpenSansRegular",
                        TextColor = Ui.ColorDe(activo ? "TextoFuerte" : "TextoMedio"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                },
                GestureRecognizers =
                {
                    Toque(() =>
                    {
                        _color = elegido;
                        PintarColores();
                    })
                }
            });
        }
    }

    private static View DibujoVehiculo(string tipo, string hex) => new GraphicsView
    {
        WidthRequest = 46,
        HeightRequest = 26,
        InputTransparent = true,
        HorizontalOptions = LayoutOptions.Center,
        Drawable = new SiluetaVehiculo(tipo, Color.FromArgb(hex))
    };

    private sealed class SiluetaVehiculo : IDrawable
    {
        private readonly string _tipo;
        private readonly Color _pintura;

        public SiluetaVehiculo(string tipo, Color pintura)
        {
            _tipo = tipo;
            _pintura = pintura;
        }

        public void Draw(ICanvas canvas, RectF area)
        {
            canvas.SaveState();
            canvas.Scale(area.Width / 80f, area.Height / 40f);
            if (_tipo == "MOTO") Moto(canvas);
            else if (_tipo == "CAMIONETA") Camioneta(canvas);
            else Auto(canvas);
            canvas.RestoreState();
        }

        private void Auto(ICanvas canvas)
        {
            canvas.FillColor = _pintura;
            canvas.FillRoundedRectangle(6, 16, 68, 14, 6);
            canvas.FillRoundedRectangle(22, 6, 36, 14, 6);
            canvas.FillColor = Color.FromArgb("#E8EEF5");
            canvas.FillRoundedRectangle(26, 8, 13, 8, 2);
            canvas.FillRoundedRectangle(42, 8, 13, 8, 2);
            Rueda(canvas, 22, 30);
            Rueda(canvas, 58, 30);
        }

        private void Camioneta(ICanvas canvas)
        {
            canvas.FillColor = _pintura;
            canvas.FillRoundedRectangle(2, 18, 36, 12, 3);
            canvas.FillRoundedRectangle(30, 6, 30, 24, 6);
            canvas.FillRoundedRectangle(56, 16, 20, 14, 4);
            canvas.FillColor = Color.FromArgb("#E8EEF5");
            canvas.FillRoundedRectangle(36, 9, 16, 10, 2);
            Rueda(canvas, 16, 30);
            Rueda(canvas, 62, 30);
        }

        private void Moto(ICanvas canvas)
        {
            Rueda(canvas, 16, 28);
            Rueda(canvas, 62, 28);
            canvas.StrokeColor = _pintura;
            canvas.StrokeSize = 4;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(16, 28, 30, 16);
            canvas.DrawLine(30, 16, 48, 14);
            canvas.DrawLine(48, 14, 64, 26);
            canvas.FillColor = _pintura;
            canvas.FillRoundedRectangle(30, 11, 18, 8, 3);
        }

        private static void Rueda(ICanvas canvas, float x, float y)
        {
            canvas.FillColor = Color.FromArgb("#111827");
            canvas.FillCircle(x, y, 6.5f);
            canvas.FillColor = Color.FromArgb("#F8FAFC");
            canvas.FillCircle(x, y, 2.5f);
        }
    }

    private static string EtiquetaPerfil(string nombre) => nombre switch
    {
        "Tercera Edad" => "Tercera Edad\n50%",
        "Capacidades Especiales" => "Capacidades\nEspeciales · 50%",
        "Embarazada" => "Embarazada\n25%",
        "Estudiante" => "Estudiante\n25%",
        _ => nombre
    };

    private static TapGestureRecognizer Toque(Action accion)
    {
        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) => accion();
        return toque;
    }
}
