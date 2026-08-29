namespace SmartEvent.Dominio.Enumeraciones;

/// <summary>
/// Roles del sistema. Los nombres coinciden con la restriccion CK_Rol_Nombre.
/// </summary>
public enum RolUsuario
{
    Coordinador = 0,
    Administrador = 1
}

/// <summary>
/// Permisos de la aplicacion.
///
/// El examen exige "menu por permisos" con los roles ADMINISTRADOR y
/// COORDINADOR, pero no define la matriz. La matriz adoptada es:
///
///   COORDINADOR    : operar reservas (crear, editar, consultar, analizar con
///                    IA, confirmar, cancelar, finalizar) y consultar clientes.
///   ADMINISTRADOR  : todo lo anterior, mas el mantenimiento de clientes,
///                    salones y recursos, la auditoria de integraciones y los
///                    descuentos superiores al 10%.
/// </summary>
public enum Permiso
{
    GestionarReservas,
    ConsultarReservas,
    AnalizarConIa,
    ConfirmarReserva,
    CancelarReserva,
    GestionarCatalogos,
    VerAuditoriaIntegraciones,
    AplicarDescuentoAlto
}

/// <summary>Nivel de riesgo devuelto por el analisis de IA.</summary>
public enum NivelRiesgo
{
    Bajo = 0,
    Medio = 1,
    Alto = 2
}

/// <summary>Motivo por el que se genera un correo al cliente.</summary>
public enum TipoEventoCorreo
{
    Confirmacion = 0,
    Cancelacion = 1
}

/// <summary>Resultado del intento de envio de un correo.</summary>
public enum EstadoCorreo
{
    Enviado = 0,
    Error = 1
}

/// <summary>
/// Conversiones entre los enumerados y el texto que usan la base de datos y la
/// respuesta JSON de la IA. Se centralizan aqui para que ninguna capa invente
/// sus propias cadenas magicas.
/// </summary>
public static class TextosEnumeracion
{
    public static RolUsuario RolDesde(string valor) => valor?.Trim().ToUpperInvariant() switch
    {
        "ADMINISTRADOR" => RolUsuario.Administrador,
        "COORDINADOR"   => RolUsuario.Coordinador,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), $"Rol no reconocido: '{valor}'.")
    };

    public static string ATexto(RolUsuario rol) => rol switch
    {
        RolUsuario.Administrador => "ADMINISTRADOR",
        RolUsuario.Coordinador   => "COORDINADOR",
        _ => throw new ArgumentOutOfRangeException(nameof(rol))
    };

    /// <summary>
    /// Acepta el texto del contrato JSON de la IA (BAJO, MEDIO, ALTO).
    /// Devuelve false si el valor no es valido, para que el llamador lo trate
    /// como respuesta invalida en lugar de lanzar una excepcion.
    /// </summary>
    public static bool TryNivelRiesgo(string? valor, out NivelRiesgo nivel)
    {
        switch (valor?.Trim().ToUpperInvariant())
        {
            case "BAJO":  nivel = NivelRiesgo.Bajo;  return true;
            case "MEDIO": nivel = NivelRiesgo.Medio; return true;
            case "ALTO":  nivel = NivelRiesgo.Alto;  return true;
            default:      nivel = NivelRiesgo.Bajo;  return false;
        }
    }

    public static string ATexto(NivelRiesgo nivel) => nivel switch
    {
        NivelRiesgo.Bajo  => "BAJO",
        NivelRiesgo.Medio => "MEDIO",
        NivelRiesgo.Alto  => "ALTO",
        _ => throw new ArgumentOutOfRangeException(nameof(nivel))
    };

    public static string ATexto(TipoEventoCorreo tipo) => tipo switch
    {
        TipoEventoCorreo.Confirmacion => "CONFIRMACION",
        TipoEventoCorreo.Cancelacion  => "CANCELACION",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo))
    };

    public static TipoEventoCorreo TipoEventoDesde(string valor) => valor?.Trim().ToUpperInvariant() switch
    {
        "CONFIRMACION" => TipoEventoCorreo.Confirmacion,
        "CANCELACION"  => TipoEventoCorreo.Cancelacion,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), $"Tipo de evento no reconocido: '{valor}'.")
    };

    public static string ATexto(EstadoCorreo estado) => estado switch
    {
        EstadoCorreo.Enviado => "ENVIADO",
        EstadoCorreo.Error   => "ERROR",
        _ => throw new ArgumentOutOfRangeException(nameof(estado))
    };

    public static EstadoCorreo EstadoCorreoDesde(string valor) => valor?.Trim().ToUpperInvariant() switch
    {
        "ENVIADO" => EstadoCorreo.Enviado,
        "ERROR"   => EstadoCorreo.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(valor), $"Estado de correo no reconocido: '{valor}'.")
    };
}

/// <summary>
/// Matriz de permisos por rol. Es la unica fuente de verdad sobre que puede
/// hacer cada rol; FrmPrincipal construye su menu consultando esta clase.
/// </summary>
public static class PermisosPorRol
{
    private static readonly Permiso[] DelCoordinador =
    [
        Permiso.GestionarReservas,
        Permiso.ConsultarReservas,
        Permiso.AnalizarConIa,
        Permiso.ConfirmarReserva,
        Permiso.CancelarReserva
    ];

    private static readonly Permiso[] DelAdministrador =
    [
        Permiso.GestionarReservas,
        Permiso.ConsultarReservas,
        Permiso.AnalizarConIa,
        Permiso.ConfirmarReserva,
        Permiso.CancelarReserva,
        Permiso.GestionarCatalogos,
        Permiso.VerAuditoriaIntegraciones,
        Permiso.AplicarDescuentoAlto
    ];

    public static IReadOnlyList<Permiso> De(RolUsuario rol) =>
        rol == RolUsuario.Administrador ? DelAdministrador : DelCoordinador;

    public static bool Tiene(RolUsuario rol, Permiso permiso) => De(rol).Contains(permiso);
}
