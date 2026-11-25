using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Muestra el archivo de registro de la app para poder revisarlo sin depender de Visual
/// Studio. Se abre desde «Perfil → Registro de la app».
/// </summary>
public partial class vRegistro : ContentPage
{
    public vRegistro(string contenido)
    {
        InitializeComponent();
        txtRegistro.Text = contenido;
        ActualizarResumen();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ActualizarResumen();
    }

    private void ActualizarResumen()
    {
        try
        {
            var archivo = Registro.ArchivoReciente();
            var existe = File.Exists(archivo);
            lblResumen.Text = existe
                ? $"Archivo: {Path.GetFileName(archivo)}\nCarpeta: {Registro.RutaCarpeta}"
                : "Todavía no hay ningún archivo de registro.";
        }
        catch (Exception ex)
        {
            lblResumen.Text = "No se pudo leer la ruta del registro: " + ex.Message;
        }
    }

    private void OnActualizarClicked(object? sender, EventArgs e)
    {
        txtRegistro.Text = Registro.Ultimas(150);
        ActualizarResumen();
    }

    private async void OnCopiarClicked(object? sender, EventArgs e)
    {
        try
        {
            await Clipboard.Default.SetTextAsync(txtRegistro.Text ?? "");
            await Ui.Exito(this, "Registro copiado al portapapeles.");
        }
        catch (Exception ex)
        {
            Registro.Error("vRegistro", "No se pudo copiar el registro.", ex);
            await Ui.Error(this, "No se pudo copiar: " + ex.Message);
        }
    }

    private async void OnBorrarClicked(object? sender, EventArgs e)
    {
        if (!await Ui.Confirmar(this, "Borrar registro",
                "Se eliminarán todos los archivos de registro de este equipo. ¿Continuar?"))
            return;

        try
        {
            var borrados = Registro.BorrarTodo();
            txtRegistro.Text = Registro.Ultimas(150);
            ActualizarResumen();
            await Ui.Exito(this, $"Se borraron {borrados} archivo(s) de registro.");
        }
        catch (Exception ex)
        {
            Registro.Error("vRegistro", "No se pudo borrar el registro.", ex);
            await Ui.Error(this, "No se pudo borrar: " + ex.Message);
        }
    }
}
