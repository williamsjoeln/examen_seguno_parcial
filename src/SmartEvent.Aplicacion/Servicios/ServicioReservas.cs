using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Aplicacion.Sesion;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Excepciones;
using SmartEvent.Dominio.Ia;
using SmartEvent.Dominio.Reglas;

namespace SmartEvent.Aplicacion.Servicios;

/// <summary>
/// Resultado de confirmar o cancelar una reserva.
///
/// Separa DELIBERADAMENTE el resultado del cambio de estado del resultado del
/// envio de correo, porque son dos operaciones independientes: el examen exige
/// que un fallo de SMTP no deshaga ni repita el cambio de estado (CA-07).
/// </summary>
public sealed class ResultadoOperacionReserva
{
    public required ResultadoCambioEstado CambioEstado { get; init; }

    /// <summary>Resultado del correo. Null si no se llego a intentar.</summary>
    public ResultadoEnvioCorreo? Correo { get; init; }

    /// <summary>Texto completo para mostrar al usuario.</summary>
    public string MensajeResumen =>
        Correo is null
            ? CambioEstado.Mensaje
            : $"{CambioEstado.Mensaje}{Environment.NewLine}{Correo.MensajeUsuario}";

    /// <summary>El estado cambio y ademas el correo salio bien.</summary>
    public bool TodoCorrecto => CambioEstado.HuboCambio && Correo?.Exitoso == true;
}

/// <summary>
/// Casos de uso de reservas. Es el unico punto por el que pasan las
/// operaciones de negocio de FrmReservaEdicion y FrmReservasConsulta.
///
/// ORDEN DE LAS OPERACIONES AL CONFIRMAR O CANCELAR, que conviene saber
/// explicar en la defensa:
///
///   1. Se cambia el estado en SQL Server, dentro de su propia transaccion.
///      El procedimiento valida email, disponibilidad vigente, analisis de IA
///      y transiciones permitidas. Si algo falla, no cambia nada.
///   2. SOLO SI el estado quedo bien, se intenta enviar el correo.
///   3. El intento de correo se audita SIEMPRE, salga bien o mal.
///
/// El correo va DESPUES y FUERA de la transaccion a proposito. Si fuera al
/// reves, un servidor SMTP lento bloquearia la transaccion de la base de datos,
/// y un fallo de red obligaria a deshacer un cambio de estado que era
/// perfectamente valido. Como el cambio de estado es idempotente, reintentar el
/// correo nunca lo duplica.
/// </summary>
public sealed class ServicioReservas
{
    private readonly IReservaRepositorio _reservas;
    private readonly IAuditoriaRepositorio _auditoria;
    private readonly IServicioCorreo _correo;
    private readonly IServicioAnalisisIa _ia;
    private readonly IRegistradorSeguro _registro;
    private readonly SesionUsuario _sesion;

    public ServicioReservas(
        IReservaRepositorio reservas,
        IAuditoriaRepositorio auditoria,
        IServicioCorreo correo,
        IServicioAnalisisIa ia,
        IRegistradorSeguro registro,
        SesionUsuario sesion)
    {
        _reservas = reservas ?? throw new ArgumentNullException(nameof(reservas));
        _auditoria = auditoria ?? throw new ArgumentNullException(nameof(auditoria));
        _correo = correo ?? throw new ArgumentNullException(nameof(correo));
        _ia = ia ?? throw new ArgumentNullException(nameof(ia));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    // ===================== CONSULTA Y EDICION =====================

    public Task<PaginaReservas> ConsultarAsync(FiltroConsultaReserva filtro, CancellationToken cancelacion) =>
        _reservas.ConsultarAsync(filtro, cancelacion);

    public Task<Reserva?> ObtenerAsync(int idReserva, CancellationToken cancelacion) =>
        _reservas.ObtenerPorIdAsync(idReserva, cancelacion);

    public Task<IReadOnlyList<ConflictoDisponibilidad>> ValidarDisponibilidadAsync(
        SolicitudGuardarReserva solicitud, CancellationToken cancelacion) =>
        _reservas.ValidarDisponibilidadAsync(
            solicitud.IdReserva,
            solicitud.IdSalon,
            solicitud.FechaEvento,
            solicitud.HoraInicio,
            solicitud.HoraFin,
            solicitud.NumeroInvitados,
            solicitud.Detalles,
            cancelacion);

    /// <summary>
    /// Guarda la reserva. Antes valida en el cliente para dar un mensaje
    /// inmediato y senalar el campo concreto; despues llama al procedimiento
    /// almacenado, que es quien decide de verdad.
    /// </summary>
    public async Task<ResultadoGuardarReserva> GuardarAsync(
        SolicitudGuardarReserva solicitud,
        int capacidadSalon,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        _sesion.Exigir(Permiso.GestionarReservas);

        var problemas = ValidadorReserva.Validar(solicitud, capacidadSalon, _sesion.Rol);

        if (problemas.Count > 0)
        {
            throw new ExcepcionNegocio(
                "No se puede guardar la reserva:" + Environment.NewLine + ValidadorReserva.Describir(problemas));
        }

        return await _reservas.GuardarAsync(solicitud, cancelacion).ConfigureAwait(false);
    }

    // ===================== ANALISIS DE IA =====================

    /// <summary>
    /// Ejecuta el analisis de IA y PERSISTE SIEMPRE el resultado, salga bien o
    /// mal. Nunca lanza excepcion por un fallo del servicio: devuelve la
    /// ejecucion marcada como no exitosa para que la aplicacion siga operativa
    /// (caso CA-09).
    ///
    /// LIMITE DE LA IA: este metodo devuelve informacion y nada mas. No cambia
    /// el estado de la reserva, no toca los totales y no ejecuta ninguna otra
    /// operacion. Confirmar o cancelar sigue siendo una decision del usuario.
    /// </summary>
    public async Task<EjecucionAnalisisIa> AnalizarConIaAsync(Reserva reserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        _sesion.Exigir(Permiso.AnalizarConIa);

        var ejecucion = await _ia.AnalizarAsync(reserva, cancelacion).ConfigureAwait(false);

        // La auditoria se guarda igual si el analisis fallo: el examen exige
        // registrar el modelo utilizado, el resultado y el error.
        await _auditoria.RegistrarAnalisisIaAsync(new RegistroAnalisisIa
        {
            IdReserva = reserva.IdReserva,
            Proveedor = ejecucion.Proveedor,
            Modelo = ejecucion.Modelo,
            PromptVersion = ejecucion.PromptVersion,
            RespuestaJson = ejecucion.RespuestaJson,
            NivelRiesgo = ejecucion.Resultado?.Nivel,
            TokensEntrada = ejecucion.TokensEntrada,
            TokensSalida = ejecucion.TokensSalida,
            DuracionMs = ejecucion.DuracionMs,
            Exitoso = ejecucion.Exitoso,
            Error = ejecucion.DetalleTecnico,
            IdUsuario = _sesion.IdUsuario
        }, cancelacion).ConfigureAwait(false);

        return ejecucion;
    }

    /// <summary>
    /// Registra una CONTINGENCIA MANUAL: la justificacion que permite confirmar
    /// una reserva cuando el analisis de IA no esta disponible (regla D22).
    ///
    /// Queda auditada en evt.AnalisisIA con EsContingenciaManual = 1, de modo
    /// que siempre se puede saber que reservas se confirmaron sin analisis y
    /// con que justificacion.
    /// </summary>
    public async Task RegistrarContingenciaIaAsync(
        int idReserva,
        string justificacion,
        CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.AnalizarConIa);

        if (!ReglasReserva.JustificacionContingenciaEsValida(justificacion))
        {
            throw new ExcepcionNegocio(
                $"La justificacion de contingencia debe tener al menos "
                + $"{ReglasReserva.LongitudMinimaJustificacionContingencia} caracteres.");
        }

        await _auditoria.RegistrarAnalisisIaAsync(new RegistroAnalisisIa
        {
            IdReserva = idReserva,
            Proveedor = "CONTINGENCIA",
            Modelo = "N/A",
            PromptVersion = _ia.PromptVersion,
            Exitoso = false,
            Error = "Confirmacion autorizada sin analisis de IA mediante contingencia manual.",
            EsContingenciaManual = true,
            JustificacionContingencia = justificacion.Trim(),
            IdUsuario = _sesion.IdUsuario
        }, cancelacion).ConfigureAwait(false);

        _registro.Advertencia(
            $"Contingencia manual registrada para la reserva {idReserva} por el usuario {_sesion.IdUsuario}.");
    }

    // ===================== CAMBIOS DE ESTADO =====================

    /// <summary>
    /// Confirma la reserva y, si el estado cambio, intenta notificar al cliente.
    /// </summary>
    public Task<ResultadoOperacionReserva> ConfirmarAsync(int idReserva, CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.ConfirmarReserva);
        return CambiarEstadoYNotificarAsync(
            idReserva, EstadoReserva.Confirmada, null, TipoEventoCorreo.Confirmacion, cancelacion);
    }

    /// <summary>
    /// Cancela la reserva con un motivo obligatorio de al menos 20 caracteres
    /// y, si el estado cambio, notifica al cliente.
    /// </summary>
    public Task<ResultadoOperacionReserva> CancelarAsync(
        int idReserva, string motivo, CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.CancelarReserva);

        if (!ReglasReserva.MotivoCancelacionEsValido(motivo))
        {
            throw new ExcepcionNegocio(
                $"Para cancelar una reserva debe indicar un motivo de al menos "
                + $"{ReglasReserva.LongitudMinimaMotivoCancelacion} caracteres.");
        }

        return CambiarEstadoYNotificarAsync(
            idReserva, EstadoReserva.Cancelada, motivo.Trim(), TipoEventoCorreo.Cancelacion, cancelacion);
    }

    /// <summary>
    /// Marca la reserva como FINALIZADA. No genera correo: el evento ya ocurrio
    /// y el examen solo exige notificar al confirmar o cancelar.
    /// </summary>
    public async Task<ResultadoCambioEstado> FinalizarAsync(int idReserva, CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.GestionarReservas);

        return await _reservas
            .CambiarEstadoAsync(idReserva, EstadoReserva.Finalizada, null, _sesion.IdUsuario, cancelacion)
            .ConfigureAwait(false);
    }

    private async Task<ResultadoOperacionReserva> CambiarEstadoYNotificarAsync(
        int idReserva,
        EstadoReserva estadoNuevo,
        string? motivo,
        TipoEventoCorreo tipoEvento,
        CancellationToken cancelacion)
    {
        // --- Paso 1: el cambio de estado, en su propia transaccion ---
        var cambio = await _reservas
            .CambiarEstadoAsync(idReserva, estadoNuevo, motivo, _sesion.IdUsuario, cancelacion)
            .ConfigureAwait(false);

        // Si la reserva ya estaba en ese estado no se vuelve a notificar de
        // forma automatica: para eso existe el reenvio explicito.
        if (!cambio.HuboCambio)
        {
            return new ResultadoOperacionReserva { CambioEstado = cambio };
        }

        // --- Paso 2: el correo, fuera de la transaccion ---
        var correo = await IntentarEnviarCorreoAsync(idReserva, tipoEvento, motivo, cancelacion)
            .ConfigureAwait(false);

        return new ResultadoOperacionReserva { CambioEstado = cambio, Correo = correo };
    }

    // ===================== CORREO =====================

    /// <summary>
    /// Reenvio explicito solicitado por el usuario desde la interfaz.
    ///
    /// NO toca el estado de la reserva: solo vuelve a intentar el correo. Es lo
    /// que permite demostrar CA-07, porque cada intento queda auditado con su
    /// propio numero de intento y la reserva no se duplica.
    /// </summary>
    public async Task<ResultadoEnvioCorreo> ReenviarCorreoAsync(
        int idReserva,
        TipoEventoCorreo tipoEvento,
        CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.ConsultarReservas);

        _registro.Informacion(
            $"Reenvio manual de correo solicitado. Reserva={idReserva} Tipo={tipoEvento} "
            + $"Usuario={_sesion.IdUsuario}.");

        return await IntentarEnviarCorreoAsync(idReserva, tipoEvento, null, cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Envia el correo y audita SIEMPRE el intento, tanto si sale bien como si
    /// falla. Nunca propaga la excepcion: el examen exige que un fallo de SMTP
    /// no interrumpa el flujo de la aplicacion.
    /// </summary>
    private async Task<ResultadoEnvioCorreo> IntentarEnviarCorreoAsync(
        int idReserva,
        TipoEventoCorreo tipoEvento,
        string? motivo,
        CancellationToken cancelacion)
    {
        var reserva = await _reservas.ObtenerPorIdAsync(idReserva, cancelacion).ConfigureAwait(false);

        if (reserva is null)
        {
            return new ResultadoEnvioCorreo(
                false,
                "No se encontro la reserva para notificar al cliente.",
                "La reserva no existe al momento de construir el correo.",
                _correo.DescripcionServidor,
                0);
        }

        var datos = new DatosCorreoReserva
        {
            Reserva = reserva,
            TipoEvento = tipoEvento,
            Motivo = motivo
        };

        var asunto = _correo.ConstruirAsunto(datos);
        ResultadoEnvioCorreo resultado;

        try
        {
            resultado = await _correo.EnviarAsync(datos, cancelacion).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Red de seguridad: aunque la implementacion de correo fallara de
            // una forma no prevista, la aplicacion sigue funcionando.
            _registro.Error($"Fallo no controlado al enviar el correo de la reserva {idReserva}.", ex);

            resultado = new ResultadoEnvioCorreo(
                false,
                "No se pudo enviar el correo al cliente. Puede reintentarlo desde la consulta de reservas.",
                $"{ex.GetType().Name}: {ex.Message}",
                _correo.DescripcionServidor,
                0);
        }

        // --- La auditoria del intento se guarda pase lo que pase ---
        try
        {
            await _auditoria.RegistrarCorreoAsync(new RegistroCorreo
            {
                IdReserva = idReserva,
                Destinatario = reserva.ClienteEmail,
                Asunto = asunto,
                TipoEvento = tipoEvento,
                Estado = resultado.Exitoso ? EstadoCorreo.Enviado : EstadoCorreo.Error,
                Error = resultado.Exitoso ? null : (resultado.DetalleTecnico ?? "Error no especificado."),
                ServidorSmtp = resultado.ServidorSmtp,
                DuracionMs = resultado.DuracionMs,
                IdUsuario = _sesion.IdUsuario
            }, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Si ni siquiera se puede auditar, se deja constancia en el log y se
            // continua: no tiene sentido tumbar la operacion por esto.
            _registro.Error($"No se pudo auditar el intento de correo de la reserva {idReserva}.", ex);
        }

        return resultado;
    }

    // ===================== AUDITORIA =====================

    public Task<IReadOnlyList<ReservaAuditoria>> ConsultarCambiosEstadoAsync(
        int idReserva, CancellationToken cancelacion) =>
        _auditoria.ConsultarCambiosEstadoAsync(idReserva, cancelacion);

    public Task<IReadOnlyList<AnalisisIa>> ConsultarAnalisisAsync(
        FiltroAuditoriaIa filtro, CancellationToken cancelacion) =>
        _auditoria.ConsultarAnalisisIaAsync(filtro, cancelacion);

    public Task<IReadOnlyList<CorreoEnviado>> ConsultarCorreosAsync(
        FiltroAuditoriaCorreo filtro, CancellationToken cancelacion) =>
        _auditoria.ConsultarCorreosAsync(filtro, cancelacion);
}
