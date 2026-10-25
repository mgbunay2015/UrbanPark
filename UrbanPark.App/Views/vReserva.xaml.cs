using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Reserva de un puesto para una fecha y hora concretas: el cliente elige cuándo entra y
/// cuándo piensa salir, ve la duración y el total estimado, y al confirmar se genera el QR.
/// La otra opción (entrar en el acto) se resuelve directamente desde la pantalla de puestos.
/// </summary>
public partial class vReserva : ContentPage
{
    private readonly SedeDto _sede;
    private readonly EspacioDto _puesto;
    private readonly string _placa;
    private readonly double _tarifa;
    private bool _cargando = true;
    private bool _enviando;

    public vReserva(SedeDto sede, EspacioDto puesto, string placa, int descuento = 0)
    {
        InitializeComponent();
        _sede = sede;
        _puesto = puesto;
        _placa = placa;
        _tarifa = puesto.TarifaHora * (1 - Math.Clamp(descuento, 0, 100) / 100.0);

        lblSede.Text = sede.Nombre;
        lblDireccion.Text = $"📍 {sede.Direccion}";
        lblCodigoPuesto.Text = puesto.Codigo;
        lblPuesto.Text = $"Puesto {puesto.Codigo} · placa {placa} · {Ui.Tarifa(puesto.TarifaHora)}";
        lblTarifa.Text = Ui.Tarifa(puesto.TarifaHora) + (descuento > 0 ? $" (−{descuento}%)" : "");

        var ahora = DateTime.Now;
        fecha.MinimumDate = ahora.Date;
        fecha.MaximumDate = ahora.Date.AddDays(60);
        fecha.Date = ahora.Date.AddDays(ahora.Hour >= 22 ? 1 : 0);

        // Por defecto: la próxima hora en punto y dos horas de estancia.
        var inicio = new TimeSpan(Math.Min(23, ahora.Hour + 1), 0, 0);
        horaIngreso.Time = inicio;
        horaSalida.Time = inicio.Add(TimeSpan.FromHours(2));

        _cargando = false;
        ActualizarResumen();
    }

    // ================================================================ cálculo
    private DateTime Dia => (fecha.Date ?? DateTime.Today).Date;

    private TimeSpan HoraInicio => horaIngreso.Time ?? TimeSpan.FromHours(9);

    private TimeSpan HoraFin => horaSalida.Time ?? TimeSpan.FromHours(11);

    private DateTime Inicio => Dia + HoraInicio;

    private DateTime Fin
    {
        get
        {
            var fin = Dia + HoraFin;
            return fin <= Inicio ? fin.AddDays(1) : fin; // si la salida es antes, se entiende al día siguiente
        }
    }

    private int Horas
    {
        get
        {
            var minutos = Math.Max(1, (Fin - Inicio).TotalMinutes);
            return (int)Math.Ceiling(minutos / 60.0);
        }
    }

    private double Total => Math.Round(Horas * _tarifa, 2, MidpointRounding.AwayFromZero);

    private void ActualizarResumen()
    {
        var horas = Horas;
        lblHoras.Text = horas == 1 ? "1 hora" : $"{horas} horas";
        lblTotal.Text = Ui.Moneda(Total);

        var texto = $"{Inicio:dd/MM/yyyy} de {Inicio:HH:mm} a {Fin:HH:mm}";
        if (Fin.Date > Inicio.Date) texto += $" (sale el {Fin:dd/MM/yyyy})";
        lblDuracion.Text = "Reserva: " + texto;

        var valido = Inicio >= DateTime.Now.AddMinutes(-5);
        btnReservar.IsEnabled = valido && !_enviando;
        lblDuracion.TextColor = valido ? Ui.ColorDe("TextoClaroMedio") : Ui.ColorDe("Peligro");

        if (!valido)
            lblDuracion.Text = "Esa fecha y hora ya pasaron. Elige una franja futura.";
    }

    private void OnFechaCambiada(object? sender, DateChangedEventArgs e)
    {
        if (_cargando) return;
        ActualizarResumen();
    }

    private void OnHoraCambiada(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_cargando) return;
        if (e.PropertyName is not (nameof(TimePicker.Time) or nameof(TimePicker.PropertyChanged))) return;
        ActualizarResumen();
    }

    private void OnHoyClicked(object? sender, EventArgs e)
    {
        fecha.Date = DateTime.Today;
        ActualizarResumen();
    }

    // ================================================================ acciones
    private async void OnReservarClicked(object? sender, EventArgs e)
    {
        if (_enviando) return;

        if (Inicio < DateTime.Now.AddMinutes(-5))
        {
            await Ui.Aviso(this, "Elige una fecha y hora futuras para la reserva.");
            return;
        }

        _enviando = true;
        btnReservar.IsEnabled = false;
        btnReservar.Text = "Reservando…";
        try
        {
            var pase = await ApiCliente.EstacionarAsync(_placa, _puesto.Id, Inicio, Fin);
            await Navigation.PushAsync(new vQrPuesto(pase));
            // la reserva ya quedó: se quita esta pantalla del historial para no repetirla
            var pila = Navigation.NavigationStack;
            if (pila.Count > 2 && pila[^3] is vReserva anterior)
                Navigation.RemovePage(anterior);
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            _enviando = false;
            btnReservar.IsEnabled = true;
            btnReservar.Text = "Reservar y generar QR";
        }
    }

    private async void OnCancelarClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }
}
