using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;

namespace UrbanPark.App.Servicios;

/// <summary>
/// Placa de Ecuador dibujada con controles de MAUI, con las proporciones oficiales
/// (404 × 154 mm): franja naranja con el logo ANT y «ECUADOR», cuerpo claro y numeración
/// grande. Debajo se muestran los datos del vehículo. Se genera al instante, sin imágenes
/// guardadas en el servidor.
/// </summary>
public static class PlacaEcuador
{
    private static readonly Color Tinta = Color.FromArgb("#0F172A");
    private static readonly Color Naranja = Color.FromArgb("#F2A03C");
    private static readonly Color CuerpoPlaca = Color.FromArgb("#EDF0F4");
    private static readonly Color TextoClaro = Color.FromArgb("#F8FAFC");
    private static readonly Color TextoSuave = Color.FromArgb("#7C8A9C");
    private static readonly Color Acento = Color.FromArgb("#F2C51A");

    /// <summary>Solo la placa; <paramref name="ancho"/> en unidades de pantalla.</summary>
    public static View Crear(string? placa, string? tipo, double ancho, bool resaltada = true)
    {
        var codigo = Formatear(placa);
        var anchoPlaca = Math.Clamp(ancho, 100, 210);
        var alto = anchoPlaca / 2.62;
        var altoCabecera = Math.Clamp(alto * 0.23, 11, 18);

        var cabecera = new Border
        {
            BackgroundColor = Naranja,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(5, 5, 0, 0) },
            HeightRequest = altoCabecera,
            Padding = new Thickness(4, 0, 4, 0),
            Content = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ],
                ColumnSpacing = 3
            }
        };
        var barra = (Grid)cabecera.Content;
        barra.Add(LogoAnt(altoCabecera), 0, 0);
        barra.Add(Etiqueta("ECUADOR", Math.Clamp(altoCabecera * 0.40, 6, 10), 2.5, TextAlignment.Center), 0, 1);
        barra.Add(Etiqueta("D", Math.Clamp(altoCabecera * 0.52, 7, 13), 0, TextAlignment.End), 0, 2);

        var tamano = alto * 0.44;
        if (codigo.Length > 8) tamano *= 0.86;

        var cuerpo = new Grid
        {
            BackgroundColor = CuerpoPlaca,
            Children =
            {
                new Label
                {
                    Text = codigo,
                    FontFamily = "OpenSansSemibold",
                    FontAttributes = FontAttributes.Bold,
                    FontSize = Math.Clamp(tamano, 15, 36),
                    CharacterSpacing = 0.5,
                    TextColor = Tinta,
                    HorizontalTextAlignment = TextAlignment.Center,
                    VerticalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.NoWrap
                }
            }
        };

        var lienzo = new Grid
        {
            RowDefinitions = [new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Star)],
            RowSpacing = 0
        };
        lienzo.Add(cabecera, 0, 0);
        lienzo.Add(cuerpo, 0, 1);

        return new Border
        {
            BackgroundColor = Colors.White,
            Stroke = resaltada ? Acento : Color.FromArgb("#94A3B8"),
            StrokeThickness = resaltada ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = 0,
            WidthRequest = anchoPlaca,
            HeightRequest = alto,
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.35f, Radius = 8, Offset = new Point(0, 3) },
            Content = lienzo
        };
    }

    /// <summary>Datos del vehículo para poner debajo de la placa, en la misma tarjeta.</summary>
    public static View CrearDatos(VehiculoDto vehiculo, bool expandido = true)
    {
        var marca = (vehiculo.Marca ?? "").Trim();
        var color = (vehiculo.Color ?? "").Trim();

        var detalle = new List<string>();
        if (marca.Length > 0) detalle.Add(marca);
        if (color.Length > 0) detalle.Add(color);

        var textos = new VerticalStackLayout
        {
            Spacing = 1,
            Children =
            {
                new Label
                {
                    Text = $"{Ui.IconoTipo(vehiculo.Tipo)} {TipoLargo(vehiculo.Tipo)}",
                    FontFamily = "OpenSansSemibold",
                    FontSize = 10,
                    TextColor = TextoClaro,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = detalle.Count == 0 ? "Sin marca registrada" : string.Join(" · ", detalle),
                    FontSize = 9,
                    TextColor = TextoSuave,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = $"{PerfilEmoji(vehiculo.Propietario)} {PerfilCorto(vehiculo.Propietario)}",
                    FontSize = 9,
                    TextColor = TextoSuave,
                    LineBreakMode = LineBreakMode.TailTruncation
                }
            }
        };

        var descuento = Ui.DescuentoDe(vehiculo.Propietario);
        if (descuento <= 0) return textos;

        textos.Children.Add(new Border
        {
            BackgroundColor = Acento,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = 5 },
            Padding = new Thickness(5, 0),
            Margin = new Thickness(0, 2, 0, 0),
            HorizontalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = $"−{descuento}% descuento",
                FontFamily = "OpenSansSemibold",
                FontSize = 8,
                TextColor = Tinta
            }
        });

        return textos;
    }

    /// <summary>
    /// Datos del vehículo elegido, en una lista muy compacta: cada fila lleva la etiqueta
    /// a la izquierda y el valor a la derecha. Se coloca a la derecha de la placa.
    /// </summary>
    public static View CrearFichaCompleta(VehiculoDto vehiculo, bool estacionado)
    {
        var marca = (vehiculo.Marca ?? "").Trim();
        var color = (vehiculo.Color ?? "").Trim();
        var descuento = Math.Max(vehiculo.Descuento, Ui.DescuentoDe(vehiculo.Propietario));

        var contenido = new VerticalStackLayout { Spacing = 2 };

        contenido.Children.Add(Linea("TIPO", $"{Ui.IconoTipo(vehiculo.Tipo)} {TipoLargo(vehiculo.Tipo)}"));
        contenido.Children.Add(Linea("MARCA", marca.Length == 0 ? "Sin registrar" : marca));
        contenido.Children.Add(Linea("COLOR", color.Length == 0 ? "—" : color));
        contenido.Children.Add(Linea("DESC.", descuento > 0 ? $"{descuento}%" : "—", descuento > 0 ? Acento : null));
        contenido.Children.Add(Linea("COND.",
            $"{PerfilEmoji(vehiculo.Propietario)} {PerfilCorto(vehiculo.Propietario)}"));
        contenido.Children.Add(Linea("ESTADO", estacionado ? "🚗 Estacionado" : "🟢 Libre",
            estacionado ? Acento : Color.FromArgb("#34D399")));

        return new Border
        {
            BackgroundColor = Color.FromArgb("#0F1620"),
            Stroke = Color.FromArgb("#28323F"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = new Thickness(7, 5),
            VerticalOptions = LayoutOptions.Center,
            Content = contenido
        };
    }

    /// <summary>Fila etiqueta + valor, en una sola línea.</summary>
    private static View Linea(string etiqueta, string valor, Color? color = null)
    {
        var fila = new Grid
        {
            ColumnDefinitions = [new(new GridLength(46)), new(GridLength.Star)],
            ColumnSpacing = 4
        };

        fila.Add(new Label
        {
            Text = etiqueta,
            FontFamily = "OpenSansSemibold",
            FontSize = 6.5,
            CharacterSpacing = 0.4,
            TextColor = TextoSuave,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap
        }, 0);

        fila.Add(new Label
        {
            Text = valor,
            FontSize = 8,
            TextColor = color ?? TextoClaro,
            LineBreakMode = LineBreakMode.TailTruncation,
            VerticalOptions = LayoutOptions.Center,
            MaxLines = 1
        }, 1);

        return fila;
    }

    /// <summary>ABC1234 / abc-1234 → ABC-1234 (guion después de las tres primeras letras).</summary>
    public static string Formatear(string? placa)
    {
        var limpio = new string((placa ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return limpio.Length <= 3 ? limpio : limpio[..3] + "-" + limpio[3..];
    }

    private static Label Etiqueta(string texto, double tamano, double espaciado, TextAlignment alineacion) => new()
    {
        Text = texto,
        FontFamily = "OpenSansSemibold",
        FontAttributes = FontAttributes.Bold,
        FontSize = tamano,
        CharacterSpacing = espaciado,
        TextColor = Tinta,
        HorizontalTextAlignment = alineacion,
        VerticalOptions = LayoutOptions.Center
    };

    /// <summary>Franja tricolor del escudo ANT (marca, no el escudo oficial).</summary>
    private static View LogoAnt(double altoCabecera)
    {
        var alto = Math.Clamp(altoCabecera * 0.66, 8, 19);
        var ancho = alto * 1.85;

        var franjas = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(0.44, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(0.28, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(0.28, GridUnitType.Star))
            ]
        };
        franjas.Add(new BoxView { Color = Color.FromArgb("#FFD100") }, 0);
        franjas.Add(new BoxView { Color = Color.FromArgb("#0033A0") }, 1);
        franjas.Add(new BoxView { Color = Color.FromArgb("#EF3340") }, 2);

        return new Border
        {
            Stroke = Tinta,
            StrokeThickness = 0.8,
            StrokeShape = new RoundRectangle { CornerRadius = 2 },
            WidthRequest = ancho,
            HeightRequest = alto,
            Padding = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = new Grid
            {
                Children =
                {
                    franjas,
                    new Label
                    {
                        Text = "ANT",
                        FontFamily = "OpenSansSemibold",
                        FontSize = Math.Clamp(alto * 0.52, 4, 7.5),
                        TextColor = Colors.White,
                        HorizontalTextAlignment = TextAlignment.Center,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };
    }

    private static string TipoLargo(string? tipo) => (tipo ?? "AUTO").ToUpperInvariant() switch
    {
        "MOTO" => "Motocicleta",
        "CAMIONETA" => "Camioneta",
        _ => "Automóvil"
    };

    private static string PerfilCorto(string? perfil)
    {
        var p = (perfil ?? "").ToLowerInvariant();
        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial")) return "Capacidades especiales";
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor")) return "Tercera edad";
        if (p.Contains("embaraz")) return "Embarazada";
        if (p.Contains("estudiante") || p.Contains("estudiate")) return "Estudiante";
        if (p.Contains("mujer")) return "Mujer";
        if (p.Contains("hombre")) return "Hombre";
        return string.IsNullOrWhiteSpace(perfil) ? "Sin perfil" : perfil!;
    }

    private static string PerfilEmoji(string? perfil)
    {
        var p = (perfil ?? "").ToLowerInvariant();
        if (p.Contains("capacidades") || p.Contains("discapacit") || p.Contains("especial")) return "♿";
        if (p.Contains("tercera") || p.Contains("anciano") || p.Contains("mayor")) return "👴";
        if (p.Contains("embaraz")) return "🤰";
        if (p.Contains("estudiante") || p.Contains("estudiate")) return "🎓";
        if (p.Contains("mujer")) return "👩";
        if (p.Contains("hombre")) return "👨";
        return "👤";
    }
}
