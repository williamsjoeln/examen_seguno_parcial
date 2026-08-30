using Microsoft.Extensions.DependencyInjection;

namespace SmartEvent.WinForms.Comun;

/// <summary>
/// Crea formularios resolviendo sus dependencias del contenedor, PERO SIN QUE
/// EL CONTENEDOR SE QUEDE CON UNA REFERENCIA A ELLOS.
///
/// POR QUE EXISTE ESTA CLASE (defecto real detectado al ejecutar la aplicacion):
///
/// Al principio los formularios se registraban con AddTransient y se pedian con
/// GetRequiredService. El problema es que el contenedor de Microsoft RASTREA
/// todos los servicios transitorios que implementan IDisposable para liberarlos
/// cuando el propio contenedor se libera. Un Form implementa IDisposable, asi
/// que ocurrian dos cosas malas:
///
///   1. FUGA DE MEMORIA: el contenedor guardaba una referencia a CADA formulario
///      abierto durante toda la sesion. Abrir y cerrar la consulta de reservas
///      cincuenta veces dejaba cincuenta formularios vivos en memoria.
///
///   2. DOBLE LIBERACION: Windows Forms libera el formulario al cerrarlo, y al
///      salir de la aplicacion el contenedor lo volvia a liberar. La segunda
///      llamada reventaba con ObjectDisposedException al intentar cancelar un
///      CancellationTokenSource ya liberado.
///
/// ActivatorUtilities.CreateInstance resuelve las dependencias exactamente
/// igual, pero devuelve un objeto que el contenedor NO rastrea: su ciclo de
/// vida queda en manos de Windows Forms, que es quien sabe cuando se cierra una
/// ventana.
///
/// Los formularios se siguen REGISTRANDO en el contenedor aunque no se resuelvan
/// desde el, porque asi ValidateOnBuild comprueba al arrancar que todas sus
/// dependencias existen. Es una verificacion gratuita que evita descubrir un
/// registro olvidado en mitad de la demostracion.
/// </summary>
internal sealed class FabricaFormularios
{
    private readonly IServiceProvider _servicios;

    public FabricaFormularios(IServiceProvider servicios) =>
        _servicios = servicios ?? throw new ArgumentNullException(nameof(servicios));

    /// <summary>Crea un formulario con sus dependencias inyectadas por constructor.</summary>
    public T Crear<T>() where T : Form => ActivatorUtilities.CreateInstance<T>(_servicios);
}
