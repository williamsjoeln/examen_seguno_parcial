namespace SmartEvent.Dominio.Entidades;

/// <summary>
/// Cliente que contrata un evento. Refleja evt.Cliente.
/// </summary>
public sealed class Cliente
{
    public int IdCliente { get; set; }
    public string Identificacion { get; set; } = string.Empty;
    public string Nombres { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public bool Estado { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }

    /// <summary>Texto para mostrar en listas desplegables y busquedas.</summary>
    public string Descripcion => $"{Identificacion} - {Nombres}";

    public override string ToString() => Descripcion;
}

/// <summary>
/// Salon donde se realiza el evento. Refleja evt.Salon.
/// La TarifaBase es el punto de partida del Subtotal de toda reserva.
/// </summary>
public sealed class Salon
{
    public int IdSalon { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Ubicacion { get; set; }
    public int Capacidad { get; set; }
    public decimal TarifaBase { get; set; }
    public bool Estado { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }

    public string Descripcion => $"{Nombre} (capacidad {Capacidad})";

    public override string ToString() => Descripcion;
}

/// <summary>
/// Recurso o servicio que puede reservarse. Refleja evt.Recurso.
/// StockTotal es el inventario global; el stock DISPONIBLE para una fecha y
/// horario concretos lo calcula SQL Server descontando las reservas activas que
/// se cruzan con esa franja.
/// </summary>
public sealed class Recurso
{
    public int IdRecurso { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int StockTotal { get; set; }
    public decimal PrecioUnitario { get; set; }
    public bool Estado { get; set; } = true;
    public DateTime FechaCreacion { get; set; }
    public DateTime? FechaModificacion { get; set; }

    public string Descripcion => $"{Nombre} ({Tipo})";

    public override string ToString() => Descripcion;
}
