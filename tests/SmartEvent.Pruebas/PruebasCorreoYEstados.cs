using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Integraciones.Correo;
using SmartEvent.Infraestructura.Configuracion;
using SmartEvent.Infraestructura.Registro;
using Xunit;

namespace SmartEvent.Pruebas;

/// <summary>
/// Casos de aceptacion CA-06 y CA-07, ejecutados de extremo a extremo contra la
/// base de datos real y un servidor SMTP real.
///
/// CA-06  Confirmar una reserva valida: debe cambiar UNA sola vez de estado,
///        generar correo y dejar auditoria.
/// CA-07  Simular falla SMTP y reintentar: no se duplica la reserva ni el
///        cambio de estado, y quedan AMBOS intentos auditados.
///
/// COMO SE SIMULA LA FALLA DE SMTP: el primer envio apunta a un puerto donde no
/// escucha nadie, de modo que MailKit falla de verdad con un error de conexion.
/// El reintento apunta al servidor real. Asi la prueba es totalmente automatica
/// y no depende de que alguien apague un servicio a mano.
///
/// El servidor real recomendado para las evidencias es smtp4dev en local:
///     dotnet tool install -g Rnwood.Smtp4dev
///     smtp4dev --smtpport=2525 --urls=http://localhost:5080
/// Si no esta levantado, las pruebas que lo necesitan se omiten con un mensaje
/// explicativo en lugar de fallar.
/// </summary>
[Collection(ColeccionIntegracion.Nombre)]
public sealed class PruebasCorreoYEstados
{
    private readonly ContextoPruebas _ctx;

    public PruebasCorreoYEstados(ContextoPruebas contexto) => _ctx = contexto;

    private const string HostSmtpPruebas = "localhost";
    private const int PuertoSmtpReal = 2525;

    /// <summary>Puerto donde deliberadamente no escucha nadie, para forzar el fallo.</summary>
    private const int PuertoSmtpCaido = 65123;

    private static RegistradorArchivo CrearRegistrador() =>
        new(Options.Create(new OpcionesRegistro
        {
            CarpetaLogs = Path.Combine(Path.GetTempPath(), "smartevent-pruebas"),
            DiasRetencion = 1
        }));

    /// <summary>Comprueba si hay un servidor SMTP escuchando en el puerto indicado.</summary>
    private static bool HaySmtpEscuchando()
    {
        try
        {
            using var cliente = new TcpClient();
            var conexion = cliente.BeginConnect(HostSmtpPruebas, PuertoSmtpReal, null, null);
            var conectado = conexion.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(700));

            if (conectado)
            {
                cliente.EndConnect(conexion);
            }

            return conectado;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static ServicioCorreoMailKit CrearServicioCorreo(int puerto, RegistradorArchivo registro) =>
        new(Options.Create(new OpcionesSmtp
        {
            Host = HostSmtpPruebas,
            Puerto = puerto,
            UsarSsl = false,
            RemitenteNombre = "SmartEvent AI",
            RemitenteCorreo = "no-responder@smartevent.local",
            SegundosTiempoEspera = 10
        }), registro);

    /// <summary>Crea una reserva confirmable: la guarda y le registra una contingencia de IA.</summary>
    private async Task<(int IdReserva, string Codigo, int IdUsuario)> CrearReservaConfirmableAsync(
        int desplazamientoDias, CancellationToken ct)
    {
        var coord = await AutenticarAsync("coordinador", "Coord#2026", ct);
        var admin = await AutenticarAsync("admin", "Admin#2026", ct);

        var clientes = await _ctx.Catalogos.ConsultarClientesAsync("1712345678", true, ct);
        var salones = await _ctx.Catalogos.ConsultarSalonesAsync(null, true, ct);
        var recursos = await _ctx.Catalogos.ConsultarRecursosAsync(null, true, ct);

        var reserva = await _ctx.Reservas.GuardarAsync(new SolicitudGuardarReserva
        {
            IdCliente = clientes.Single(c => c.Identificacion == "1712345678").IdCliente,
            IdSalon = salones.Single(s => s.Nombre == "Salon Guayaquil").IdSalon,
            FechaEvento = DateOnly.FromDateTime(DateTime.Today)
                .AddDays(ContextoPruebas.DiasDesplazamientoBase + desplazamientoDias),
            HoraInicio = new TimeSpan(10, 0, 0),
            HoraFin = new TimeSpan(14, 0, 0),
            NumeroInvitados = 60,
            Observacion = "Reserva para prueba de correo",
            IdUsuario = coord,
            Detalles =
            [
                new LineaDetalleSolicitud(recursos.Single(r => r.Nombre == "Proyector 4K").IdRecurso, 2, 45.00m, 0m),
                new LineaDetalleSolicitud(recursos.Single(r => r.Nombre == "Silla ejecutiva").IdRecurso, 60, 3.50m, 0m)
            ]
        }, ct);

        // Sin analisis de IA la confirmacion se rechaza (regla D22). Se registra
        // una contingencia manual justificada, que es la via que contempla el
        // propio examen.
        await _ctx.Auditoria.RegistrarAnalisisIaAsync(new RegistroAnalisisIa
        {
            IdReserva = reserva.IdReserva,
            Proveedor = "CONTINGENCIA",
            Modelo = "N/A",
            PromptVersion = "v1",
            Exitoso = false,
            Error = "Analisis omitido en la prueba automatizada de correo.",
            EsContingenciaManual = true,
            JustificacionContingencia =
                "Prueba automatizada del flujo de confirmacion y notificacion por correo electronico.",
            IdUsuario = admin
        }, ct);

        return (reserva.IdReserva, reserva.Codigo, coord);
    }

    private async Task<int> AutenticarAsync(string usuario, string contrasena, CancellationToken ct)
    {
        var parametros = await _ctx.Usuarios.ObtenerParametrosDerivacionAsync(usuario, ct);

        var hash = Dominio.Seguridad.HashContrasena.DerivarConParametros(
            contrasena, parametros.SaltBase64, parametros.Iteraciones);

        var resultado = await _ctx.Usuarios.AutenticarAsync(usuario, hash, "PRUEBAS", ct);

        Assert.True(resultado.Autenticado, $"No se pudo autenticar a '{usuario}'.");
        return resultado.Usuario!.IdUsuario;
    }

    /// <summary>Envia el correo de la reserva y registra el intento, igual que hace ServicioReservas.</summary>
    private async Task<ResultadoEnvioCorreo> EnviarYAuditarAsync(
        ServicioCorreoMailKit servicio, int idReserva, int idUsuario, CancellationToken ct)
    {
        var reserva = await _ctx.Reservas.ObtenerPorIdAsync(idReserva, ct);
        Assert.NotNull(reserva);

        var datos = new DatosCorreoReserva
        {
            Reserva = reserva!,
            TipoEvento = TipoEventoCorreo.Confirmacion
        };

        var resultado = await servicio.EnviarAsync(datos, ct);

        await _ctx.Auditoria.RegistrarCorreoAsync(new RegistroCorreo
        {
            IdReserva = idReserva,
            Destinatario = reserva!.ClienteEmail,
            Asunto = servicio.ConstruirAsunto(datos),
            TipoEvento = TipoEventoCorreo.Confirmacion,
            Estado = resultado.Exitoso ? EstadoCorreo.Enviado : EstadoCorreo.Error,
            Error = resultado.Exitoso ? null : resultado.DetalleTecnico,
            ServidorSmtp = resultado.ServidorSmtp,
            DuracionMs = resultado.DuracionMs,
            IdUsuario = idUsuario
        }, ct);

        return resultado;
    }

    // =====================================================================
    // CA-06
    // =====================================================================

    /// <summary>
    /// CA-06: confirmar una reserva valida debe cambiar UNA sola vez de estado,
    /// generar correo y dejar auditoria.
    /// </summary>
    [Fact]
    public async Task CA06_ConfirmarReservaValida_CambiaUnaVez_EnviaCorreoYAudita()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        Assert.SkipUnless(HaySmtpEscuchando(),
            $"No hay un servidor SMTP escuchando en {HostSmtpPruebas}:{PuertoSmtpReal}. "
            + "Ejecute: smtp4dev --smtpport=2525 --urls=http://localhost:5080");

        var ct = TestContext.Current.CancellationToken;
        using var registro = CrearRegistrador();

        var (idReserva, codigo, idUsuario) = await CrearReservaConfirmableAsync(20, ct);

        // ---------- Cambio de estado ----------
        var cambio = await _ctx.Reservas.CambiarEstadoAsync(
            idReserva, EstadoReserva.Confirmada, null, idUsuario, ct);

        Assert.True(cambio.HuboCambio);
        Assert.Equal(EstadoReserva.Confirmada, cambio.Estado);

        // ---------- Correo ----------
        var servicio = CrearServicioCorreo(PuertoSmtpReal, registro);
        var envio = await EnviarYAuditarAsync(servicio, idReserva, idUsuario, ct);

        Assert.True(envio.Exitoso, $"El correo no se envio: {envio.DetalleTecnico}");
        Assert.Equal($"{HostSmtpPruebas}:{PuertoSmtpReal}", envio.ServidorSmtp);

        // ---------- Auditoria: UNA sola transicion a CONFIRMADA ----------
        var auditoria = await _ctx.Auditoria.ConsultarCambiosEstadoAsync(idReserva, ct);
        Assert.Single(auditoria, a => a.EstadoNuevo == EstadoReserva.Confirmada);

        // ---------- Auditoria: UN correo, en estado ENVIADO ----------
        var correos = await _ctx.Auditoria.ConsultarCorreosAsync(
            new FiltroAuditoriaCorreo { IdReserva = idReserva }, ct);

        Assert.Single(correos);
        Assert.Equal(EstadoCorreo.Enviado, correos[0].Estado);
        Assert.Equal(1, correos[0].Intento);
        Assert.Null(correos[0].Error);
        Assert.Contains(codigo, correos[0].Asunto, StringComparison.Ordinal);
    }

    // =====================================================================
    // CA-07
    // =====================================================================

    /// <summary>
    /// CA-07: tras una falla de SMTP y un reintento correcto, la reserva NO se
    /// duplica, el cambio de estado NO se repite, y quedan auditados los DOS
    /// intentos con numeros distintos.
    /// </summary>
    [Fact]
    public async Task CA07_FallaSmtpYReintento_NoDuplicaNadaYAuditaAmbosIntentos()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        Assert.SkipUnless(HaySmtpEscuchando(),
            $"No hay un servidor SMTP escuchando en {HostSmtpPruebas}:{PuertoSmtpReal}. "
            + "Ejecute: smtp4dev --smtpport=2525 --urls=http://localhost:5080");

        var ct = TestContext.Current.CancellationToken;
        using var registro = CrearRegistrador();

        var (idReserva, _, idUsuario) = await CrearReservaConfirmableAsync(21, ct);

        // ---------- Confirmacion: una sola vez ----------
        var cambio = await _ctx.Reservas.CambiarEstadoAsync(
            idReserva, EstadoReserva.Confirmada, null, idUsuario, ct);

        Assert.True(cambio.HuboCambio);

        // ---------- INTENTO 1: el servidor SMTP no responde ----------
        var servicioCaido = CrearServicioCorreo(PuertoSmtpCaido, registro);
        var fallido = await EnviarYAuditarAsync(servicioCaido, idReserva, idUsuario, ct);

        Assert.False(fallido.Exitoso);
        Assert.False(string.IsNullOrWhiteSpace(fallido.DetalleTecnico));

        // El mensaje al usuario indica que la reserva NO se modifico.
        Assert.Contains("NO se modifico", fallido.MensajeUsuario, StringComparison.OrdinalIgnoreCase);

        // ---------- INTENTO 2: reenvio contra el servidor real ----------
        var servicioReal = CrearServicioCorreo(PuertoSmtpReal, registro);
        var correcto = await EnviarYAuditarAsync(servicioReal, idReserva, idUsuario, ct);

        Assert.True(correcto.Exitoso, $"El reenvio no se envio: {correcto.DetalleTecnico}");

        // ---------- Comprobacion 1: LOS DOS intentos quedaron auditados ----------
        var correos = (await _ctx.Auditoria.ConsultarCorreosAsync(
                new FiltroAuditoriaCorreo { IdReserva = idReserva }, ct))
            .OrderBy(c => c.Intento)
            .ToList();

        Assert.Equal(2, correos.Count);

        Assert.Equal(1, correos[0].Intento);
        Assert.Equal(EstadoCorreo.Error, correos[0].Estado);
        Assert.False(string.IsNullOrWhiteSpace(correos[0].Error));
        Assert.Equal($"{HostSmtpPruebas}:{PuertoSmtpCaido}", correos[0].ServidorSmtp);

        Assert.Equal(2, correos[1].Intento);
        Assert.Equal(EstadoCorreo.Enviado, correos[1].Estado);
        Assert.Null(correos[1].Error);
        Assert.Equal($"{HostSmtpPruebas}:{PuertoSmtpReal}", correos[1].ServidorSmtp);

        // ---------- Comprobacion 2: el estado cambio UNA sola vez ----------
        var auditoria = await _ctx.Auditoria.ConsultarCambiosEstadoAsync(idReserva, ct);
        Assert.Single(auditoria, a => a.EstadoNuevo == EstadoReserva.Confirmada);

        // ---------- Comprobacion 3: la reserva NO se duplico ----------
        var reserva = await _ctx.Reservas.ObtenerPorIdAsync(idReserva, ct);
        Assert.NotNull(reserva);

        var consulta = await _ctx.Reservas.ConsultarAsync(
            new FiltroConsultaReserva { Codigo = reserva!.Codigo }, ct);

        Assert.Equal(1, consulta.TotalFilas);
        Assert.Equal(EstadoReserva.Confirmada, reserva.Estado);

        // ---------- Comprobacion 4: reconfirmar es idempotente ----------
        var reconfirmar = await _ctx.Reservas.CambiarEstadoAsync(
            idReserva, EstadoReserva.Confirmada, null, idUsuario, ct);

        Assert.True(reconfirmar.SinCambio);

        var auditoriaFinal = await _ctx.Auditoria.ConsultarCambiosEstadoAsync(idReserva, ct);
        Assert.Single(auditoriaFinal, a => a.EstadoNuevo == EstadoReserva.Confirmada);
    }

    // =====================================================================
    // Cancelacion con correo
    // =====================================================================

    /// <summary>
    /// Cancelar una reserva confirmada genera su propio correo, con numeracion
    /// de intento independiente de la de confirmacion.
    /// </summary>
    [Fact]
    public async Task Cancelacion_GeneraCorreoPropioYNumeracionIndependiente()
    {
        Assert.SkipWhen(_ctx.MotivoOmision is not null, _ctx.MotivoOmision ?? "");
        Assert.SkipUnless(HaySmtpEscuchando(),
            $"No hay un servidor SMTP escuchando en {HostSmtpPruebas}:{PuertoSmtpReal}. "
            + "Ejecute: smtp4dev --smtpport=2525 --urls=http://localhost:5080");

        var ct = TestContext.Current.CancellationToken;
        using var registro = CrearRegistrador();

        var (idReserva, _, idUsuario) = await CrearReservaConfirmableAsync(22, ct);

        await _ctx.Reservas.CambiarEstadoAsync(idReserva, EstadoReserva.Confirmada, null, idUsuario, ct);

        var servicio = CrearServicioCorreo(PuertoSmtpReal, registro);
        await EnviarYAuditarAsync(servicio, idReserva, idUsuario, ct);

        // ---------- Cancelacion con motivo valido ----------
        const string motivo = "El cliente reprogramo el evento por cambio en su calendario corporativo.";

        var cancelacion = await _ctx.Reservas.CambiarEstadoAsync(
            idReserva, EstadoReserva.Cancelada, motivo, idUsuario, ct);

        Assert.True(cancelacion.HuboCambio);

        // ---------- Correo de cancelacion ----------
        var reserva = await _ctx.Reservas.ObtenerPorIdAsync(idReserva, ct);

        var datos = new DatosCorreoReserva
        {
            Reserva = reserva!,
            TipoEvento = TipoEventoCorreo.Cancelacion,
            Motivo = motivo
        };

        var envio = await servicio.EnviarAsync(datos, ct);
        Assert.True(envio.Exitoso);

        // El cuerpo del correo incluye el motivo de la cancelacion.
        var html = servicio.ConstruirCuerpoHtml(datos);
        Assert.Contains("Motivo de cancelacion", html, StringComparison.Ordinal);
        Assert.Contains("reprogramo el evento", html, StringComparison.Ordinal);
        Assert.Contains("CANCELADA", html, StringComparison.Ordinal);

        await _ctx.Auditoria.RegistrarCorreoAsync(new RegistroCorreo
        {
            IdReserva = idReserva,
            Destinatario = reserva!.ClienteEmail,
            Asunto = servicio.ConstruirAsunto(datos),
            TipoEvento = TipoEventoCorreo.Cancelacion,
            Estado = EstadoCorreo.Enviado,
            ServidorSmtp = envio.ServidorSmtp,
            DuracionMs = envio.DuracionMs,
            IdUsuario = idUsuario
        }, ct);

        // ---------- La numeracion de intentos es independiente por tipo ----------
        var confirmaciones = await _ctx.Auditoria.ConsultarCorreosAsync(
            new FiltroAuditoriaCorreo { IdReserva = idReserva, TipoEvento = TipoEventoCorreo.Confirmacion }, ct);

        var cancelaciones = await _ctx.Auditoria.ConsultarCorreosAsync(
            new FiltroAuditoriaCorreo { IdReserva = idReserva, TipoEvento = TipoEventoCorreo.Cancelacion }, ct);

        Assert.Single(confirmaciones);
        Assert.Single(cancelaciones);
        Assert.Equal(1, confirmaciones[0].Intento);
        Assert.Equal(1, cancelaciones[0].Intento);

        // ---------- CANCELADA es terminal ----------
        var terminal = await Assert.ThrowsAsync<Dominio.Excepciones.ExcepcionNegocio>(
            () => _ctx.Reservas.CambiarEstadoAsync(
                idReserva, EstadoReserva.Finalizada, null, idUsuario, ct));

        Assert.Equal(50020, terminal.NumeroSql);
    }
}
