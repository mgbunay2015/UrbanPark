using QRCoder;
using UrbanPark.App.Servicios;

namespace UrbanPark.App.Views;

/// <summary>
/// Activación del autenticador de Google: muestra el QR (otpauth://) para escanearlo y
/// confirma el primer código de 6 dígitos. A partir de ahí el login pide ese código.
/// </summary>
public partial class vTotp : ContentPage
{
    private bool _preparando;
    private bool _confirmando;

    public vTotp()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_preparando) await PrepararAsync();
    }

    private async Task PrepararAsync()
    {
        _preparando = true;
        btnConfirmar.IsEnabled = false;
        try
        {
            var datos = await ApiCliente.TotpPrepararAsync();
            lblSecreto.Text = datos.Secreto;
            imgQr.Source = ImagenQr(datos.OtpAuthUri);
            lblAyuda.Text = datos.Mensaje;
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
            lblAyuda.Text = "No se pudo preparar el autenticador. Vuelve a intentarlo.";
        }
        finally
        {
            _preparando = false;
            btnConfirmar.IsEnabled = true;
        }
    }

    private async void OnConfirmarClicked(object? sender, EventArgs e)
    {
        if (_confirmando) return;

        var codigo = (txtCodigo.Text ?? "").Trim();
        if (codigo.Length != 6)
        {
            await Ui.Aviso(this, "Escribe el código de 6 dígitos que muestra Google Authenticator.");
            txtCodigo.Focus();
            return;
        }

        _confirmando = true;
        btnConfirmar.IsEnabled = false;
        btnConfirmar.Text = "Comprobando…";
        try
        {
            var estado = await ApiCliente.TotpConfirmarAsync(codigo);
            await Ui.Exito(this, estado.Mensaje);
            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            await Ui.MostrarExcepcion(this, ex);
            txtCodigo.Text = "";
            txtCodigo.Focus();
        }
        finally
        {
            _confirmando = false;
            btnConfirmar.IsEnabled = true;
            btnConfirmar.Text = "Confirmar y activar";
        }
    }

    private async void OnCancelarClicked(object? sender, EventArgs e)
    {
        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
    }

    private static ImageSource ImagenQr(string texto)
    {
        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(texto, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(datos);
        var bytes = png.GetGraphic(14);
        return ImageSource.FromStream(() => new MemoryStream(bytes));
    }
}
