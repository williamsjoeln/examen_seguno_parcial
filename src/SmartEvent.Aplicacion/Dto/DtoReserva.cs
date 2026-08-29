using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Aplicacion.Dto;

/// <summary>
/// Una linea del detalle tal como la envia el formulario al guardar.
/// Deliberadamente NO incluye SubtotalLinea: ese valor lo calcula SQL Server.
/// </summary>
public sealed record LineaDetalleSolicitud(
    int IdRecurso,
    int Cantidad,
    decimal PrecioUnitario,
    decimal PorcentajeDescuento);

/// <summary>
/// Datos que el formulario envia para crear o actualizar una reserva.
///
/// Observe que NO viajan Subtotal, Descuento, Impuesto ni Total. El
/// procedimiento almacenado los recalcula siempre a partir de la tarifa base
/// del salon y de las lineas, de modo que la interfaz no puede alterarlos ni
/// aunque se manipulara (regla D15: SQL Server es la fuente definitiva).
/// </summary>
public sealed class SolicitudGuardarReserva
{
    /// <summary>Null para crear una reserva nueva; con valor para editar.</summary>
    public int? IdReserva { get; init; }

    public int IdCliente { get; init; }
    public int IdSalon { get; init; }
    public DateOnly FechaEvento { get; init; }
    public TimeSpan HoraInicio { get; init; }
    public TimeSpan HoraFin { get; init; }
    public int NumeroInvitados { get; init; }
    public string? Observacion { get; init; }
    public decimal PorcentajeDescuentoGlobal { get; init; }

    /// <summary>Usuario que realiza la operacion. El SP verifica su rol para autorizar descuentos.</summary>
    public int IdUsuario { get; init; }

    public IReadOnlyList<LineaDetalleSolicitud> Detalles { get; init; } = [];
}

/// <summary>Lo que devuelve evt.sp_Reserva_Guardar: IdReserva, codigo y mensaje.</summary>
public sealed record ResultadoGuardarReserva(int IdReserva, string Codigo, string Mensaje);

/// <summary>
/// Un conflicto detectado por evt.sp_Disponibilidad_Validar.
/// La lista vacia significa que la reserva es viable.
/// </summary>
public sealed record ConflictoDisponibilidad(string Codigo, string Mensaje);

/// <summary>Filtros combinables de FrmReservasConsulta. Todos son opcionales.</summary>
public sealed class FiltroConsultaReserva
{
    public string? Codigo { get; init; }
    public int? IdCliente { get; init; }
    public string? TextoCliente { get; init; }
    public DateOnly? FechaDesde { get; init; }
    public DateOnly? FechaHasta { get; init; }
    public int? IdSalon { get; init; }
    public EstadoReserva? Estado { get; init; }

    /// <summary>Pagina solicitada, comenzando en 1.</summary>
    public int Pagina { get; init; } = 1;

    /// <summary>Filas por pagina para la carga progresiva.</summary>
    public int TamanoPagina { get; init; } = 25;
}

/// <summary>Fila del listado de reservas.</summary>
public sealed class ResumenReserva
{
    public int IdReserva { get; init; }
    public string Codigo { get; init; } = string.Empty;
    public int IdCliente { get; init; }
    public string ClienteNombres { get; init; } = string.Empty;
    public string ClienteEmail { get; init; } = string.Empty;
    public int IdSalon { get; init; }
    public string SalonNombre { get; init; } = string.Empty;
    public DateOnly FechaEvento { get; init; }
    public TimeSpan HoraInicio { get; init; }
    public TimeSpan HoraFin { get; init; }
    public int NumeroInvitados { get; init; }
    public EstadoReserva Estado { get; init; }
    public decimal Subtotal { get; init; }
    public decimal Descuento { get; init; }
    public decimal Impuesto { get; init; }
    public decimal Total { get; init; }
    public string? Observacion { get; init; }
    public int TotalDetalles { get; init; }
    public string? UsuarioCreacion { get; init; }
    public DateTime FechaCreacion { get; init; }

    public string Horario =>
        $"{HoraInicio:hh\\:mm} - {HoraFin:hh\\:mm}";
}

/// <summary>Una pagina de resultados, con el total de filas que cumplen el filtro.</summary>
public sealed record PaginaReservas(IReadOnlyList<ResumenReserva> Filas, int TotalFilas, int Pagina, int TamanoPagina)
{
    public int TotalPaginas => TamanoPagina <= 0 ? 1 : (int)Math.Ceiling(TotalFilas / (double)TamanoPagina);
    public bool HayMas => Pagina < TotalPaginas;
}

/// <summary>Resultado de evt.sp_Reserva_CambiarEstado.</summary>
/// <param name="Resultado">0 = cambio aplicado, 1 = ya estaba en ese estado (sin cambio).</param>
/// <param name="Mensaje">Texto apto para mostrar al usuario.</param>
/// <param name="Estado">Estado en el que quedo la reserva.</param>
public sealed record ResultadoCambioEstado(int Resultado, string Mensaje, EstadoReserva Estado)
{
    /// <summary>El estado cambio realmente en esta llamada.</summary>
    public bool HuboCambio => Resultado == 0;

    /// <summary>
    /// La reserva ya estaba en el estado solicitado. Es la respuesta idempotente
    /// que impide duplicar el cambio de estado al reintentar (caso CA-07).
    /// </summary>
    public bool SinCambio => Resultado == 1;
}
