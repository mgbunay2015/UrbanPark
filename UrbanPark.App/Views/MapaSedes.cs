using Microsoft.Maui.Graphics;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;
using GFont = Microsoft.Maui.Graphics.Font;

namespace UrbanPark.App.Views;

/// <summary>
/// Mapa de los parqueaderos de la zona con estética de mapa real (tipo Google Maps):
/// manzanas, avenidas con su nombre, parques en verde y cada sede como un marcador
/// tocable. Se dibuja con Microsoft.Maui.Graphics, sin claves ni conexión: las avenidas
/// y los parques están en su latitud/longitud real de Quito.
/// </summary>
public sealed class MapaSedes : GraphicsView, IDrawable
{
    // ------------------------------------------------------------------ paleta del mapa
    private static readonly Color Tierra = Color.FromArgb("#DFE5EA");
    private static readonly Color Bloque = Color.FromArgb("#F7F9FB");
    private static readonly Color BloqueBorde = Color.FromArgb("#C7D0D9");
    private static readonly Color Edificio = Color.FromArgb("#E9EDF1");
    private static readonly Color Parque = Color.FromArgb("#C1E4CF");
    private static readonly Color ParqueBorde = Color.FromArgb("#A6D4B8");
    private static readonly Color CalleBorde = Color.FromArgb("#CBD3DA");
    private static readonly Color Calle = Colors.White;
    private static readonly Color EtiquetaCalle = Color.FromArgb("#8B98A7");
    private static readonly Color Halo = Colors.White;

    // ------------------------------------------------------------------ pines
    private static readonly Color PinBlanco = Colors.White;
    private static readonly Color PinBorde = Color.FromArgb("#5A6B80");
    private static readonly Color PinTexto = Color.FromArgb("#243244");
    private static readonly Color Acento = Color.FromArgb("#F2C51A");
    private static readonly Color AcentoBorde = Color.FromArgb("#D2A80F");
    private static readonly Color Tinta = Color.FromArgb("#16202B");
    private static readonly Color Azul = Color.FromArgb("#1A73E8");
    private static readonly Color Exito = Color.FromArgb("#1E9E5A");
    private static readonly Color Peligro = Color.FromArgb("#E0483C");
    private static readonly Color InfoFondo = Colors.White;
    private static readonly Color InfoTexto = Color.FromArgb("#202124");
    private static readonly Color InfoSuave = Color.FromArgb("#5F6B7A");

    /// <summary>Avenidas de referencia: latitud para las horizontales, longitud para las verticales.</summary>
    private static readonly (double Valor, string Nombre)[] AvenidasHorizontales =
    [
        (-0.1732, "Av. Naciones Unidas"),
        (-0.1800, "Av. Eloy Alfaro"),
        (-0.1885, "Av. Amazonas"),
        (-0.1975, "Av. Colón"),
        (-0.2050, "Av. Patria"),
        (-0.2110, "Av. 10 de Agosto"),
        (-0.2200, "Av. Maldonado")
    ];

    private static readonly (double Valor, string Nombre)[] AvenidasVerticales =
    [
        (-78.5150, "Av. Occidental"),
        (-78.5055, "Av. Gran Colombia"),
        (-78.4975, "Av. 10 de Agosto"),
        (-78.4878, "Av. 6 de Diciembre"),
        (-78.4800, "Av. Shyris")
    ];

    /// <summary>Parques en su ubicación real (se pintan en verde como en Google Maps).</summary>
    private static readonly (string Nombre, double LatMin, double LatMax, double LngMin, double LngMax)[] Parques =
    [
        ("Parque La Carolina", -0.1905, -0.1690, -78.4930, -78.4780),
        ("Parque El Ejido", -0.2120, -0.2030, -78.5010, -78.4930),
        ("Parque La Alameda", -0.2175, -0.2110, -78.5090, -78.5030),
        ("Parque Bicentenario", -0.1560, -0.1430, -78.4870, -78.4760)
    ];

    private readonly Dictionary<long, RectF> _pines = [];
    private List<SedeDto> _sedes = [];
    private long? _seleccionada;
    private double _latUsuario = Ubicacion.LatitudQuito;
    private double _lngUsuario = Ubicacion.LongitudQuito;
    private bool _usuarioReal;
    private RectF _areaPines;
    private double _minLat = -0.25, _maxLat = -0.15, _minLng = -78.55, _maxLng = -78.45;

    public MapaSedes()
    {
        Drawable = this;
        BackgroundColor = Tierra;
        StartInteraction += OnTocando;
    }

    /// <summary>Recorta el mapa con las esquinas redondeadas de la tarjeta que lo contiene.</summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0 || height <= 0) return;

        Clip = new Microsoft.Maui.Controls.Shapes.RoundRectangleGeometry(
            new CornerRadius(13), new RectF(0, 0, (float)width, (float)height));
    }

    public event EventHandler<SedeDto>? SedeElegida;

    /// <summary>Se pulsa el pin ya seleccionado: la pantalla puede abrir sus puestos.</summary>
    public event EventHandler<SedeDto>? SedeConfirmada;

    public void Pintar(List<SedeDto> sedes, long? seleccionada, double latUsuario, double lngUsuario, bool usuarioReal)
    {
        _sedes = sedes ?? [];
        _seleccionada = seleccionada;
        _latUsuario = latUsuario;
        _lngUsuario = lngUsuario;
        _usuarioReal = usuarioReal;
        CalcularLimites();
        Invalidate();
    }

    public void Seleccionar(long? sedeId)
    {
        _seleccionada = sedeId;
        Invalidate();
    }

    // ================================================================ geometria
    private void CalcularLimites()
    {
        var lats = new List<double> { _latUsuario };
        var lngs = new List<double> { _lngUsuario };
        foreach (var sede in _sedes)
        {
            lats.Add(sede.Latitud);
            lngs.Add(sede.Longitud);
        }

        var minLat = lats.Min();
        var maxLat = lats.Max();
        var minLng = lngs.Min();
        var maxLng = lngs.Max();

        // 20 % de margen y un mínimo para que un par de sedes no se peguen a los bordes.
        var margenLat = Math.Max((maxLat - minLat) * 0.20, 0.0035);
        var margenLng = Math.Max((maxLng - minLng) * 0.20, 0.0055);

        _minLat = minLat - margenLat;
        _maxLat = maxLat + margenLat;
        _minLng = minLng - margenLng;
        _maxLng = maxLng + margenLng;
    }

    private PointF Proyectar(double latitud, double longitud)
    {
        var ancho = Math.Max(_areaPines.Width, 1);
        var alto = Math.Max(_areaPines.Height, 1);
        var x = (float)(_areaPines.Left + (longitud - _minLng) / (_maxLng - _minLng) * ancho);
        var y = (float)(_areaPines.Top + (_maxLat - latitud) / (_maxLat - _minLat) * alto);
        return new PointF(x, y);
    }

    // ================================================================ dibujo
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Tierra;
        canvas.FillRectangle(dirtyRect);

        var ancho = dirtyRect.Width > 0 ? dirtyRect.Width : 320;
        var alto = dirtyRect.Height > 0 ? dirtyRect.Height : 380;

        _areaPines = new RectF(18, 18, Math.Max(40, ancho - 36), Math.Max(40, alto - 52));

        var horizontales = ConstruirLineas(esHorizontal: true);
        var verticales = ConstruirLineas(esHorizontal: false);

        DibujarAgua(canvas);
        DibujarParques(canvas);
        DibujarManzanas(canvas, horizontales, verticales);
        DibujarCalles(canvas, horizontales, verticales);
        DibujarNombresDeCalles(canvas, horizontales, verticales);
        DibujarUsuario(canvas);
        DibujarPines(canvas);
    }

    /// <summary>
    /// Avenidas reales + calles intermedias, para que el mapa tenga una retícula creíble.
    /// Cada línea guarda su nombre solo si corresponde a una avenida conocida.
    /// </summary>
    private List<(float Pos, bool Mayor, string? Nombre)> ConstruirLineas(bool esHorizontal)
    {
        var reales = esHorizontal ? AvenidasHorizontales : AvenidasVerticales;
        var desde = esHorizontal ? _minLat : _minLng;
        var hasta = esHorizontal ? _maxLat : _maxLng;
        var pixeles = Math.Max(esHorizontal ? _areaPines.Height : _areaPines.Width, 1);

        // Una calle cada ~58 px: así las manzanas quedan cuadradas, como en un mapa real,
        // sin importar el zoom con el que salga la pantalla.
        var paso = (hasta - desde) * 58.0 / pixeles;

        var valores = new List<double>();
        for (var v = desde + paso * 0.5; v < hasta; v += paso)
            valores.Add(v);

        // Las avenidas conocidas se clavan en su sitio: si hay una calle muy cerca se reemplaza
        // (y pasa a ser avenida con nombre); si no, se agrega.
        var nombres = new Dictionary<int, string>();
        foreach (var (valor, nombre) in reales)
        {
            if (valor <= desde || valor >= hasta) continue;

            var cercano = -1;
            var menor = double.MaxValue;
            for (var i = 0; i < valores.Count; i++)
            {
                var distancia = Math.Abs(valores[i] - valor);
                if (distancia >= menor) continue;
                menor = distancia;
                cercano = i;
            }

            if (cercano >= 0 && menor < paso * 0.55)
            {
                valores[cercano] = valor;
                nombres[cercano] = nombre;
            }
            else
            {
                valores.Add(valor);
                nombres[valores.Count - 1] = nombre;
            }
        }

        var lineas = new List<(float, bool, string?)>(valores.Count);
        var ordenados = valores.Select((v, i) => (Valor: v, Nombre: nombres.TryGetValue(i, out var n) ? n : null))
            .OrderBy(x => x.Valor);

        foreach (var (valor, nombre) in ordenados)
        {
            var punto = esHorizontal ? Proyectar(valor, _minLng).Y : Proyectar(_maxLat, valor).X;
            lineas.Add((punto, nombre is not null, nombre));
        }

        return lineas;
    }

    /// <summary>
    /// Río Machángara y laguna de La Carolina: el agua es lo que hace que un mapa se
    /// reconozca de un vistazo, así que se dibuja debajo de las calles (como en Google Maps).
    /// </summary>
    private void DibujarAgua(ICanvas canvas)
    {
        var machangara = new (double Lat, double Lng)[]
        {
            (-0.2480, -78.5090), (-0.2380, -78.5055), (-0.2280, -78.5005), (-0.2180, -78.4955),
            (-0.2080, -78.4905), (-0.1980, -78.4855), (-0.1880, -78.4800), (-0.1780, -78.4740),
            (-0.1680, -78.4680), (-0.1580, -78.4630)
        };

        var camino = new PathF();
        for (var i = 0; i < machangara.Length; i++)
        {
            var punto = Proyectar(machangara[i].Lat, machangara[i].Lng);
            if (i == 0) camino.MoveTo(punto);
            else camino.LineTo(punto);
        }

        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeColor = Color.FromArgb("#B9D8F2");
        canvas.StrokeSize = 11;
        canvas.DrawPath(camino);
        canvas.StrokeColor = Color.FromArgb("#A3CBEE");
        canvas.StrokeSize = 9;
        canvas.DrawPath(camino);

        // laguna de La Carolina
        var laguna = Proyectar(-0.1836, -78.4868);
        var ancho = (float)(Proyectar(0, 0.0016).X - Proyectar(0, 0).X);
        var alto = (float)(Proyectar(-0.0016, 0).Y - Proyectar(0, 0).Y);
        var rect = new RectF(laguna.X - ancho * 1.3f, laguna.Y - alto * 0.55f, ancho * 2.6f, alto * 1.5f);
        if (rect.Width > 4 && rect.Height > 3 && Dentro(laguna))
        {
            canvas.FillColor = Color.FromArgb("#A3CBEE");
            canvas.FillEllipse(rect);
        }
    }

    private void DibujarParques(ICanvas canvas)
    {
        foreach (var parque in Parques)
        {
            var a = Proyectar(parque.LatMax, parque.LngMin);
            var b = Proyectar(parque.LatMin, parque.LngMax);
            var rect = new RectF(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            if (rect.Width < 6 || rect.Height < 6) continue;
            if (rect.Right < _areaPines.Left || rect.Left > _areaPines.Right) continue;

            canvas.FillColor = Parque;
            canvas.FillRoundedRectangle(rect, 6);
            canvas.StrokeColor = ParqueBorde;
            canvas.StrokeSize = 1;
            canvas.DrawRoundedRectangle(rect, 6);

            if (rect.Width > 90 && rect.Height > 34)
            {
                canvas.FontColor = Color.FromArgb("#4E8C68");
                canvas.FontSize = 7.5f;
                canvas.Font = GFont.Default;
                canvas.DrawString(parque.Nombre, rect.X + 4, rect.Y + 3, rect.Width - 8, 12,
                    HorizontalAlignment.Left, VerticalAlignment.Top, TextFlow.ClipBounds);
            }
        }
    }

    private void DibujarManzanas(ICanvas canvas,
        List<(float Pos, bool Mayor, string? Nombre)> horizontales,
        List<(float Pos, bool Mayor, string? Nombre)> verticales)
    {
        for (var i = 0; i < verticales.Count - 1; i++)
        {
            for (var j = 0; j < horizontales.Count - 1; j++)
            {
                var x0 = verticales[i].Pos + 2f;
                var x1 = verticales[i + 1].Pos - 2f;
                var y0 = horizontales[j].Pos + 2f;
                var y1 = horizontales[j + 1].Pos - 2f;
                if (x1 - x0 < 5 || y1 - y0 < 5) continue;

                var centro = new PointF((x0 + x1) / 2, (y0 + y1) / 2);
                if (EnParque(centro)) continue;

                var bloque = new RectF(x0, y0, x1 - x0, y1 - y0);
                canvas.FillColor = Bloque;
                canvas.FillRoundedRectangle(bloque, 2.5f);
                canvas.StrokeColor = BloqueBorde;
                canvas.StrokeSize = 1.1f;
                canvas.DrawRoundedRectangle(bloque, 2.5f);

                // huella del edificio: da textura de mapa urbano a las manzanas grandes
                if (bloque.Width > 34 && bloque.Height > 26)
                {
                    var dentro = new RectF(bloque.X + bloque.Width * 0.22f, bloque.Y + bloque.Height * 0.22f,
                        bloque.Width * 0.56f, bloque.Height * 0.56f);
                    canvas.FillColor = Edificio;
                    canvas.FillRoundedRectangle(dentro, 2);
                }
            }
        }
    }

    private void DibujarCalles(ICanvas canvas,
        List<(float Pos, bool Mayor, string? Nombre)> horizontales,
        List<(float Pos, bool Mayor, string? Nombre)> verticales)
    {
        // Primero todos los bordes y después todas las calzadas: así los cruces quedan unidos.
        canvas.StrokeColor = CalleBorde;
        foreach (var (pos, mayor, _) in horizontales)
        {
            canvas.StrokeSize = mayor ? 8.2f : 4.4f;
            canvas.DrawLine(new PointF(_areaPines.Left - 12, pos), new PointF(_areaPines.Right + 12, pos));
        }

        foreach (var (pos, mayor, _) in verticales)
        {
            canvas.StrokeSize = mayor ? 8.2f : 4.4f;
            canvas.DrawLine(new PointF(pos, _areaPines.Top - 12), new PointF(pos, _areaPines.Bottom + 12));
        }

        canvas.StrokeColor = Calle;
        foreach (var (pos, mayor, _) in horizontales)
        {
            canvas.StrokeSize = mayor ? 5.8f : 2.8f;
            canvas.DrawLine(new PointF(_areaPines.Left - 12, pos), new PointF(_areaPines.Right + 12, pos));
        }

        foreach (var (pos, mayor, _) in verticales)
        {
            canvas.StrokeSize = mayor ? 5.8f : 2.8f;
            canvas.DrawLine(new PointF(pos, _areaPines.Top - 12), new PointF(pos, _areaPines.Bottom + 12));
        }
    }

    private void DibujarNombresDeCalles(ICanvas canvas,
        List<(float Pos, bool Mayor, string? Nombre)> horizontales,
        List<(float Pos, bool Mayor, string? Nombre)> verticales)
    {
        canvas.Font = GFont.Default;

        foreach (var (pos, mayor, nombre) in horizontales)
        {
            if (!mayor || string.IsNullOrWhiteSpace(nombre)) continue;
            var ancho = Math.Min(150, _areaPines.Width * 0.55f);
            EscribirConHalo(canvas, nombre!, _areaPines.Left + 6, pos - 14, ancho, 11, HorizontalAlignment.Left);
        }

        foreach (var (pos, mayor, nombre) in verticales)
        {
            if (!mayor || string.IsNullOrWhiteSpace(nombre)) continue;
            var y = _areaPines.Bottom - 46;
            canvas.SaveState();
            canvas.Rotate(-90, pos + 4, y + 26);
            EscribirConHalo(canvas, nombre!, pos + 6, y, 120, 11, HorizontalAlignment.Left);
            canvas.RestoreState();
        }
    }

    /// <summary>Texto gris con halo blanco: así se lee sobre las calles, como en un mapa.</summary>
    private static void EscribirConHalo(ICanvas canvas, string texto, float x, float y, float ancho, float alto,
        HorizontalAlignment alineacion)
    {
        canvas.FontSize = 8;

        canvas.FontColor = Halo;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                canvas.DrawString(texto, x + dx, y + dy, ancho, alto, alineacion, VerticalAlignment.Center);
            }
        }

        canvas.FontColor = EtiquetaCalle;
        canvas.DrawString(texto, x, y, ancho, alto, alineacion, VerticalAlignment.Center);
    }

    private bool EnParque(PointF punto)
    {
        foreach (var parque in Parques)
        {
            var a = Proyectar(parque.LatMax, parque.LngMin);
            var b = Proyectar(parque.LatMin, parque.LngMax);
            var rect = new RectF(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            if (rect.Contains(punto)) return true;
        }

        return false;
    }

    private bool Dentro(PointF p) =>
        p.X >= _areaPines.Left - 30 && p.X <= _areaPines.Right + 30 &&
        p.Y >= _areaPines.Top - 30 && p.Y <= _areaPines.Bottom + 40;

    // ================================================================ usuario
    private void DibujarUsuario(ICanvas canvas)
    {
        var p = Proyectar(_latUsuario, _lngUsuario);
        if (!Dentro(p)) return;

        // halo de precisión
        canvas.FillColor = Azul.WithAlpha(0.14f);
        canvas.FillCircle(p, 26);
        canvas.FillColor = Azul.WithAlpha(0.20f);
        canvas.FillCircle(p, 15);

        // punto azul con anillo blanco, como el "estás aquí" de Google Maps
        canvas.FillColor = Colors.White;
        canvas.FillCircle(p, 8);
        canvas.FillColor = Azul;
        canvas.FillCircle(p, 5.5f);

        var texto = _usuarioReal ? "Estás aquí" : "Ubicación aproximada";
        var ancho = 96f;
        var caja = new RectF(p.X - ancho / 2, p.Y + 12, ancho, 15);
        canvas.FillColor = InfoFondo.WithAlpha(0.92f);
        canvas.FillRoundedRectangle(caja, 7);
        canvas.FontColor = InfoSuave;
        canvas.FontSize = 7;
        canvas.Font = GFont.Default;
        canvas.DrawString(texto, caja, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    // ================================================================ pines
    private void DibujarPines(ICanvas canvas)
    {
        _pines.Clear();
        if (_sedes.Count == 0) return;

        // la sede elegida se pinta al final para que quede encima
        foreach (var sede in _sedes.OrderByDescending(s => _seleccionada == s.Id ? 0 : 1))
        {
            var ancla = Proyectar(sede.Latitud, sede.Longitud);
            if (ancla.X < 0 || ancla.Y < 0) continue;

            var elegida = _seleccionada == sede.Id;
            var r = elegida ? 15f : 11.5f;
            var alto = r * 1.75f;

            _pines[sede.Id] = new RectF(ancla.X - r - 4, ancla.Y - alto - r, (r + 4) * 2, alto + r + 8);

            // sombra del marcador
            canvas.FillColor = Color.FromArgb("#93A0AF").WithAlpha(0.35f);
            canvas.FillEllipse(new RectF(ancla.X - r * 0.55f, ancla.Y + 1, r * 1.1f, r * 0.42f));

            var forma = FormaMarcador(ancla, r);
            canvas.FillColor = elegida ? Acento : PinBlanco;
            canvas.FillPath(forma);
            canvas.StrokeColor = elegida ? AcentoBorde : PinBorde;
            canvas.StrokeSize = elegida ? 2f : 1.6f;
            canvas.DrawPath(forma);

            // código corto dentro de la cabeza del marcador
            canvas.FontColor = elegida ? Tinta : PinTexto;
            canvas.Font = GFont.DefaultBold;
            canvas.FontSize = elegida ? 10.5f : 8.5f;
            canvas.DrawString(sede.Codigo, ancla.X - r, ancla.Y - alto - r * 0.55f, r * 2, r * 1.1f,
                HorizontalAlignment.Center, VerticalAlignment.Center);

            // punto de disponibilidad
            canvas.FillColor = sede.PlazasLibresVehiculo > 0 || sede.PlazasLibres > 0 ? Exito : Peligro;
            canvas.FillCircle(new PointF(ancla.X + r, ancla.Y - alto - r * 0.2f), 2.6f);

            if (elegida)
                DibujarVentanaInfo(canvas, sede, ancla, r, alto);
        }
    }

    /// <summary>Marcador tipo Google: cabeza redonda y punta apoyada en el punto exacto.</summary>
    private static PathF FormaMarcador(PointF ancla, float r)
    {
        var cx = ancla.X;
        var cy = ancla.Y - r * 1.75f;

        var p = new PathF();
        p.MoveTo(cx - r * 0.82f, cy + r * 0.52f);
        p.CurveTo(cx - r * 1.24f, cy - r * 0.42f, cx - r * 0.52f, cy - r * 1.32f, cx, cy - r * 1.32f);
        p.CurveTo(cx + r * 0.52f, cy - r * 1.32f, cx + r * 1.24f, cy - r * 0.42f, cx + r * 0.82f, cy + r * 0.52f);
        p.LineTo(cx + r * 0.22f, cy + r * 1.05f);
        p.LineTo(cx, cy + r * 1.62f);
        p.LineTo(cx - r * 0.22f, cy + r * 1.05f);
        p.Close();
        return p;
    }

    /// <summary>Recorta un texto largo para que quepa en la ventana del mapa.</summary>
    private static string Recortar(string texto, int maximo) =>
        string.IsNullOrWhiteSpace(texto) ? "" : texto.Length <= maximo ? texto : texto[..(maximo - 1)].TrimEnd() + "…";
    /// <summary>Ventana blanca de información, como el globo de Google Maps.</summary>
    private void DibujarVentanaInfo(ICanvas canvas, SedeDto sede, PointF ancla, float r, float alto)
    {
        var nombre = sede.Nombre.Replace("Urban Park ", "");
        var detalle = string.Join(" · ", new[]
        {
            sede.DistanciaTexto,
            sede.LibreTexto
        }.Where(t => !string.IsNullOrWhiteSpace(t)));

        var ancho = Math.Max(176f, Math.Min(nombre.Length * 6.4f + 80f, _areaPines.Width - 8));
        var x = Math.Clamp(ancla.X - ancho / 2, _areaPines.Left + 2, _areaPines.Right - ancho - 2);
        var arriba = ancla.Y - alto - r - 8;
        var altoVentana = 60f;
        var y = arriba - altoVentana;

        // si no cabe arriba, la ventana se pinta debajo del marcador
        var debajo = y < _areaPines.Top + 2;
        if (debajo) y = ancla.Y + 10;

        var caja = new RectF(x, y, ancho, altoVentana);

        canvas.FillColor = Color.FromArgb("#8A97A6").WithAlpha(0.22f);
        canvas.FillRoundedRectangle(new RectF(caja.X + 1, caja.Y + 3, caja.Width, caja.Height), 8);

        canvas.FillColor = InfoFondo;
        canvas.FillRoundedRectangle(caja, 8);
        canvas.StrokeColor = Color.FromArgb("#E3E7EC");
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(caja, 8);

        // flecha hacia el marcador
        var flecha = new PathF();
        var fx = Math.Clamp(ancla.X, caja.X + 14, caja.Right - 14);
        if (!debajo)
        {
            flecha.MoveTo(fx - 7, caja.Bottom - 1);
            flecha.LineTo(fx + 7, caja.Bottom - 1);
            flecha.LineTo(fx, caja.Bottom + 8);
        }
        else
        {
            flecha.MoveTo(fx - 7, caja.Top + 1);
            flecha.LineTo(fx + 7, caja.Top + 1);
            flecha.LineTo(fx, caja.Top - 8);
        }

        flecha.Close();
        canvas.FillColor = InfoFondo;
        canvas.FillPath(flecha);

        canvas.FontColor = InfoTexto;
        canvas.Font = GFont.DefaultBold;
        canvas.FontSize = 10.5f;
        canvas.DrawString(nombre, caja.X + 10, caja.Y + 6, caja.Width - 20, 15,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.FontColor = InfoSuave;
        canvas.Font = GFont.Default;
        canvas.FontSize = 8.2f;
        canvas.DrawString(Recortar(sede.Direccion, 38), caja.X + 10, caja.Y + 24, caja.Width - 20, 12,
            HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.ClipBounds);

        canvas.FontColor = InfoSuave;
        canvas.FontSize = 7.8f;
        canvas.DrawString(detalle, caja.X + 10, caja.Y + 40, caja.Width - 20, 13,
            HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.ClipBounds);
    }

    // ================================================================ toque
    private void OnTocando(object? sender, TouchEventArgs e)
    {
        if (e.Touches.Length == 0) return;
        var punto = e.Touches[0];

        SedeDto? encontrada = null;
        var mejorDistancia = float.MaxValue;

        foreach (var (id, rect) in _pines)
        {
            if (!rect.Contains(punto)) continue;
            var centro = new PointF(rect.Center.X, rect.Center.Y);
            var distancia = Math.Abs(centro.X - punto.X) + Math.Abs(centro.Y - punto.Y);
            if (distancia >= mejorDistancia) continue;
            mejorDistancia = distancia;
            encontrada = _sedes.FirstOrDefault(s => s.Id == id);
        }

        if (encontrada is null) return;

        var yaEstaba = _seleccionada == encontrada.Id;
        _seleccionada = encontrada.Id;
        Invalidate();

        if (yaEstaba)
            SedeConfirmada?.Invoke(this, encontrada);
        else
            SedeElegida?.Invoke(this, encontrada);
    }
}
