using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.WinForms.Composicion;
using SmartEvent.WinForms.Comun;
using SmartEvent.WinForms.Formularios;

namespace SmartEvent.WinForms;

/// <summary>
/// Punto de entrada de SmartEvent AI.
///
/// Responsabilidades, en orden:
///   1. Montar el contenedor de dependencias.
///   2. Instalar el MANEJO CENTRALIZADO DE EXCEPCIONES, para que ningun error
///      no previsto muestre una pantalla azul de .NET al usuario ni cierre la
///      aplicacion sin explicacion (Examen SS8).
///   3. Comprobar que hay configuracion minima y, si no, explicar que falta.
///   4. Mostrar FrmLogin y, si la autenticacion es correcta, FrmPrincipal.
/// </summary>
internal static class Program
{
    private static ServiceProvider? _servicios;
    private static IRegistradorSeguro? _registro;

    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        try
        {
            _servicios = ContenedorServicios.Construir();
            _registro = _servicios.GetRequiredService<IRegistradorSeguro>();
        }
        catch (Exception ex)
        {
            // Fallo al montar el contenedor: no hay registro disponible todavia.
            MessageBox.Show(
                "No se pudo iniciar la aplicacion." + Environment.NewLine + Environment.NewLine
                + "Revise el archivo appsettings.json o las variables de entorno. "
                + "El README explica la configuracion necesaria." + Environment.NewLine + Environment.NewLine
                + "Detalle: " + ex.Message,
                "SmartEvent AI",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        InstalarManejoCentralizadoDeExcepciones();

        _registro.Informacion("=== Inicio de la aplicacion SmartEvent AI ===");

        // --- Configuracion minima: mensaje util en lugar de una excepcion ---
        var configuracion = _servicios.GetRequiredService<IConfiguration>();
        var problema = ContenedorServicios.ComprobarConfiguracionMinima(configuracion);

        if (problema is not null)
        {
            MessageBox.Show(problema, "SmartEvent AI - Configuracion pendiente",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _registro.Advertencia("La aplicacion se cerro por falta de cadena de conexion.");
            return;
        }

        // --- Inicio de sesion ---
        using (var login = _servicios.GetRequiredService<FrmLogin>())
        {
            if (login.ShowDialog() != DialogResult.OK)
            {
                _registro.Informacion("La aplicacion se cerro desde el inicio de sesion.");
                return;
            }
        }

        // --- Ventana principal ---
        var principal = _servicios.GetRequiredService<FrmPrincipal>();
        Application.Run(principal);

        _registro.Informacion("=== Fin de la aplicacion SmartEvent AI ===");
        _servicios.Dispose();
    }

    /// <summary>
    /// Captura las excepciones que se escapan de cualquier controlador de
    /// eventos y de cualquier hilo en segundo plano.
    ///
    /// Sin esto, un error no previsto en un manejador de eventos cerraria la
    /// aplicacion mostrando una traza completa, que es exactamente lo que la
    /// regla D25 del examen prohibe.
    /// </summary>
    private static void InstalarManejoCentralizadoDeExcepciones()
    {
        // Excepciones dentro del bucle de mensajes de Windows Forms.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        Application.ThreadException += (_, e) =>
            MostrarErrorNoPrevisto(e.Exception, cerrar: false);

        // Excepciones de cualquier otro hilo: aqui el proceso ya no se puede
        // salvar, pero al menos se registra y se avisa con un mensaje decente.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            MostrarErrorNoPrevisto(e.ExceptionObject as Exception, cerrar: true);

        // Excepciones de tareas asincronicas que nadie observo.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            _registro?.Error("Excepcion no observada en una tarea asincronica.", e.Exception);
            e.SetObserved();
        };
    }

    private static void MostrarErrorNoPrevisto(Exception? excepcion, bool cerrar)
    {
        // El detalle tecnico va SOLO al archivo de registro.
        _registro?.Error("Error no previsto en la aplicacion.", excepcion);

        var mensaje = cerrar
            ? "Ocurrio un error inesperado y la aplicacion debe cerrarse."
            : "Ocurrio un error inesperado. La operacion no se completo, pero puede seguir trabajando.";

        AyudasUi.MostrarError(
            mensaje + Environment.NewLine + Environment.NewLine
            + "El detalle tecnico se guardo en el archivo de registro:" + Environment.NewLine
            + (_registro?.ArchivoActual ?? "(no disponible)"));
    }
}
