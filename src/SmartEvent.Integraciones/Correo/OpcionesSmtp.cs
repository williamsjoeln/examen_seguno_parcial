namespace SmartEvent.Integraciones.Correo;

/// <summary>
/// Configuracion del servidor de correo.
///
/// NINGUNA credencial esta escrita en el codigo. Usuario y contrasena se
/// enlazan desde variables de entorno (Smtp__Usuario, Smtp__Password) o desde
/// appsettings.json, que esta en .gitignore.
///
/// Para las evidencias del examen se recomienda smtp4dev en local: habla SMTP
/// de verdad, no pide credenciales y muestra los correos en una bandeja web.
///     dotnet tool install -g Rnwood.Smtp4dev
///     smtp4dev --smtpport=2525 --urls=http://localhost:5080
/// </summary>
public sealed class OpcionesSmtp
{
    public const string Seccion = "Smtp";

    public string Host { get; set; } = string.Empty;
    public int Puerto { get; set; } = 25;

    /// <summary>Si es true se usa SSL/TLS implicito; si es false se intenta STARTTLS cuando el servidor lo ofrece.</summary>
    public bool UsarSsl { get; set; }

    /// <summary>Vacio cuando el servidor no exige autenticacion, como smtp4dev.</summary>
    public string? Usuario { get; set; }

    /// <summary>Vacio cuando el servidor no exige autenticacion. NUNCA se registra ni se persiste.</summary>
    public string? Password { get; set; }

    public string RemitenteNombre { get; set; } = "SmartEvent AI";
    public string RemitenteCorreo { get; set; } = "no-responder@smartevent.local";

    /// <summary>Tiempo maximo de espera del envio completo.</summary>
    public int SegundosTiempoEspera { get; set; } = 20;

    /// <summary>Indica si hay configuracion suficiente para intentar un envio.</summary>
    public bool EstaConfigurado => !string.IsNullOrWhiteSpace(Host) && Puerto > 0;

    /// <summary>El servidor exige autenticacion.</summary>
    public bool RequiereAutenticacion =>
        !string.IsNullOrWhiteSpace(Usuario) && !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// Descripcion del servidor SIN credenciales. Es lo unico que se guarda en
    /// com.CorreoEnviado.ServidorSmtp y lo unico que se muestra en pantalla.
    /// </summary>
    public string Descripcion =>
        EstaConfigurado ? $"{Host}:{Puerto}" : "(SMTP sin configurar)";
}
