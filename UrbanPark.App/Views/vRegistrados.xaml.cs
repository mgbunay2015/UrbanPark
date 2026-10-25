using Microsoft.Maui.Controls.Shapes;
using UrbanPark.App.Modelos;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>Listado de vehículos registrados. Se abre desde el botón junto a la placa.</summary>
public partial class vRegistrados : ContentPage
{
    private List<VehiculoDto> _vehiculos = [];

    public vRegistrados()
    {
        InitializeComponent();
        lblTitulo.Text = Sesion.EsCliente ? "Mis registrados" : "Registrados";
        Title = "Registrados";
        Shell.SetNavBarIsVisible(this, false);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await CargarAsync();
    }

    private async void OnRegistrarClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            await Navigation.PushAsync(new vVehiculos());
    }

    private async Task CargarAsync()
    {
        spinner.IsVisible = spinner.IsRunning = true;
        try
        {
            _vehiculos = await ApiCliente.ListarVehiculosAsync() ?? [];
            PintarLista();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
        finally
        {
            spinner.IsRunning = false;
            spinner.IsVisible = false;
        }
    }

    private void PintarLista()
    {
        contenedorVehiculos.Clear();

        if (_vehiculos.Count == 0)
        {
            contenedorVehiculos.Add(new Label
            {
                Text = "No hay vehículos registrados.",
                Style = Ui.EstiloDe("EstadoVacio")
            });
            return;
        }

        foreach (var vehiculo in _vehiculos
                     .OrderByDescending(v => v.Activo)
                     .ThenBy(v => v.Marca ?? "", StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(v => v.Placa, StringComparer.OrdinalIgnoreCase))
            contenedorVehiculos.Add(FilaVehiculo(vehiculo));
    }

    private View FilaVehiculo(VehiculoDto vehiculo)
    {
        var marca = string.IsNullOrWhiteSpace(vehiculo.Marca) ? "-" : vehiculo.Marca!.Trim();
        var color = string.IsNullOrWhiteSpace(vehiculo.Color) ? "" : vehiculo.Color!.Trim();

        var formateado = new FormattedString();
        formateado.Spans.Add(new Span
        {
            Text = vehiculo.Placa,
            FontFamily = "OpenSansSemibold",
            FontSize = 13
        });
        formateado.Spans.Add(new Span
        {
            Text = $"   {Ui.IconoTipo(vehiculo.Tipo)}{vehiculo.Tipo} · {marca} {color}".TrimEnd()
        });

        var detalle = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            Children =
            {
                new Label
                {
                    FormattedText = formateado,
                    TextColor = Ui.ColorDe("TextoFuerte"),
                    LineBreakMode = LineBreakMode.WordWrap
                },
                new Label
                {
                    Text = TextoPerfil(vehiculo.Propietario),
                    Style = Ui.EstiloDe("TextoSuave"),
                    LineBreakMode = LineBreakMode.WordWrap
                }
            }
        };

        if (!vehiculo.Activo)
        {
            detalle.Children.Add(new Label
            {
                Text = "De baja",
                FontSize = 12,
                TextColor = Ui.ColorDe("Peligro")
            });
        }

        var placa = vehiculo.Placa;
        var id = vehiculo.Id;
        var estaActivo = vehiculo.Activo;

        var btnUsar = new Button
        {
            Text = "Usar placa",
            Style = Ui.EstiloDe("BotonMini"),
            IsVisible = vehiculo.Activo,
            VerticalOptions = LayoutOptions.Center
        };
        btnUsar.Clicked += async (_, _) =>
        {
            vVehiculos.PlacaPendiente = placa;
            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync();
            else
                await Navigation.PushAsync(new vVehiculos());
        };

        var btnBio = new Button
        {
            Text = "Huella",
            Style = Ui.EstiloDe("BotonMini"),
            IsVisible = vehiculo.Activo,
            VerticalOptions = LayoutOptions.Center
        };
        btnBio.Clicked += async (_, _) =>
        {
            try
            {
                await BiometriaServicio.EnrolarAsync(this, id, placa);
            }
            catch (Exception ex)
            {
                await Ui.MostrarExcepcion(this, ex);
            }
        };

        var btnBaja = new Button
        {
            Text = estaActivo ? "Dar de baja" : "Activar",
            Style = Ui.EstiloDe("BotonMini"),
            BackgroundColor = estaActivo ? Colors.Transparent : Ui.ColorDe("Acento"),
            TextColor = Ui.ColorDe(estaActivo ? "Peligro" : "TextoFuerte"),
            BorderColor = Ui.ColorDe(estaActivo ? "Peligro" : "AcentoOscuro"),
            BorderWidth = estaActivo ? 1 : 0,
            VerticalOptions = LayoutOptions.Center
        };
        btnBaja.Clicked += async (_, _) => await CambiarBajaAsync(id, placa, !estaActivo);

        var acciones = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { btnUsar, btnBio, btnBaja }
        };

        var grilla = new Grid
        {
            ColumnDefinitions = [new(GridLength.Star), new(GridLength.Auto)],
            ColumnSpacing = 12
        };
        grilla.Add(detalle, 0);
        grilla.Add(acciones, 1);

        return new Border
        {
            Style = Ui.EstiloDe("TarjetaLista"),
            Margin = new Thickness(0, 0, 0, 10),
            Opacity = vehiculo.Activo ? 1 : 0.55,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Content = grilla
        };
    }

    private async Task CambiarBajaAsync(long id, string placa, bool activar)
    {
        var titulo = activar ? "Activar vehículo" : "Dar de baja";
        var mensaje = activar
            ? $"¿Volver a activar {placa}? Podrá usarse de nuevo."
            : $"¿Dar de baja {placa}? No se borra: queda desactivado y no se podrá usar hasta activarlo.";
        if (!await Ui.Confirmar(this, titulo, mensaje))
            return;

        try
        {
            await ApiCliente.CambiarActivoVehiculoAsync(id, activar);
            await Ui.Exito(this, activar ? $"{placa} quedó activo." : $"{placa} quedó de baja.");
            await CargarAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
        }
    }

    private static string TextoPerfil(string? perfil)
    {
        var descuento = Ui.DescuentoDe(perfil);
        var nombre = perfil switch
        {
            "Hombre" => "👨 Hombre",
            "Mujer" => "👩 Mujer",
            "Tercera Edad" or "Anciano" => "👴 Tercera edad",
            "Capacidades Especiales" or "Discapacitado" => "♿ Capacidades especiales",
            "Embarazada" => "🤰 Embarazada",
            "Estudiante" => "🎓 Estudiante",
            _ => string.IsNullOrWhiteSpace(perfil) ? "" : perfil
        };
        return descuento > 0 ? $"{nombre} · {descuento}%" : nombre;
    }
}
