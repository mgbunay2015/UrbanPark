namespace UrbanPark.Api.Servicios;

/// <summary>
/// Reemplaza la demo de Docker del sistema original (docker compose stop/start ms-espacios).
/// En la version movil el backend es un unico proceso, asi que la caida del servicio se
/// simula con una bandera para poder demostrar el Circuit Breaker + fallback igual que antes.
/// </summary>
public sealed class EstadoDemoService
{
    private readonly object _candado = new();
    private bool _caido;

    public EstadoDemoService(IConfiguration configuracion)
    {
        _caido = configuracion.GetValue("Demo:EspaciosCaido", false);
    }

    public bool EspaciosCaido
    {
        get { lock (_candado) return _caido; }
    }

    /// <summary>Ya no existe un contenedor real; se mantiene el campo por compatibilidad del contrato.</summary>
    public string ContenedorId => "urbanpark-espacios-interno";

    public void Detener()
    {
        lock (_candado) _caido = true;
    }

    public void Iniciar()
    {
        lock (_candado) _caido = false;
    }
}
