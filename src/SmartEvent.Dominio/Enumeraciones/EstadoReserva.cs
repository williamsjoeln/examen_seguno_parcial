namespace SmartEvent.Dominio.Enumeraciones;

/// <summary>
/// Estados posibles de una reserva. Los valores de texto coinciden EXACTAMENTE
/// con los admitidos por la restriccion CK_Reserva_Estado de la base de datos.
/// </summary>
public enum EstadoReserva
{
    Borrador = 0,
    Confirmada = 1,
    Finalizada = 2,
    Cancelada = 3
}

/// <summary>
/// Maquina de estados de la reserva.
///
///     BORRADOR   -> CONFIRMADA | CANCELADA
///     CONFIRMADA -> FINALIZADA | CANCELADA
///     FINALIZADA -> terminal
///     CANCELADA  -> terminal
///
/// Esta clase es la copia en C# de la logica que valida
/// evt.sp_Reserva_CambiarEstado. Sirve para que la interfaz habilite o
/// deshabilite botones sin ir a la base de datos, pero NO es la autoridad:
/// la decision final siempre la toma el procedimiento almacenado.
/// </summary>
public static class MaquinaEstadosReserva
{
    private static readonly Dictionary<EstadoReserva, EstadoReserva[]> Transiciones = new()
    {
        [EstadoReserva.Borrador]   = [EstadoReserva.Confirmada, EstadoReserva.Cancelada],
        [EstadoReserva.Confirmada] = [EstadoReserva.Finalizada, EstadoReserva.Cancelada],
        [EstadoReserva.Finalizada] = [],
        [EstadoReserva.Cancelada]  = []
    };

    /// <summary>Estados que ya no admiten ningun cambio posterior.</summary>
    public static bool EsTerminal(EstadoReserva estado) =>
        estado is EstadoReserva.Finalizada or EstadoReserva.Cancelada;

    /// <summary>Indica si la reserva admite edicion de cliente, salon, fecha, horario y detalles.</summary>
    public static bool EsEditable(EstadoReserva estado) => estado == EstadoReserva.Borrador;

    /// <summary>Indica si la transicion solicitada esta permitida.</summary>
    public static bool PuedeTransitar(EstadoReserva actual, EstadoReserva destino) =>
        Transiciones.TryGetValue(actual, out var permitidos) && permitidos.Contains(destino);

    /// <summary>Estados a los que se puede pasar desde el estado indicado.</summary>
    public static IReadOnlyList<EstadoReserva> TransicionesDesde(EstadoReserva actual) =>
        Transiciones.TryGetValue(actual, out var permitidos) ? permitidos : [];

    /// <summary>Convierte el texto almacenado en la base de datos al enumerado.</summary>
    public static EstadoReserva Desde(string valor) => valor?.Trim().ToUpperInvariant() switch
    {
        "BORRADOR"   => EstadoReserva.Borrador,
        "CONFIRMADA" => EstadoReserva.Confirmada,
        "FINALIZADA" => EstadoReserva.Finalizada,
        "CANCELADA"  => EstadoReserva.Cancelada,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), $"Estado de reserva no reconocido: '{valor}'.")
    };

    /// <summary>Convierte el enumerado al texto que espera la base de datos.</summary>
    public static string ATexto(EstadoReserva estado) => estado switch
    {
        EstadoReserva.Borrador   => "BORRADOR",
        EstadoReserva.Confirmada => "CONFIRMADA",
        EstadoReserva.Finalizada => "FINALIZADA",
        EstadoReserva.Cancelada  => "CANCELADA",
        _ => throw new ArgumentOutOfRangeException(nameof(estado))
    };
}
