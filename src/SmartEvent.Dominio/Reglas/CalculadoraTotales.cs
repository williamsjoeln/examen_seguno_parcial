namespace SmartEvent.Dominio.Reglas;

/// <summary>
/// Resultado del calculo de totales de una reserva.
/// </summary>
/// <param name="Subtotal">Tarifa base del salon mas la suma de subtotales de linea.</param>
/// <param name="Descuento">Monto del descuento global, en dinero.</param>
/// <param name="BaseNeta">Subtotal menos el descuento global.</param>
/// <param name="Impuesto">15% sobre la base neta.</param>
/// <param name="Total">Base neta mas impuesto.</param>
public readonly record struct TotalesReserva(
    decimal Subtotal,
    decimal Descuento,
    decimal BaseNeta,
    decimal Impuesto,
    decimal Total);

/// <summary>Una linea del detalle, reducida a lo que hace falta para calcular.</summary>
/// <param name="Cantidad">Unidades solicitadas.</param>
/// <param name="PrecioUnitario">Precio por unidad.</param>
/// <param name="PorcentajeDescuento">Descuento de la linea, de 0 a 20.</param>
public readonly record struct LineaCalculo(
    int Cantidad,
    decimal PrecioUnitario,
    decimal PorcentajeDescuento);

/// <summary>
/// Calculo de totales de una reserva.
///
/// Es la copia EXACTA en C# de la aritmetica que ejecuta
/// evt.sp_Reserva_Guardar. El examen pide que los totales se recalculen en la
/// interfaz Y en SQL Server, y que el valor persistido por el procedimiento sea
/// la fuente definitiva. Esta clase cubre la primera mitad: el calculo en
/// tiempo real mientras el usuario edita la grilla.
///
/// Formulas (Examen SS6):
///     SubtotalLinea = Cantidad * PrecioUnitario * (1 - PorcentajeDescuento/100)
///     Subtotal      = TarifaBase del salon + SUM(SubtotalLinea)
///     Descuento     = Subtotal * PorcentajeDescuentoGlobal/100
///     BaseNeta      = Subtotal - Descuento
///     Impuesto      = BaseNeta * 15%
///     Total         = BaseNeta + Impuesto
///
/// Todos los redondeos usan MidpointRounding.AwayFromZero, que es el mismo
/// criterio que aplica la funcion ROUND de SQL Server. Si se usara el redondeo
/// bancario por defecto de .NET, la interfaz y la base de datos podrian
/// discrepar en un centavo.
/// </summary>
public static class CalculadoraTotales
{
    /// <summary>Calcula el subtotal de una linea del detalle, redondeado a dos decimales.</summary>
    public static decimal CalcularSubtotalLinea(int cantidad, decimal precioUnitario, decimal porcentajeDescuento)
    {
        var bruto = cantidad * precioUnitario * (1m - (porcentajeDescuento / 100m));
        return Math.Round(bruto, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Calcula el subtotal de una linea del detalle.</summary>
    public static decimal CalcularSubtotalLinea(LineaCalculo linea) =>
        CalcularSubtotalLinea(linea.Cantidad, linea.PrecioUnitario, linea.PorcentajeDescuento);

    /// <summary>
    /// Calcula los totales completos de la reserva.
    /// </summary>
    /// <param name="tarifaBaseSalon">Tarifa base del salon seleccionado.</param>
    /// <param name="lineas">Lineas del detalle.</param>
    /// <param name="porcentajeDescuentoGlobal">Descuento global de la cabecera, de 0 a 20.</param>
    public static TotalesReserva Calcular(
        decimal tarifaBaseSalon,
        IEnumerable<LineaCalculo> lineas,
        decimal porcentajeDescuentoGlobal)
    {
        ArgumentNullException.ThrowIfNull(lineas);

        var sumaLineas = 0m;
        foreach (var linea in lineas)
        {
            sumaLineas += CalcularSubtotalLinea(linea);
        }

        var subtotal  = Math.Round(tarifaBaseSalon + sumaLineas, 2, MidpointRounding.AwayFromZero);
        var descuento = Math.Round(subtotal * (porcentajeDescuentoGlobal / 100m), 2, MidpointRounding.AwayFromZero);
        var baseNeta  = Math.Round(subtotal - descuento, 2, MidpointRounding.AwayFromZero);
        var impuesto  = Math.Round(baseNeta * ReglasReserva.TasaImpuesto, 2, MidpointRounding.AwayFromZero);
        var total     = Math.Round(baseNeta + impuesto, 2, MidpointRounding.AwayFromZero);

        return new TotalesReserva(subtotal, descuento, baseNeta, impuesto, total);
    }
}
