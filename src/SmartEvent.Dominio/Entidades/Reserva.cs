using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Reglas;

namespace SmartEvent.Dominio.Entidades;

/// <summary>
/// Cabecera de la reserva. Refleja evt.Reserva.
///
/// Los importes (Subtotal, Descuento, Impuesto, Total) se exponen como
/// propiedades de solo lectura desde fuera del dominio: se rellenan al leer de
/// la base de datos o al recalcular con <see cref="RecalcularTotales"/>. La
/// interfaz NUNCA los escribe a mano, y al guardar ni siquiera se envian al
/// procedimiento almacenado: SQL Server los vuelve a calcular.
/// </summary>
public sealed class Reserva
{
    public int IdReserva { get; set; }
    public string Codigo { get; set; } = string.Empty;

    public int IdCliente { get; set; }
    public string ClienteNombres { get; set; } = string.Empty;
    public string ClienteEmail { get; set; } = string.Empty;
    public string? ClienteIdentificacion { get; set; }
    public string? ClienteTelefono { get; set; }

    public int IdSalon { get; set; }
    public string SalonNombre { get; set; } = string.Empty;
    public int SalonCapacidad { get; set; }
    public decimal SalonTarifaBase { get; set; }

    public DateOnly FechaEvento { get; set; }
    public TimeSpan HoraInicio { get; set; }
    public TimeSpan HoraFin { get; set; }
    public int NumeroInvitados { get; set; }

    public EstadoReserva Estado { get; set; } = EstadoReserva.Borrador;

    public decimal Subtotal { get; private set; }
    public decimal PorcentajeDescuentoGlobal { get; set; }
    public decimal Descuento { get; private set; }
    public decimal Impuesto { get; private set; }
    public decimal Total { get; private set; }

    public string? Observacion { get; set; }

    public int IdUsuarioCreacion { get; set; }
    public string? UsuarioCreacion { get; set; }
    public DateTime FechaCreacion { get; set; }
    public int? IdUsuarioModificacion { get; set; }
    public DateTime? FechaModificacion { get; set; }

    /// <summary>Lineas del detalle. Una reserva valida tiene al menos una (regla D07).</summary>
    public List<ReservaDetalle> Detalles { get; } = [];

    /// <summary>Duracion del evento, derivada del horario.</summary>
    public TimeSpan Duracion => HoraFin - HoraInicio;

    /// <summary>La reserva admite edicion de cliente, salon, fecha, horario y detalles (regla D19).</summary>
    public bool EsEditable => MaquinaEstadosReserva.EsEditable(Estado);

    /// <summary>Indica si la reserva ya existe en la base de datos.</summary>
    public bool EsNueva => IdReserva <= 0;

    /// <summary>
    /// Recalcula Subtotal, Descuento, Impuesto y Total a partir de la tarifa
    /// base del salon y de las lineas actuales. Es lo que se invoca en cada
    /// cambio de la grilla para el "calculo en tiempo real" que exige el examen.
    /// </summary>
    public TotalesReserva RecalcularTotales()
    {
        var lineas = Detalles.Select(d => new LineaCalculo(d.Cantidad, d.PrecioUnitario, d.PorcentajeDescuento));
        var totales = CalculadoraTotales.Calcular(SalonTarifaBase, lineas, PorcentajeDescuentoGlobal);

        Subtotal  = totales.Subtotal;
        Descuento = totales.Descuento;
        Impuesto  = totales.Impuesto;
        Total     = totales.Total;

        foreach (var detalle in Detalles)
        {
            detalle.RecalcularSubtotal();
        }

        return totales;
    }

    /// <summary>
    /// Asigna los importes leidos de la base de datos. Solo la capa de datos
    /// debe usarlo: son los valores que calculo SQL Server, la fuente
    /// definitiva segun el examen.
    /// </summary>
    public void EstablecerImportesPersistidos(decimal subtotal, decimal descuento, decimal impuesto, decimal total)
    {
        Subtotal  = subtotal;
        Descuento = descuento;
        Impuesto  = impuesto;
        Total     = total;
    }
}

/// <summary>
/// Linea del detalle de una reserva. Refleja evt.ReservaDetalle.
/// </summary>
public sealed class ReservaDetalle
{
    public int IdDetalle { get; set; }
    public int IdReserva { get; set; }

    public int IdRecurso { get; set; }
    public string RecursoNombre { get; set; } = string.Empty;
    public string RecursoTipo { get; set; } = string.Empty;
    public int RecursoStock { get; set; }

    public int Cantidad { get; set; }
    public decimal PrecioUnitario { get; set; }
    public decimal PorcentajeDescuento { get; set; }

    public decimal SubtotalLinea { get; private set; }

    /// <summary>Recalcula el subtotal de la linea con la misma formula que SQL Server.</summary>
    public decimal RecalcularSubtotal()
    {
        SubtotalLinea = CalculadoraTotales.CalcularSubtotalLinea(Cantidad, PrecioUnitario, PorcentajeDescuento);
        return SubtotalLinea;
    }

    /// <summary>Asigna el subtotal leido de la base de datos.</summary>
    public void EstablecerSubtotalPersistido(decimal subtotal) => SubtotalLinea = subtotal;
}
