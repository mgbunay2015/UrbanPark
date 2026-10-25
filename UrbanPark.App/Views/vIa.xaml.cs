using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Pantalla «Asignación con DeepSeek» (la especificación la llama «Asignación con Ollama»).
/// Incluye el asistente de chat con historial de conversación.
/// La presentación usa el sistema de diseño (Resources/Styles) desde XAML y desde el código.
/// </summary>
public partial class vIa : ContentPage
{
    /// <summary>Historial de la conversación: objetos anónimos con «rol» y «contenido».</summary>
    private readonly List<object> historial = [];

    private readonly List<VehiculoDto> _vehiculos = [];

    private static readonly HashSet<string> MarcasConEmblema = new(StringComparer.OrdinalIgnoreCase)
    {
        "Toyota", "Chevrolet", "Kia", "Hyundai", "Nissan", "Mazda", "Volkswagen", "Ford", "Honda", "Suzuki",
        "Fiat", "Jeep", "Mitsubishi", "Peugeot", "Renault",
        "Audi", "BMW", "Citroen", "Mercedes", "Seat", "Subaru"
    };

    private string? _placa;
    private bool ocupado;
    private readonly EspacioDto? _plazaElegida;

    public vIa() : this(null)
    {
    }

    public vIa(EspacioDto? plazaElegida)
    {
        InitializeComponent();
        _plazaElegida = plazaElegida;
        if (plazaElegida is not null)
            lblPlazaElegida.Text = $"Plaza elegida en el mapa: {plazaElegida.Codigo} · {plazaElegida.Zona} · {plazaElegida.TipoVehiculo}.";

        AgregarChat("IA", "Hola, soy el asistente de Urban Park. Pregúntame por plazas libres, tarifas o tickets.");
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarVehiculosAsync();
    }

    private async Task CargarVehiculosAsync()
    {
        spinnerVehiculos.IsVisible = true;
        spinnerVehiculos.IsRunning = true;
        try
        {
            var lista = await ApiCliente.ListarVehiculosAsync() ?? [];
            _vehiculos.Clear();
            _vehiculos.AddRange(lista);
            lblSinVehiculos.Text = "No hay vehículos registrados. Regístralos en Vehículos y vuelve aquí.";
            if (_placa is not null && _vehiculos.All(v => !string.Equals(v.Placa, _placa, StringComparison.OrdinalIgnoreCase)))
                _placa = null;
        }
        catch (Exception ex)
        {
            _vehiculos.Clear();
            lblSinVehiculos.IsVisible = true;
            lblSinVehiculos.Text = ex is ApiExcepcion ? ex.Message : "No se pudieron cargar los vehículos.";
        }
        finally
        {
            spinnerVehiculos.IsRunning = false;
            spinnerVehiculos.IsVisible = false;
            PintarVehiculos();
        }
    }

    private void PintarVehiculos()
    {
        iconosVehiculos.Children.Clear();
        var hayActivos = _vehiculos.Any(v => v.Activo);
        lblSinVehiculos.IsVisible = !hayActivos;
        if (!hayActivos)
            lblSinVehiculos.Text = _vehiculos.Count == 0
                ? "No hay vehículos registrados. Regístralos en Vehículos y vuelve aquí."
                : "No hay vehículos activos. Actívalos de nuevo en Vehículos.";

        foreach (var vehiculo in _vehiculos.Where(v => v.Activo))
        {
            var placa = vehiculo.Placa;
            var activo = string.Equals(_placa, placa, StringComparison.OrdinalIgnoreCase);
            var marca = string.IsNullOrWhiteSpace(vehiculo.Marca) ? "Sin marca" : vehiculo.Marca!.Trim();
            var color = string.IsNullOrWhiteSpace(vehiculo.Color) ? "" : vehiculo.Color!.Trim();

            iconosVehiculos.Children.Add(new Border
            {
                BackgroundColor = Ui.ColorDe(activo ? "Acento" : "Blanco"),
                Stroke = Ui.ColorDe(activo ? "AcentoOscuro" : "BordeClaro"),
                StrokeThickness = activo ? 2 : 1,
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                WidthRequest = 168,
                Padding = new Thickness(10, 10),
                Margin = new Thickness(0, 0, 8, 8),
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        Emblema(vehiculo),
                        new Label
                        {
                            Text = placa,
                            FontFamily = "OpenSansSemibold",
                            FontSize = 15,
                            TextColor = Ui.ColorDe("TextoFuerte"),
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = $"{Ui.IconoTipo(vehiculo.Tipo)} {vehiculo.Tipo} · {marca} {color}".Trim(),
                            FontSize = 11,
                            TextColor = Ui.ColorDe("TextoFuerte"),
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.TailTruncation
                        },
                        new Label
                        {
                            Text = TextoPerfil(vehiculo.Propietario),
                            FontSize = 12,
                            TextColor = Ui.ColorDe("TextoFuerte"),
                            HorizontalTextAlignment = TextAlignment.Center,
                            LineBreakMode = LineBreakMode.TailTruncation
                        }
                    }
                },
                GestureRecognizers =
                {
                    Toque(() => ElegirVehiculo(placa))
                }
            });
        }
    }

    private static View Emblema(VehiculoDto vehiculo)
    {
        var marca = (vehiculo.Marca ?? "").Trim();
        var emblema = MarcasConEmblema.FirstOrDefault(m =>
            marca.Equals(m, StringComparison.OrdinalIgnoreCase) ||
            marca.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase));
        if (emblema is not null)
        {
            return new Image
            {
                Source = "marca_" + emblema.ToLowerInvariant() + ".png",
                WidthRequest = 56,
                HeightRequest = 36,
                Aspect = Aspect.AspectFit,
                HorizontalOptions = LayoutOptions.Center
            };
        }

        return new Label
        {
            Text = Ui.IconoTipo(vehiculo.Tipo),
            FontSize = 18,
            HorizontalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center
        };
    }

    private static string TextoPerfil(string? perfil) => perfil switch
    {
        "Hombre" => "👨 Hombre",
        "Mujer" => "👩 Mujer",
        "Tercera Edad" or "Anciano" => "👴 Tercera edad",
        "Capacidades Especiales" or "Discapacitado" => "♿ Capacidades especiales",
        "Embarazada" => "🤰 Embarazada",
        "Estudiante" => "🎓 Estudiante",
        _ => string.IsNullOrWhiteSpace(perfil) ? "Sin perfil" : perfil
    };

    /// <summary>La IA recibe el tipo, la marca, el color y quién conduce.</summary>
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

    private VehiculoDto? VehiculoSeleccionado() =>
        _vehiculos.FirstOrDefault(v => string.Equals(v.Placa, _placa, StringComparison.OrdinalIgnoreCase));

    private static TapGestureRecognizer Toque(Action accion)
    {
        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) => accion();
        return toque;
    }

    private void ElegirVehiculo(string placa)
    {
        if (ocupado) return;
        _placa = placa;
        PintarVehiculos();
    }

    // ================================================================ estado de la IA
    private async void OnEstadoClicked(object? sender, EventArgs e)
    {
        btnEstado.IsEnabled = false;
        try
        {
            var estado = await ApiCliente.EstadoIaAsync();
            lblEstadoIa.Text = $"{estado.Mensaje} · modelo {estado.Modelo} · {estado.BaseUrl}";
            lblEstadoIa.TextColor = estado.Disponible ? Ui.ColorDe("Exito") : Ui.ColorDe("Advertencia");
        }
        catch (Exception ex)
        {
            lblEstadoIa.Text = ex.Message;
            lblEstadoIa.TextColor = Ui.ColorDe("Peligro");
        }
        finally
        {
            btnEstado.IsEnabled = true;
        }
    }

    // ================================================================ recomendar
    private async void OnRecomendarClicked(object? sender, EventArgs e)
    {
        if (ocupado) return;

        var vehiculo = VehiculoSeleccionado();
        if (vehiculo is null)
        {
            await Ui.Aviso(this, "Toca primero uno de tus vehículos.");
            return;
        }

        var tipo = string.IsNullOrWhiteSpace(vehiculo.Tipo) ? "AUTO" : vehiculo.Tipo;
        var preferencia = PerfilParaIa(vehiculo);

        var aviso = await MensajeTicketAbiertoAsync(vehiculo.Placa);
        if (aviso is not null)
        {
            PintarError("No se pudo recomendar", aviso);
            return;
        }

        Bloquear(true);
        MostrarTrabajando($"Ollama está recomendando el puesto para {vehiculo.Placa}…");

        try
        {
            var r = await ApiCliente.RecomendarAsync(tipo, preferencia, vehiculo.Placa);
            PintarRecomendacion(r);
        }
        catch (Exception ex)
        {
            PintarError("No se pudo recomendar", ex is ApiExcepcion ? ex.Message : "Ocurrió un error: " + ex.Message);
        }
        finally
        {
            Bloquear(false);
        }
    }

    // ================================================================ asignar manual
    private async void OnAsignarManualClicked(object? sender, EventArgs e)
    {
        if (ocupado) return;

        var vehiculo = VehiculoSeleccionado();
        if (vehiculo is null)
        {
            await Ui.Aviso(this, "Toca uno de tus vehículos para asignar la plaza.");
            return;
        }

        var aviso = await MensajeTicketAbiertoAsync(vehiculo.Placa);
        if (aviso is not null)
        {
            PintarError("No se pudo asignar", aviso);
            return;
        }

        if (_plazaElegida is null)
        {
            await AsignarSeleccionadoAsync("No habías elegido una plaza en el mapa.");
            return;
        }

        Bloquear(true);
        try
        {
            var plazas = await ApiCliente.ListarEspaciosAsync() ?? [];
            var plaza = plazas.FirstOrDefault(p => p.Id == _plazaElegida.Id) ?? _plazaElegida;
            var porque = PorQueNoCumple(plaza, vehiculo);
            if (porque is not null)
            {
                Bloquear(false);
                await AsignarSeleccionadoAsync($"{plaza.Codigo} no cumple: {porque}");
                return;
            }

            var ticket = await ApiCliente.AbrirTicketAsync(vehiculo.Placa, plaza.Id);
            PintarManual(ticket, plaza);
            await Ui.Exito(this, $"Plaza {plaza.Codigo} asignada a {vehiculo.Placa} con ticket {ticket.Codigo}.");
            if (await Ui.Confirmar(this, "Asignación lista",
                    "¿Quieres volver al menú? La información ya quedó guardada."))
                await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            Bloquear(false);
            var detalle = ex is ApiExcepcion api ? api.Message : ex.Message;
            await AsignarSeleccionadoAsync($"{_plazaElegida.Codigo} no se pudo ocupar ({detalle}).");
        }
        finally
        {
            Bloquear(false);
        }
    }

    /// <summary>Null si el vehículo puede ocupar esa plaza. Si no, el motivo para pasar a la IA.</summary>
    private static string? PorQueNoCumple(EspacioDto plaza, VehiculoDto vehiculo)
    {
        if (plaza.Ocupado)
            return "ya está ocupada";
        if (!vehiculo.Activo)
            return "el vehículo está de baja";

        var tipoVehiculo = string.IsNullOrWhiteSpace(vehiculo.Tipo) ? "AUTO" : vehiculo.Tipo.Trim().ToUpperInvariant();
        var tipoPlaza = (plaza.TipoVehiculo ?? "").Trim().ToUpperInvariant();
        var perfil = vehiculo.Propietario ?? "";

        if (EsDiscapacidad(perfil))
            return tipoPlaza == "DISCAPACIDAD" ? null : "no es un puesto de discapacidad";

        if (EsEmbarazada(perfil))
        {
            if (tipoPlaza != "EMBARAZADA")
                return "no es un puesto de embarazada";
            return tipoVehiculo == "MOTO" ? "el puesto de embarazada no es para moto" : null;
        }

        if (tipoPlaza == "DISCAPACIDAD")
            return "es preferencial y este conductor no tiene capacidades especiales";
        if (tipoPlaza == "EMBARAZADA")
            return "es para embarazadas y este conductor no lo está";
        if (tipoPlaza != tipoVehiculo)
            return $"es para {tipoPlaza} y el vehículo es {tipoVehiculo}";
        if (PrioridadPerfil(plaza, perfil) != 0)
            return "no corresponde a la zona de este conductor";
        return null;
    }

    private static bool EsEmbarazada(string perfil) => perfil.ToLowerInvariant().Contains("embaraz");

    private static bool PlazaComun(string tipo) => tipo is not "DISCAPACIDAD" and not "EMBARAZADA";

    private static bool EsDiscapacidad(string perfil)
    {
        var p = perfil.ToLowerInvariant();
        return p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial");
    }

    /// <summary>0 si la plaza encaja con el perfil. Misma regla que la IA del API.</summary>
    private static int PrioridadPerfil(EspacioDto plaza, string perfil)
    {
        var p = perfil.ToLowerInvariant();
        var tipo = (plaza.TipoVehiculo ?? "").ToUpperInvariant();
        var zona = (plaza.Zona ?? "").ToUpperInvariant();
        var nivel = plaza.Nivel ?? 9;

        if (EsDiscapacidad(perfil))
            return tipo == "DISCAPACIDAD" ? 0 : 10;
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor"))
            return PlazaComun(tipo) && zona.Contains("ZONA A") && nivel == 1 ? 0 : 8;
        if (EsEmbarazada(perfil))
            return tipo == "EMBARAZADA" ? 0 : 8;
        if (p.Contains("estudiante") || p.Contains("estudiate"))
            return PlazaComun(tipo) && zona.Contains("ZONA C") ? 0 : 8;
        if (p.Contains("mujer"))
            return PlazaComun(tipo) && zona.Contains("ZONA D") ? 0 : 8;
        if (p.Contains("hombre"))
            return PlazaComun(tipo) && (zona.Contains("ZONA B") || zona.Contains("ZONA C")) ? 0 : 8;
        return 0;
    }

    private void PintarManual(TicketDto ticket, EspacioDto plaza)
    {
        Limpiar();
        contenedorResultado.Add(Encabezado("ASIGNACIÓN MANUAL", "Acento"));
        contenedorResultado.Add(TituloResultado($"Asignado: {plaza.Codigo} · {plaza.Zona}", Ui.ColorDe("Acento")));
        contenedorResultado.Add(Divisor());
        contenedorResultado.Add(Fila("PLACA · TICKET", $"Placa: {ticket.Placa} · Ticket: {ticket.Codigo}"));
        contenedorResultado.Add(Fila("PLAZA",
            $"Plaza ID: {plaza.Id} · {Ui.Tarifa(plaza.TarifaHora)} · Nivel {Ui.Nivel(plaza.Nivel)} · {plaza.TipoVehiculo}"));
        contenedorResultado.Add(Fila("MOTIVO", "La plaza que elegiste en el mapa cumple para este vehículo."));
    }

    // ================================================================ asignar
    private async void OnAsignarClicked(object? sender, EventArgs e) => await AsignarSeleccionadoAsync();

    private async Task AsignarSeleccionadoAsync(string? avisoPrevio = null)
    {
        if (ocupado) return;

        var vehiculo = VehiculoSeleccionado();
        if (vehiculo is null)
        {
            await Ui.Aviso(this, "Toca uno de tus vehículos para que la IA elija el puesto.");
            return;
        }

        var tipo = string.IsNullOrWhiteSpace(vehiculo.Tipo) ? "AUTO" : vehiculo.Tipo;
        var preferencia = PerfilParaIa(vehiculo);

        var aviso = await MensajeTicketAbiertoAsync(vehiculo.Placa);
        if (aviso is not null)
        {
            PintarError("No se pudo asignar", aviso);
            return;
        }

        Bloquear(true);
        var previa = string.IsNullOrWhiteSpace(avisoPrevio) ? "" : avisoPrevio.Trim() + " ";
        MostrarTrabajando($"{previa}Ollama está eligiendo y asignando el puesto para {vehiculo.Placa}…");

        try
        {
            var a = await ApiCliente.AsignarAsync(vehiculo.Placa, tipo, preferencia);
            PintarAsignacion(a);

            var texto = $"Plaza {a.EspacioCodigo} asignada a {a.Placa} con ticket {a.TicketCodigo}.";
            if (!string.IsNullOrWhiteSpace(avisoPrevio))
                texto = avisoPrevio.Trim() + "\n\n" + texto;
            await Ui.Exito(this, texto);

            if (await Ui.Confirmar(this, "Asignación lista",
                    "¿Quieres volver al menú? La información ya quedó guardada."))
                await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            PintarError("No se pudo asignar", ex is ApiExcepcion ? ex.Message : "Ocurrió un error: " + ex.Message);
        }
        finally
        {
            Bloquear(false);
        }
    }

    // ================================================================ pintado de la tarjeta de resultado
    /// <summary>Deja el panel de resultado listo en su aspecto neutro (tarjeta oscura del sistema).</summary>
    private void Limpiar()
    {
        contenedorResultado.Clear();
        panelResultado.IsVisible = true;
        panelResultado.Style = Ui.EstiloDe("Tarjeta");
        panelResultado.Stroke = Ui.ColorDe("SuperficieBorde");
        panelResultado.BackgroundColor = Ui.ColorDe("Superficie");
    }

    private void MostrarTrabajando(string mensaje)
    {
        Limpiar();
        contenedorResultado.Add(Encabezado("RESULTADO", "TextoSuave"));
        contenedorResultado.Add(Parrafo(mensaje, 15, fuerte: true));
    }

    private void PintarError(string titulo, string mensaje)
    {
        Limpiar();
        // El mismo contenedor cambia a aspecto rojo cuando hay error.
        panelResultado.Stroke = Ui.ColorDe("Peligro");
        panelResultado.BackgroundColor = Ui.ColorDe("Superficie");

        contenedorResultado.Add(Encabezado("RESULTADO", "Peligro"));
        contenedorResultado.Add(TituloResultado(titulo, Ui.ColorDe("Peligro")));
        contenedorResultado.Add(Divisor());
        contenedorResultado.Add(Parrafo(mensaje, 13));
        contenedorResultado.Add(Parrafo(
            "Consejo: en Tickets cierra el ticket abierto de esa placa, o usa otra placa.",
            12, suave: true, color: Ui.ColorDe("Advertencia")));
    }

    private void PintarAsignacion(AsignacionDto a)
    {
        Limpiar();

        contenedorResultado.Add(Encabezado("ASIGNACIÓN LISTA", "Acento"));
        contenedorResultado.Add(TituloResultado($"Asignado: {a.EspacioCodigo} · {a.Zona}", Ui.ColorDe("Acento")));
        contenedorResultado.Add(Divisor());

        contenedorResultado.Add(Fila("PLACA · TICKET", $"Placa: {a.Placa} · Ticket: {a.TicketCodigo} (ID {a.TicketId})"));
        contenedorResultado.Add(Fila("PLAZA",
            $"Plaza ID: {a.EspacioId} · {Ui.Tarifa(a.TarifaHora)} · Nivel {Ui.Nivel(a.Nivel)}"));
        contenedorResultado.Add(Fila("MOTIVO", a.Motivo, color: Ui.ColorDe("Primario")));

        contenedorResultado.Add(Fila("EXPLICACIÓN DE LA IA", a.ExplicacionIa, cursiva: true));
        contenedorResultado.Add(Parrafo($"Proveedor: {a.ProveedorIa}", 11, suave: true));
    }

    private void PintarRecomendacion(RecomendacionDto r)
    {
        Limpiar();

        contenedorResultado.Add(Encabezado("RECOMENDACIÓN", "Acento"));
        contenedorResultado.Add(TituloResultado($"Sugerencia: {r.EspacioCodigo} · {r.ZonaRecomendada}", Ui.ColorDe("Acento")));
        contenedorResultado.Add(Divisor());

        contenedorResultado.Add(Fila("PLAZA",
            $"ID plaza: {r.EspacioId} · {Ui.Tarifa(r.TarifaHora)} · Nivel {Ui.Nivel(r.Nivel)}"));
        contenedorResultado.Add(Fila("MOTIVO", r.Motivo, color: Ui.ColorDe("Primario")));
        contenedorResultado.Add(Fila("EXPLICACIÓN DE LA IA", r.ExplicacionIa, cursiva: true));

        var alternativas = r.Alternativas.Count == 0 ? "ninguna" : string.Join(", ", r.Alternativas);
        contenedorResultado.Add(Divisor());
        contenedorResultado.Add(Parrafo($"Proveedor: {r.ProveedorIa}", 11, suave: true));
        contenedorResultado.Add(Parrafo($"Alternativas: {alternativas}", 11, suave: true));
    }

    // ================================================================ piezas de la tarjeta de resultado
    private static Label Encabezado(string texto, string token) => new()
    {
        Text = texto,
        Style = Ui.EstiloDe("EncabezadoTablaClaro"),
        TextColor = Ui.ColorDe(token)
    };

    private static Label TituloResultado(string texto, Color color) => new()
    {
        Text = texto,
        FontSize = 16,
        FontAttributes = FontAttributes.Bold,
        TextColor = color,
        LineBreakMode = LineBreakMode.WordWrap
    };

    private static BoxView Divisor() => new()
    {
        Style = Ui.EstiloDe("Divisor"),
        Margin = new Thickness(0, 2)
    };

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

    /// <summary>Fila del resultado: etiqueta pequeña en mayúsculas + valor, con divisor debajo.</summary>
    private static View Fila(string etiqueta, string valor, bool cursiva = false, Color? color = null)
    {
        var pila = new VerticalStackLayout { Spacing = 3 };
        pila.Add(new Label
        {
            Text = etiqueta,
            Style = Ui.EstiloDe("EtiquetaCampoClaro")
        });
        pila.Add(new Label
        {
            Text = valor,
            FontSize = 14,
            FontAttributes = cursiva ? FontAttributes.Italic : FontAttributes.None,
            TextColor = color ?? Ui.ColorDe("TextoClaro"),
            LineBreakMode = LineBreakMode.WordWrap
        });

        var contenedor = new VerticalStackLayout { Spacing = 10 };
        contenedor.Add(pila);
        contenedor.Add(new BoxView { Style = Ui.EstiloDe("Divisor") });
        return contenedor;
    }

    private async Task<string?> MensajeTicketAbiertoAsync(string placa)
    {
        try
        {
            var tickets = await ApiCliente.ListarTicketsAsync() ?? [];
            var abierto = tickets.FirstOrDefault(t =>
                t.Abierto && string.Equals(t.Placa, placa, StringComparison.OrdinalIgnoreCase));
            if (abierto is null)
                return null;

            var plazas = await ApiCliente.ListarEspaciosAsync() ?? [];
            var plaza = plazas.FirstOrDefault(p => p.Id == abierto.EspacioId);
            if (plaza is not null && !plaza.Ocupado)
                return null;

            var donde = string.IsNullOrWhiteSpace(abierto.EspacioCodigo) ? "" : $" en {abierto.EspacioCodigo}";
            return $"La placa {placa} ya tiene un ticket ABIERTO{donde}. Ve a Tickets y pulsa «Cerrar / cobrar» antes de asignar otra plaza.";
        }
        catch
        {
            return null;
        }
    }

    private void Bloquear(bool estado)
    {
        ocupado = estado;
        btnAsignar.IsEnabled = !estado;
        btnManual.IsEnabled = !estado;
        btnRecomendar.IsEnabled = !estado;
    }

    // ================================================================ chat
    private async void OnEnviarClicked(object? sender, EventArgs e)
    {
        var texto = (txtMensaje.Text ?? "").Trim();
        if (texto.Length == 0)
        {
            await Ui.Aviso(this, "Escribe un mensaje para el asistente.");
            return;
        }

        AgregarChat("Tú", texto);
        txtMensaje.Text = "";
        btnEnviar.IsEnabled = false;
        txtMensaje.IsEnabled = false;

        // Burbuja de espera con el mismo aspecto que las respuestas de la IA.
        View esperando = BurbujaIa("IA", "pensando…");
        contenedorChat.Add(esperando);
        ScrollAlFinal();

        try
        {
            var respuesta = await ApiCliente.ChatAsync(texto, historial);

            contenedorChat.Remove(esperando);

            historial.Add(new { rol = "user", contenido = texto });
            historial.Add(new { rol = "assistant", contenido = respuesta.Respuesta });
            if (historial.Count > 24)
                historial.RemoveRange(0, historial.Count - 24);

            AgregarChat("IA", respuesta.Respuesta);

            if (!respuesta.UsaIa)
                await Ui.Aviso(this,
                    "Ollama no respondió: el backend usó el respaldo local. La respuesta puede ser menos precisa.");
        }
        catch (Exception ex)
        {
            contenedorChat.Remove(esperando);
            AgregarChat("IA", ex is ApiExcepcion ? ex.Message : "Ocurrió un error: " + ex.Message);
        }
        finally
        {
            btnEnviar.IsEnabled = true;
            txtMensaje.IsEnabled = true;
        }
    }

    /// <summary>Dibuja la burbuja del chat: el usuario a la derecha y la IA a la izquierda.</summary>
    private void AgregarChat(string quien, string texto)
    {
        contenedorChat.Add(quien == "Tú" ? BurbujaUsuario(texto) : BurbujaIa(quien, texto));

        ScrollAlFinal();
    }

    private static View BurbujaUsuario(string texto) => new Border
    {
        Style = Ui.EstiloDe("TarjetaLista"),
        BackgroundColor = Ui.ColorDe("Primario"),
        Stroke = Ui.ColorDe("PrimarioOscuro"),
        Padding = new Thickness(8, 6),
        HorizontalOptions = LayoutOptions.End,
        MaximumWidthRequest = 320,
        Content = new Label
        {
            Text = texto,
            FontSize = 13,
            TextColor = Ui.ColorDe("Blanco"),
            LineBreakMode = LineBreakMode.WordWrap
        }
    };

    private static View BurbujaIa(string quien, string texto)
    {
        var pila = new VerticalStackLayout { Spacing = 3 };
        pila.Add(new Label
        {
            Text = quien,
            Style = Ui.EstiloDe("EncabezadoTablaClaro"),
            TextColor = Ui.ColorDe("Acento")
        });
        pila.Add(new Label
        {
            Text = texto,
            FontSize = 13,
            TextColor = Ui.ColorDe("TextoClaro"),
            LineBreakMode = LineBreakMode.WordWrap
        });

        return new Border
        {
            Style = Ui.EstiloDe("TarjetaLista"),
            BackgroundColor = Ui.ColorDe("SuperficieAlta"),
            Stroke = Ui.ColorDe("SuperficieBorde"),
            Padding = new Thickness(8, 6),
            HorizontalOptions = LayoutOptions.Start,
            MaximumWidthRequest = 360,
            Content = pila
        };
    }

    private void ScrollAlFinal()
    {
        // Se difiere el desplazamiento para que el contenedor ya tenga el alto recalculado.
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                await Task.Delay(60);
                await scrollChat.ScrollToAsync(0, Math.Max(0, contenedorChat.Height), false);
            }
            catch (Exception)
            {
                // el desplazamiento es solo cosmético
            }
        });
    }
}
