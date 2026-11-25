using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// QR de salida: se habilita al pagar el ticket y dura 10 minutos.
///
/// Muestra el mismo ticket que la pantalla del puesto (<see cref="TicketDigital"/>), con el
/// QR grande, el distintivo del conductor y los datos del pase; solo cambia el panel del valor,
/// que aquí indica lo pagado y el tiempo que queda para salir.
/// </summary>
public partial class vSalida : ContentPage
{
    private IDispatcherTimer? _reloj;
    private readonly Dictionary<long, Label> _cuentas = new();
    private bool _desbloqueado;
    private bool _verificando;
    private double _anchoPintado;
    private double _altoPintado;

    public vSalida()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await PaseSalida.SincronizarQrsPagadosAsync();
            PaseSalida.Cambio -= OnPaseCambio;
            PaseSalida.Cambio += OnPaseCambio;

            if (_verificando) return;

            if (_desbloqueado)
            {
                MostrarQr();
                return;
            }

            if (!await BiometriaServicio.DispositivoExigeVerificacionBiometricaAsync())
            {
                Registro.Info("vSalida", "Sin huella en el celular: se muestra el QR sin verificación.");
                MostrarQr();
                return;
            }

            // Hay huella en el equipo: pide confirmación antes de mostrar el QR.
            vistaQr.IsVisible = false;
            panelCandado.IsVisible = true;
            _reloj?.Stop();
        }
        catch (Exception ex)
        {
            Registro.Error("vSalida", "No se pudo preparar la pantalla de salida.", ex);
        }
    }

    private async void OnDesbloquearClicked(object? sender, EventArgs e)
    {
        if (_verificando) return;
        _verificando = true;
        try
        {
            if (!await BiometriaServicio.DesbloquearQrAsync(this))
            {
                Registro.Aviso("vSalida", "El usuario no superó la verificación de huella; el QR sigue oculto.");
                return;
            }

            Registro.Info("vSalida", "QR de salida desbloqueado con huella.");
            MostrarQr();
        }
        catch (Exception ex)
        {
            // El sensor puede fallar o no existir: la pantalla debe seguir usable.
            Registro.Error("vSalida", "Falló la verificación de huella al desbloquear el QR.", ex);
            await Ui.Aviso(this, "No se pudo verificar la huella. Inténtalo otra vez.", "Verificación");
        }
        finally
        {
            _verificando = false;
        }
    }

    private void MostrarQr()
    {
        _desbloqueado = true;
        panelCandado.IsVisible = false;
        vistaQr.IsVisible = true;
        Pintar();
        IniciarReloj();
    }

    protected override void OnDisappearing()
    {
        PaseSalida.Cambio -= OnPaseCambio;
        if (!_verificando)
        {
            _desbloqueado = false;
            _reloj?.Stop();
        }
        base.OnDisappearing();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (!_desbloqueado || width <= 0 || height <= 0) return;
        if (Math.Abs(width - _anchoPintado) < 12 && Math.Abs(height - _altoPintado) < 12) return;
        _anchoPintado = width;
        _altoPintado = height;
        Pintar();
    }

    private void OnPaseCambio(object? sender, EventArgs e)
    {
        if (_desbloqueado) Dispatcher.Dispatch(Pintar);
    }

    private double AnchoUtil() => Width > 80 ? Width : 360;

    /// <summary>Lado del QR: ocupa el ancho disponible sin desbordar la tarjeta.</summary>
    private double LadoQr() => Math.Clamp(AnchoUtil() - 110, 160, 260);

    private void Pintar()
    {
        try
        {
            PintarPases();
        }
        catch (Exception ex)
        {
            Registro.Error("vSalida", "No se pudo dibujar el ticket de salida.", ex);
            contenedorPases.Clear();
            contenedorPases.Add(new Label
            {
                Text = "No se pudo mostrar el QR. Cierra esta pantalla y vuelve a abrirla.",
                Style = Ui.EstiloDe("TextoSuave"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24)
            });
        }
    }

    private void PintarPases()
    {
        var pases = PaseSalida.Activos.OrderBy(p => p.ExpiraEn).ToList();
        contenedorPases.Clear();
        _cuentas.Clear();

        if (pases.Count == 0)
        {
            lblAyuda.Text = "No hay un QR vigente. Se habilita al pagar un ticket y dura 10 minutos.";
            contenedorPases.Add(new Label
            {
                Text = "Todos los pases ya vencieron.",
                Style = Ui.EstiloDe("TextoSuave"),
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 24)
            });
            return;
        }

        lblAyuda.Text = "Muestra este QR en la salida. Vence a los 10 minutos del pago.";

        foreach (var pase in pases)
            contenedorPases.Add(Tarjeta(pase));
    }

    /// <summary>
    /// Ticket completo del pase pagado. Reutiliza <see cref="TicketDigital"/> para que el
    /// cliente vea el mismo comprobante que en la pantalla del puesto.
    /// </summary>
    private View Tarjeta(PaseSalida.Pase pase)
    {
        var modelo = Modelo(pase);

        var cuenta = new Label
        {
            Text = PaseSalida.TextoRestante(pase),
            FontFamily = "OpenSansSemibold",
            FontSize = 26,
            TextColor = Ui.ColorDe("Acento"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _cuentas[pase.Id] = cuenta;

        // Panel del valor pagado + tiempo restante para salir.
        var panel = new Border
        {
            BackgroundColor = Color.FromArgb("#141B24"),
            Stroke = Ui.ColorDe("Exito"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 1,
                Children =
                {
                    new Label
                    {
                        Text = "TICKET PAGADO",
                        FontFamily = "OpenSansSemibold",
                        FontSize = 10,
                        CharacterSpacing = 1,
                        TextColor = Ui.ColorDe("TextoClaroSuave"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = Ui.Moneda(pase.Monto),
                        FontFamily = "OpenSansSemibold",
                        FontSize = 30,
                        TextColor = Ui.ColorDe("Exito"),
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    cuenta,
                    new Label
                    {
                        Text = "Tiempo para salir",
                        FontSize = 10,
                        TextColor = Ui.ColorDe("TextoClaroSuave"),
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };

        var contenido = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                TicketDigital.Crear(modelo, LadoQr()),
                panel
            }
        };

        return contenido;
    }

    /// <summary>Arma el pase completo con los datos que guardó el cobro.</summary>
    private static PasePuestoDto Modelo(PaseSalida.Pase pase)
    {
        var datos = pase.Datos;
        if (datos is not null)
        {
            // El pase guardado al cobrar ya trae todos los datos del ticket original.
            datos.QrPayload = pase.Payload;
            return datos;
        }

        // Respaldo 1: el detalle completo quedó en SQLite al abrir el ticket del puesto.
        // Se usa cuando el JSON con todo el ticket no cabía en Preferences (varios
        // vehículos a la vez) y hubo que guardar el pase reducido.
        try
        {
            var guardado = BaseLocal.LeerPase(pase.Id);
            if (guardado is not null)
            {
                guardado.QrPayload = pase.Payload;
                return guardado;
            }
        }
        catch (Exception ex)
        {
            Registro.Aviso("vSalida", $"No se pudo leer el detalle del pase en SQLite: {ex.Message}");
        }

        // Respaldo 2: se arma con los datos mínimos que sí guarda el pase.
        return new PasePuestoDto
        {
            TicketId = pase.Id,
            TicketCodigo = pase.Codigo,
            Placa = pase.Placa,
            EspacioCodigo = pase.Plaza,
            SedeNombre = pase.Sede,
            TarifaHora = pase.TarifaHora,
            Entrada = pase.Entrada,
            MontoAhora = pase.Monto,
            Estado = "CERRADO",
            Usuario = pase.Usuario,
            Descuento = pase.Descuento,
            PerfilConductor = pase.Perfil,
            Marca = pase.Marca,
            Color = pase.Color,
            QrPayload = pase.Payload
        };
    }

    private void IniciarReloj()
    {
        _reloj?.Stop();
        _reloj = Dispatcher.CreateTimer();
        _reloj.Interval = TimeSpan.FromSeconds(1);
        _reloj.Tick += (_, _) =>
        {
            try
            {
                var pases = PaseSalida.Activos;
                if (pases.Count == 0)
                {
                    _reloj?.Stop();
                    Pintar();
                    return;
                }

                if (_cuentas.Count != pases.Count)
                {
                    Pintar();
                    return;
                }

                foreach (var pase in pases)
                {
                    if (_cuentas.TryGetValue(pase.Id, out var lbl))
                        lbl.Text = PaseSalida.TextoRestante(pase);
                }
            }
            catch (Exception ex)
            {
                Registro.Aviso("vSalida", $"Falló la actualización del reloj de salida: {ex.Message}");
            }
        };
        _reloj.Start();
    }
}
