namespace UrbanPark.Api.Servicios;

/// <summary>Se lanza cuando el servicio de espacios no esta disponible (equivale al fallo de Feign).</summary>
public sealed class EspaciosNoDisponibleException : Exception
{
    public EspaciosNoDisponibleException(string mensaje) : base(mensaje) { }
}

/// <summary>Resultado de una ejecucion protegida: incluye si se uso el fallback.</summary>
public sealed record ResultadoResiliente<T>(T Valor, bool Fallback, string? Mensaje, long Milisegundos);

/// <summary>
/// Circuit Breaker + Retry implementado a mano (sin dependencias externas), replicando los
/// parametros documentados de Resilience4j para la instancia "espaciosClient":
///   slidingWindowSize 10, failureRateThreshold 50%, waitDurationInOpenState 10s,
///   permittedNumberOfCallsInHalfOpenState 3, automaticTransitionFromOpenToHalfOpen.
///   Retry: maxAttempts 3, waitDuration 500ms con backoff exponencial x2.
///
/// DIFERENCIA DELIBERADA (mejora documentada): el original no definia minimumNumberOfCalls,
/// por lo que tomaba el valor por defecto de Resilience4j (100). Con una ventana de 10 el
/// circuito NUNCA podia abrirse. Aqui el minimo es el tamaño de la ventana (10), que es el
/// comportamiento que la demo pretende mostrar.
/// </summary>
public sealed class ResilienciaService
{
    private readonly int _ventana;
    private readonly int _umbralPorcentaje;
    private readonly int _esperaAbiertoMs;
    private readonly int _permitidasMitadAbierto;
    private readonly int _intentos;
    private readonly int _baseMs;
    private readonly double _multiplicador;

    private readonly object _candado = new();
    private readonly Queue<bool> _resultados = new();
    private string _estado = "CLOSED";
    private DateTime _abiertoHasta = DateTime.MinValue;
    private int _llamadasMitadAbierto;
    private long _transiciones = 0;

    public ResilienciaService(IConfiguration configuracion)
    {
        _ventana = configuracion.GetValue("Resiliencia:SlidingWindowSize", 10);
        _umbralPorcentaje = configuracion.GetValue("Resiliencia:FailureRateThreshold", 50);
        _esperaAbiertoMs = configuracion.GetValue("Resiliencia:WaitDurationInOpenStateSegundos", 10) * 1000;
        _permitidasMitadAbierto = configuracion.GetValue("Resiliencia:PermittedCallsInHalfOpen", 3);
        _intentos = Math.Max(1, configuracion.GetValue("Resiliencia:RetryMaxAttempts", 3));
        _baseMs = configuracion.GetValue("Resiliencia:RetryBaseMs", 500);
        _multiplicador = configuracion.GetValue("Resiliencia:RetryMultiplicador", 2);
    }

    public int Ventana => _ventana;
    public int UmbralPorcentaje => _umbralPorcentaje;
    public int IntentosMaximos => _intentos;

    public string Estado
    {
        get
        {
            lock (_candado)
            {
                TransicionarSiCorresponde();
                return _estado;
            }
        }
    }

    public int LlamadasEnVentana { get { lock (_candado) return _resultados.Count; } }
    public int FallosEnVentana { get { lock (_candado) return _resultados.Count(r => !r); } }
    public double TasaFallo
    {
        get
        {
            lock (_candado)
                return _resultados.Count == 0 ? 0 : Math.Round(_resultados.Count(r => !r) * 100.0 / _resultados.Count, 0);
        }
    }
    public long Transiciones { get { lock (_candado) return _transiciones; } }

    private void TransicionarSiCorresponde()
    {
        if (_estado == "OPEN" && DateTime.UtcNow >= _abiertoHasta)
        {
            _estado = "HALF_OPEN";
            _llamadasMitadAbierto = 0;
            _transiciones++;
        }
    }

    private bool PuedePasar(out string motivo)
    {
        lock (_candado)
        {
            TransicionarSiCorresponde();

            if (_estado == "OPEN")
            {
                var restante = Math.Max(0, (int)(_abiertoHasta - DateTime.UtcNow).TotalMilliseconds);
                motivo = $"Circuit Breaker ABIERTO: se rechaza la llamada sin intentarla (faltan {restante} ms para pasar a HALF_OPEN).";
                return false;
            }

            if (_estado == "HALF_OPEN")
            {
                if (_llamadasMitadAbierto >= _permitidasMitadAbierto)
                {
                    motivo = $"Circuit Breaker en HALF_OPEN: ya se permitieron {_permitidasMitadAbierto} llamadas de prueba.";
                    return false;
                }
                _llamadasMitadAbierto++;
            }

            motivo = "";
            return true;
        }
    }

    private void Registrar(bool exito)
    {
        lock (_candado)
        {
            _resultados.Enqueue(exito);
            while (_resultados.Count > _ventana)
                _resultados.Dequeue();

            if (_estado == "HALF_OPEN")
            {
                if (!exito)
                {
                    Abrir();
                }
                else if (_llamadasMitadAbierto >= _permitidasMitadAbierto)
                {
                    _estado = "CLOSED";
                    _resultados.Clear();
                    _transiciones++;
                }
                return;
            }

            if (_estado == "CLOSED" && _resultados.Count >= _ventana)
            {
                var tasa = _resultados.Count(r => !r) * 100.0 / _resultados.Count;
                if (tasa >= _umbralPorcentaje)
                    Abrir();
            }
        }
    }

    private void Abrir()
    {
        _estado = "OPEN";
        _abiertoHasta = DateTime.UtcNow.AddMilliseconds(_esperaAbiertoMs);
        _llamadasMitadAbierto = 0;
        _transiciones++;
    }

    public void Reiniciar()
    {
        lock (_candado)
        {
            _resultados.Clear();
            _estado = "CLOSED";
            _abiertoHasta = DateTime.MinValue;
            _llamadasMitadAbierto = 0;
        }
    }

    /// <summary>
    /// Ejecuta la accion con Circuit Breaker + Retry. Si todo falla, devuelve el fallback
    /// (nunca propaga la excepcion, igual que el fallback de Resilience4j del original).
    /// </summary>
    public async Task<ResultadoResiliente<T>> EjecutarAsync<T>(Func<Task<T>> accion, Func<Exception, T> fallback)
    {
        var cronometro = System.Diagnostics.Stopwatch.StartNew();

        if (!PuedePasar(out var motivoCircuito))
        {
            Registrar(false);
            var ex = new EspaciosNoDisponibleException(motivoCircuito);
            cronometro.Stop();
            return new ResultadoResiliente<T>(fallback(ex), true, ex.Message, cronometro.ElapsedMilliseconds);
        }

        Exception? ultimo = null;
        for (var intento = 1; intento <= _intentos; intento++)
        {
            try
            {
                var valor = await accion();
                Registrar(true);
                cronometro.Stop();
                return new ResultadoResiliente<T>(valor, false, null, cronometro.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                ultimo = ex;
                if (intento < _intentos)
                {
                    var espera = _baseMs * Math.Pow(_multiplicador, intento - 1);
                    await Task.Delay(TimeSpan.FromMilliseconds(espera));
                }
            }
        }

        Registrar(false);
        cronometro.Stop();
        var mensaje = ultimo?.Message ?? "Fallo desconocido";
        return new ResultadoResiliente<T>(fallback(ultimo ?? new Exception(mensaje)), true, mensaje, cronometro.ElapsedMilliseconds);
    }
}
