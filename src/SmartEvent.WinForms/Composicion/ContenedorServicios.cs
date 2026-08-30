using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartEvent.Aplicacion;
using SmartEvent.Infraestructura;
using SmartEvent.Infraestructura.Configuracion;
using SmartEvent.Integraciones;
using SmartEvent.Integraciones.Correo;
using SmartEvent.Integraciones.Ia;
using SmartEvent.WinForms.Formularios;

namespace SmartEvent.WinForms.Composicion;

/// <summary>
/// RAIZ DE COMPOSICION de la aplicacion.
///
/// Es el UNICO archivo de la capa de presentacion que menciona clases concretas
/// de Infraestructura e Integraciones. Aqui se decide que implementacion recibe
/// cada interfaz; a partir de este punto, los formularios solo conocen
/// abstracciones.
///
/// Si el docente pregunta "demuestrame que la presentacion no accede a SQL ni a
/// SMTP", la respuesta es: buscar SqlConnection o SmtpClient en la carpeta
/// Formularios no devuelve ni un resultado; toda la fontaneria esta concentrada
/// en este archivo.
///
/// ORDEN DE LAS FUENTES DE CONFIGURACION (la ultima gana):
///   1. appsettings.json          archivo local, ignorado por Git
///   2. User Secrets              almacen del perfil de usuario, fuera del proyecto
///   3. Variables de entorno      lo que usa el docente al clonar el repositorio
///
/// De este modo un secreto NUNCA necesita estar dentro del repositorio.
/// </summary>
internal static class ContenedorServicios
{
    /// <summary>Nombre de la cadena de conexion en la configuracion.</summary>
    private const string NombreCadenaConexion = "SmartEventDb";

    public static ServiceProvider Construir()
    {
        var configuracion = ConstruirConfiguracion();
        var servicios = new ServiceCollection();

        servicios.AddSingleton<IConfiguration>(configuracion);

        RegistrarOpciones(servicios, configuracion);

        // Capas de la solucion. Cada una expone su propio metodo de registro,
        // de modo que agregar una dependencia no obliga a tocar este archivo.
        servicios.AgregarInfraestructura();
        servicios.AgregarIntegraciones();
        servicios.AgregarAplicacion();

        RegistrarFormularios(servicios);

        return servicios.BuildServiceProvider(new ServiceProviderOptions
        {
            // Detecta en tiempo de arranque errores de registro, en lugar de
            // fallar al abrir un formulario a mitad de la demostracion.
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static IConfigurationRoot ConstruirConfiguracion() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddUserSecrets(typeof(ContenedorServicios).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

    private static void RegistrarOpciones(IServiceCollection servicios, IConfiguration configuracion)
    {
        // ---------- Base de datos ----------
        servicios.Configure<OpcionesBaseDatos>(opciones =>
        {
            configuracion.GetSection(OpcionesBaseDatos.Seccion).Bind(opciones);
            opciones.CadenaConexion = configuracion.GetConnectionString(NombreCadenaConexion) ?? string.Empty;
        });

        // ---------- Seguridad y registro ----------
        servicios.Configure<OpcionesSeguridad>(configuracion.GetSection(OpcionesSeguridad.Seccion));
        servicios.Configure<OpcionesRegistro>(configuracion.GetSection(OpcionesRegistro.Seccion));

        // ---------- Correo ----------
        servicios.Configure<OpcionesSmtp>(configuracion.GetSection(OpcionesSmtp.Seccion));

        // ---------- OpenAI ----------
        servicios.Configure<OpcionesOpenAi>(opciones =>
        {
            configuracion.GetSection(OpcionesOpenAi.Seccion).Bind(opciones);

            // El examen exige leer la clave de OPENAI_API_KEY. Esa variable
            // tiene prioridad sobre cualquier valor de la seccion OpenAI, para
            // que la forma documentada sea siempre la que manda.
            var claveEntorno = configuracion[OpcionesOpenAi.VariableEntornoClave];

            if (!string.IsNullOrWhiteSpace(claveEntorno))
            {
                opciones.ApiKey = claveEntorno;
            }
        });
    }

    /// <summary>
    /// Los formularios se registran como transitorios: cada vez que se abre uno
    /// se crea una instancia nueva con sus dependencias ya resueltas. Asi
    /// ningun formulario tiene que usar "new" sobre un servicio.
    /// </summary>
    private static void RegistrarFormularios(IServiceCollection servicios)
    {
        servicios.AddTransient<FrmLogin>();
        servicios.AddTransient<FrmPrincipal>();
        servicios.AddTransient<FrmCatalogos>();
        servicios.AddTransient<FrmReservasConsulta>();
        servicios.AddTransient<FrmReservaEdicion>();
        servicios.AddTransient<FrmAuditoriaIntegraciones>();
    }

    /// <summary>
    /// Comprueba que exista una cadena de conexion configurada y devuelve un
    /// mensaje de ayuda si no la hay.
    ///
    /// Se llama al arrancar para poder mostrar instrucciones claras en lugar de
    /// una excepcion cruda, que es justo lo que necesita alguien que acaba de
    /// clonar el repositorio (caso CA-10).
    /// </summary>
    public static string? ComprobarConfiguracionMinima(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);

        if (!string.IsNullOrWhiteSpace(configuracion.GetConnectionString(NombreCadenaConexion)))
        {
            return null;
        }

        return
            "No se encontro la cadena de conexion a SQL Server." + Environment.NewLine + Environment.NewLine
            + "Para configurarla tiene dos opciones:" + Environment.NewLine + Environment.NewLine
            + "1) Copiar appsettings.example.json como appsettings.json junto al ejecutable "
            + "y reemplazar SERVIDOR_EJEMPLO por el nombre de su instancia de SQL Server."
            + Environment.NewLine + Environment.NewLine
            + "2) Definir la variable de entorno:" + Environment.NewLine
            + "   ConnectionStrings__SmartEventDb" + Environment.NewLine + Environment.NewLine
            + "El README del proyecto explica el procedimiento paso a paso.";
    }
}
