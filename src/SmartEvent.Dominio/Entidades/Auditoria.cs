using SmartEvent.Dominio.Enumeraciones;

namespace SmartEvent.Dominio.Entidades;

/// <summary>
/// Usuario que inicio sesion. Refleja lo que devuelve seg.sp_Usuario_Autenticar
/// en su segunda fase.
///
/// OBSERVACION DE SEGURIDAD: esta clase NO tiene ninguna propiedad para el hash
/// de la contrasena. El hash almacenado nunca sale de SQL Server, asi que no
/// existe siquiera un lugar donde la interfaz pudiera guardarlo.
/// </summary>
public sealed class UsuarioAutenticado
{
    public int IdUsuario { get; init; }
    public string NombreUsuario { get; init; } = string.Empty;
    public string NombreCompleto { get; init; } = string.Empty;
    public RolUsuario Rol { get; init; }
    public DateTime FechaInicioSesion { get; init; } = DateTime.Now;

    /// <summary>Indica si el usuario tiene el permiso indicado, segun su rol.</summary>
    public bool Tiene(Permiso permiso) => PermisosPorRol.Tiene(Rol, permiso);

    /// <summary>Atajo usado por la regla D13 (descuentos superiores al 10%).</summary>
    public bool EsAdministrador => Rol == RolUsuario.Administrador;

    public string DescripcionRol => TextosEnumeracion.ATexto(Rol);

    public override string ToString() => $"{NombreCompleto} ({NombreUsuario} - {DescripcionRol})";
}

/// <summary>
/// Registro de un analisis de IA. Refleja evt.AnalisisIA.
/// Se persiste tanto el exito como el fallo: el examen exige guardar el modelo
/// utilizado, el resultado y el error cuando corresponda.
/// </summary>
public sealed class AnalisisIa
{
    public int IdAnalisis { get; set; }
    public int IdReserva { get; set; }
    public string? ReservaCodigo { get; set; }

    public string Proveedor { get; set; } = string.Empty;
    public string Modelo { get; set; } = string.Empty;
    public string PromptVersion { get; set; } = string.Empty;

    public string? RespuestaJson { get; set; }
    public NivelRiesgo? NivelRiesgo { get; set; }
    public int? TokensEntrada { get; set; }
    public int? TokensSalida { get; set; }
    public int? DuracionMs { get; set; }

    public bool Exitoso { get; set; }
    public string? Error { get; set; }

    public bool EsContingenciaManual { get; set; }
    public string? JustificacionContingencia { get; set; }

    public int IdUsuario { get; set; }
    public string? Usuario { get; set; }
    public DateTime Fecha { get; set; }

    /// <summary>
    /// Indica si este registro habilita la confirmacion de la reserva
    /// (regla D22): analisis exitoso, o contingencia manual justificada.
    /// </summary>
    public bool HabilitaConfirmacion =>
        Exitoso
        || (EsContingenciaManual && Reglas.ReglasReserva.JustificacionContingenciaEsValida(JustificacionContingencia));
}

/// <summary>
/// Registro de un intento de envio de correo. Refleja com.CorreoEnviado.
/// Nunca contiene credenciales: ServidorSmtp guarda solo host y puerto.
/// </summary>
public sealed class CorreoEnviado
{
    public int IdCorreo { get; set; }
    public int IdReserva { get; set; }
    public string? ReservaCodigo { get; set; }

    public string Destinatario { get; set; } = string.Empty;
    public string Asunto { get; set; } = string.Empty;
    public TipoEventoCorreo TipoEvento { get; set; }

    /// <summary>Numero de intento. Un reenvio genera un registro nuevo con el siguiente numero.</summary>
    public short Intento { get; set; }

    public EstadoCorreo Estado { get; set; }
    public string? Error { get; set; }
    public string? ServidorSmtp { get; set; }
    public int? DuracionMs { get; set; }

    public int IdUsuario { get; set; }
    public string? Usuario { get; set; }
    public DateTime FechaIntento { get; set; }

    public bool FueExitoso => Estado == EstadoCorreo.Enviado;
}

/// <summary>
/// Traza de un cambio de estado de la reserva. Refleja evt.ReservaAuditoria.
/// Es la evidencia de CA-06 (una sola transicion) y CA-07 (el reintento de
/// correo no repite el cambio de estado).
/// </summary>
public sealed class ReservaAuditoria
{
    public int IdAuditoria { get; set; }
    public int IdReserva { get; set; }
    public EstadoReserva EstadoAnterior { get; set; }
    public EstadoReserva EstadoNuevo { get; set; }
    public string? Motivo { get; set; }
    public int IdUsuario { get; set; }
    public string? Usuario { get; set; }
    public DateTime Fecha { get; set; }
}
