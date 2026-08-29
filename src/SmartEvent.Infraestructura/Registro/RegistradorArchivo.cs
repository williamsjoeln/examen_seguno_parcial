using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Infraestructura.Configuracion;

namespace SmartEvent.Infraestructura.Registro;

/// <summary>
/// Registro local de eventos en archivo, con REDACCION AUTOMATICA DE SECRETOS.
///
/// El examen exige "logging local sin registrar claves, contrasenas ni el
/// cuerpo completo de informacion sensible", y penaliza con 0 en Seguridad si
/// una clave real aparece publicada en cualquier parte.
///
/// Confiar en que quien escriba una linea de log se acuerde de no incluir un
/// secreto es fragil. Por eso la redaccion NO es opcional: todo texto pasa
/// obligatoriamente por <see cref="Redactar"/> antes de tocar el disco, de modo
/// que aunque alguien escriba por descuido la cadena de conexion completa, en
/// el archivo aparecera enmascarada.
///
/// Se implementa a mano en lugar de usar una libreria de registro para tener
/// control total sobre lo que se escribe y no arrastrar una dependencia mas.
/// </summary>
public sealed class RegistradorArchivo : IRegistradorSeguro, IDisposable
{
    private readonly OpcionesRegistro _opciones;
    private readonly string _carpeta;

    // Un unico bloqueo protege la escritura: varios formularios pueden registrar
    // desde tareas distintas al mismo tiempo.
    // Se usa object y no System.Threading.Lock porque ese tipo es de .NET 9 y
    // aqui el objetivo obligatorio del examen es .NET 8.
    private readonly object _candado = new();
    private bool _dispuesto;

    /// <summary>
    /// Patrones de secretos. Se compilan una sola vez y tienen tiempo maximo de
    /// ejecucion para evitar retroceso catastrofico con textos muy largos.
    /// </summary>
    private static readonly (Regex Patron, string Reemplazo)[] Redacciones =
    [
        // Claves de API tipicas: OpenAI (sk-...), Groq (gsk_...), GitHub (github_pat_...)
        (new Regex(@"\b(sk|gsk)[-_][A-Za-z0-9_\-]{16,}", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)), "[CLAVE-API-REDACTADA]"),
        (new Regex(@"\bgithub_pat_[A-Za-z0-9_]{16,}", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200)), "[CLAVE-API-REDACTADA]"),

        // Cabecera de autorizacion
        (new Regex(@"(?i)\bBearer\s+[A-Za-z0-9._\-]{8,}", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)), "Bearer [REDACTADO]"),

        // Claves de configuracion escritas como clave=valor o "clave": "valor"
        (new Regex(@"(?i)\b(password|pwd|contrasena|api[_-]?key|apikey|token|secret)\b\s*[:=]\s*""?[^""&;,\s]+""?", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)), "$1=[REDACTADO]"),

        // Cadena de conexion completa
        (new Regex(@"(?i)\b(Server|Data Source)\s*=\s*[^;]+;.*?(Database|Initial Catalog)\s*=\s*[^;]+;?", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200)), "[CADENA-CONEXION-REDACTADA]")
    ];

    public RegistradorArchivo(IOptions<OpcionesRegistro> opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _opciones = opciones.Value;

        _carpeta = Path.IsPathRooted(_opciones.CarpetaLogs)
            ? _opciones.CarpetaLogs
            : Path.Combine(AppContext.BaseDirectory, _opciones.CarpetaLogs);

        Directory.CreateDirectory(_carpeta);
        EliminarArchivosAntiguos();
    }

    /// <summary>Un archivo por dia, para que sea facil localizar el del examen.</summary>
    public string ArchivoActual =>
        Path.Combine(_carpeta, $"smartevent-{DateTime.Now:yyyy-MM-dd}.log");

    public void Informacion(string mensaje) => Escribir("INFO", mensaje, null);

    public void Advertencia(string mensaje) => Escribir("AVISO", mensaje, null);

    public void Error(string mensaje, Exception? excepcion = null) => Escribir("ERROR", mensaje, excepcion);

    /// <summary>
    /// Elimina de un texto cualquier fragmento que parezca un secreto.
    /// Es publico y estatico para poder probarlo de forma aislada.
    /// </summary>
    public static string Redactar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var resultado = texto;

        foreach (var (patron, reemplazo) in Redacciones)
        {
            try
            {
                resultado = patron.Replace(resultado, reemplazo);
            }
            catch (RegexMatchTimeoutException)
            {
                // Si un texto patologico agota el tiempo, se prefiere perder el
                // detalle antes que arriesgarse a escribir un secreto sin filtrar.
                return "[TEXTO OMITIDO: no se pudo verificar que estuviera libre de secretos]";
            }
        }

        return resultado;
    }

    private void Escribir(string nivel, string mensaje, Exception? excepcion)
    {
        if (_dispuesto)
        {
            return;
        }

        var linea = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(nivel).Append("] ")
            .Append(Redactar(mensaje));

        if (excepcion is not null)
        {
            // Se registra el tipo y el mensaje de la excepcion, y la traza solo
            // en el archivo. La traza NUNCA llega a la interfaz de usuario.
            linea.AppendLine()
                 .Append("    ").Append(excepcion.GetType().FullName).Append(": ")
                 .Append(Redactar(excepcion.Message));

            if (!string.IsNullOrWhiteSpace(excepcion.StackTrace))
            {
                linea.AppendLine().Append(Redactar(excepcion.StackTrace));
            }

            if (excepcion.InnerException is not null)
            {
                linea.AppendLine()
                     .Append("    Causa: ").Append(excepcion.InnerException.GetType().Name)
                     .Append(": ").Append(Redactar(excepcion.InnerException.Message));
            }
        }

        try
        {
            lock (_candado)
            {
                File.AppendAllText(ArchivoActual, linea.ToString() + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // Que falle el registro nunca debe tumbar la aplicacion.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void EliminarArchivosAntiguos()
    {
        try
        {
            var limite = DateTime.Now.AddDays(-Math.Max(1, _opciones.DiasRetencion));

            foreach (var archivo in Directory.EnumerateFiles(_carpeta, "smartevent-*.log"))
            {
                if (File.GetLastWriteTime(archivo) < limite)
                {
                    File.Delete(archivo);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void Dispose() => _dispuesto = true;
}
