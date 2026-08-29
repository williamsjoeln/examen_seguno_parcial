using SmartEvent.Aplicacion.Dto;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Ia;

namespace SmartEvent.Aplicacion.Contratos;

/// <summary>
/// Resultado del intento de envio de un correo, ya normalizado.
/// Nunca lanza excepcion hacia arriba por un fallo de red: el examen exige que
/// un fallo de SMTP no interrumpa el flujo (caso CA-07).
/// </summary>
/// <param name="Exitoso">Indica si el servidor acepto el mensaje.</param>
/// <param name="MensajeUsuario">Texto apto para mostrar en pantalla.</param>
/// <param name="DetalleTecnico">Detalle controlado para la columna Error de la auditoria.</param>
/// <param name="ServidorSmtp">Host y puerto usados. Sin credenciales.</param>
/// <param name="DuracionMs">Tiempo que tardo el intento.</param>
public sealed record ResultadoEnvioCorreo(
    bool Exitoso,
    string MensajeUsuario,
    string? DetalleTecnico,
    string ServidorSmtp,
    int DuracionMs);

/// <summary>
/// Envio de correo al cliente. La implementacion con MailKit vive en
/// SmartEvent.Integraciones; la capa de presentacion solo conoce esta interfaz,
/// por lo que ningun formulario puede abrir una conexion SMTP.
/// </summary>
public interface IServicioCorreo
{
    /// <summary>
    /// Construye y envia el correo HTML de confirmacion o cancelacion.
    /// Aplica tiempo de espera y respeta la cancelacion.
    /// </summary>
    Task<ResultadoEnvioCorreo> EnviarAsync(DatosCorreoReserva datos, CancellationToken cancelacion);

    /// <summary>
    /// Genera el cuerpo HTML sin enviarlo. Sirve para previsualizar el mensaje
    /// en pantalla y para las evidencias.
    /// </summary>
    string ConstruirCuerpoHtml(DatosCorreoReserva datos);

    /// <summary>Genera el asunto del mensaje.</summary>
    string ConstruirAsunto(DatosCorreoReserva datos);

    /// <summary>Descripcion del servidor configurado (host y puerto). Nunca incluye credenciales.</summary>
    string DescripcionServidor { get; }
}

/// <summary>
/// Analisis de riesgo con la Responses API. La implementacion vive en
/// SmartEvent.Integraciones.
///
/// LIMITE DE RESPONSABILIDAD (Examen SS10): la IA SOLO RECOMIENDA. Esta interfaz
/// no expone ningun metodo capaz de confirmar, cancelar, modificar totales ni
/// ejecutar SQL. Devuelve informacion; las acciones las toma siempre el usuario.
/// </summary>
public interface IServicioAnalisisIa
{
    /// <summary>
    /// Envia a la Responses API unicamente los datos necesarios de la reserva y
    /// devuelve el analisis ya deserializado y validado.
    ///
    /// No lanza excepciones por fallos del servicio: devuelve una ejecucion
    /// marcada como no exitosa con un mensaje seguro, de modo que la aplicacion
    /// siga operativa (caso CA-09).
    /// </summary>
    Task<EjecucionAnalisisIa> AnalizarAsync(Reserva reserva, CancellationToken cancelacion);

    /// <summary>Indica si hay una clave configurada. Permite avisar antes de intentar la llamada.</summary>
    bool EstaConfigurado { get; }

    /// <summary>Modelo configurado, para mostrarlo en pantalla y auditarlo.</summary>
    string Modelo { get; }

    /// <summary>Proveedor deducido de la URL base (OPENAI, GROQ...). Se guarda en la auditoria.</summary>
    string Proveedor { get; }

    /// <summary>Version del prompt utilizada. Se persiste en evt.AnalisisIA.PromptVersion.</summary>
    string PromptVersion { get; }
}

/// <summary>
/// Registro local de eventos, con redaccion automatica de secretos.
///
/// El examen exige "logging local sin registrar claves, contrasenas ni el
/// cuerpo completo de informacion sensible". La implementacion filtra cualquier
/// texto que se parezca a una clave de API, una contrasena o una cadena de
/// conexion ANTES de escribirlo en disco.
/// </summary>
public interface IRegistradorSeguro
{
    void Informacion(string mensaje);
    void Advertencia(string mensaje);
    void Error(string mensaje, Exception? excepcion = null);

    /// <summary>Ruta del archivo de log actual, para mostrarla en el diagnostico.</summary>
    string ArchivoActual { get; }
}
