using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Punto verde/rojo con el texto «En línea» / «Sin conexión», superpuesto en la esquina
/// superior derecha de cada pantalla.
///
/// Solo usa formas primitivas: un <see cref="BoxView"/> cuadrado como punto y una etiqueta.
/// Se evitó a propósito cualquier forma vectorial (Ellipse, RoundRectangle) y cualquier
/// <see cref="Border"/>: en WinUI esa combinación, superpuesta al contenido de una página con
/// <see cref="ScrollView"/>, disparaba <c>LayoutCycleException</c> y dejaba la app colgada
/// consumiendo memoria. Todas las medidas son fijas para que el texto nunca cambie el tamaño.
/// </summary>
public sealed class BarraConexion : Grid
{
    private static readonly Color Verde = Color.FromArgb("#22C55E");
    private static readonly Color Rojo = Color.FromArgb("#EF4444");

    private const double AnchoBadge = 100;
    private const double AltoBadge = 18;
    private const double AnchoTexto = 72;

    private static readonly List<WeakReference<BarraConexion>> Vivas = [];
    private static bool _escuchando;

    private readonly BoxView _punto;
    private readonly Label _texto;

    public BarraConexion()
    {
        _punto = new BoxView
        {
            WidthRequest = 7,
            HeightRequest = 7,
            Color = Rojo,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        _texto = new Label
        {
            Text = "Sin conexión",
            FontSize = 9,
            FontFamily = "OpenSansSemibold",
            TextColor = Color.FromArgb("#E2E8F0"),
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            LineBreakMode = LineBreakMode.NoWrap,
            MaxLines = 1,
            WidthRequest = AnchoTexto
        };

        var fila = new HorizontalStackLayout
        {
            Spacing = 4,
            Children = { _punto, _texto }
        };

        Children.Add(fila);

        BackgroundColor = Color.FromArgb("#DD141B24");
        Padding = new Thickness(7, 0);
        WidthRequest = AnchoBadge;
        HeightRequest = AltoBadge;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Start;
        InputTransparent = true;      // no debe quitarle toques a la pantalla

        Escuchar();
        Vivas.Add(new WeakReference<BarraConexion>(this));
        Actualizar();
    }

    /// <summary>
    /// Pone el indicador encima del contenido de la página sin tocar el diseño original: el
    /// contenido pasa a un Grid y el punto se dibuja en la esquina superior derecha.
    /// </summary>
    public static void Superponer(Page? pagina)
    {
        try
        {
            if (pagina is not ContentPage { Content: not null } cp) return;
            if (YaTiene(cp)) return;

            var indicador = new BarraConexion();

            // Si la raíz ya es un Grid, el badge entra como hermano (última capa). No se
            // reemparenta NADA: envolver el contenido en otro Grid en WinUI hacía que los
            // toques (chips, botones, scroll) no llegaran nunca al contenido.
            if (cp.Content is Grid raiz && raiz.RowDefinitions.Count <= 1 && raiz.ColumnDefinitions.Count <= 1)
            {
                if (raiz.RowDefinitions.Count == 0 && raiz.ColumnDefinitions.Count == 0)
                {
                    raiz.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                    raiz.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                }

                raiz.Children.Add(indicador);
                Grid.SetRow(indicador, 0);
                Grid.SetColumn(indicador, 0);
                return;
            }

            // Cualquier otro contenido se deja intacto: el badge es cosmético y no vale
            // romper la interacción de la pantalla por él.
        }
        catch (Exception ex)
        {
            // una pantalla con un contenido atípico se deja como está: nunca debe fallar por esto
            Registro.Aviso("Conexion", $"No se pudo poner el indicador en {pagina?.GetType().Name}: {ex.Message}");
        }
    }

    private static bool YaTiene(ContentPage pagina) =>
        pagina.Content is Grid grid && grid.Children.OfType<BarraConexion>().Any();

    private static void Escuchar()
    {
        if (_escuchando) return;
        _escuchando = true;
        MonitorConexion.Cambio += (_, _) => ActualizarTodas();
    }

    private static void ActualizarTodas()
    {
        // Solo se cambian color y texto: no se toca la estructura ni las medidas de la
        // pantalla, así que no puede dispararse un ciclo de diseño.
        for (var i = Vivas.Count - 1; i >= 0; i--)
        {
            if (!Vivas[i].TryGetTarget(out var barra))
            {
                Vivas.RemoveAt(i);
                continue;
            }

            try { barra.Dispatcher.Dispatch(barra.Actualizar); }
            catch (Exception) { /* la pantalla se está cerrando */ }
        }
    }

    private void Actualizar()
    {
        try
        {
            var enLinea = MonitorConexion.EnLinea;
            _punto.Color = enLinea ? Verde : Rojo;
            _texto.Text = MonitorConexion.Descripcion;
        }
        catch (Exception)
        {
            // si la pantalla ya no existe, no hay nada que pintar
        }
    }
}
