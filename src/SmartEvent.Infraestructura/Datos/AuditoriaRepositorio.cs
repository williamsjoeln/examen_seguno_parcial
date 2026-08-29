using System.Data;
using Microsoft.Data.SqlClient;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Infraestructura.Datos;

/// <summary>
/// Acceso a datos de auditoria: analisis de IA, correos enviados y cambios de
/// estado. Es la fuente de FrmAuditoriaIntegraciones.
///
/// NOTA DE SEGURIDAD: en esta clase no se escribe ni se lee ninguna credencial.
/// De la configuracion SMTP solo se persiste host y puerto, y de OpenAI solo el
/// proveedor y el modelo. La API key jamas toca la base de datos.
/// </summary>
public sealed class AuditoriaRepositorio : IAuditoriaRepositorio
{
    private readonly IFabricaConexion _fabrica;
    private readonly IRegistradorSeguro _registro;

    public AuditoriaRepositorio(IFabricaConexion fabrica, IRegistradorSeguro registro)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
    }

    // ======================== ANALISIS DE IA ========================

    /// <summary>
    /// Persiste el resultado de un analisis, HAYA SALIDO BIEN O MAL. El examen
    /// exige guardar el modelo utilizado, el resultado y el error cuando
    /// corresponda: un fallo tambien es informacion auditable.
    /// </summary>
    public async Task<int> RegistrarAnalisisIaAsync(RegistroAnalisisIa registro, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(registro);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_AnalisisIA_Registrar", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, registro.IdReserva);
            comando.Agregar("@Proveedor", SqlDbType.VarChar, 30, registro.Proveedor);
            comando.Agregar("@Modelo", SqlDbType.VarChar, 80, registro.Modelo);
            comando.Agregar("@PromptVersion", SqlDbType.VarChar, 20, registro.PromptVersion);
            comando.Agregar("@RespuestaJson", SqlDbType.NVarChar, -1, registro.RespuestaJson);
            comando.Agregar("@NivelRiesgo", SqlDbType.VarChar, 6,
                registro.NivelRiesgo.HasValue ? TextosEnumeracion.ATexto(registro.NivelRiesgo.Value) : null);
            comando.Agregar("@TokensEntrada", SqlDbType.Int, registro.TokensEntrada);
            comando.Agregar("@TokensSalida", SqlDbType.Int, registro.TokensSalida);
            comando.Agregar("@DuracionMs", SqlDbType.Int, registro.DuracionMs);
            comando.Agregar("@Exitoso", SqlDbType.Bit, registro.Exitoso);
            comando.Agregar("@Error", SqlDbType.NVarChar, 500, Recortar(registro.Error, 500));
            comando.Agregar("@EsContingenciaManual", SqlDbType.Bit, registro.EsContingenciaManual);
            comando.Agregar("@JustificacionContingencia", SqlDbType.NVarChar, 500,
                Recortar(registro.JustificacionContingencia, 500));
            comando.Agregar("@IdUsuario", SqlDbType.Int, registro.IdUsuario);

            var salidaId = comando.AgregarSalida("@IdAnalisis", SqlDbType.Int);

            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            var id = salidaId.Salida<int>() ?? 0;

            _registro.Informacion(
                $"Analisis de IA registrado. Id={id} Reserva={registro.IdReserva} "
                + $"Proveedor={registro.Proveedor} Modelo={registro.Modelo} Exitoso={registro.Exitoso}.");

            return id;
        }
        catch (SqlException ex)
        {
            _registro.Error($"Error de SQL al registrar el analisis de IA de la reserva {registro.IdReserva}.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public async Task<IReadOnlyList<AnalisisIa>> ConsultarAnalisisIaAsync(
        FiltroAuditoriaIa filtro, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_AnalisisIA_Consultar", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, filtro.IdReserva);
            comando.Agregar("@Codigo", SqlDbType.VarChar, 24, Normalizar(filtro.Codigo));
            comando.AgregarFecha("@FechaDesde", filtro.FechaDesde);
            comando.AgregarFecha("@FechaHasta", filtro.FechaHasta);
            comando.Agregar("@SoloErrores", SqlDbType.Bit, filtro.SoloErrores);
            comando.Agregar("@NivelRiesgo", SqlDbType.VarChar, 6,
                filtro.NivelRiesgo.HasValue ? TextosEnumeracion.ATexto(filtro.NivelRiesgo.Value) : null);
            comando.Agregar("@MaximoFilas", SqlDbType.Int, filtro.MaximoFilas);

            var lista = new List<AnalisisIa>();
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                var nivelTexto = lector.TextoNulo("NivelRiesgo");
                NivelRiesgo? nivel = null;

                if (nivelTexto is not null && TextosEnumeracion.TryNivelRiesgo(nivelTexto, out var convertido))
                {
                    nivel = convertido;
                }

                lista.Add(new AnalisisIa
                {
                    IdAnalisis                = lector.Entero("IdAnalisis"),
                    IdReserva                 = lector.Entero("IdReserva"),
                    ReservaCodigo             = lector.TextoNulo("ReservaCodigo"),
                    Proveedor                 = lector.Texto("Proveedor"),
                    Modelo                    = lector.Texto("Modelo"),
                    PromptVersion             = lector.Texto("PromptVersion"),
                    NivelRiesgo               = nivel,
                    TokensEntrada             = lector.EnteroNulo("TokensEntrada"),
                    TokensSalida              = lector.EnteroNulo("TokensSalida"),
                    DuracionMs                = lector.EnteroNulo("DuracionMs"),
                    Exitoso                   = lector.Booleano("Exitoso"),
                    Error                     = lector.TextoNulo("Error"),
                    EsContingenciaManual      = lector.Booleano("EsContingenciaManual"),
                    JustificacionContingencia = lector.TextoNulo("JustificacionContingencia"),
                    RespuestaJson             = lector.TextoNulo("RespuestaJson"),
                    Usuario                   = lector.TextoNulo("Usuario"),
                    Fecha                     = lector.FechaHora("Fecha")
                });
            }

            return lista;
        }
        catch (SqlException ex)
        {
            _registro.Error("Error de SQL al consultar los analisis de IA.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    // ======================== CORREO ENVIADO ========================

    /// <summary>
    /// Registra un intento de correo. El numero de intento lo calcula SQL Server
    /// a partir de los intentos previos de la misma reserva y tipo de evento,
    /// asi que un reenvio siempre queda como una fila nueva y numerada. Esa es
    /// exactamente la evidencia que pide el caso CA-07.
    /// </summary>
    public async Task<(int IdCorreo, short Intento)> RegistrarCorreoAsync(
        RegistroCorreo registro, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(registro);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("com.sp_CorreoEnviado_Registrar", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, registro.IdReserva);
            comando.Agregar("@Destinatario", SqlDbType.VarChar, 150, registro.Destinatario);
            comando.Agregar("@Asunto", SqlDbType.NVarChar, 200, Recortar(registro.Asunto, 200));
            comando.Agregar("@TipoEvento", SqlDbType.VarChar, 20, TextosEnumeracion.ATexto(registro.TipoEvento));
            comando.Agregar("@Estado", SqlDbType.VarChar, 10, TextosEnumeracion.ATexto(registro.Estado));
            comando.Agregar("@Error", SqlDbType.NVarChar, 500, Recortar(registro.Error, 500));
            comando.Agregar("@ServidorSmtp", SqlDbType.VarChar, 120, Recortar(registro.ServidorSmtp, 120));
            comando.Agregar("@DuracionMs", SqlDbType.Int, registro.DuracionMs);
            comando.Agregar("@IdUsuario", SqlDbType.Int, registro.IdUsuario);

            var salidaId      = comando.AgregarSalida("@IdCorreo", SqlDbType.Int);
            var salidaIntento = comando.AgregarSalida("@Intento", SqlDbType.SmallInt);

            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            var id = salidaId.Salida<int>() ?? 0;
            var intento = salidaIntento.Salida<short>() ?? (short)1;

            _registro.Informacion(
                $"Intento de correo registrado. Id={id} Reserva={registro.IdReserva} "
                + $"Intento={intento} Estado={TextosEnumeracion.ATexto(registro.Estado)}.");

            return (id, intento);
        }
        catch (SqlException ex)
        {
            _registro.Error($"Error de SQL al registrar el correo de la reserva {registro.IdReserva}.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public async Task<IReadOnlyList<CorreoEnviado>> ConsultarCorreosAsync(
        FiltroAuditoriaCorreo filtro, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("com.sp_CorreoEnviado_Consultar", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, filtro.IdReserva);
            comando.Agregar("@Codigo", SqlDbType.VarChar, 24, Normalizar(filtro.Codigo));
            comando.Agregar("@Destinatario", SqlDbType.VarChar, 150, Normalizar(filtro.Destinatario));
            comando.AgregarFecha("@FechaDesde", filtro.FechaDesde);
            comando.AgregarFecha("@FechaHasta", filtro.FechaHasta);
            comando.Agregar("@Estado", SqlDbType.VarChar, 10,
                filtro.Estado.HasValue ? TextosEnumeracion.ATexto(filtro.Estado.Value) : null);
            comando.Agregar("@TipoEvento", SqlDbType.VarChar, 20,
                filtro.TipoEvento.HasValue ? TextosEnumeracion.ATexto(filtro.TipoEvento.Value) : null);
            comando.Agregar("@MaximoFilas", SqlDbType.Int, filtro.MaximoFilas);

            var lista = new List<CorreoEnviado>();
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                lista.Add(new CorreoEnviado
                {
                    IdCorreo      = lector.Entero("IdCorreo"),
                    IdReserva     = lector.Entero("IdReserva"),
                    ReservaCodigo = lector.TextoNulo("ReservaCodigo"),
                    Destinatario  = lector.Texto("Destinatario"),
                    Asunto        = lector.Texto("Asunto"),
                    TipoEvento    = TextosEnumeracion.TipoEventoDesde(lector.Texto("TipoEvento")),
                    Intento       = lector.Corto("Intento"),
                    Estado        = TextosEnumeracion.EstadoCorreoDesde(lector.Texto("Estado")),
                    Error         = lector.TextoNulo("Error"),
                    ServidorSmtp  = lector.TextoNulo("ServidorSmtp"),
                    DuracionMs    = lector.EnteroNulo("DuracionMs"),
                    Usuario       = lector.TextoNulo("Usuario"),
                    FechaIntento  = lector.FechaHora("FechaIntento")
                });
            }

            return lista;
        }
        catch (SqlException ex)
        {
            _registro.Error("Error de SQL al consultar los correos enviados.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    // ==================== CAMBIOS DE ESTADO ====================

    public async Task<IReadOnlyList<ReservaAuditoria>> ConsultarCambiosEstadoAsync(
        int idReserva, CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_ReservaAuditoria_Consultar", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, idReserva);

            var lista = new List<ReservaAuditoria>();
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                lista.Add(new ReservaAuditoria
                {
                    IdAuditoria    = lector.Entero("IdAuditoria"),
                    IdReserva      = lector.Entero("IdReserva"),
                    EstadoAnterior = MaquinaEstadosReserva.Desde(lector.Texto("EstadoAnterior")),
                    EstadoNuevo    = MaquinaEstadosReserva.Desde(lector.Texto("EstadoNuevo")),
                    Motivo         = lector.TextoNulo("Motivo"),
                    Usuario        = lector.TextoNulo("Usuario"),
                    Fecha          = lector.FechaHora("Fecha")
                });
            }

            return lista;
        }
        catch (SqlException ex)
        {
            _registro.Error($"Error de SQL al consultar la auditoria de la reserva {idReserva}.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    private static string? Normalizar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>
    /// Recorta un texto a la longitud de la columna. Evita que un mensaje de
    /// error muy largo provoque un fallo al insertar la propia auditoria, que
    /// seria el peor momento para fallar.
    /// </summary>
    private static string? Recortar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        var limpio = texto.Trim();
        return limpio.Length <= maximo ? limpio : limpio[..maximo];
    }
}
