using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Administración masiva de tarifas: porcentaje, precio fijo o tarifa base de sede.
/// Solo ADMIN.
/// </summary>
public partial class vTarifas : ContentPage
{
    private static readonly string[] Modos = ["PORCENTAJE", "FIJO", "TARIFA_BASE"];
    private static readonly string?[] Zonas = [null, "A", "B", "C", "D"];
    private static readonly string?[] Tipos = [null, "AUTO", "MOTO", "CAMIONETA", "DISCAPACIDAD", "EMBARAZADA"];

    private readonly HashSet<long> _sedesElegidas = [];
    private List<TarifaSedeResumenDto> _resumen = [];
    private bool _cargando;

    public vTarifas()
    {
        InitializeComponent();
        pickerModo.ItemsSource = new[]
        {
            "PORCENTAJE (+/- %)",
            "FIJO ($/hora)",
            "TARIFA_BASE (sede + recalcular plazas)"
        };
        pickerModo.SelectedIndex = 0;
        pickerZona.ItemsSource = new[] { "Todas", "Zona A", "Zona B", "Zona C", "Zona D" };
        pickerZona.SelectedIndex = 0;
        pickerTipo.ItemsSource = new[] { "Todos", "Auto", "Moto", "Camioneta", "Discapacidad", "Embarazada" };
        pickerTipo.SelectedIndex = 0;
        OnModoChanged(null, EventArgs.Empty);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!Sesion.EsAdmin)
        {
            await Ui.Error(this, "No tienes acceso a esta pantalla con tu perfil");
            await Navigation.PopAsync();
            return;
        }

        await CargarAsync();
    }

    private async void OnActualizarClicked(object? sender, EventArgs e) => await CargarAsync();

    private void OnModoChanged(object? sender, EventArgs e)
    {
        lblModoAyuda.Text = pickerModo.SelectedIndex switch
        {
            1 => "Precio fijo por hora en las plazas filtradas (ej.: 2.75).",
            2 => "Nueva tarifa base de la sede; recalcula AUTO/MOTO/CAMIONETA por zona.",
            _ => "Ej.: 10 sube un 10 %; -5 baja un 5 %. Afecta plazas (y sedes si no filtras zona/tipo)."
        };
    }

    private async Task CargarAsync()
    {
        if (_cargando) return;
        _cargando = true;
        spinner.IsVisible = spinner.IsRunning = true;
        try
        {
            _resumen = await ApiCliente.TarifasResumenAsync() ?? [];
            PintarSedes();
            PintarResumen();
            lblPie.Text = $"{_resumen.Count} parqueaderos · { _resumen.Sum(r => r.Plazas)} plazas";
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinner.IsRunning = false;
            spinner.IsVisible = false;
            _cargando = false;
        }
    }

    private void PintarSedes()
    {
        filaSedes.Children.Clear();
        filaSedes.Children.Add(Chip("Todas", _sedesElegidas.Count == 0, () =>
        {
            _sedesElegidas.Clear();
            PintarSedes();
        }));

        foreach (var sede in _resumen.OrderBy(s => s.Nombre, StringComparer.OrdinalIgnoreCase))
        {
            var id = sede.SedeId;
            var etiqueta = string.IsNullOrWhiteSpace(sede.Codigo) ? sede.Nombre : sede.Codigo;
            filaSedes.Children.Add(Chip(etiqueta, _sedesElegidas.Contains(id), () =>
            {
                if (!_sedesElegidas.Add(id)) _sedesElegidas.Remove(id);
                PintarSedes();
            }));
        }

        lblSedes.Text = _sedesElegidas.Count switch
        {
            0 => "Todas las sedes",
            1 => _resumen.First(s => s.SedeId == _sedesElegidas.First()).Nombre,
            _ => $"{_sedesElegidas.Count} sedes elegidas"
        };
    }

    private View Chip(string texto, bool activo, Action alPulsar)
    {
        var chip = new Border
        {
            Style = Ui.EstiloDe(activo ? "ChipActivo" : "Chip"),
            Padding = new Thickness(10, 5),
            Content = new Label
            {
                Text = texto,
                FontSize = 11,
                FontFamily = "OpenSansSemibold",
                TextColor = activo ? Ui.ColorDe("TextoFuerte") : Ui.ColorDe("TextoClaroMedio")
            }
        };
        var toque = new TapGestureRecognizer();
        toque.Tapped += (_, _) => alPulsar();
        chip.GestureRecognizers.Add(toque);
        return chip;
    }

    private void PintarResumen()
    {
        listaResumen.Clear();
        var mostrar = _sedesElegidas.Count == 0
            ? _resumen
            : _resumen.Where(r => _sedesElegidas.Contains(r.SedeId)).ToList();

        foreach (var s in mostrar)
        {
            var grilla = new Grid
            {
                ColumnDefinitions =
                [
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto)
                ],
                ColumnSpacing = 8
            };

            var textos = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new Label
                    {
                        Text = s.Nombre.Replace("Urban Park ", ""),
                        FontFamily = "OpenSansSemibold",
                        FontSize = 13,
                        TextColor = Ui.ColorDe("TextoClaro")
                    },
                    new Label
                    {
                        Text =
                            $"Base {Ui.Tarifa(s.TarifaBase)} · plazas {Ui.Tarifa(s.TarifaMin)} – {Ui.Tarifa(s.TarifaMax)}",
                        FontSize = 10,
                        TextColor = Ui.ColorDe("TextoClaroSuave")
                    }
                }
            };

            var contador = new Label
            {
                Text = s.Plazas.ToString(),
                FontFamily = "OpenSansSemibold",
                FontSize = 14,
                TextColor = Ui.ColorDe("Acento"),
                VerticalOptions = LayoutOptions.Center
            };

            grilla.Add(textos, 0, 0);
            grilla.Add(contador, 1, 0);

            listaResumen.Add(new Border
            {
                Style = Ui.EstiloDe("TarjetaLista"),
                Padding = new Thickness(10, 8),
                Content = grilla
            });
        }
    }

    private async void OnAplicarClicked(object? sender, EventArgs e)
    {
        if (!double.TryParse((txtValor.Text ?? "").Trim().Replace(',', '.'),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var valor) &&
            !double.TryParse(txtValor.Text, out valor))
        {
            await Ui.Aviso(this, "Indica un valor numérico.");
            return;
        }

        var idx = pickerModo.SelectedIndex < 0 ? 0 : pickerModo.SelectedIndex;
        var modo = Modos[Math.Min(idx, Modos.Length - 1)];
        var zona = Zonas[Math.Clamp(pickerZona.SelectedIndex, 0, Zonas.Length - 1)];
        var tipo = Tipos[Math.Clamp(pickerTipo.SelectedIndex, 0, Tipos.Length - 1)];

        var sedes = _sedesElegidas.Count > 0 ? _sedesElegidas.ToList() : null;
        var resumenSedes = sedes?.Count ?? _resumen.Count;

        var texto = modo switch
        {
            "PORCENTAJE" => $"¿Aplicar {valor:+0.##;-0.##}% a {resumenSedes} sede(s) con los filtros elegidos?",
            "FIJO" => $"¿Poner ${valor:0.00}/h en las plazas filtradas?",
            _ => $"¿Tarifa base ${valor:0.00}/h y recalcular plazas en {resumenSedes} sede(s)?"
        };

        if (!await Ui.Confirmar(this, "Confirmar cambio masivo", texto))
            return;

        btnAplicar.IsEnabled = false;
        try
        {
            var resultado = await ApiCliente.TarifasAplicarAsync(modo, valor, sedes, zona, tipo);
            await Ui.Exito(this, resultado.Detalle.Length > 0 ? resultado.Detalle : resultado.Mensaje);
            lblPie.Text = $"Actualizado: {resultado.Plazas} plazas, {resultado.Sedes} sedes (base).";
            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            btnAplicar.IsEnabled = true;
        }
    }
}
