namespace SmartEvent.Infraestructura.Configuracion;

/// <summary>
/// Configuracion de acceso a la base de datos.
///
/// La cadena de conexion NUNCA esta escrita en el codigo: se enlaza desde
/// appsettings.json (que esta en .gitignore) o desde la variable de entorno
/// ConnectionStrings__SmartEventDb, que tiene prioridad.
/// </summary>
public sealed class OpcionesBaseDatos
{
    public const string Seccion = "BaseDatos";

    /// <summary>Cadena de conexion completa. Se asigna en la raiz de composicion.</summary>
    public string CadenaConexion { get; set; } = string.Empty;

    /// <summary>Segundos que se espera a que responda un procedimiento almacenado.</summary>
    public int SegundosTiempoEsperaComando { get; set; } = 30;

    /// <summary>
    /// Devuelve la cadena de conexion sin la parte de credenciales, apta para
    /// mostrarse en pantalla o escribirse en el log.
    ///
    /// Es la funcion que garantiza que un mensaje de diagnostico jamas revele
    /// una contrasena, aunque alguien configure autenticacion de SQL Server en
    /// lugar de autenticacion de Windows.
    /// </summary>
    public string DescripcionSegura()
    {
        if (string.IsNullOrWhiteSpace(CadenaConexion))
        {
            return "(sin configurar)";
        }

        var partesSeguras = CadenaConexion
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !EsClaveSensible(p))
            .ToArray();

        return string.Join("; ", partesSeguras);
    }

    private static bool EsClaveSensible(string parte)
    {
        var nombre = parte.Split('=', 2)[0].Trim();

        return nombre.Equals("Password", StringComparison.OrdinalIgnoreCase)
            || nombre.Equals("Pwd", StringComparison.OrdinalIgnoreCase)
            || nombre.Equals("User ID", StringComparison.OrdinalIgnoreCase)
            || nombre.Equals("UID", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Parametros del bloqueo temporal del inicio de sesion.</summary>
public sealed class OpcionesSeguridad
{
    public const string Seccion = "Seguridad";

    /// <summary>Intentos fallidos consecutivos antes de bloquear la cuenta.</summary>
    public int MaximoIntentosFallidos { get; set; } = 3;

    /// <summary>Minutos que dura el bloqueo temporal.</summary>
    public int MinutosBloqueo { get; set; } = 3;
}

/// <summary>Configuracion del registro local de eventos.</summary>
public sealed class OpcionesRegistro
{
    public const string Seccion = "Registro";

    /// <summary>Carpeta de los archivos de log. Si es relativa, se resuelve junto al ejecutable.</summary>
    public string CarpetaLogs { get; set; } = "logs";

    /// <summary>Dias que se conservan los archivos antes de eliminarse.</summary>
    public int DiasRetencion { get; set; } = 7;
}
