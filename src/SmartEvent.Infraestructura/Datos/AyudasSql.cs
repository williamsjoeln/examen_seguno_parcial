using System.Data;
using Microsoft.Data.SqlClient;
using SmartEvent.Dominio.Excepciones;

namespace SmartEvent.Infraestructura.Datos;

/// <summary>
/// Extensiones para leer columnas de forma segura frente a NULL y para agregar
/// parametros TIPADOS.
///
/// Por que existe: llamar directamente a lector.GetString(i) revienta con
/// InvalidCastException si la columna es NULL, y usar lector["Col"].ToString()
/// esconde errores de nombre de columna hasta que ocurren en produccion. Estas
/// extensiones resuelven ambas cosas y dejan los repositorios legibles.
/// </summary>
internal static class ExtensionesLector
{
    public static string Texto(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? string.Empty : lector.GetString(indice);
    }

    public static string? TextoNulo(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? null : lector.GetString(indice);
    }

    public static int Entero(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? 0 : lector.GetInt32(indice);
    }

    public static int? EnteroNulo(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? null : lector.GetInt32(indice);
    }

    public static short Corto(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? (short)0 : lector.GetInt16(indice);
    }

    public static decimal Decimal(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? 0m : lector.GetDecimal(indice);
    }

    public static bool Booleano(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return !lector.IsDBNull(indice) && lector.GetBoolean(indice);
    }

    public static DateTime FechaHora(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? DateTime.MinValue : lector.GetDateTime(indice);
    }

    public static DateTime? FechaHoraNula(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? null : lector.GetDateTime(indice);
    }

    /// <summary>
    /// Lee una columna DATE de SQL Server como DateOnly.
    /// Microsoft.Data.SqlClient devuelve DateTime para el tipo DATE, asi que la
    /// conversion se hace aqui una sola vez y no en cada repositorio.
    /// </summary>
    public static DateOnly Fecha(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice)
            ? default
            : DateOnly.FromDateTime(lector.GetDateTime(indice));
    }

    /// <summary>Lee una columna TIME de SQL Server como TimeSpan.</summary>
    public static TimeSpan Hora(this SqlDataReader lector, string columna)
    {
        var indice = lector.GetOrdinal(columna);
        return lector.IsDBNull(indice) ? TimeSpan.Zero : lector.GetTimeSpan(indice);
    }
}

/// <summary>
/// Ayudas para construir parametros TIPADOS.
///
/// Todos los parametros se declaran con su SqlDbType y su tamano explicitos.
/// Esto es lo que exige el examen ("parametros tipados") y ademas evita
/// conversiones implicitas que degradan el rendimiento de los indices.
/// </summary>
internal static class ExtensionesParametros
{
    public static SqlParameter Agregar(this SqlCommand comando, string nombre, SqlDbType tipo, object? valor)
    {
        var parametro = comando.Parameters.Add(nombre, tipo);
        parametro.Value = valor ?? DBNull.Value;
        return parametro;
    }

    public static SqlParameter Agregar(this SqlCommand comando, string nombre, SqlDbType tipo, int tamano, object? valor)
    {
        var parametro = comando.Parameters.Add(nombre, tipo, tamano);
        parametro.Value = valor ?? DBNull.Value;
        return parametro;
    }

    /// <summary>Agrega un parametro de fecha a partir de un DateOnly.</summary>
    public static SqlParameter AgregarFecha(this SqlCommand comando, string nombre, DateOnly? fecha)
    {
        var parametro = comando.Parameters.Add(nombre, SqlDbType.Date);
        parametro.Value = fecha.HasValue
            ? fecha.Value.ToDateTime(TimeOnly.MinValue)
            : DBNull.Value;
        return parametro;
    }

    /// <summary>Agrega un parametro de hora a partir de un TimeSpan.</summary>
    public static SqlParameter AgregarHora(this SqlCommand comando, string nombre, TimeSpan hora)
    {
        var parametro = comando.Parameters.Add(nombre, SqlDbType.Time);
        parametro.Value = hora;
        return parametro;
    }

    /// <summary>Agrega un parametro de salida.</summary>
    public static SqlParameter AgregarSalida(this SqlCommand comando, string nombre, SqlDbType tipo, int tamano = 0)
    {
        var parametro = tamano > 0
            ? comando.Parameters.Add(nombre, tipo, tamano)
            : comando.Parameters.Add(nombre, tipo);

        parametro.Direction = ParameterDirection.Output;
        return parametro;
    }

    /// <summary>Devuelve el valor de un parametro de salida, o null si vino DBNull.</summary>
    public static T? Salida<T>(this SqlParameter parametro) where T : struct =>
        parametro.Value is null || parametro.Value == DBNull.Value ? null : (T)parametro.Value;

    /// <summary>Devuelve el texto de un parametro de salida, o null si vino DBNull.</summary>
    public static string? SalidaTexto(this SqlParameter parametro) =>
        parametro.Value is null || parametro.Value == DBNull.Value ? null : (string)parametro.Value;
}

/// <summary>
/// Traduce los errores de SQL Server a excepciones de la aplicacion.
///
/// ESTE ES EL PUNTO CLAVE DE LA REGLA D25 del examen: "los mensajes de error
/// deben ser comprensibles y no revelar cadenas de conexion, claves, SQL
/// interno ni stack traces".
///
/// El criterio es simple y facil de defender:
///   numero &gt;= 50000  -> es un THROW escrito por nosotros en un procedimiento
///                        almacenado. El mensaje ya esta redactado para el
///                        usuario final, asi que se muestra tal cual.
///   numero &lt; 50000   -> es un error interno del motor (violacion de clave,
///                        interbloqueo, tiempo agotado...). El mensaje original
///                        podria contener nombres de objetos internos, asi que
///                        se sustituye por uno generico y el detalle real va
///                        UNICAMENTE al archivo de registro.
/// </summary>
internal static class TraductorErroresSql
{
    /// <summary>Numero a partir del cual un error de SQL Server es un error de negocio propio.</summary>
    public const int PrimerNumeroErrorNegocio = 50_000;

    public static Exception Traducir(SqlException excepcion)
    {
        ArgumentNullException.ThrowIfNull(excepcion);

        if (excepcion.Number >= PrimerNumeroErrorNegocio)
        {
            return new ExcepcionNegocio(excepcion.Message, excepcion.Number, excepcion);
        }

        var mensaje = excepcion.Number switch
        {
            2601 or 2627 => "Ya existe un registro con esos datos. Revise los campos que deben ser unicos.",
            547          => "La operacion no se puede completar porque el registro esta relacionado con otros datos.",
            1205         => "La operacion se cancelo por un bloqueo simultaneo en la base de datos. Intente nuevamente.",
            -2           => "La base de datos tardo demasiado en responder. Intente nuevamente en unos segundos.",
            18456        => "No se pudo iniciar sesion en la base de datos. Verifique la configuracion de la conexion.",
            4060 or 911  => "La base de datos SmartEventAI no existe o no esta disponible. Ejecute database/00_SmartEventAI.sql.",
            _            => "Ocurrio un error al acceder a la base de datos. El detalle tecnico quedo registrado en el archivo de registro."
        };

        return new ExcepcionNegocio(mensaje, excepcion);
    }
}
