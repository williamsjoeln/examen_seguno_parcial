namespace SmartEvent.Dominio.Excepciones;

/// <summary>
/// Error de REGLA DE NEGOCIO cuyo mensaje puede mostrarse tal cual al usuario.
///
/// Es la pieza central del manejo de errores exigido por la regla D25 del
/// examen: "los mensajes de error deben ser comprensibles y no revelar cadenas
/// de conexion, claves, SQL interno ni stack traces".
///
/// El criterio de toda la aplicacion es:
///   - ExcepcionNegocio  -> el mensaje se muestra al usuario. Es texto escrito
///                          por nosotros, en los procedimientos almacenados o
///                          en los servicios; no contiene informacion tecnica.
///   - cualquier otra    -> se muestra un mensaje generico y el detalle tecnico
///                          va SOLO al log local.
///
/// La capa de datos traduce a esta excepcion los errores de SQL Server cuyo
/// numero es mayor o igual a 50000, que son exactamente los que lanzamos
/// nosotros con THROW en el catalogo 50001..50024.
/// </summary>
public class ExcepcionNegocio : Exception
{
    /// <summary>
    /// Numero del error de SQL Server que lo origino, si viene de la base de
    /// datos. Es null cuando la validacion se hizo en C#.
    /// </summary>
    public int? NumeroSql { get; }

    public ExcepcionNegocio(string mensaje)
        : base(mensaje)
    {
    }

    public ExcepcionNegocio(string mensaje, int numeroSql)
        : base(mensaje)
    {
        NumeroSql = numeroSql;
    }

    public ExcepcionNegocio(string mensaje, Exception innerException)
        : base(mensaje, innerException)
    {
    }

    public ExcepcionNegocio(string mensaje, int numeroSql, Exception innerException)
        : base(mensaje, innerException)
    {
        NumeroSql = numeroSql;
    }
}

/// <summary>
/// Error al comunicarse con un servicio externo (SMTP u OpenAI).
///
/// Se distingue de <see cref="ExcepcionNegocio"/> porque la aplicacion debe
/// SEGUIR FUNCIONANDO cuando ocurre: el examen exige que un fallo de correo o
/// de IA no interrumpa el flujo ni cierre la aplicacion (casos CA-07 y CA-09).
///
/// El mensaje de esta excepcion es apto para el usuario; el detalle tecnico
/// viaja en <see cref="DetalleTecnico"/> y solo se escribe en el log y en la
/// columna Error de la tabla de auditoria correspondiente.
/// </summary>
public sealed class ExcepcionIntegracion : Exception
{
    /// <summary>Nombre del servicio afectado, para el log y la auditoria.</summary>
    public string Servicio { get; }

    /// <summary>
    /// Detalle tecnico controlado, ya recortado y sin secretos. Nunca se
    /// muestra en un cuadro de dialogo.
    /// </summary>
    public string DetalleTecnico { get; }

    /// <summary>Indica si tiene sentido que el usuario reintente la operacion.</summary>
    public bool EsReintentable { get; }

    public ExcepcionIntegracion(
        string servicio,
        string mensajeUsuario,
        string detalleTecnico,
        bool esReintentable = true,
        Exception? innerException = null)
        : base(mensajeUsuario, innerException)
    {
        Servicio = servicio;
        DetalleTecnico = detalleTecnico;
        EsReintentable = esReintentable;
    }
}
