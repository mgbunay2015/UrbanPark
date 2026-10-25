using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>Día seleccionado, lista del día y barras de la semana. Modelo claro de la maqueta.</summary>
public partial class vHoy : ContentPage
{
    private DateTime _dia = DateTime.Today;
    private List<TicketDto> _tickets = [];

    public vHoy()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarAsync();
    }

    private async Task CargarAsync()
    {
        try
        {
            _tickets = await ApiCliente.ListarTicketsAsync() ?? [];
        }
        catch
        {
            _tickets = [];
        }

        PintarDias();
        PintarDia();
    }

    private void PintarDias()
    {
        dias.Children.Clear();
        var lunes = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek + (int)DayOfWeek.Monday);
        if (DateTime.Today.DayOfWeek == DayOfWeek.Sunday)
            lunes = DateTime.Today.AddDays(-6);

        string[] letras = ["L", "M", "X", "J", "V", "S", "D"];
        for (var i = 0; i < 7; i++)
        {
            var fecha = lunes.AddDays(i);
            var seleccionado = fecha.Date == _dia.Date;
            var tarjeta = new Border
            {
                BackgroundColor = seleccionado ? Color.FromArgb("#FFE14A") : Colors.White,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(10, 8),
                WidthRequest = 42,
                Content = new VerticalStackLayout
                {
                    Spacing = 2,
                    Children =
                    {
                        new Label
                        {
                            Text = letras[i],
                            FontSize = 11,
                            TextColor = Color.FromArgb("#6B7280"),
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        new Label
                        {
                            Text = fecha.Day.ToString(),
                            FontFamily = "OpenSansSemibold",
                            FontSize = 16,
                            TextColor = Color.FromArgb("#1E1B2E"),
                            HorizontalTextAlignment = TextAlignment.Center
                        }
                    }
                }
            };
            var elegida = fecha;
            tarjeta.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    _dia = elegida;
                    PintarDias();
                    PintarDia();
                })
            });
            dias.Add(tarjeta);
        }
    }

    private void PintarDia()
    {
        lblFecha.Text = _dia.ToString("d 'de' MMMM", new System.Globalization.CultureInfo("es-EC"));
        lista.Children.Clear();

        var delDia = _tickets.Where(t => t.Entrada.Date == _dia.Date).OrderByDescending(t => t.Entrada).Take(8).ToList();
        if (delDia.Count == 0)
        {
            lista.Add(new Label
            {
                Text = "No hay tickets este día.",
                TextColor = Color.FromArgb("#E4DCFF"),
                FontSize = 14
            });
        }
        else
        {
            foreach (var ticket in delDia)
            {
                lista.Add(new Label
                {
                    Text = $"●  {ticket.Placa}   {ticket.EspacioCodigo ?? "—"}   {ticket.Entrada:HH:mm}",
                    TextColor = Colors.White,
                    FontSize = 14
                });
            }
        }

        var lunes = _dia.AddDays(-(int)_dia.DayOfWeek + (int)DayOfWeek.Monday);
        if (_dia.DayOfWeek == DayOfWeek.Sunday)
            lunes = _dia.AddDays(-6);
        var valores = new double[7];
        for (var i = 0; i < 7; i++)
        {
            var fecha = lunes.AddDays(i).Date;
            valores[i] = _tickets.Count(t => t.Entrada.Date == fecha);
        }
        barras.Drawable = new BarrasSemana(valores);
    }

    private sealed class BarrasSemana : IDrawable
    {
        private readonly double[] _valores;
        public BarrasSemana(double[] valores) => _valores = valores;

        public void Draw(ICanvas canvas, RectF area)
        {
            var maximo = Math.Max(1, _valores.Max());
            var ancho = area.Width / 7f;
            string[] letras = ["L", "M", "X", "J", "V", "S", "D"];
            for (var i = 0; i < 7; i++)
            {
                var alto = (float)(area.Height - 22) * (float)(_valores[i] / maximo);
                var x = i * ancho + ancho * 0.28f;
                canvas.FillColor = Color.FromArgb("#6C4DFF");
                canvas.FillRoundedRectangle(x, area.Height - 20 - alto, ancho * 0.44f, Math.Max(4, alto), 6);
                canvas.FontColor = Color.FromArgb("#1E1B2E");
                canvas.FontSize = 11;
                canvas.DrawString(letras[i], i * ancho, area.Height - 16, ancho, 16,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }
}
