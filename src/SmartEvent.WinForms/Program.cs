namespace SmartEvent.WinForms;

/// <summary>
/// Punto de entrada de la aplicacion de escritorio.
/// NOTA: en esta fase solo se verifica que el esqueleto de la solucion compile.
/// La raiz de composicion (contenedor de dependencias, configuracion segura y
/// arranque de FrmLogin) se implementa en la fase de presentacion.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        MessageBox.Show(
            "SmartEvent AI - esqueleto de solucion verificado.",
            "SmartEvent AI",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }
}
