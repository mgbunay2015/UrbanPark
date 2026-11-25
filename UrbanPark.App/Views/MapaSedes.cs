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
    private static readonly Color Tierra = Color.FromArgb("#DDE4EB");
    private static readonly Color TierraClaro = Color.FromArgb("#EEF2F6");
    private static readonly Color Bloque = Color.FromArgb("#F5F8FA");
    private static readonly Color BloqueBorde = Color.FromArgb("#B8C4D0");
    private static readonly Color Edificio = Color.FromArgb("#E4E9EE");
    private static readonly Color Parque = Color.FromArgb("#B8DEC8");
    private static readonly Color ParqueBorde = Color.FromArgb("#8FC4A4");
    private static readonly Color CalleBorde = Color.FromArgb("#BCC6D0");
    private static readonly Color Calle = Color.FromArgb("#FCFDFE");
    private static readonly Color EtiquetaCalle = Color.FromArgb("#6B7A8C");
    private static readonly Color Halo = Colors.White;
    private static readonly Color AguaClaro = Color.FromArgb("#A8D4F5");
    private static readonly Color AguaOscuro = Color.FromArgb("#7BB8E8");

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
        SizeChanged += (_, _) =>
        {
            if (Width > 1 && Height > 1)
                Invalidate();
        };
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
        DibujarFondo(canvas, dirtyRect);

        var ancho = dirtyRect.Width > 0 ? dirtyRect.Width : 320;
        var alto = dirtyRect.Height > 0 ? dirtyRect.Height : 380;

        _areaPines = new RectF(14, 14, Math.Max(40, ancho - 28), Math.Max(40, alto - 72));

        var horizontales = ConstruirLineas(esHorizontal: true);
        var verticales = ConstruirLineas(esHorizontal: false);

        DibujarAgua(canvas);
        DibujarParques(canvas);
        DibujarManzanas(canvas, horizontales, verticales);
        DibujarCalles(canvas, horizontales, verticales);
        DibujarNombresDeCalles(canvas, horizontales, verticales);
        DibujarUsuario(canvas);
        DibujarPines(canvas);
        DibujarLeyenda(canvas, dirtyRect);
        DibujarAtribucion(canvas, dirtyRect);
    }

    /// <summary>Fondo del mapa (color sólido: compatible con Android).</summary>
    private static void DibujarFondo(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = TierraClaro;
        canvas.FillRectangle(dirtyRect);
        canvas.FillColor = Tierra.WithAlpha(0.35f);
        canvas.FillRectangle(new RectF(0, dirtyRect.Height * 0.55f, dirtyRect.Width, dirtyRect.Height * 0.45f));
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
        canvas.StrokeColor = AguaOscuro.WithAlpha(0.35f);
        canvas.StrokeSize = 16;
        canvas.DrawPath(camino);
        canvas.StrokeColor = AguaClaro;
        canvas.StrokeSize = 12;
        canvas.DrawPath(camino);
        canvas.StrokeColor = Color.FromArgb("#C5E4FA");
        canvas.StrokeSize = 8;
        canvas.DrawPath(camino);

        // laguna de La Carolina
        var laguna = Proyectar(-0.1836, -78.4868);
        var ancho = (float)(Proyectar(0, 0.0016).X - Proyectar(0, 0).X);
        var alto = (float)(Proyectar(-0.0016, 0).Y - Proyectar(0, 0).Y);
        var rect = new RectF(laguna.X - ancho * 1.3f, laguna.Y - alto * 0.55f, ancho * 2.6f, alto * 1.5f);
        if (rect.Width > 4 && rect.Height > 3 && Dentro(laguna))
        {
            canvas.FillColor = AguaClaro;
            canvas.FillEllipse(rect);
            canvas.StrokeColor = AguaOscuro.WithAlpha(0.4f);
            canvas.StrokeSize = 1;
            canvas.DrawEllipse(rect);
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

            if (rect.Width > 72 && rect.Height > 28)
            {
                canvas.FontColor = Color.FromArgb("#3D7A5C");
                canvas.FontSize = 7f;
                canvas.Font = GFont.DefaultBold;
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

        canvas.FillColor = Azul.WithAlpha(0.10f);
        canvas.FillCircle(p, 28);
        canvas.FillColor = Azul.WithAlpha(0.18f);
        canvas.FillCircle(p, 18);
        canvas.FillColor = Azul.WithAlpha(0.28f);
        canvas.FillCircle(p, 10);

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 2.5f;
        canvas.FillColor = Azul;
        canvas.FillCircle(p, 6.5f);
        canvas.DrawCircle(p, 6.5f);

        var texto = _usuarioReal ? "Estás aquí" : "Ubicación aprox.";
        var ancho = 102f;
        var caja = new RectF(p.X - ancho / 2, p.Y + 14, ancho, 17);
        canvas.FillColor = Color.FromArgb("#1A73E8");
        canvas.FillRoundedRectangle(caja, 9);
        canvas.FontColor = Colors.White;
        canvas.FontSize = 7.5f;
        canvas.Font = GFont.DefaultBold;
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
            var libre = sede.PlazasLibresVehiculo > 0 || sede.PlazasLibres > 0;
            var r = elegida ? 16f : 12f;
            var alto = r * 1.75f;

            _pines[sede.Id] = new RectF(ancla.X - r - 6, ancla.Y - alto - r, (r + 6) * 2, alto + r + 10);

            canvas.FillColor = Color.FromArgb("#64748B").WithAlpha(0.28f);
            canvas.FillEllipse(new RectF(ancla.X - r * 0.62f, ancla.Y + 2, r * 1.24f, r * 0.48f));

            if (elegida)
            {
                canvas.FillColor = Acento.WithAlpha(0.35f);
                canvas.FillCircle(new PointF(ancla.X, ancla.Y - alto * 0.45f), r * 1.55f);
            }

            var forma = FormaMarcador(ancla, r);
            canvas.FillColor = elegida ? Acento : PinBlanco;
            canvas.FillPath(forma);
            canvas.StrokeColor = elegida ? AcentoBorde : PinBorde;
            canvas.StrokeSize = elegida ? 2.2f : 1.5f;
            canvas.DrawPath(forma);

            canvas.FontColor = elegida ? Tinta : PinTexto;
            canvas.Font = GFont.DefaultBold;
            canvas.FontSize = elegida ? 11f : 9f;
            canvas.DrawString(sede.Codigo, ancla.X - r, ancla.Y - alto - r * 0.55f, r * 2, r * 1.1f,
                HorizontalAlignment.Center, VerticalAlignment.Center);

            var badgeR = 5.2f;
            var badge = new PointF(ancla.X + r * 0.72f, ancla.Y - alto - r * 0.15f);
            canvas.FillColor = Colors.White;
            canvas.FillCircle(badge, badgeR + 1.2f);
            canvas.FillColor = libre ? Exito : Peligro;
            canvas.FillCircle(badge, badgeR);

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
        var libres = sede.PlazasLibresVehiculo > 0 ? sede.PlazasLibresVehiculo : sede.PlazasLibres;
        var estadoPlazas = libres > 0 ? $"{libres} libres" : "Sin plazas";
        var detalle = string.Join(" · ", new[]
        {
            sede.DistanciaTexto,
            estadoPlazas
        }.Where(t => !string.IsNullOrWhiteSpace(t)));

        var ancho = Math.Max(188f, Math.Min(nombre.Length * 6.6f + 88f, _areaPines.Width - 12));
        var x = Math.Clamp(ancla.X - ancho / 2, _areaPines.Left + 4, _areaPines.Right - ancho - 4);
        var arriba = ancla.Y - alto - r - 10;
        var altoVentana = 68f;
        var y = arriba - altoVentana;

        var debajo = y < _areaPines.Top + 4;
        if (debajo) y = ancla.Y + 12;

        var caja = new RectF(x, y, ancho, altoVentana);

        canvas.FillColor = Color.FromArgb("#334155").WithAlpha(0.12f);
        canvas.FillRoundedRectangle(new RectF(caja.X + 2, caja.Y + 4, caja.Width, caja.Height), 10);

        canvas.FillColor = InfoFondo;
        canvas.FillRoundedRectangle(caja, 10);
        canvas.StrokeColor = Color.FromArgb("#DDE3EA");
        canvas.StrokeSize = 1.2f;
        canvas.DrawRoundedRectangle(caja, 10);

        var barra = new RectF(caja.X, caja.Y, caja.Width, 3.5f);
        canvas.FillColor = Acento;
        canvas.FillRoundedRectangle(barra, 10);

        var flecha = new PathF();
        var fx = Math.Clamp(ancla.X, caja.X + 16, caja.Right - 16);
        if (!debajo)
        {
            flecha.MoveTo(fx - 8, caja.Bottom - 1);
            flecha.LineTo(fx + 8, caja.Bottom - 1);
            flecha.LineTo(fx, caja.Bottom + 9);
        }
        else
        {
            flecha.MoveTo(fx - 8, caja.Top + 1);
            flecha.LineTo(fx + 8, caja.Top + 1);
            flecha.LineTo(fx, caja.Top - 9);
        }

        flecha.Close();
        canvas.FillColor = InfoFondo;
        canvas.FillPath(flecha);
        canvas.StrokeColor = Color.FromArgb("#DDE3EA");
        canvas.StrokeSize = 1;
        canvas.DrawPath(flecha);

        canvas.FontColor = InfoTexto;
        canvas.Font = GFont.DefaultBold;
        canvas.FontSize = 11f;
        canvas.DrawString(nombre, caja.X + 10, caja.Y + 10, caja.Width - 20, 16,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        canvas.FontColor = InfoSuave;
        canvas.Font = GFont.Default;
        canvas.FontSize = 8.5f;
        canvas.DrawString(Recortar(sede.Direccion, 42), caja.X + 10, caja.Y + 28, caja.Width - 20, 13,
            HorizontalAlignment.Left, VerticalAlignment.Center, TextFlow.ClipBounds);

        var pillAncho = Math.Min(caja.Width - 20, detalle.Length * 5.2f + 16);
        var pill = new RectF(caja.X + 10, caja.Y + 46, pillAncho, 16);
        canvas.FillColor = libres > 0 ? Color.FromArgb("#E8F8EF") : Color.FromArgb("#FEECEC");
        canvas.FillRoundedRectangle(pill, 8);
        canvas.FontColor = libres > 0 ? Exito : Peligro;
        canvas.Font = GFont.DefaultBold;
        canvas.FontSize = 8f;
        canvas.DrawString(detalle, pill, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private void DibujarLeyenda(ICanvas canvas, RectF dirtyRect)
    {
        var ancho = 118f;
        var alto = 52f;
        var x = dirtyRect.Right - ancho - 10;
        var y = dirtyRect.Top + 42;

        var caja = new RectF(x, y, ancho, alto);
        canvas.FillColor = Colors.White.WithAlpha(0.94f);
        canvas.FillRoundedRectangle(caja, 8);
        canvas.StrokeColor = Color.FromArgb("#E2E8F0");
        canvas.StrokeSize = 1;
        canvas.DrawRoundedRectangle(caja, 8);

        canvas.Font = GFont.Default;
        canvas.FontSize = 7f;
        canvas.FontColor = InfoSuave;
        canvas.DrawString("Leyenda", caja.X + 8, caja.Y + 4, caja.Width - 16, 10,
            HorizontalAlignment.Left, VerticalAlignment.Center);

        DibujarItemLeyenda(canvas, caja.X + 8, caja.Y + 18, Exito, "Plazas disponibles");
        DibujarItemLeyenda(canvas, caja.X + 8, caja.Y + 32, Peligro, "Sin cupo");
        DibujarItemLeyenda(canvas, caja.X + 8, caja.Y + 46, Acento, "Seleccionado", circulo: false);
    }

    private static void DibujarItemLeyenda(ICanvas canvas, float x, float y, Color color, string texto, bool circulo = true)
    {
        if (circulo)
        {
            canvas.FillColor = color;
            canvas.FillCircle(new PointF(x + 4, y), 3.5f);
        }
        else
        {
            canvas.FillColor = color;
            canvas.FillRoundedRectangle(new RectF(x, y - 3, 8, 6), 2);
        }

        canvas.FontColor = InfoTexto;
        canvas.FontSize = 7f;
        canvas.DrawString(texto, x + 12, y - 5, 100, 12, HorizontalAlignment.Left, VerticalAlignment.Center);
    }

    private static void DibujarAtribucion(ICanvas canvas, RectF dirtyRect)
    {
        var caja = new RectF(8, dirtyRect.Bottom - 22, 92, 14);
        canvas.FillColor = Colors.White.WithAlpha(0.88f);
        canvas.FillRoundedRectangle(caja, 6);
        canvas.FontColor = Color.FromArgb("#94A3B8");
        canvas.Font = GFont.DefaultBold;
        canvas.FontSize = 6.5f;
        canvas.DrawString("Urban Park · Quito", caja, HorizontalAlignment.Center, VerticalAlignment.Center);
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
