using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using QRCoder;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Dibuja el ticket digital con el diseño de la marca: franja amarilla, datos del ticket,
/// distintivo del conductor, código QR grande y pie con la sede y el logo.
///
/// Lo usan la pantalla del puesto (<c>vQrPuesto</c>) y la de salida (<c>vSalida</c>), para que
/// el cliente vea siempre el mismo comprobante: antes la salida mostraba una tarjeta resumida
/// y el usuario lo percibía como «otro ticket».
/// </summary>
public static class TicketDigital
{
    private static readonly Color Tinta = Color.FromArgb("#111827");
    private static readonly Color Crema = Color.FromArgb("#FEF7E0");
    private static readonly Color Marron = Color.FromArgb("#78350F");
    private static readonly Color Ambar = Color.FromArgb("#B45309");
    private static readonly Color Gris = Color.FromArgb("#6B7280");
    private static readonly Color Acento = Color.FromArgb("#F2C51A");

    /// <summary>
    /// Tarjeta blanca del ticket. Devuelve el control y, por referencia, la imagen del QR
    /// (para poder regenerarla si cambia el contenido).
    /// </summary>
    public static View Crear(PasePuestoDto pase, double anchoQr = 190)
    {
        var cultura = CultureInfo.GetCultureInfo("es-EC");

        // ---------- cabecera ----------
        // Sin FontFamily: la app solo registra OpenSans, y pedir "monospace" hacía que
        // FontManager.FindFontFamilyName fallara al dibujar el ticket.
        var izquierda = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                Etiqueta($"N: {Codigo(pase)}", 10, null, Tinta),
                Etiqueta(pase.Entrada.ToString("dd/MM/yyyy", cultura), 10, null, Tinta),
                Etiqueta(PlacaEcuador.Formatear(pase.Placa).Replace("-", ""), 11, null, Tinta, bold: true)
            }
        };

        var centro = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.Center
        };
        var perfil = Perfil(pase.PerfilConductor, pase.Descuento);
        if (perfil is not null) centro.Children.Add(perfil);

        var detalleVehiculo = string.Join(" · ",
            new[] { pase.Marca?.Trim(), pase.Color?.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s)));
        if (detalleVehiculo.Length > 0)
        {
            centro.Children.Add(new Label
            {
                Text = detalleVehiculo,
                FontSize = 7,
                TextColor = Gris,
                HorizontalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.TailTruncation
            });
        }

        var derecha = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Start,
            Children =
            {
                Etiqueta($"E: {NumeroEntrada(pase)}", 10, null, Tinta, alineacion: TextAlignment.End),
                Etiqueta(pase.Entrada.ToString("HH:mm", cultura), 10, null, Tinta, alineacion: TextAlignment.End)
            }
        };

        var cabecera = new Grid
        {
            Padding = new Thickness(12, 6, 12, 0),
            ColumnSpacing = 6,
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ]
        };
        cabecera.Add(izquierda, 0, 0);
        cabecera.Add(centro, 1, 0);
        cabecera.Add(derecha, 2, 0);

        // ---------- QR ----------
        var qr = new Image
        {
            Source = ImagenQr(string.IsNullOrWhiteSpace(pase.QrPayload) ? PayloadLocal(pase) : pase.QrPayload),
            WidthRequest = anchoQr,
            HeightRequest = anchoQr,
            Aspect = Aspect.AspectFit
        };
        var zonaQr = new Grid { Padding = new Thickness(0, 8, 0, 12), HorizontalOptions = LayoutOptions.Center };
        zonaQr.Add(qr);

        // ---------- pie ----------
        var separador = new BoxView { HeightRequest = 2, Color = Acento, Margin = new Thickness(13, 0) };

        var pieIzquierda = new VerticalStackLayout
        {
            Spacing = 3,
            Children =
            {
                Etiqueta(string.IsNullOrWhiteSpace(pase.SedeNombre) ? "URBAN PARK" : pase.SedeNombre!.ToUpperInvariant(),
                    11, null, Tinta, espaciado: 0.5),
                Logo()
            }
        };

        var pieDerecha = new VerticalStackLayout
        {
            Spacing = 0,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
            Children =
            {
                Etiqueta("FAVOR VER", 8, null, Ambar, bold: true, alineacion: TextAlignment.End, espaciado: 0.4),
                Etiqueta("AL REVERSO →", 8, null, Ambar, bold: true, alineacion: TextAlignment.End, espaciado: 0.4)
            }
        };

        var pie = new Grid
        {
            Padding = new Thickness(13, 8, 13, 11),
            ColumnSpacing = 8,
            ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)]
        };
        pie.Add(pieIzquierda, 0, 0);
        pie.Add(pieDerecha, 1, 0);

        // ---------- franja superior ----------
        var franja = new Polygon
        {
            Points =
            {
                new Point(0, 26),
                new Point(80, 0),
                new Point(160, 26)
            },
            Fill = new SolidColorBrush(Acento),
            HeightRequest = 15,
            HorizontalOptions = LayoutOptions.Fill
        };

        var contenido = new VerticalStackLayout { Spacing = 0 };
        contenido.Children.Add(franja);
        contenido.Children.Add(cabecera);
        contenido.Children.Add(zonaQr);
        contenido.Children.Add(separador);
        contenido.Children.Add(pie);

        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = Acento,
            StrokeThickness = 2,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Padding = 0,
            Shadow = new Shadow { Brush = Colors.Black, Offset = new Point(0, 4), Radius = 14, Opacity = 0.45f },
            Content = contenido
        };
    }

    /// <summary>Panel del valor: para la entrada es «a pagar»; para la salida, «pagado».</summary>
    public static View PanelValor(string etiqueta, string monto, string tiempo, string calculo,
        Color colorMonto, out Label lblTiempo)
    {
        lblTiempo = new Label
        {
            Text = tiempo,
            FontSize = 12,
            TextColor = Color.FromArgb("#B6C2D1"),
            HorizontalTextAlignment = TextAlignment.Center
        };

        var contenido = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                new Label
                {
                    Text = etiqueta,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 10,
                    CharacterSpacing = 1,
                    TextColor = Color.FromArgb("#94A3B8"),
                    HorizontalTextAlignment = TextAlignment.Center
                },
                new Label
                {
                    Text = monto,
                    FontFamily = "OpenSansSemibold",
                    FontSize = 32,
                    TextColor = colorMonto,
                    HorizontalTextAlignment = TextAlignment.Center
                },
                lblTiempo,
                new Label
                {
                    Text = calculo,
                    FontSize = 10,
                    TextColor = Color.FromArgb("#7C8A9C"),
                    HorizontalTextAlignment = TextAlignment.Center
                }
            }
        };

        return new Border
        {
            BackgroundColor = Color.FromArgb("#141B24"),
            Stroke = Acento,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12, 8),
            Content = contenido
        };
    }

    // ================================================================ piezas
    private static Label Etiqueta(string texto, double size, string? font, Color color,
        bool bold = false, TextAlignment alineacion = TextAlignment.Start, double espaciado = 0)
    {
        var l = new Label
        {
            Text = texto,
            FontSize = size,
            TextColor = color,
            HorizontalTextAlignment = alineacion,
            LineBreakMode = LineBreakMode.NoWrap
        };
        if (font is not null) l.FontFamily = font;
        if (bold) l.FontAttributes = FontAttributes.Bold;
        if (espaciado > 0) l.CharacterSpacing = espaciado;
        return l;
    }

    private static View? Perfil(string? perfil, int descuento)
    {
        var (icono, nombre) = Icono(perfil);
        if (string.IsNullOrWhiteSpace(nombre)) return null;

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
                    FontSize = 7.5,
                    TextColor = Marron,
                    LineBreakMode = LineBreakMode.NoWrap
                }
            }
        };

        if (descuento > 0)
        {
            textos.Children.Add(new Label
            {
                Text = $"{descuento}% descuento",
                FontSize = 7,
                TextColor = Ambar,
                LineBreakMode = LineBreakMode.NoWrap
            });
        }

        var fila = new HorizontalStackLayout
        {
            Spacing = 4,
            Children =
            {
                new Label { Text = icono, FontSize = 14, VerticalOptions = LayoutOptions.Center },
                textos
            }
        };

        return new Border
        {
            BackgroundColor = Crema,
            Stroke = Acento,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            Padding = new Thickness(7, 3),
            HorizontalOptions = LayoutOptions.Center,
            Content = fila
        };
    }

    private static View Logo()
    {
        var logo = new Image
        {
            Source = "logo_urbanpark.png",
            WidthRequest = 26,
            HeightRequest = 26,
            Aspect = Aspect.AspectFit,
            VerticalOptions = LayoutOptions.Center
        };

        var nombre = new Label { VerticalOptions = LayoutOptions.Center, FontSize = 14, FontFamily = "OpenSansSemibold" };
        var texto = new FormattedString();
        texto.Spans.Add(new Span { Text = "URBAN", TextColor = Tinta });
        texto.Spans.Add(new Span { Text = " PARK", TextColor = Color.FromArgb("#C9A012") });
        nombre.FormattedText = texto;

        var fila = new HorizontalStackLayout { Spacing = 6 };
        fila.Children.Add(logo);
        fila.Children.Add(nombre);
        return fila;
    }

    private static string Codigo(PasePuestoDto pase)
    {
        var digitos = new string((pase.TicketCodigo ?? "").Where(char.IsDigit).ToArray());
        if (digitos.Length == 0) return pase.TicketId.ToString("D7");
        return digitos.Length > 7 ? digitos[^7..] : digitos.PadLeft(7, '0');
    }

    private static string NumeroEntrada(PasePuestoDto pase)
    {
        var digitos = new string((pase.EspacioCodigo ?? "").Where(char.IsDigit).ToArray());
        return digitos.Length == 0 ? "000" : digitos.PadLeft(3, '0');
    }

    public static ImageSource ImagenQr(string texto)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(datos);
        var bytes = png.GetGraphic(16);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }

    /// <summary>Contenido del QR cuando el pase no trae el campo del servidor.</summary>
    private static string PayloadLocal(PasePuestoDto pase) =>
        string.Join('|',
            "URBANPARK",
            "ESTACION",
            pase.TicketCodigo,
            pase.SedeCodigo,
            pase.SedeNombre,
            pase.SedeDireccion,
            pase.EspacioCodigo,
            pase.Zona,
            pase.Placa,
            pase.Usuario,
            pase.Entrada.ToString("O", CultureInfo.InvariantCulture),
            pase.TarifaHora.ToString("0.00", CultureInfo.InvariantCulture),
            pase.Descuento.ToString(CultureInfo.InvariantCulture),
            Ui.MontoEstimado(pase.Entrada, pase.TarifaHora, pase.Descuento)
                .ToString("0.00", CultureInfo.InvariantCulture),
            pase.SedeLatitud.ToString("0.000000", CultureInfo.InvariantCulture),
            pase.SedeLongitud.ToString("0.000000", CultureInfo.InvariantCulture),
            pase.Modo,
            pase.SalidaPrevista?.ToString("O", CultureInfo.InvariantCulture) ?? "");

    private static (string Icono, string Nombre) Icono(string? perfil)
    {
        var p = (perfil ?? "").ToLowerInvariant();
        if (p.Length == 0) return ("", "");
        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial"))
            return ("♿", "Capacidades especiales");
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor"))
            return ("👴", "Tercera edad");
        if (p.Contains("embaraz"))
            return ("🤰", "Embarazada");
        if (p.Contains("estudiante") || p.Contains("estudiate"))
            return ("🎓", "Estudiante");
        if (p.Contains("mujer"))
            return ("👩", "Mujer");
        if (p.Contains("hombre"))
            return ("👨", "Hombre");
        return ("👤", perfil!);
    }
}
