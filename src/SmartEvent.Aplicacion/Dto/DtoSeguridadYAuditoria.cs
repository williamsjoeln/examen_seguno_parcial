using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Aplicacion.Dto;

/// <summary>
/// Parametros de derivacion devueltos por la PRIMERA FASE de
/// seg.sp_Usuario_Autenticar.
///
/// Contiene el algoritmo, las iteraciones y el salt, pero NUNCA el hash
/// almacenado: ese no sale jamas de SQL Server. Con estos datos la aplicacion
/// calcula el hash de la contrasena escrita, y la segunda fase lo compara
/// dentro del motor.
/// </summary>
public sealed record ParametrosDerivacion(
    string Algoritmo,
    int Iteraciones,
    string SaltBase64,
    bool EstaBloqueado,
    int SegundosBloqueo);

/// <summary>Resultado de la SEGUNDA FASE de seg.sp_Usuario_Autenticar.</summary>
public sealed class ResultadoAutenticacion
{
    public bool Autenticado { get; init; }

    /// <summary>Datos del usuario. Es null cuando la autenticacion fallo.</summary>
    public UsuarioAutenticado? Usuario { get; init; }

    /// <summary>Segundos que faltan para que expire el bloqueo temporal, si lo hay.</summary>
    public int SegundosBloqueo { get; init; }

    /// <summary>
    /// Mensaje apto para el usuario. Es deliberadamente el MISMO para usuario
    /// inexistente, usuario inactivo y contrasena incorrecta, de modo que no se
    /// pueda averiguar que cuentas existen.
    /// </summary>
    public string Mensaje { get; init; } = string.Empty;

    public bool EstaBloqueado => SegundosBloqueo > 0;

    public static ResultadoAutenticacion Correcto(UsuarioAutenticado usuario, string mensaje) =>
        new() { Autenticado = true, Usuario = usuario, Mensaje = mensaje };

    public static ResultadoAutenticacion Rechazado(string mensaje, int segundosBloqueo = 0) =>
        new() { Autenticado = false, Mensaje = mensaje, SegundosBloqueo = segundosBloqueo };
}

/// <summary>Datos que se persisten tras ejecutar un analisis de IA, salga bien o mal.</summary>
public sealed class RegistroAnalisisIa
{
    public int IdReserva { get; init; }
    public string Proveedor { get; init; } = string.Empty;
    public string Modelo { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public string? RespuestaJson { get; init; }
    public NivelRiesgo? NivelRiesgo { get; init; }
    public int? TokensEntrada { get; init; }
    public int? TokensSalida { get; init; }
    public int? DuracionMs { get; init; }
    public bool Exitoso { get; init; }
    public string? Error { get; init; }
    public bool EsContingenciaManual { get; init; }
    public string? JustificacionContingencia { get; init; }
    public int IdUsuario { get; init; }
}

/// <summary>Datos que se persisten tras cada intento de envio de correo.</summary>
public sealed class RegistroCorreo
{
    public int IdReserva { get; init; }
    public string Destinatario { get; init; } = string.Empty;
    public string Asunto { get; init; } = string.Empty;
    public TipoEventoCorreo TipoEvento { get; init; }
    public EstadoCorreo Estado { get; init; }
    public string? Error { get; init; }

    /// <summary>Solo host y puerto. JAMAS usuario ni contrasena.</summary>
    public string? ServidorSmtp { get; init; }

    public int? DuracionMs { get; init; }
    public int IdUsuario { get; init; }
}

/// <summary>Filtros de la pestana de correos de FrmAuditoriaIntegraciones.</summary>
public sealed class FiltroAuditoriaCorreo
{
    public int? IdReserva { get; init; }
    public string? Codigo { get; init; }
    public string? Destinatario { get; init; }
    public DateOnly? FechaDesde { get; init; }
    public DateOnly? FechaHasta { get; init; }
    public EstadoCorreo? Estado { get; init; }
    public TipoEventoCorreo? TipoEvento { get; init; }
    public int MaximoFilas { get; init; } = 200;
}

/// <summary>Filtros de la pestana de analisis de IA de FrmAuditoriaIntegraciones.</summary>
public sealed class FiltroAuditoriaIa
{
    public int? IdReserva { get; init; }
    public string? Codigo { get; init; }
    public DateOnly? FechaDesde { get; init; }
    public DateOnly? FechaHasta { get; init; }
    public bool SoloErrores { get; init; }
    public NivelRiesgo? NivelRiesgo { get; init; }
    public int MaximoFilas { get; init; } = 200;
}

/// <summary>Datos que necesita el correo HTML, ya listos para renderizar.</summary>
public sealed class DatosCorreoReserva
{
    public required Reserva Reserva { get; init; }
    public required TipoEventoCorreo TipoEvento { get; init; }

    /// <summary>Motivo de la cancelacion, cuando el evento es una cancelacion.</summary>
    public string? Motivo { get; init; }
}
