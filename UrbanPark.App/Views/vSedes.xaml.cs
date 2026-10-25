using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Primera pantalla de la aplicación: el mapa con los 12 parqueaderos del grupo.
/// El cliente ve dónde está cada uno, su distancia y sus plazas libres, puede pedirle a la IA
/// que le diga cuál le conviene y desde aquí entra a elegir el puesto.
/// </summary>
public partial class vSedes : ContentPage
{
    private static readonly Color Amarillo = Color.FromArgb("#F2C51A");
    private static readonly Color SuperficieAlta = Color.FromArgb("#1C2530");
    private static readonly Color Borde = Color.FromArgb("#28323F");
    private static readonly Color Gris = Color.FromArgb("#7C8A9C");
    private static readonly Color Claro = Color.FromArgb("#F8FAFC");
    private static readonly Color Tinta = Color.FromArgb("#0F172A");
    private static readonly Color Azul = Color.FromArgb("#2D4E8E");
    private static readonly Color Verde = Color.FromArgb("#15803D");
    private static readonly Color FondoTarjeta = Colors.White;

    private readonly List<(string? Codigo, string Texto)> _tipos =
    [
        (null, "🚗 Todos"),
        ("AUTO", "🚗 Auto"),
        ("MOTO", "🏍 Moto"),
        ("CAMIONETA", "🚙 Camioneta")
    ];

    private List<SedeDto> _sedes = [];
    private List<VehiculoDto> _vehiculos = [];
    private SedeDto? _seleccionada;
    private string? _tipo = "AUTO";
    private TicketDto? _ticketAbierto;
    private bool _cargando;
    private string? _perfilConductor;

    public vSedes()
    {
        InitializeComponent();

        mapa.SedeElegida += (_, sede) => Seleccionar(sede, false);
        mapa.SedeConfirmada += async (_, sede) => await AbrirPuestosAsync(sede);

        PintarTipos();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // La recomendación con IA es para CLIENTE y ADMIN (el OPERADOR asigna desde Tickets).
        btnIa.IsVisible = Sesion.RolPrincipal != "OPERADOR";
        await CargarAsync();
    }

    private async void OnRefrescarClicked(object? sender, EventArgs e) => await CargarAsync();

    // ================================================================ carga
    private async Task CargarAsync()
    {
        if (_cargando) return;
        _cargando = true;
        spinner.IsVisible = spinner.IsRunning = true;
        btnRefrescar.IsEnabled = false;

        try
        {
            var (lat, lng, real) = await Ubicacion.ObtenerAsync();
            lblUbicacion.Text = real
                ? $"Tu ubicación: {Ubicacion.Texto}"
                : "Sin GPS: usamos el centro-norte de Quito para calcular distancias";

            if (_vehiculos.Count == 0)
                _vehiculos = await VehiculosSeguroAsync();

            var vehiculo = VehiculoPreferido();
            _perfilConductor = vehiculo?.Propietario;

            _sedes = await ApiCliente.SedesAsync(lat, lng, _tipo, _perfilConductor) ?? [];
            if (_sedes.Count == 0)
            {
                lblAvisoMapa.Text = "No hay parqueaderos con plazas libres para ese vehículo";
            }
            else
            {
                lblAvisoMapa.Text = $"{_sedes.Count} parqueaderos cerca · toca un pin";
            }

            // Mantiene la selección si la sede sigue existiendo; si no, propone la más cercana.
            var anterior = _seleccionada is null ? null : _sedes.FirstOrDefault(s => s.Id == _seleccionada.Id);
            _seleccionada = anterior ?? _sedes.FirstOrDefault();

            PintarMapa(real);
            PintarLista();
            PintarSeleccion();
            await CargarEstacionadoAsync();
        }
        catch (Exception ex)
        {
            lblAvisoMapa.Text = "No se pudo cargar el mapa";
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinner.IsRunning = false;
            spinner.IsVisible = false;
            btnRefrescar.IsEnabled = true;
            _cargando = false;
        }
    }

    private VehiculoDto? VehiculoPreferido() =>
        _vehiculos.Where(v => v.Activo)
            .OrderByDescending(v => string.Equals(v.Tipo, _tipo, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
        ?? _vehiculos.FirstOrDefault();

    private static async Task<List<VehiculoDto>> VehiculosSeguroAsync()
    {
        try { return await ApiCliente.ListarVehiculosAsync() ?? []; }
        catch (Exception) { return []; }
    }

    private async Task CargarEstacionadoAsync()
    {
        try
        {
            var abiertos = await ApiCliente.TicketsAbiertosAsync() ?? [];
            _ticketAbierto = abiertos.FirstOrDefault(t =>
                string.IsNullOrWhiteSpace(t.Usuario) || t.Usuario == Sesion.Username);

            if (_ticketAbierto is null)
            {
                panelEstacionado.IsVisible = false;
                return;
            }

            lblEstPlaza.Text = $"Estás en la plaza {_ticketAbierto.EspacioCodigo ?? _ticketAbierto.EspacioId.ToString()}";
            lblEstValor.Text = Ui.Moneda(Ui.MontoEstimado(_ticketAbierto.Entrada, _ticketAbierto.TarifaHora,
                Ui.DescuentoDe(_vehiculos.FirstOrDefault(v => v.Placa == _ticketAbierto.Placa)?.Propietario)));
            lblEstDetalle.Text = $"{_ticketAbierto.Zona} · placa {_ticketAbierto.Placa} · ticket {_ticketAbierto.Codigo}";
            panelEstacionado.IsVisible = true;
        }
        catch (Exception)
        {
            panelEstacionado.IsVisible = false;
        }
    }

    // ================================================================ tipos
    private void PintarTipos()
    {
        contenedorTipos.Children.Clear();
        foreach (var (codigo, texto) in _tipos)
        {
            var elegido = _tipo == codigo;
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
            toque.Tapped += async (_, _) =>
            {
                if (_tipo == codigo) return;
                _tipo = codigo;
                PintarTipos();
                await CargarAsync();
            };
            chip.GestureRecognizers.Add(toque);
            contenedorTipos.Children.Add(chip);
        }
    }

    // ================================================================ mapa y lista
    private void PintarMapa(bool usuarioReal)
    {
        mapa.Pintar(_sedes, _seleccionada?.Id, Ubicacion.Latitud, Ubicacion.Longitud, usuarioReal);
    }

    private void PintarLista()
    {
        listaSedes.Clear();
        foreach (var sede in _sedes)
            listaSedes.Add(TarjetaSede(sede));
    }

    private View TarjetaSede(SedeDto sede)
    {
        var elegida = _seleccionada?.Id == sede.Id;
        var libres = sede.PlazasLibresVehiculo > 0 ? sede.PlazasLibresVehiculo : sede.PlazasLibres;
        var lleno = libres == 0;

        var inicial = new Border
        {
            BackgroundColor = elegida ? Amarillo : Color.FromArgb("#E3EBFA"),
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            WidthRequest = 44,
            HeightRequest = 44,
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = sede.Codigo,
                FontFamily = "OpenSansSemibold",
                FontSize = 12,
                TextColor = elegida ? Tinta : Azul,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var titulo = new Label
        {
            Text = Limpiar(sede.Nombre),
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            TextColor = Tinta,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var direccion = new Label
        {
            Text = "📍 " + sede.Direccion,
            FontSize = 11,
            TextColor = Color.FromArgb("#475569"),
            LineBreakMode = LineBreakMode.WordWrap
        };

        var detalle = new Label
        {
            Text = string.Join(" · ", new[]
            {
                sede.Techado ? "Techado" : "",
                sede.Vigilancia ? "Vigilancia" : "",
                sede.Horario
            }.Where(t => !string.IsNullOrWhiteSpace(t))),
            FontSize = 10,
            TextColor = Gris
        };

        var textos = new VerticalStackLayout
        {
            Spacing = 2,
            Children = { titulo, direccion, detalle }
        };

        var distancia = new Label
        {
            Text = string.IsNullOrWhiteSpace(sede.DistanciaTexto) ? "" : sede.DistanciaTexto,
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = Azul,
            HorizontalTextAlignment = TextAlignment.End
        };

        var caminando = new Label
        {
            Text = sede.CaminandoTexto,
            FontSize = 10,
            TextColor = Giris(),
            HorizontalTextAlignment = TextAlignment.End
        };

        var libresLabel = new Label
        {
            Text = lleno ? "Lleno" : $"{libres} libres",
            FontFamily = "OpenSansSemibold",
            FontSize = 11,
            TextColor = lleno ? Color.FromArgb("#B91C1C") : Verde,
            HorizontalTextAlignment = TextAlignment.End
        };

        var tarifa = new Label
        {
            Text = Ui.Tarifa(sede.TarifaBase),
            FontSize = 11,
            TextColor = Color.FromArgb("#475569"),
            HorizontalTextAlignment = TextAlignment.End
        };

        var derecha = new VerticalStackLayout
        {
            Spacing = 1,
            Children = { distancia, caminando, libresLabel, tarifa }
        };

        var grilla = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        grilla.Add(inicial, 0, 0);
        grilla.Add(textos, 1, 0);
        grilla.Add(derecha, 2, 0);

        var tarjeta = new Border
        {
            BackgroundColor = FondoTarjeta,
            Stroke = elegida ? Amarillo : Color.FromArgb("#E5E9F0"),
            StrokeThickness = elegida ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(10, 9),
            Opacity = lleno ? 0.75 : 1,
            Content = grilla
        };

        var toque = new TapGestureRecognizer();
        toque.Tapped += async (_, _) =>
        {
            Seleccionar(sede, true);
            await AbrirPuestosAsync(sede);
        };
        tarjeta.GestureRecognizers.Add(toque);
        return tarjeta;
    }

    private static Color Giris() => Color.FromArgb("#64748B");

    private static string Limpiar(string nombre) => nombre.Replace("Urban Park ", "Urban Park · ");

    private void Seleccionar(SedeDto sede, bool pintarLista)
    {
        _seleccionada = sede;
        mapa.Seleccionar(sede.Id);
        if (pintarLista) PintarLista();
        PintarSeleccion();
    }

    private void PintarSeleccion()
    {
        if (_seleccionada is null)
        {
            barraSeleccion.IsVisible = false;
            return;
        }

        var s = _seleccionada;
        lblSelNombre.Text = Limpiar(s.Nombre);
        lblSelDetalle.Text = string.Join(" · ", new[]
        {
            s.DistanciaTexto,
            s.CaminandoTexto,
            s.LibreTexto,
            Ui.Tarifa(s.TarifaBase)
        }.Where(t => !string.IsNullOrWhiteSpace(t)));
        barraSeleccion.IsVisible = true;
    }

    private async Task AbrirPuestosAsync(SedeDto sede)
    {
        if (sede.PlazasLibres <= 0 && sede.PlazasLibresVehiculo <= 0)
        {
            await Ui.Informar(this, $"{sede.Nombre} está lleno. Prueba otro parqueadero del mapa.");
            return;
        }

        await Navigation.PushAsync(new vPlazas(sede));
    }

    private async void OnVerPuestosClicked(object? sender, EventArgs e)
    {
        if (_seleccionada is null) return;
        await AbrirPuestosAsync(_seleccionada);
    }

    private async void OnTodasLasPlazasClicked(object? sender, EventArgs e) =>
        await Navigation.PushAsync(new vPlazas());

    private async void OnEstacionadoTapped(object? sender, TappedEventArgs e)
    {
        if (_ticketAbierto is null) return;
        await Navigation.PushAsync(new vQrPuesto(_ticketAbierto));
    }

    // ================================================================ IA
    private async void OnIaClicked(object? sender, EventArgs e)
    {
        if (_sedes.Count == 0)
        {
            await Ui.Informar(this, "Todavía no cargaron los parqueaderos. Pulsa ↻ y vuelve a intentar.");
            return;
        }

        btnIa.IsEnabled = false;
        var textoOriginal = btnIa.Text;
        btnIa.Text = "Pensando…";
        try
        {
            var (lat, lng, _) = await Ubicacion.ObtenerAsync(true);
            var vehiculo = VehiculoPreferido();
            var tipo = vehiculo?.Tipo is { Length: > 0 } t ? t : _tipo;
            var recomendacion = await ApiCliente.RecomendarSedeAsync(tipo ?? "AUTO", lat, lng,
                vehiculo?.Propietario, vehiculo?.Placa);

            PintarRecomendacion(recomendacion);

            var elegida = _sedes.FirstOrDefault(s => s.Id == recomendacion.SedeId);
            if (elegida is not null) Seleccionar(elegida, true);
        }
        catch (Exception ex)
        {
            PintarErrorIa(ex is ApiExcepcion ? ex.Message : "No se pudo consultar la IA: " + ex.Message);
        }
        finally
        {
            btnIa.IsEnabled = true;
            btnIa.Text = textoOriginal;
        }
    }

    private void PintarRecomendacion(RecomendacionSedeDto r)
    {
        contenidoIa.Clear();
        panelIa.IsVisible = true;
        panelIa.Stroke = Amarillo;

        contenidoIa.Add(new Label
        {
            Text = "🤖 EL PARQUEADERO QUE TE CONVIENE",
            Style = Ui.EstiloDe("EncabezadoTabla"),
            TextColor = Amarillo
        });

        contenidoIa.Add(new Label
        {
            Text = r.Nombre,
            FontFamily = "OpenSansSemibold",
            FontSize = 17,
            TextColor = Claro
        });

        contenidoIa.Add(new Label
        {
            Text = $"📍 {r.Direccion}",
            FontSize = 12,
            TextColor = Color.FromArgb("#B6C2D1")
        });

        contenidoIa.Add(new Label
        {
            Text = string.Join("  ·  ", new[]
            {
                $"{r.DistanciaKm:0.0} km",
                $"{r.TiempoCaminandoMin} min a pie",
                $"{r.PlazasLibres} libres",
                Ui.Tarifa(r.TarifaBase),
                $"{r.Puntaje}/100"
            }),
            FontSize = 11,
            TextColor = Amarillo
        });

        contenidoIa.Add(new Label
        {
            Text = r.Motivo,
            FontFamily = "OpenSansSemibold",
            FontSize = 13,
            TextColor = Claro
        });

        if (!string.IsNullOrWhiteSpace(r.ExplicacionIa))
        {
            contenidoIa.Add(new Label
            {
                Text = r.ExplicacionIa,
                FontSize = 12,
                TextColor = Color.FromArgb("#B6C2D1"),
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        if (!string.IsNullOrWhiteSpace(r.ProveedorIa))
        {
            contenidoIa.Add(new Label
            {
                Text = $"Motor: {r.ProveedorIa}",
                FontSize = 9,
                TextColor = Gris
            });
        }

        if (r.Alternativas.Count > 0)
        {
            contenidoIa.Add(new Label
            {
                Text = "Otras opciones",
                Style = Ui.EstiloDe("EncabezadoTabla"),
                TextColor = Gris,
                Margin = new Thickness(0, 4, 0, 0)
            });

            foreach (var alternativa in r.Alternativas.Take(3))
            {
                contenidoIa.Add(FilaAlternativa(alternativa));
            }
        }

        var ver = new Button
        {
            Text = "Ver puestos de esta sede",
            BackgroundColor = Amarillo,
            TextColor = Tinta,
            FontFamily = "OpenSansSemibold",
            FontSize = 12,
            CornerRadius = 10,
            HeightRequest = 36,
            Margin = new Thickness(0, 6, 0, 0)
        };
        ver.Clicked += async (_, _) =>
        {
            var sede = _sedes.FirstOrDefault(s => s.Id == r.SedeId);
            if (sede is not null) await AbrirPuestosAsync(sede);
        };
        contenidoIa.Add(ver);
    }

    private View FilaAlternativa(SedeCandidataDto alternativa)
    {
        var fila = new Border
        {
            BackgroundColor = SuperficieAlta,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = new Thickness(8, 6),
            Content = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 8,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 1,
                        Children =
                        {
                            new Label
                            {
                                Text = alternativa.Nombre.Replace("Urban Park ", ""),
                                FontFamily = "OpenSansSemibold",
                                FontSize = 12,
                                TextColor = Claro
                            },
                            new Label
                            {
                                Text = alternativa.Motivo,
                                FontSize = 10,
                                TextColor = Gris
                            }
                        }
                    },
                    new Label
                    {
                        Text = $"{alternativa.Puntaje}",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 14,
                        TextColor = Amarillo,
                        HorizontalTextAlignment = TextAlignment.End,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) =>
        {
            var sede = _sedes.FirstOrDefault(s => s.Id == alternativa.SedeId);
            if (sede is not null) Seleccionar(sede, true);
        };
        fila.GestureRecognizers.Add(toque);
        return fila;
    }

    private void PintarErrorIa(string mensaje)
    {
        contenidoIa.Clear();
        panelIa.IsVisible = true;
        panelIa.Stroke = Ui.ColorDe("Peligro");

        contenidoIa.Add(new Label
        {
            Text = "No se pudo recomendar",
            FontFamily = "OpenSansSemibold",
            FontSize = 14,
            TextColor = Ui.ColorDe("Peligro")
        });
        contenidoIa.Add(new Label
        {
            Text = mensaje,
            FontSize = 12,
            TextColor = Color.FromArgb("#B6C2D1"),
            LineBreakMode = LineBreakMode.WordWrap
        });
        contenidoIa.Add(new Label
        {
            Text = "Mientras tanto puedes elegir el parqueadero tocando un pin del mapa.",
            FontSize = 11,
            TextColor = Gris
        });
    }
}
