using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SmartEvent.Dominio.Excepciones;
using SmartEvent.Infraestructura.Configuracion;

namespace SmartEvent.Infraestructura.Datos;

/// <summary>
/// Crea conexiones a SQL Server.
/// </summary>
public interface IFabricaConexion
{
    /// <summary>
    /// Crea y abre una conexion NUEVA. El llamador es responsable de liberarla,
    /// siempre con "await using".
    /// </summary>
    Task<SqlConnection> AbrirAsync(CancellationToken cancelacion);

    /// <summary>Crea un comando de procedimiento almacenado con el tiempo de espera configurado.</summary>
    SqlCommand CrearComando(string procedimiento, SqlConnection conexion);

    /// <summary>Descripcion de la conexion sin credenciales, para diagnostico.</summary>
    string DescripcionSegura { get; }
}

/// <summary>
/// Implementacion de <see cref="IFabricaConexion"/> para SQL Server.
///
/// DECISION DE DISENO IMPORTANTE PARA LA DEFENSA:
/// esta clase NO guarda ninguna conexion. Cada llamada a AbrirAsync devuelve
/// una conexion nueva que el llamador cierra con "await using". El examen
/// prohibe expresamente "mantener una conexion global abierta", y ademas seria
/// incorrecto en una aplicacion con operaciones asincronicas concurrentes:
/// una SqlConnection no es segura para usarse desde varias tareas a la vez.
///
/// El costo de abrir conexiones se compensa solo, porque
/// Microsoft.Data.SqlClient mantiene un POOL de conexiones fisicas: al cerrar,
/// la conexion vuelve al pool en lugar de destruirse.
/// </summary>
public sealed class FabricaConexionSql : IFabricaConexion
{
    private readonly OpcionesBaseDatos _opciones;

    public FabricaConexionSql(IOptions<OpcionesBaseDatos> opciones)
    {
        ArgumentNullException.ThrowIfNull(opciones);
        _opciones = opciones.Value;

        if (string.IsNullOrWhiteSpace(_opciones.CadenaConexion))
        {
            throw new InvalidOperationException(
                "No se configuro la cadena de conexion a SQL Server. Defina ConnectionStrings:SmartEventDb "
                + "en appsettings.json o la variable de entorno ConnectionStrings__SmartEventDb. "
                + "Consulte appsettings.example.json y el README.");
        }
    }

    public string DescripcionSegura => _opciones.DescripcionSegura();

    public async Task<SqlConnection> AbrirAsync(CancellationToken cancelacion)
    {
        var conexion = new SqlConnection(_opciones.CadenaConexion);

        try
        {
            await conexion.OpenAsync(cancelacion).ConfigureAwait(false);
            return conexion;
        }
        catch (OperationCanceledException)
        {
            await conexion.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        catch (SqlException ex)
        {
            await conexion.DisposeAsync().ConfigureAwait(false);

            // Se traduce a un mensaje comprensible SIN incluir la cadena de
            // conexion ni el detalle interno de SQL Server (regla D25).
            throw new ExcepcionNegocio(
                "No se pudo conectar con la base de datos. Verifique que SQL Server este en ejecucion "
                + "y que la cadena de conexion sea correcta. Si el problema persiste, revise el archivo de registro.",
                ex);
        }
    }

    public SqlCommand CrearComando(string procedimiento, SqlConnection conexion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(procedimiento);
        ArgumentNullException.ThrowIfNull(conexion);

        return new SqlCommand(procedimiento, conexion)
        {
            // Siempre StoredProcedure: en toda la aplicacion no existe una sola
            // sentencia SQL escrita en C#, y mucho menos concatenada.
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = _opciones.SegundosTiempoEsperaComando
        };
    }
}
