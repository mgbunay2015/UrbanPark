using Microsoft.Maui.Controls.Shapes;
using QRCoder;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>QRs de salida vigentes, uno por vehículo cobrado, con cuenta atrás de 10 minutos.</summary>
public partial class vSalida : ContentPage
{
    private IDispatcherTimer? _reloj;
    private readonly Dictionary<string, Label> _cuentas = new(StringComparer.OrdinalIgnoreCase);
    private bool _desbloqueado;
    private bool _verificando;
    private double _anchoPintado;
    private double _altoPintado;

    public vSalida()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        PaseSalida.Cambio -= OnPaseCambio;
        PaseSalida.Cambio += OnPaseCambio;
        if (_verificando)
            return;
        if (_desbloqueado)
        {
            panelCandado.IsVisible = false;
            vistaQr.IsVisible = true;
            Pintar();
            IniciarReloj();
            return;
        }

        vistaQr.IsVisible = false;
        panelCandado.IsVisible = true;
        _reloj?.Stop();
    }

    private async void OnDesbloquearClicked(object? sender, EventArgs e)
    {
        _verificando = true;
        try
        {
            if (!await BiometriaServicio.DesbloquearQrAsync(this))
                return;

            _desbloqueado = true;
            panelCandado.IsVisible = false;
            vistaQr.IsVisible = true;
            Pintar();
            IniciarReloj();
        }
        finally
        {
            _verificando = false;
        }
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
        if (!_desbloqueado || width <= 0 || height <= 0)
            return;
        if (Math.Abs(width - _anchoPintado) < 12 && Math.Abs(height - _altoPintado) < 12)
            return;
        _anchoPintado = width;
        _altoPintado = height;
        Pintar();
    }

    private void OnPaseCambio(object? sender, EventArgs e)
    {
        if (_desbloqueado) Dispatcher.Dispatch(Pintar);
    }

    private double AnchoUtil() => Width > 80 ? Width : 360;

    private double AltoUtil() => Height > 80 ? Height : 640;

    /// <summary>Alto de un pase para que, con el título, llene la pantalla y el ticket quede sobre la barra inferior.</summary>
    private double AltoPase() => Math.Max(AltoUtil() - 132, 300);

    /// <summary>Lado del QR: el mayor cuadrado que cabe junto al ticket.</summary>
    private double LadoQr()
    {
        var porAncho = AnchoUtil() - 52;
        var porAlto = AltoPase() - 176;
        return Math.Max(180, Math.Min(porAncho, porAlto));
    }

    private void Pintar()
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

        lblAyuda.Text = pases.Count == 1
            ? "Muestra este QR en la salida. Vence a los 10 minutos del pago."
            : $"Tienes {pases.Count} QRs activos. Muestra el de cada vehículo en la salida.";

        foreach (var pase in pases)
            contenedorPases.Add(Tarjeta(pase));
    }

    private View Tarjeta(PaseSalida.Pase pase)
    {
        var cuenta = new Label
        {
            Text = PaseSalida.TextoRestante(pase),
            FontFamily = "OpenSansSemibold",
            FontSize = 32,
            TextColor = Ui.ColorDe("AcentoOscuro"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        _cuentas[pase.Codigo] = cuenta;

        var lado = LadoQr();
        var qr = new Border
        {
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = 10,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = new Image
            {
                Source = ImagenQr(pase.Payload),
                WidthRequest = lado,
                HeightRequest = lado,
                Aspect = Aspect.AspectFit
            }
        };

        var tiempo = new Label
        {
            Text = "Tiempo para salir",
            Style = Ui.EstiloDe("TextoSuave"),
            HorizontalTextAlignment = TextAlignment.Center
        };
        var cuerpoQr = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 2
        };
        cuerpoQr.Add(qr, 0, 0);
        cuerpoQr.Add(cuenta, 0, 1);
        cuerpoQr.Add(tiempo, 0, 2);

        var tarjetaQr = new Border
        {
            Style = Ui.EstiloDe("TarjetaClara"),
            Padding = new Thickness(12, 12, 12, 8),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = cuerpoQr
        };

        var encabezado = new Label { Text = "TICKET PAGADO", Style = Ui.EstiloDe("EncabezadoTablaClaro"), VerticalOptions = LayoutOptions.Center };
        var monto = new Label
        {
            Text = $"Pagado {Ui.Moneda(pase.Monto)}",
            FontFamily = "OpenSansSemibold",
            FontSize = 18,
            TextColor = Ui.ColorDe("Exito"),
            HorizontalTextAlignment = TextAlignment.End,
            VerticalOptions = LayoutOptions.Center
        };
        var placa = new Label
        {
            Text = pase.Placa,
            FontFamily = "OpenSansSemibold",
            FontSize = 32,
            TextColor = Ui.ColorDe("TextoClaro")
        };
        var detalle = new Label
        {
            Text = $"Ticket {pase.Codigo}" +
                   (string.IsNullOrWhiteSpace(pase.Plaza) ? "" : $"   ·   plaza {pase.Plaza}"),
            Style = Ui.EstiloDe("TextoSuaveClaro"),
            FontSize = 15
        };

        var cuerpoTicket = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 4
        };
        cuerpoTicket.Add(encabezado, 0, 0);
        cuerpoTicket.Add(monto, 1, 0);
        cuerpoTicket.Add(placa, 0, 1);
        Grid.SetColumnSpan(placa, 2);
        cuerpoTicket.Add(detalle, 0, 2);
        Grid.SetColumnSpan(detalle, 2);

        var ticket = new Border
        {
            Style = Ui.EstiloDe("Tarjeta"),
            Padding = new Thickness(16, 14),
            HorizontalOptions = LayoutOptions.Fill,
            Content = cuerpoTicket
        };

        var paseVista = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 10,
            HeightRequest = AltoPase(),
            HorizontalOptions = LayoutOptions.Fill,
            Children = { tarjetaQr, ticket }
        };
        Grid.SetRow(ticket, 1);
        return paseVista;
    }

    private void IniciarReloj()
    {
        _reloj?.Stop();
        _reloj = Dispatcher.CreateTimer();
        _reloj.Interval = TimeSpan.FromSeconds(1);
        _reloj.Tick += (_, _) =>
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
                if (_cuentas.TryGetValue(pase.Codigo, out var lbl))
                    lbl.Text = PaseSalida.TextoRestante(pase);
            }
        };
        _reloj.Start();
    }

    private static ImageSource ImagenQr(string texto)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(datos);
        var bytes = png.GetGraphic(16);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }
}
