using Microsoft.Extensions.Options;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Infraestructura.Configuracion;
using SmartEvent.Infraestructura.Datos;
using SmartEvent.Infraestructura.Registro;
using Xunit;

namespace SmartEvent.Pruebas;

/// <summary>
/// Contexto compartido por las pruebas de integracion.
///
/// Estas pruebas se ejecutan CONTRA LA BASE DE DATOS REAL. Es deliberado: los
/// casos de aceptacion del examen exigen comprobar transacciones, cruces de
/// horario y stock, y nada de eso se puede verificar con objetos simulados,
/// porque la logica vive en los procedimientos almacenados.
///
/// La cadena de conexion se lee de la variable de entorno
/// ConnectionStrings__SmartEventDb. Si no esta definida, las pruebas se marcan
/// como omitidas con un mensaje explicativo en lugar de fallar: asi el
/// proyecto sigue compilando y ejecutandose en un equipo recien clonado que
/// todavia no configuro nada.
/// </summary>
public sealed class ContextoPruebas : IAsyncLifetime
{
    public const string NombreVariableConexion = "ConnectionStrings__SmartEventDb";

    /// <summary>
    /// Las pruebas trabajan con fechas muy lejanas (mas de 400 dias) para no
    /// interferir con datos reales. Antes de cada ejecucion se borran las
    /// reservas de ese rango, de modo que la bateria sea REPETIBLE: sin esto,
    /// la segunda ejecucion chocaria por cruce de horario con las reservas que
    /// creo la primera.
    /// </summary>
    public const int DiasDesplazamientoBase = 400;

    public string? CadenaConexion { get; }

    /// <summary>Motivo por el que se omiten las pruebas, o null si se pueden ejecutar.</summary>
    public string? MotivoOmision { get; }

    public IFabricaConexion Fabrica { get; }
    public IRegistradorSeguro Registro { get; }
    public IUsuarioRepositorio Usuarios { get; }
    public ICatalogoRepositorio Catalogos { get; }
    public IReservaRepositorio Reservas { get; }
    public IAuditoriaRepositorio Auditoria { get; }

    public ContextoPruebas()
    {
        CadenaConexion = Environment.GetEnvironmentVariable(NombreVariableConexion);

        if (string.IsNullOrWhiteSpace(CadenaConexion))
        {
            MotivoOmision =
                $"No se definio la variable de entorno {NombreVariableConexion}. "
                + "Consulte el README para configurarla antes de ejecutar las pruebas de integracion.";
        }

        var opcionesRegistro = Options.Create(new OpcionesRegistro
        {
            CarpetaLogs = Path.Combine(Path.GetTempPath(), "smartevent-pruebas"),
            DiasRetencion = 1
        });

        var registrador = new RegistradorArchivo(opcionesRegistro);
        Registro = registrador;
        _registrador = registrador;

        var opcionesBd = Options.Create(new OpcionesBaseDatos
        {
            // Cadena de marcador cuando no hay configuracion: la fabrica exige
            // un valor no vacio, pero ninguna prueba llegara a usarla porque
            // todas comprueban MotivoOmision primero.
            CadenaConexion = CadenaConexion ?? "Server=(sin-configurar);Database=SmartEventAI;Trusted_Connection=True;",
            SegundosTiempoEsperaComando = 30
        });

        Fabrica = new FabricaConexionSql(opcionesBd);
        Usuarios = new UsuarioRepositorio(Fabrica, Registro, Options.Create(new OpcionesSeguridad()));
        Catalogos = new CatalogoRepositorio(Fabrica, Registro);
        Reservas = new ReservaRepositorio(Fabrica, Registro);
        Auditoria = new AuditoriaRepositorio(Fabrica, Registro);
    }

    private readonly RegistradorArchivo _registrador;

    /// <summary>Interrumpe la prueba con un mensaje claro si falta la configuracion.</summary>
    public void OmitirSiNoHayBaseDatos() =>
        Assert.SkipWhen(MotivoOmision is not null, MotivoOmision ?? string.Empty);

    /// <summary>
    /// Se ejecuta UNA vez antes de toda la bateria: deja limpia la franja de
    /// fechas reservada para las pruebas.
    ///
    /// El borrado se hace con una sentencia directa a proposito, porque la
    /// aplicacion no expone ninguna operacion de borrado de reservas: eliminar
    /// datos no es un caso de uso del sistema, solo una necesidad de las
    /// pruebas. Las claves foraneas con ON DELETE CASCADE arrastran detalle,
    /// auditoria, analisis de IA y correos asociados.
    /// </summary>
    public async ValueTask InitializeAsync()
    {
        if (MotivoOmision is not null)
        {
            return;
        }

        await using var conexion = await Fabrica.AbrirAsync(CancellationToken.None);

        await using var comando = new Microsoft.Data.SqlClient.SqlCommand(
            "DELETE FROM evt.Reserva WHERE FechaEvento >= @Desde", conexion);

        comando.Parameters.Add("@Desde", System.Data.SqlDbType.Date).Value =
            DateTime.Today.AddDays(DiasDesplazamientoBase);

        var borradas = await comando.ExecuteNonQueryAsync(CancellationToken.None);

        Registro.Informacion(
            $"Limpieza previa a las pruebas: {borradas} reserva(s) de prueba eliminadas.");
    }

    public ValueTask DisposeAsync()
    {
        _registrador.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Agrupa las pruebas de integracion para que compartan una sola instancia del
/// contexto y no se ejecuten en paralelo entre si. Es necesario porque todas
/// escriben en las mismas tablas.
/// </summary>
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class ColeccionIntegracion : ICollectionFixture<ContextoPruebas>
{
    public const string Nombre = "Integracion SmartEvent";
}
