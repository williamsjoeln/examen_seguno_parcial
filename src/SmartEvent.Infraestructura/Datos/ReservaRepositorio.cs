using System.Data;
using Microsoft.Data.SqlClient;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Excepciones;

namespace SmartEvent.Infraestructura.Datos;

/// <summary>
/// Acceso a datos de reservas. Es la clase mas importante de la capa de
/// infraestructura: aqui se construye el parametro tipo tabla (TVP) que permite
/// guardar cabecera y detalle en UNA SOLA llamada y, por tanto, en una sola
/// transaccion del procedimiento almacenado.
/// </summary>
public sealed class ReservaRepositorio : IReservaRepositorio
{
    /// <summary>Nombre del tipo tabla definido en la base de datos.</summary>
    private const string NombreTipoTabla = "evt.ReservaDetalleTipo";

    private readonly IFabricaConexion _fabrica;
    private readonly IRegistradorSeguro _registro;

    public ReservaRepositorio(IFabricaConexion fabrica, IRegistradorSeguro registro)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
    }

    /// <summary>
    /// Construye la tabla en memoria que viaja como TVP.
    ///
    /// El orden y los tipos de las columnas deben coincidir EXACTAMENTE con la
    /// definicion de evt.ReservaDetalleTipo. Si no coincidieran, SQL Server
    /// rechazaria la llamada, cosa que ya es en si una proteccion.
    /// </summary>
    private static DataTable ConstruirTablaDetalles(IReadOnlyList<LineaDetalleSolicitud> detalles)
    {
        var tabla = new DataTable();
        tabla.Columns.Add("IdRecurso", typeof(int));
        tabla.Columns.Add("Cantidad", typeof(int));
        tabla.Columns.Add("PrecioUnitario", typeof(decimal));
        tabla.Columns.Add("PorcentajeDescuento", typeof(decimal));

        foreach (var linea in detalles)
        {
            tabla.Rows.Add(linea.IdRecurso, linea.Cantidad, linea.PrecioUnitario, linea.PorcentajeDescuento);
        }

        return tabla;
    }

    /// <summary>
    /// Guarda la reserva completa invocando evt.sp_Reserva_Guardar.
    ///
    /// Observe que NO se envian Subtotal, Descuento, Impuesto ni Total: el
    /// procedimiento los recalcula. Tampoco se abre ninguna transaccion desde
    /// C#: la transaccion la controla el procedimiento almacenado, tal como
    /// exige el examen.
    /// </summary>
    public async Task<ResultadoGuardarReserva> GuardarAsync(
        SolicitudGuardarReserva solicitud,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (solicitud.Detalles.Count == 0)
        {
            // Se comprueba tambien aqui para dar un mensaje inmediato, pero la
            // regla la impone igualmente el procedimiento almacenado.
            throw new ExcepcionNegocio("La reserva debe incluir al menos un recurso o servicio.");
        }

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Reserva_Guardar", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, solicitud.IdReserva);
            comando.Agregar("@IdCliente", SqlDbType.Int, solicitud.IdCliente);
            comando.Agregar("@IdSalon", SqlDbType.Int, solicitud.IdSalon);
            comando.AgregarFecha("@FechaEvento", solicitud.FechaEvento);
            comando.AgregarHora("@HoraInicio", solicitud.HoraInicio);
            comando.AgregarHora("@HoraFin", solicitud.HoraFin);
            comando.Agregar("@NumeroInvitados", SqlDbType.Int, solicitud.NumeroInvitados);
            comando.Agregar("@Observacion", SqlDbType.NVarChar, 500, solicitud.Observacion);
            comando.Agregar("@PorcentajeDescuentoGlobal", SqlDbType.Decimal, solicitud.PorcentajeDescuentoGlobal)
                   .SetPrecision(5, 2);
            comando.Agregar("@IdUsuario", SqlDbType.Int, solicitud.IdUsuario);

            // --- El parametro tipo tabla: TODO el detalle en un solo parametro ---
            var parametroDetalles = comando.Parameters.AddWithValue("@Detalles", ConstruirTablaDetalles(solicitud.Detalles));
            parametroDetalles.SqlDbType = SqlDbType.Structured;
            parametroDetalles.TypeName = NombreTipoTabla;

            var salidaId      = comando.AgregarSalida("@IdReservaResultado", SqlDbType.Int);
            var salidaCodigo  = comando.AgregarSalida("@CodigoResultado", SqlDbType.VarChar, 24);
            var salidaMensaje = comando.AgregarSalida("@Mensaje", SqlDbType.NVarChar, 300);

            // ExecuteNonQuery descarta el SELECT final del procedimiento pero si
            // rellena los parametros de salida, que es lo que nos interesa.
            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            var idReserva = salidaId.Salida<int>();
            var codigo = salidaCodigo.SalidaTexto();
            var mensaje = salidaMensaje.SalidaTexto();

            if (idReserva is null || string.IsNullOrWhiteSpace(codigo))
            {
                throw new ExcepcionNegocio(
                    mensaje ?? "No se pudo guardar la reserva. Verifique los datos e intente nuevamente.");
            }

            _registro.Informacion(
                $"Reserva guardada. Id={idReserva} Codigo={codigo} Detalles={solicitud.Detalles.Count} "
                + $"Usuario={solicitud.IdUsuario}.");

            return new ResultadoGuardarReserva(idReserva.Value, codigo, mensaje ?? "Reserva guardada correctamente.");
        }
        catch (SqlException ex)
        {
            // Un error aqui significa que el procedimiento ya hizo ROLLBACK: no
            // quedo cabecera ni detalles parciales (caso CA-02).
            _registro.Advertencia(
                $"Rechazo al guardar la reserva (SQL {ex.Number}) para el usuario {solicitud.IdUsuario}.");
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    /// <summary>
    /// Recupera la reserva completa. evt.sp_Reserva_ObtenerPorId devuelve DOS
    /// conjuntos de resultados: primero la cabecera y despues el detalle. Se
    /// avanza entre ellos con NextResultAsync.
    /// </summary>
    public async Task<Reserva?> ObtenerPorIdAsync(int idReserva, CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Reserva_ObtenerPorId", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, idReserva);

            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            // --- Conjunto 1: cabecera ---
            if (!await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                return null;
            }

            var reserva = new Reserva
            {
                IdReserva             = lector.Entero("IdReserva"),
                Codigo                = lector.Texto("Codigo"),
                IdCliente             = lector.Entero("IdCliente"),
                ClienteIdentificacion = lector.TextoNulo("ClienteIdentificacion"),
                ClienteNombres        = lector.Texto("ClienteNombres"),
                ClienteEmail          = lector.Texto("ClienteEmail"),
                ClienteTelefono       = lector.TextoNulo("ClienteTelefono"),
                IdSalon               = lector.Entero("IdSalon"),
                SalonNombre           = lector.Texto("SalonNombre"),
                SalonCapacidad        = lector.Entero("SalonCapacidad"),
                SalonTarifaBase       = lector.Decimal("SalonTarifaBase"),
                FechaEvento           = lector.Fecha("FechaEvento"),
                HoraInicio            = lector.Hora("HoraInicio"),
                HoraFin               = lector.Hora("HoraFin"),
                NumeroInvitados       = lector.Entero("NumeroInvitados"),
                Estado                = MaquinaEstadosReserva.Desde(lector.Texto("Estado")),
                PorcentajeDescuentoGlobal = lector.Decimal("PorcentajeDescuentoGlobal"),
                Observacion           = lector.TextoNulo("Observacion"),
                IdUsuarioCreacion     = lector.Entero("IdUsuarioCreacion"),
                UsuarioCreacion       = lector.TextoNulo("UsuarioCreacion"),
                FechaCreacion         = lector.FechaHora("FechaCreacion"),
                IdUsuarioModificacion = lector.EnteroNulo("IdUsuarioModificacion"),
                FechaModificacion     = lector.FechaHoraNula("FechaModificacion")
            };

            // Los importes se toman TAL CUAL de la base: son los que calculo
            // SQL Server, que es la fuente definitiva segun el examen.
            reserva.EstablecerImportesPersistidos(
                lector.Decimal("Subtotal"),
                lector.Decimal("Descuento"),
                lector.Decimal("Impuesto"),
                lector.Decimal("Total"));

            // --- Conjunto 2: detalle ---
            if (await lector.NextResultAsync(cancelacion).ConfigureAwait(false))
            {
                while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
                {
                    var detalle = new ReservaDetalle
                    {
                        IdDetalle           = lector.Entero("IdDetalle"),
                        IdReserva           = lector.Entero("IdReserva"),
                        IdRecurso           = lector.Entero("IdRecurso"),
                        RecursoNombre       = lector.Texto("RecursoNombre"),
                        RecursoTipo         = lector.Texto("RecursoTipo"),
                        RecursoStock        = lector.Entero("RecursoStock"),
                        Cantidad            = lector.Entero("Cantidad"),
                        PrecioUnitario      = lector.Decimal("PrecioUnitario"),
                        PorcentajeDescuento = lector.Decimal("PorcentajeDescuento")
                    };

                    detalle.EstablecerSubtotalPersistido(lector.Decimal("SubtotalLinea"));
                    reserva.Detalles.Add(detalle);
                }
            }

            return reserva;
        }
        catch (SqlException ex)
        {
            _registro.Error($"Error de SQL al obtener la reserva {idReserva}.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    /// <summary>Consulta paginada con filtros opcionales combinables.</summary>
    public async Task<PaginaReservas> ConsultarAsync(FiltroConsultaReserva filtro, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(filtro);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Reserva_Consultar", conexion);

            // Cada filtro se envia como parametro. Los null los interpreta el
            // procedimiento como "no filtrar por este campo". Nunca se arma SQL.
            comando.Agregar("@Codigo", SqlDbType.VarChar, 24, string.IsNullOrWhiteSpace(filtro.Codigo) ? null : filtro.Codigo.Trim());
            comando.Agregar("@IdCliente", SqlDbType.Int, filtro.IdCliente);
            comando.Agregar("@TextoCliente", SqlDbType.NVarChar, 150, string.IsNullOrWhiteSpace(filtro.TextoCliente) ? null : filtro.TextoCliente.Trim());
            comando.AgregarFecha("@FechaDesde", filtro.FechaDesde);
            comando.AgregarFecha("@FechaHasta", filtro.FechaHasta);
            comando.Agregar("@IdSalon", SqlDbType.Int, filtro.IdSalon);
            comando.Agregar("@Estado", SqlDbType.VarChar, 12,
                filtro.Estado.HasValue ? MaquinaEstadosReserva.ATexto(filtro.Estado.Value) : null);
            comando.Agregar("@Pagina", SqlDbType.Int, filtro.Pagina);
            comando.Agregar("@TamanoPagina", SqlDbType.Int, filtro.TamanoPagina);

            var filas = new List<ResumenReserva>();
            var totalFilas = 0;

            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                totalFilas = lector.Entero("TotalFilas");

                filas.Add(new ResumenReserva
                {
                    IdReserva       = lector.Entero("IdReserva"),
                    Codigo          = lector.Texto("Codigo"),
                    IdCliente       = lector.Entero("IdCliente"),
                    ClienteNombres  = lector.Texto("ClienteNombres"),
                    ClienteEmail    = lector.Texto("ClienteEmail"),
                    IdSalon         = lector.Entero("IdSalon"),
                    SalonNombre     = lector.Texto("SalonNombre"),
                    FechaEvento     = lector.Fecha("FechaEvento"),
                    HoraInicio      = lector.Hora("HoraInicio"),
                    HoraFin         = lector.Hora("HoraFin"),
                    NumeroInvitados = lector.Entero("NumeroInvitados"),
                    Estado          = MaquinaEstadosReserva.Desde(lector.Texto("Estado")),
                    Subtotal        = lector.Decimal("Subtotal"),
                    Descuento       = lector.Decimal("Descuento"),
                    Impuesto        = lector.Decimal("Impuesto"),
                    Total           = lector.Decimal("Total"),
                    Observacion     = lector.TextoNulo("Observacion"),
                    TotalDetalles   = lector.Entero("TotalDetalles"),
                    UsuarioCreacion = lector.TextoNulo("UsuarioCreacion"),
                    FechaCreacion   = lector.FechaHora("FechaCreacion")
                });
            }

            return new PaginaReservas(filas, totalFilas, filtro.Pagina, filtro.TamanoPagina);
        }
        catch (SqlException ex)
        {
            _registro.Error("Error de SQL al consultar reservas.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    /// <summary>
    /// Valida disponibilidad. Devuelve la lista de conflictos; vacia significa
    /// que la reserva es viable.
    /// </summary>
    public async Task<IReadOnlyList<ConflictoDisponibilidad>> ValidarDisponibilidadAsync(
        int? idReserva,
        int idSalon,
        DateOnly fechaEvento,
        TimeSpan horaInicio,
        TimeSpan horaFin,
        int numeroInvitados,
        IReadOnlyList<LineaDetalleSolicitud> detalles,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(detalles);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Disponibilidad_Validar", conexion);

            // idReserva con valor excluye la propia reserva del control de
            // cruces: es lo que hace posible el caso CA-04.
            comando.Agregar("@IdReserva", SqlDbType.Int, idReserva);
            comando.Agregar("@IdSalon", SqlDbType.Int, idSalon);
            comando.AgregarFecha("@FechaEvento", fechaEvento);
            comando.AgregarHora("@HoraInicio", horaInicio);
            comando.AgregarHora("@HoraFin", horaFin);
            comando.Agregar("@NumeroInvitados", SqlDbType.Int, numeroInvitados);

            var parametroDetalles = comando.Parameters.AddWithValue("@Detalles", ConstruirTablaDetalles(detalles));
            parametroDetalles.SqlDbType = SqlDbType.Structured;
            parametroDetalles.TypeName = NombreTipoTabla;

            var conflictos = new List<ConflictoDisponibilidad>();

            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                conflictos.Add(new ConflictoDisponibilidad(lector.Texto("Codigo"), lector.Texto("Mensaje")));
            }

            return conflictos;
        }
        catch (SqlException ex)
        {
            _registro.Error($"Error de SQL al validar la disponibilidad del salon {idSalon}.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    /// <summary>
    /// Cambia el estado de la reserva. Es idempotente: si ya estaba en el
    /// estado solicitado devuelve Resultado = 1 sin volver a cambiarlo ni
    /// escribir una segunda fila de auditoria (base de los casos CA-06 y CA-07).
    /// </summary>
    public async Task<ResultadoCambioEstado> CambiarEstadoAsync(
        int idReserva,
        EstadoReserva estadoNuevo,
        string? motivo,
        int idUsuario,
        CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Reserva_CambiarEstado", conexion);

            comando.Agregar("@IdReserva", SqlDbType.Int, idReserva);
            comando.Agregar("@EstadoNuevo", SqlDbType.VarChar, 12, MaquinaEstadosReserva.ATexto(estadoNuevo));
            comando.Agregar("@Motivo", SqlDbType.NVarChar, 500, string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim());
            comando.Agregar("@IdUsuario", SqlDbType.Int, idUsuario);

            var salidaResultado = comando.AgregarSalida("@Resultado", SqlDbType.Int);
            var salidaMensaje   = comando.AgregarSalida("@Mensaje", SqlDbType.NVarChar, 300);

            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            var resultado = salidaResultado.Salida<int>() ?? -1;
            var mensaje = salidaMensaje.SalidaTexto() ?? "No se pudo cambiar el estado de la reserva.";

            // Si Resultado es 1, el estado no cambio porque ya era el solicitado.
            var estadoFinal = resultado is 0 or 1 ? estadoNuevo : EstadoReserva.Borrador;

            _registro.Informacion(
                $"Cambio de estado de la reserva {idReserva} a {MaquinaEstadosReserva.ATexto(estadoNuevo)}: "
                + $"resultado={resultado}, usuario={idUsuario}.");

            return new ResultadoCambioEstado(resultado, mensaje, estadoFinal);
        }
        catch (SqlException ex)
        {
            _registro.Advertencia($"Rechazo al cambiar el estado de la reserva {idReserva} (SQL {ex.Number}).");
            throw TraductorErroresSql.Traducir(ex);
        }
    }
}

/// <summary>Ayuda para fijar precision y escala en parametros decimales.</summary>
internal static class ExtensionesPrecision
{
    public static SqlParameter SetPrecision(this SqlParameter parametro, byte precision, byte escala)
    {
        parametro.Precision = precision;
        parametro.Scale = escala;
        return parametro;
    }
}
