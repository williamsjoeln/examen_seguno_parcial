using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Aplicacion.Contratos;

/// <summary>
/// Acceso a datos de seguridad.
///
/// Todos los metodos son asincronicos y reciben CancellationToken, como exige
/// el examen. La implementacion vive en SmartEvent.Infraestructura y es la
/// unica que conoce Microsoft.Data.SqlClient.
/// </summary>
public interface IUsuarioRepositorio
{
    /// <summary>
    /// PRIMERA FASE de la autenticacion: obtiene algoritmo, iteraciones y salt
    /// del usuario, sin traer nunca el hash almacenado.
    /// </summary>
    Task<ParametrosDerivacion> ObtenerParametrosDerivacionAsync(
        string nombreUsuario,
        CancellationToken cancelacion);

    /// <summary>
    /// SEGUNDA FASE: envia el hash candidato para que SQL Server lo compare,
    /// actualice el contador de intentos fallidos y aplique el bloqueo temporal.
    /// </summary>
    Task<ResultadoAutenticacion> AutenticarAsync(
        string nombreUsuario,
        string hashCandidato,
        string? estacion,
        CancellationToken cancelacion);

    /// <summary>Comprueba que la base de datos responde. Alimenta el indicador de conectividad.</summary>
    Task<bool> ProbarConexionAsync(CancellationToken cancelacion);
}

/// <summary>Acceso a datos de los catalogos: clientes, salones y recursos.</summary>
public interface ICatalogoRepositorio
{
    Task<IReadOnlyList<Cliente>> ConsultarClientesAsync(string? texto, bool soloActivos, CancellationToken cancelacion);
    Task<int> GuardarClienteAsync(Cliente cliente, CancellationToken cancelacion);
    Task CambiarEstadoClienteAsync(int idCliente, bool estado, CancellationToken cancelacion);

    Task<IReadOnlyList<Salon>> ConsultarSalonesAsync(string? texto, bool soloActivos, CancellationToken cancelacion);
    Task<int> GuardarSalonAsync(Salon salon, CancellationToken cancelacion);
    Task CambiarEstadoSalonAsync(int idSalon, bool estado, CancellationToken cancelacion);

    Task<IReadOnlyList<Recurso>> ConsultarRecursosAsync(string? texto, bool soloActivos, CancellationToken cancelacion);
    Task<int> GuardarRecursoAsync(Recurso recurso, CancellationToken cancelacion);
    Task CambiarEstadoRecursoAsync(int idRecurso, bool estado, CancellationToken cancelacion);
}

/// <summary>Acceso a datos de reservas: la parte central del sistema.</summary>
public interface IReservaRepositorio
{
    /// <summary>
    /// Invoca evt.sp_Reserva_Guardar enviando el detalle completo en un unico
    /// parametro tipo tabla (TVP), de modo que cabecera y detalle se guardan
    /// dentro de la MISMA transaccion del procedimiento.
    /// </summary>
    Task<ResultadoGuardarReserva> GuardarAsync(SolicitudGuardarReserva solicitud, CancellationToken cancelacion);

    /// <summary>Recupera cabecera y detalle completos (dos conjuntos de resultados).</summary>
    Task<Reserva?> ObtenerPorIdAsync(int idReserva, CancellationToken cancelacion);

    /// <summary>Consulta paginada con filtros opcionales combinables.</summary>
    Task<PaginaReservas> ConsultarAsync(FiltroConsultaReserva filtro, CancellationToken cancelacion);

    /// <summary>
    /// Valida disponibilidad antes de guardar. Devuelve la lista de conflictos;
    /// vacia significa que la reserva es viable.
    /// </summary>
    Task<IReadOnlyList<ConflictoDisponibilidad>> ValidarDisponibilidadAsync(
        int? idReserva,
        int idSalon,
        DateOnly fechaEvento,
        TimeSpan horaInicio,
        TimeSpan horaFin,
        int numeroInvitados,
        IReadOnlyList<LineaDetalleSolicitud> detalles,
        CancellationToken cancelacion);

    /// <summary>Cambia el estado validando las transiciones permitidas. Es idempotente.</summary>
    Task<ResultadoCambioEstado> CambiarEstadoAsync(
        int idReserva,
        EstadoReserva estadoNuevo,
        string? motivo,
        int idUsuario,
        CancellationToken cancelacion);
}

/// <summary>Acceso a datos de auditoria: correos, analisis de IA y cambios de estado.</summary>
public interface IAuditoriaRepositorio
{
    Task<int> RegistrarAnalisisIaAsync(RegistroAnalisisIa registro, CancellationToken cancelacion);
    Task<IReadOnlyList<AnalisisIa>> ConsultarAnalisisIaAsync(FiltroAuditoriaIa filtro, CancellationToken cancelacion);

    /// <summary>
    /// Registra un intento de correo. El numero de intento lo calcula SQL Server
    /// a partir de los intentos previos de la misma reserva y tipo de evento,
    /// asi que un reenvio siempre queda como un registro nuevo y numerado.
    /// </summary>
    Task<(int IdCorreo, short Intento)> RegistrarCorreoAsync(RegistroCorreo registro, CancellationToken cancelacion);

    Task<IReadOnlyList<CorreoEnviado>> ConsultarCorreosAsync(FiltroAuditoriaCorreo filtro, CancellationToken cancelacion);

    Task<IReadOnlyList<ReservaAuditoria>> ConsultarCambiosEstadoAsync(int idReserva, CancellationToken cancelacion);
}
