using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Integraciones.Correo;
using SmartEvent.Integraciones.Ia;
using SmartEvent.Infraestructura.Configuracion;
using SmartEvent.Infraestructura.Registro;
using Xunit;

namespace SmartEvent.Pruebas;

/// <summary>
/// Pruebas de las integraciones de correo y de IA.
///
/// Cubren los casos CA-08 y CA-09 del examen y la exigencia de codificar el
/// HTML del correo.
/// </summary>
public sealed class PruebasIntegraciones
{
    /// <summary>Valores admitidos por el contrato del examen para nivelRiesgo.</summary>
    private static readonly string[] NivelesRiesgoValidos = ["BAJO", "MEDIO", "ALTO"];

    private static RegistradorArchivo CrearRegistrador() =>
        new(Options.Create(new OpcionesRegistro
        {
            CarpetaLogs = Path.Combine(Path.GetTempPath(), "smartevent-pruebas"),
            DiasRetencion = 1
        }));

    /// <summary>Reserva de ejemplo, construida en memoria, sin tocar la base de datos.</summary>
    private static Reserva CrearReservaEjemplo(string nombreCliente = "Corporacion Andina S.A.")
    {
        var reserva = new Reserva
        {
            IdReserva = 1,
            Codigo = "RSV-20260928-000001",
            IdCliente = 1,
            ClienteNombres = nombreCliente,
            ClienteEmail = "eventos@corporacionandina.ejemplo.com",
            IdSalon = 1,
            SalonNombre = "Salon Quito",
            SalonCapacidad = 120,
            SalonTarifaBase = 450.00m,
            FechaEvento = DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            HoraInicio = new TimeSpan(9, 0, 0),
            HoraFin = new TimeSpan(13, 0, 0),
            NumeroInvitados = 90,
            Estado = EstadoReserva.Confirmada,
            Observacion = "Evento corporativo anual"
        };

        reserva.Detalles.Add(new ReservaDetalle
        {
            IdDetalle = 1, IdRecurso = 1, RecursoNombre = "Proyector 4K", RecursoTipo = "Equipo",
            RecursoStock = 10, Cantidad = 3, PrecioUnitario = 45.00m, PorcentajeDescuento = 0m
        });
        reserva.Detalles.Add(new ReservaDetalle
        {
            IdDetalle = 2, IdRecurso = 5, RecursoNombre = "Silla ejecutiva", RecursoTipo = "Mobiliario",
            RecursoStock = 300, Cantidad = 90, PrecioUnitario = 3.50m, PorcentajeDescuento = 5m
        });
        reserva.Detalles.Add(new ReservaDetalle
        {
            IdDetalle = 3, IdRecurso = 7, RecursoNombre = "Servicio de catering", RecursoTipo = "Alimentacion",
            RecursoStock = 500, Cantidad = 90, PrecioUnitario = 9.75m, PorcentajeDescuento = 10m
        });

        reserva.RecalcularTotales();
        return reserva;
    }

    // =====================================================================
    // CORREO
    // =====================================================================

    [Fact]
    public void Correo_CuerpoHtml_IncluyeTodosLosDatosExigidosPorElExamen()
    {
        using var registro = CrearRegistrador();

        var servicio = new ServicioCorreoMailKit(
            Options.Create(new OpcionesSmtp { Host = "localhost", Puerto = 2525 }), registro);

        var reserva = CrearReservaEjemplo();
        var datos = new DatosCorreoReserva { Reserva = reserva, TipoEvento = TipoEventoCorreo.Confirmacion };

        var html = servicio.ConstruirCuerpoHtml(datos);
        var asunto = servicio.ConstruirAsunto(datos);

        // El examen exige codigo, cliente, salon, fecha, horario, recursos,
        // total y estado.
        Assert.Contains(reserva.Codigo, html, StringComparison.Ordinal);
        Assert.Contains("Corporacion Andina", html, StringComparison.Ordinal);
        Assert.Contains("Salon Quito", html, StringComparison.Ordinal);
        Assert.Contains("09:00", html, StringComparison.Ordinal);
        Assert.Contains("13:00", html, StringComparison.Ordinal);
        Assert.Contains("CONFIRMADA", html, StringComparison.Ordinal);

        // Tabla HTML del detalle, con las tres lineas.
        Assert.Contains("<table", html, StringComparison.Ordinal);
        Assert.Contains("Proyector 4K", html, StringComparison.Ordinal);
        Assert.Contains("Silla ejecutiva", html, StringComparison.Ordinal);
        Assert.Contains("Servicio de catering", html, StringComparison.Ordinal);

        Assert.Contains(reserva.Codigo, asunto, StringComparison.Ordinal);
    }

    [Fact]
    public void Correo_NombreConEtiquetasHtml_SeCodificaYNoSeInyecta()
    {
        using var registro = CrearRegistrador();

        var servicio = new ServicioCorreoMailKit(
            Options.Create(new OpcionesSmtp { Host = "localhost", Puerto = 2525 }), registro);

        // Un cliente cuyo nombre contiene etiquetas: si no se codificara, el
        // correo llevaria un script ejecutable.
        var reserva = CrearReservaEjemplo("<script>alert('x')</script> & Cia");
        var datos = new DatosCorreoReserva { Reserva = reserva, TipoEvento = TipoEventoCorreo.Confirmacion };

        var html = servicio.ConstruirCuerpoHtml(datos);

        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("&amp; Cia", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Correo_ServidorInexistente_DevuelveErrorSinLanzarExcepcion()
    {
        using var registro = CrearRegistrador();

        // Puerto donde no escucha nadie: simula la falla SMTP del caso CA-07.
        var servicio = new ServicioCorreoMailKit(
            Options.Create(new OpcionesSmtp
            {
                Host = "localhost",
                Puerto = 65123,
                SegundosTiempoEspera = 5
            }), registro);

        var datos = new DatosCorreoReserva
        {
            Reserva = CrearReservaEjemplo(),
            TipoEvento = TipoEventoCorreo.Confirmacion
        };

        // No debe lanzar: la aplicacion tiene que seguir funcionando.
        var resultado = await servicio.EnviarAsync(datos, TestContext.Current.CancellationToken);

        Assert.False(resultado.Exitoso);
        Assert.False(string.IsNullOrWhiteSpace(resultado.MensajeUsuario));
        Assert.False(string.IsNullOrWhiteSpace(resultado.DetalleTecnico));
        Assert.Equal("localhost:65123", resultado.ServidorSmtp);
    }

    [Fact]
    public async Task Correo_SinConfiguracion_NoIntentaEnviarYAvisa()
    {
        using var registro = CrearRegistrador();

        var servicio = new ServicioCorreoMailKit(Options.Create(new OpcionesSmtp()), registro);

        var datos = new DatosCorreoReserva
        {
            Reserva = CrearReservaEjemplo(),
            TipoEvento = TipoEventoCorreo.Cancelacion,
            Motivo = "El cliente desistio del evento por recorte de presupuesto."
        };

        var resultado = await servicio.EnviarAsync(datos, TestContext.Current.CancellationToken);

        Assert.False(resultado.Exitoso);
        Assert.Contains("no", resultado.MensajeUsuario, StringComparison.OrdinalIgnoreCase);
    }

    // =====================================================================
    // ANALISIS DE IA
    // =====================================================================

    private static ServicioAnalisisIaResponses CrearServicioIa(OpcionesOpenAi opciones, RegistradorArchivo registro)
    {
        var servicios = new ServiceCollection();
        servicios.AddHttpClient(ServicioAnalisisIaResponses.NombreClienteHttp);
        var proveedor = servicios.BuildServiceProvider();

        return new ServicioAnalisisIaResponses(
            proveedor.GetRequiredService<IHttpClientFactory>(),
            Options.Create(opciones),
            registro);
    }

    /// <summary>
    /// CA-09: sin clave configurada, la aplicacion sigue operativa y muestra un
    /// mensaje seguro.
    /// </summary>
    [Fact]
    public async Task CA09_SinClaveDeApi_LaAplicacionSigueOperativaYAvisa()
    {
        using var registro = CrearRegistrador();

        var servicio = CrearServicioIa(new OpcionesOpenAi
        {
            ApiKey = null,
            BaseUrl = "https://api.openai.com/v1",
            Modelo = "gpt-5-mini"
        }, registro);

        Assert.False(servicio.EstaConfigurado);

        // No lanza excepcion: devuelve una ejecucion fallida y controlada.
        var ejecucion = await servicio.AnalizarAsync(CrearReservaEjemplo(), TestContext.Current.CancellationToken);

        Assert.False(ejecucion.Exitoso);
        Assert.Null(ejecucion.Resultado);
        Assert.False(string.IsNullOrWhiteSpace(ejecucion.MensajeUsuario));

        // El mensaje al usuario NO revela el nombre de la variable de entorno
        // ni ningun detalle tecnico: eso solo va a la auditoria.
        Assert.DoesNotContain("OPENAI_API_KEY", ejecucion.MensajeUsuario!, StringComparison.Ordinal);
        Assert.Contains("OPENAI_API_KEY", ejecucion.DetalleTecnico!, StringComparison.Ordinal);
    }

    /// <summary>CA-09: tiempo de espera agotado, tratado sin cerrar la aplicacion.</summary>
    [Fact]
    public async Task CA09_TiempoDeEsperaAgotado_SeTrataSinCerrarLaAplicacion()
    {
        using var registro = CrearRegistrador();

        // Direccion no enrutable: la conexion nunca se establece.
        var servicio = CrearServicioIa(new OpcionesOpenAi
        {
            ApiKey = "clave-de-prueba-que-no-se-usa",
            BaseUrl = "http://10.255.255.1:81/v1",
            Modelo = "modelo-de-prueba",
            SegundosTiempoEspera = 5
        }, registro);

        var ejecucion = await servicio.AnalizarAsync(CrearReservaEjemplo(), TestContext.Current.CancellationToken);

        Assert.False(ejecucion.Exitoso);
        Assert.False(string.IsNullOrWhiteSpace(ejecucion.MensajeUsuario));
        Assert.False(string.IsNullOrWhiteSpace(ejecucion.DetalleTecnico));

        // El mensaje del usuario nunca expone la direccion interna del servicio.
        Assert.DoesNotContain("10.255.255.1", ejecucion.MensajeUsuario!, StringComparison.Ordinal);
    }

    /// <summary>CA-09: clave invalida, error HTTP 401 traducido a un mensaje seguro.</summary>
    [Fact]
    public async Task CA09_ClaveInvalida_DevuelveMensajeSeguroSinExponerLaRespuesta()
    {
        var baseUrl = Environment.GetEnvironmentVariable("OpenAI__BaseUrl");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(baseUrl),
            "No se definio OpenAI__BaseUrl. Configure el servicio de IA para ejecutar esta prueba.");

        using var registro = CrearRegistrador();

        var servicio = CrearServicioIa(new OpcionesOpenAi
        {
            ApiKey = "gsk_clave_invalida_para_prueba_0000000000000000",
            BaseUrl = baseUrl!,
            Modelo = Environment.GetEnvironmentVariable("OpenAI__Modelo") ?? "openai/gpt-oss-120b",
            SegundosTiempoEspera = 30
        }, registro);

        var ejecucion = await servicio.AnalizarAsync(CrearReservaEjemplo(), TestContext.Current.CancellationToken);

        Assert.False(ejecucion.Exitoso);
        Assert.False(string.IsNullOrWhiteSpace(ejecucion.MensajeUsuario));

        // El detalle tecnico guarda el codigo HTTP para diagnostico...
        Assert.Contains("HTTP", ejecucion.DetalleTecnico!, StringComparison.Ordinal);
        // ...pero el mensaje del usuario no contiene la clave.
        Assert.DoesNotContain("gsk_", ejecucion.MensajeUsuario!, StringComparison.Ordinal);
    }

    /// <summary>
    /// CA-08: llamada REAL a la Responses API. Devuelve JSON estructurado,
    /// validado contra el contrato del examen.
    ///
    /// Se omite automaticamente si no hay clave configurada, de modo que el
    /// proyecto se pueda clonar y probar sin credenciales.
    /// </summary>
    [Fact]
    public async Task CA08_LlamadaReal_DevuelveJsonEstructuradoYValidado()
    {
        var clave = Environment.GetEnvironmentVariable(OpcionesOpenAi.VariableEntornoClave);

        Assert.SkipWhen(string.IsNullOrWhiteSpace(clave),
            $"No se definio {OpcionesOpenAi.VariableEntornoClave}. "
            + "Configure la clave para ejecutar la prueba de analisis real.");

        using var registro = CrearRegistrador();

        var servicio = CrearServicioIa(new OpcionesOpenAi
        {
            ApiKey = clave,
            BaseUrl = Environment.GetEnvironmentVariable("OpenAI__BaseUrl") ?? "https://api.openai.com/v1",
            Modelo = Environment.GetEnvironmentVariable("OpenAI__Modelo") ?? "gpt-5-mini",
            SegundosTiempoEspera = 90,
            PromptVersion = "v1"
        }, registro);

        Assert.True(servicio.EstaConfigurado);

        var ejecucion = await servicio.AnalizarAsync(CrearReservaEjemplo(), TestContext.Current.CancellationToken);

        Assert.True(ejecucion.Exitoso,
            $"El analisis fallo: {ejecucion.MensajeUsuario} | {ejecucion.DetalleTecnico}");

        var resultado = ejecucion.Resultado!;

        // Contrato exigido por el examen.
        Assert.Contains(resultado.NivelRiesgo, NivelesRiesgoValidos, StringComparer.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(resultado.Resumen));
        Assert.True(resultado.Resumen.Length <= ResultadoAnalisisIaLimites.Resumen);
        Assert.True(resultado.Alertas.Count <= ResultadoAnalisisIaLimites.MaxAlertas);
        Assert.InRange(resultado.Recomendaciones.Count,
            ResultadoAnalisisIaLimites.MinRecomendaciones, ResultadoAnalisisIaLimites.MaxRecomendaciones);
        Assert.False(string.IsNullOrWhiteSpace(resultado.CorreoSugerido));

        // Se persiste el JSON crudo y el modelo utilizado, como exige la auditoria.
        Assert.False(string.IsNullOrWhiteSpace(ejecucion.RespuestaJson));
        Assert.False(string.IsNullOrWhiteSpace(ejecucion.Modelo));
        Assert.False(string.IsNullOrWhiteSpace(ejecucion.Proveedor));
    }
}

/// <summary>Atajos a los limites del contrato, para que las pruebas se lean mejor.</summary>
internal static class ResultadoAnalisisIaLimites
{
    public const int Resumen = Dominio.Ia.ResultadoAnalisisIa.LongitudMaximaResumen;
    public const int MaxAlertas = Dominio.Ia.ResultadoAnalisisIa.MaximoAlertas;
    public const int MinRecomendaciones = Dominio.Ia.ResultadoAnalisisIa.MinimoRecomendaciones;
    public const int MaxRecomendaciones = Dominio.Ia.ResultadoAnalisisIa.MaximoRecomendaciones;
}
