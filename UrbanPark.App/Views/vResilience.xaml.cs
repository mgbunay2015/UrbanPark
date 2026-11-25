using System.Text.Json;
using System.Text.Json.Serialization;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Resilience (solo ADMIN): demo de Circuit Breaker + Retry + fallback.
/// En el sistema original los botones Detener/Iniciar usaban Docker; aquí el backend
/// los simula con una bandera (DemoDetenerAsync / DemoIniciarAsync).
/// El panel replica el estado de la pestaña #tab-resilience del frontend web.
/// </summary>
public partial class vResilience : ContentPage
{
    // ------------------------------------------------------------------ mensajes literales
    private const string Explicacion =
        "Demo de Circuit Breaker + Retry: tickets consulta la ocupación del servicio de espacios. " +
        "Si el servicio falla, se reintenta y se activa el fallback.";

    private const string Flujo =
        "Flujo sugerido: Detener → Probar 1 llamada (fallback) → Iniciar → Probar de nuevo (OK).";

    private const string Reposo =
        "Pulsa Probar 1 llamada para ver Circuit Breaker + Retry en acción.";

    private const string SinHistorial = "Sin pruebas aún.";

    // Tokens del sistema de diseño (Resources/Styles/Colors.xaml), nunca colores en linea.
    private const string TokenNeutro = "SuperficieBorde";
    private const string TokenOk = "Exito";
    private const string TokenFallback = "Advertencia";
    private const string TokenError = "Peligro";

    // ------------------------------------------------------------------ estado compartido
    private static int _consecutivo;
    private static int _ok;
    private static int _fallos;
    private static bool _ocupado;
    private static readonly List<EntradaResilience> Historial = [];

    // ------------------------------------------------------------------ estado de la vista
    private string _json = "—";
    private string _tituloEstado = "";
    private string _cuerpoEstado = Reposo;
    private string _pieEstado = "";
    private string _tokenBordeEstado = TokenNeutro;

    public vResilience()
    {
        InitializeComponent();

        lblExplicacion.Text = Explicacion;
        lblFlujo.Text = Flujo;
        lblEstadoEspacios.Text = "—";

        // El historial vive a nivel de pantalla: al volver se repinta tal cual estaba.
        RepintarHistorial();
        MostrarPanelEstado(_tituloEstado, _cuerpoEstado, _pieEstado, _tokenBordeEstado);
        PintarEstadoEspacios("—");
        lblJson.Text = string.IsNullOrWhiteSpace(_json) ? "—" : _json;
    }

    // ================================================================ guardia
    private async Task<bool> SinPermiso()
    {
        if (Sesion.EsAdmin) return false;

        await Ui.Error(this, "Solo ADMIN puede usar Resilience");
        return true;
    }

    // ================================================================ acciones de prueba
    private async void OnProbarUnaClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;
        await ProbarAsync(1, "Prueba Resilience completada");
    }

    private async void OnStressClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;
        await ProbarAsync(5, "Stress ×5 completado");
    }

    private async Task ProbarAsync(int llamadas, string mensajeFinal)
    {
        if (_ocupado) return;
        _ocupado = true;

        MostrarPanelEstado(
            $"Ejecutando {llamadas} llamada(s) a la ocupación…",
            "Espera mientras el Circuit Breaker + Retry responde.",
            "",
            TokenNeutro);

        var ultimoError = "";

        try
        {
            for (var i = 0; i < llamadas; i++)
            {
                var numero = ++_consecutivo;

                try
                {
                    var ocupacion = await ApiCliente.OcupacionRemotaAsync();
                    var ms = ocupacion.Ms ?? 0;

                    if (ocupacion.Fallback)
                    {
                        _fallos++;
                        _tituloEstado = "Fallback activo";
                        _cuerpoEstado = string.IsNullOrWhiteSpace(ocupacion.Mensaje)
                            ? "El servicio de espacios no respondió: se activó el fallback."
                            : ocupacion.Mensaje!.Trim();
                        _pieEstado = $"Origen: {Valor(ocupacion.Origen)} · {ms} ms";
                        _tokenBordeEstado = TokenFallback;

                        Historial.Insert(0, new EntradaResilience(
                            numero, "Fallback Resilience4j", "FALLBACK", TokenFallback, ms, DateTime.Now));
                    }
                    else
                    {
                        _ok++;
                        _tituloEstado = "Circuit Breaker + Retry OK";
                        _cuerpoEstado =
                            $"Ocupación live: total {ocupacion.Total} · ocupados {ocupacion.Ocupados} · " +
                            $"libres {ocupacion.Libres}";
                        _pieEstado =
                            $"{Valor(ocupacion.Resilience)} · origen: {Valor(ocupacion.Origen)} · {ms} ms";
                        _tokenBordeEstado = TokenOk;

                        Historial.Insert(0, new EntradaResilience(
                            numero, "Live espacios", "OK", TokenOk, ms, DateTime.Now));
                    }

                    _json = JsonBonito(ocupacion);
                }
                catch (Exception ex)
                {
                    _fallos++;
                    ultimoError = Mensaje(ex);

                    _tituloEstado = "Error en la prueba";
                    _cuerpoEstado = ultimoError;
                    _pieEstado = "";
                    _tokenBordeEstado = TokenError;
                    _json = ultimoError;

                    Historial.Insert(0, new EntradaResilience(
                        numero, ultimoError, "ERROR", TokenError, 0, DateTime.Now));
                }
                finally
                {
                    PintarContadores();
                    RepintarHistorial();
                    MostrarPanelEstado(_tituloEstado, _cuerpoEstado, _pieEstado, _tokenBordeEstado);
                    lblJson.Text = _json;
                }
            }

            if (string.IsNullOrEmpty(ultimoError))
                await Ui.Exito(this, mensajeFinal);
            else
                await Ui.Error(this, ultimoError);
        }
        finally
        {
            _ocupado = false;
        }
    }

    // ================================================================ limpiar
    private async void OnLimpiarClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;

        _ok = 0;
        _fallos = 0;
        _consecutivo = 0;
        Historial.Clear();

        _json = "—";
        _tituloEstado = "";
        _cuerpoEstado = Reposo;
        _pieEstado = "";
        _tokenBordeEstado = TokenNeutro;

        PintarContadores();
        RepintarHistorial();
        MostrarPanelEstado(_tituloEstado, _cuerpoEstado, _pieEstado, _tokenBordeEstado);
        lblJson.Text = "—";

        await Ui.Informar(this, "Historial Resilience limpiado");
    }

    // ================================================================ demo de espacios
    private async void OnDetenerEspaciosClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;

        var confirmado = await Ui.Confirmar(this, "Detener espacios",
            "¿Detener el servicio de espacios? Luego prueba Resilience para ver el fallback.");
        if (!confirmado) return;

        try
        {
            var respuesta = await ApiCliente.DemoDetenerAsync();
            PintarEstadoEspacios("espacios: DOWN");

            _tituloEstado = "Servicio de espacios detenido";
            _cuerpoEstado = Valor(respuesta?.Mensaje);
            _pieEstado = respuesta is null ? "" : $"acción: {Valor(respuesta.Action)}";
            _tokenBordeEstado = TokenError;
            _json = JsonBonito(respuesta);

            MostrarPanelEstado(_tituloEstado, _cuerpoEstado, _pieEstado, _tokenBordeEstado);
            lblJson.Text = _json;

            await Ui.Exito(this, _cuerpoEstado);
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
            await RefrescarEstadoEspacios(silencioso: true);
        }
    }

    private async void OnIniciarEspaciosClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;

        try
        {
            var respuesta = await ApiCliente.DemoIniciarAsync();
            PintarEstadoEspacios("espacios: UP");

            _tituloEstado = "Servicio de espacios iniciado";
            _cuerpoEstado = Valor(respuesta?.Mensaje);
            _pieEstado = respuesta is null ? "" : $"acción: {Valor(respuesta.Action)}";
            _tokenBordeEstado = TokenOk;
            _json = JsonBonito(respuesta);

            MostrarPanelEstado(_tituloEstado, _cuerpoEstado, _pieEstado, _tokenBordeEstado);
            lblJson.Text = _json;

            await Ui.Exito(this, _cuerpoEstado);

            // Tras iniciar se intenta sincronizar las plazas liberadas por pagos verificados.
            await SincronizarTrasIniciar();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
            await RefrescarEstadoEspacios(silencioso: true);
        }
    }

    private async Task SincronizarTrasIniciar()
    {
        await Ui.Informar(this, "Sincronizando plazas pendientes…");

        try
        {
            var sincronizacion = await ApiCliente.SincronizarLiberacionesAsync();

            if (sincronizacion is not null && sincronizacion.Liberadas > 0)
                await Ui.Exito(this, "Pagos verificados: plazas liberadas y disponibles");
            else
                await Ui.Aviso(this, "El servicio inició, pero aún no sincroniza. Usa «Sincronizar plazas» en Tickets.");
        }
        catch (Exception)
        {
            await Ui.Aviso(this, "El servicio inició, pero aún no sincroniza. Usa «Sincronizar plazas» en Tickets.");
        }
    }

    private async void OnEstadoEspaciosClicked(object? sender, EventArgs e)
    {
        if (await SinPermiso()) return;
        await RefrescarEstadoEspacios(silencioso: false);
    }

    private async Task RefrescarEstadoEspacios(bool silencioso)
    {
        try
        {
            var estado = await ApiCliente.DemoEstadoAsync();

            PintarEstadoEspacios(estado.Running ? "espacios: UP" : "espacios: DOWN");

            _tituloEstado = estado.Running ? "Servicio de espacios activo" : "Servicio de espacios detenido";
            _cuerpoEstado = Valor(estado.Mensaje);
            _pieEstado = $"servicio: {Valor(estado.Servicio)} · running: {(estado.Running ? "true" : "false")}";
            _tokenBordeEstado = estado.Running ? TokenOk : TokenError;
            _json = JsonBonito(estado);

            MostrarPanelEstado(_tituloEstado, _cuerpoEstado, _pieEstado, _tokenBordeEstado);
            lblJson.Text = _json;

            if (!silencioso) await Ui.Informar(this, "Estado actualizado");
        }
        catch (Exception ex)
        {
            PintarEstadoEspacios("estado: ?");

            _tituloEstado = "Error al consultar el estado";
            _cuerpoEstado = Mensaje(ex);
            _pieEstado = "";
            _tokenBordeEstado = TokenError;
            _json = _cuerpoEstado;

            MostrarPanelEstado(_tituloEstado, _cuerpoEstado, _pieEstado, _tokenBordeEstado);
            lblJson.Text = _json;

            if (!silencioso) await Ui.MostrarExcepcion(this, ex);
        }
    }

    // ================================================================ pintado
    private void PintarContadores()
    {
        lblStatOk.Text = _ok.ToString();
        lblStatFallos.Text = _fallos.ToString();
        lblStatTotal.Text = (_ok + _fallos).ToString();
    }

    /// <summary>Pinta la pildora "espacios: UP / DOWN / ?" con el badge que corresponde.</summary>
    private void PintarEstadoEspacios(string texto)
    {
        lblEstadoEspacios.Text = texto;

        if (texto.EndsWith("UP", StringComparison.Ordinal))
        {
            pillEstadoEspacios.Style = Ui.EstiloDe("BadgeOk");
            lblEstadoEspacios.Style = Ui.EstiloDe("BadgeOkTexto");
        }
        else if (texto.EndsWith("DOWN", StringComparison.Ordinal))
        {
            pillEstadoEspacios.Style = Ui.EstiloDe("BadgeOcupado");
            lblEstadoEspacios.Style = Ui.EstiloDe("BadgeOcupadoTexto");
        }
        else if (texto.EndsWith("?", StringComparison.Ordinal))
        {
            pillEstadoEspacios.Style = Ui.EstiloDe("BadgeAviso");
            lblEstadoEspacios.Style = Ui.EstiloDe("BadgeAvisoTexto");
        }
        else
        {
            pillEstadoEspacios.Style = Ui.EstiloDe("Badge");
            lblEstadoEspacios.Style = Ui.EstiloDe("BadgeTexto");
        }
    }

    /// <summary>Panel de estado: el borde y el titulo toman el color del resultado (token del sistema).</summary>
    private void MostrarPanelEstado(string titulo, string cuerpo, string pie, string tokenBorde)
    {
        lblEstadoTitulo.Text = titulo;
        lblEstadoTitulo.IsVisible = !string.IsNullOrWhiteSpace(titulo);

        lblEstadoCuerpo.Text = cuerpo;
        lblEstadoCuerpo.IsVisible = !string.IsNullOrWhiteSpace(cuerpo);

        lblEstadoPie.Text = pie;
        lblEstadoPie.IsVisible = !string.IsNullOrWhiteSpace(pie);

        panelEstado.Stroke = Ui.ColorDe(tokenBorde);
        panelEstado.StrokeThickness = 2;
        lblEstadoTitulo.TextColor = Ui.ColorDe(TokenTextoDe(tokenBorde));
    }

    /// <summary>Token de texto que acompaña a cada color de estado.</summary>
    private static string TokenTextoDe(string token) => token switch
    {
        TokenOk => "ExitoTexto",
        TokenFallback => "AdvertenciaTexto",
        TokenError => "PeligroTexto",
        _ => "TextoClaro"
    };

    /// <summary>Repinta la lista con las entradas más recientes arriba.</summary>
    private void RepintarHistorial()
    {
        listaHistorial.Clear();

        if (Historial.Count == 0)
        {
            listaHistorial.Add(new Label
            {
                Text = SinHistorial,
                Style = Ui.EstiloDe("EstadoVacio"),
                HorizontalOptions = LayoutOptions.Fill
            });
            return;
        }

        foreach (var entrada in Historial)
            listaHistorial.Add(ConstruirFila(entrada));
    }

    private static Border ConstruirFila(EntradaResilience entrada)
    {
        var insigniaEstilo = entrada.Insignia switch
        {
            "OK" => "BadgeOk",
            "FALLBACK" => "BadgeAviso",
            _ => "BadgeOcupado"
        };

        var insigniaTextoEstilo = entrada.Insignia switch
        {
            "OK" => "BadgeOkTexto",
            "FALLBACK" => "BadgeAvisoTexto",
            _ => "BadgeOcupadoTexto"
        };

        var cabecera = new Grid { ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)] };

        var titulo = new Label
        {
            Text = $"#{entrada.Numero} · {entrada.Etiqueta}",
            Style = Ui.EstiloDe("DatoFilaFuerte"),
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(titulo, 0);
        cabecera.Add(titulo);

        var insignia = new Border
        {
            Style = Ui.EstiloDe(insigniaEstilo),
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = entrada.Insignia,
                Style = Ui.EstiloDe(insigniaTextoEstilo)
            }
        };
        Grid.SetColumn(insignia, 1);
        cabecera.Add(insignia);

        var pila = new VerticalStackLayout { Spacing = 4 };
        pila.Add(cabecera);
        pila.Add(new Label
        {
            Text = $"{entrada.Hora:HH:mm:ss} · {entrada.Ms} ms",
            Style = Ui.EstiloDe("TextoSuave")
        });

        return new Border
        {
            Style = Ui.EstiloDe("TarjetaLista"),
            Margin = new Thickness(0, 0, 0, 8),
            Content = pila
        };
    }

    // ================================================================ utilidades
    private static string Valor(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? "—" : texto.Trim();

    private static string Mensaje(Exception ex) =>
        ex is ApiExcepcion ? ex.Message : "Ocurrió un error: " + ex.Message;

    private static string JsonBonito(object? valor)
    {
        if (valor is null) return "—";

        try
        {
            return JsonSerializer.Serialize(valor, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never
            });
        }
        catch (Exception)
        {
            return "—";
        }
    }

    /// <summary>Una entrada del historial de pruebas (lo más reciente primero).</summary>
    private sealed record EntradaResilience(
        int Numero,
        string Etiqueta,
        string Insignia,
        string Token,
        long Ms,
        DateTime Hora);
}
