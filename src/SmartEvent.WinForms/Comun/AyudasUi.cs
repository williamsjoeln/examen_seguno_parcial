using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Excepciones;

namespace SmartEvent.WinForms.Comun;

/// <summary>
/// Utilidades compartidas por los formularios: dialogos, colores de estado y,
/// sobre todo, la ejecucion segura de operaciones asincronicas.
/// </summary>
internal static class AyudasUi
{
    public const string TituloAplicacion = "SmartEvent AI";

    // ===================== DIALOGOS =====================

    public static void MostrarError(string mensaje) =>
        MessageBox.Show(mensaje, TituloAplicacion, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void MostrarAviso(string mensaje) =>
        MessageBox.Show(mensaje, TituloAplicacion, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void MostrarInformacion(string mensaje) =>
        MessageBox.Show(mensaje, TituloAplicacion, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static bool Confirmar(string mensaje) =>
        MessageBox.Show(mensaje, TituloAplicacion, MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            == DialogResult.Yes;

    // ===================== EJECUCION SEGURA =====================

    /// <summary>
    /// Ejecuta una operacion asincronica mostrando el cursor de espera y
    /// traduciendo cualquier excepcion a un mensaje apto para el usuario.
    ///
    /// ES LA PIEZA CENTRAL DEL MANEJO DE ERRORES EN LA INTERFAZ, y aplica el
    /// criterio de la regla D25 del examen:
    ///
    ///   ExcepcionNegocio        -> el mensaje se muestra tal cual. Es texto
    ///                              escrito por nosotros, en los procedimientos
    ///                              almacenados o en los servicios.
    ///   OperationCanceledException -> el usuario cancelo; no es un error.
    ///   cualquier otra          -> mensaje generico y el detalle SOLO al log.
    ///
    /// Devuelve true si la operacion termino correctamente.
    /// </summary>
    public static async Task<bool> EjecutarAsync(
        Control control,
        IRegistradorSeguro registro,
        Func<Task> operacion,
        string mensajeErrorGenerico = "No se pudo completar la operacion.")
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(operacion);

        var cursorAnterior = control.Cursor;
        control.Cursor = Cursors.WaitCursor;

        try
        {
            await operacion().ConfigureAwait(true);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Cancelacion pedida por el usuario: no es un error que reportar.
            return false;
        }
        catch (ExcepcionNegocio ex)
        {
            MostrarAviso(ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            registro.Error(mensajeErrorGenerico, ex);

            MostrarError(
                mensajeErrorGenerico + Environment.NewLine + Environment.NewLine
                + "El detalle tecnico se guardo en el archivo de registro.");
            return false;
        }
        finally
        {
            // Se comprueba IsDisposed porque el formulario puede haberse
            // cerrado mientras la operacion estaba en curso.
            if (!control.IsDisposed)
            {
                control.Cursor = cursorAnterior;
            }
        }
    }

    /// <summary>Version que devuelve un valor. Devuelve default si hubo error o cancelacion.</summary>
    public static async Task<T?> EjecutarAsync<T>(
        Control control,
        IRegistradorSeguro registro,
        Func<Task<T>> operacion,
        string mensajeErrorGenerico = "No se pudo completar la operacion.")
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(registro);
        ArgumentNullException.ThrowIfNull(operacion);

        var cursorAnterior = control.Cursor;
        control.Cursor = Cursors.WaitCursor;

        try
        {
            return await operacion().ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return default;
        }
        catch (ExcepcionNegocio ex)
        {
            MostrarAviso(ex.Message);
            return default;
        }
        catch (Exception ex)
        {
            registro.Error(mensajeErrorGenerico, ex);

            MostrarError(
                mensajeErrorGenerico + Environment.NewLine + Environment.NewLine
                + "El detalle tecnico se guardo en el archivo de registro.");
            return default;
        }
        finally
        {
            if (!control.IsDisposed)
            {
                control.Cursor = cursorAnterior;
            }
        }
    }

    // ===================== LIBERACION DE RECURSOS =====================

    /// <summary>
    /// Cancela y libera un CancellationTokenSource dejando la referencia en
    /// null, de modo que llamar dos veces sea inofensivo.
    ///
    /// POR QUE HACE FALTA: Dispose(bool) de un formulario puede ejecutarse mas
    /// de una vez, y llamar a Cancel() sobre un origen ya liberado lanza
    /// ObjectDisposedException. Ese fallo aparecio de verdad al cerrar sesion
    /// con la ventana de auditoria abierta, y la traza quedo registrada en el
    /// archivo de log. Concentrar aqui la liberacion evita repetir el mismo
    /// error en cada formulario.
    /// </summary>
    public static void Liberar(ref CancellationTokenSource? origen)
    {
        var actual = origen;
        origen = null;

        if (actual is null)
        {
            return;
        }

        try
        {
            actual.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ya estaba liberado: no hay nada que cancelar.
        }
        finally
        {
            actual.Dispose();
        }
    }

    /// <summary>
    /// Cancela un CancellationTokenSource sin lanzar si ya estaba liberado.
    ///
    /// Se usa en los eventos de cierre de formulario y en el boton de cancelar
    /// analisis, donde el origen puede haberse liberado antes de que llegue el
    /// evento. Cancel() sobre un origen liberado lanza ObjectDisposedException.
    /// </summary>
    public static void CancelarSeguro(CancellationTokenSource? origen)
    {
        if (origen is null)
        {
            return;
        }

        try
        {
            origen.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ya estaba liberado: no queda nada por cancelar.
        }
    }

    // ===================== PRESENTACION DE ESTADOS =====================

    /// <summary>
    /// Color de fondo de cada estado, para que en la grilla se distingan de un
    /// vistazo (requisito "estados visualmente identificables" del examen).
    /// </summary>
    public static Color ColorFondoEstado(EstadoReserva estado) => estado switch
    {
        EstadoReserva.Borrador   => Color.FromArgb(255, 243, 205),
        EstadoReserva.Confirmada => Color.FromArgb(209, 240, 220),
        EstadoReserva.Finalizada => Color.FromArgb(222, 226, 230),
        EstadoReserva.Cancelada  => Color.FromArgb(248, 215, 218),
        _                        => SystemColors.Window
    };

    public static Color ColorTextoEstado(EstadoReserva estado) => estado switch
    {
        EstadoReserva.Borrador   => Color.FromArgb(133, 100, 4),
        EstadoReserva.Confirmada => Color.FromArgb(21, 87, 36),
        EstadoReserva.Finalizada => Color.FromArgb(73, 80, 87),
        EstadoReserva.Cancelada  => Color.FromArgb(114, 28, 36),
        _                        => SystemColors.WindowText
    };

    public static Color ColorNivelRiesgo(NivelRiesgo nivel) => nivel switch
    {
        NivelRiesgo.Bajo  => Color.FromArgb(21, 87, 36),
        NivelRiesgo.Medio => Color.FromArgb(133, 100, 4),
        NivelRiesgo.Alto  => Color.FromArgb(114, 28, 36),
        _                 => SystemColors.WindowText
    };

    // ===================== ESTILOS =====================

    /// <summary>Paleta de la aplicacion, para que todos los formularios se vean iguales.</summary>
    public static class Paleta
    {
        public static readonly Color Primario = Color.FromArgb(13, 59, 102);
        public static readonly Color PrimarioClaro = Color.FromArgb(24, 92, 156);
        public static readonly Color Fondo = Color.FromArgb(244, 245, 247);
        public static readonly Color Borde = Color.FromArgb(206, 212, 218);
        public static readonly Color TextoSuave = Color.FromArgb(108, 117, 125);
        public static readonly Color Peligro = Color.FromArgb(176, 42, 55);
        public static readonly Color Exito = Color.FromArgb(27, 127, 79);
    }

    /// <summary>Aplica el aspecto estandar a un boton principal.</summary>
    public static Button EstiloPrimario(this Button boton)
    {
        ArgumentNullException.ThrowIfNull(boton);

        boton.FlatStyle = FlatStyle.Flat;
        boton.FlatAppearance.BorderSize = 0;
        boton.BackColor = Paleta.Primario;
        boton.ForeColor = Color.White;
        boton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        boton.Cursor = Cursors.Hand;
        boton.Height = 34;
        return boton;
    }

    /// <summary>Aplica el aspecto estandar a un boton secundario.</summary>
    public static Button EstiloSecundario(this Button boton)
    {
        ArgumentNullException.ThrowIfNull(boton);

        boton.FlatStyle = FlatStyle.Flat;
        boton.FlatAppearance.BorderColor = Paleta.Borde;
        boton.BackColor = Color.White;
        boton.ForeColor = Paleta.Primario;
        boton.Font = new Font("Segoe UI", 9F);
        boton.Cursor = Cursors.Hand;
        boton.Height = 34;
        return boton;
    }

    /// <summary>Aplica el aspecto estandar a un boton de accion destructiva.</summary>
    public static Button EstiloPeligro(this Button boton)
    {
        ArgumentNullException.ThrowIfNull(boton);

        boton.FlatStyle = FlatStyle.Flat;
        boton.FlatAppearance.BorderSize = 0;
        boton.BackColor = Paleta.Peligro;
        boton.ForeColor = Color.White;
        boton.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        boton.Cursor = Cursors.Hand;
        boton.Height = 34;
        return boton;
    }

    /// <summary>Configura una grilla con el aspecto y comportamiento estandar.</summary>
    public static DataGridView EstiloEstandar(this DataGridView grilla, bool soloLectura = true)
    {
        ArgumentNullException.ThrowIfNull(grilla);

        grilla.BackgroundColor = Color.White;
        grilla.BorderStyle = BorderStyle.FixedSingle;
        grilla.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grilla.EnableHeadersVisualStyles = false;
        grilla.ColumnHeadersDefaultCellStyle.BackColor = Paleta.Primario;
        grilla.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
        grilla.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
        grilla.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 4, 6, 4);
        grilla.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grilla.ColumnHeadersHeight = 34;
        grilla.RowTemplate.Height = 28;
        grilla.RowHeadersVisible = false;
        grilla.AllowUserToAddRows = false;
        grilla.AllowUserToDeleteRows = false;
        grilla.AllowUserToResizeRows = false;
        grilla.ReadOnly = soloLectura;
        grilla.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grilla.MultiSelect = false;
        grilla.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grilla.DefaultCellStyle.SelectionBackColor = Paleta.PrimarioClaro;
        grilla.DefaultCellStyle.SelectionForeColor = Color.White;
        grilla.DefaultCellStyle.Padding = new Padding(4, 0, 4, 0);

        return grilla;
    }

    /// <summary>Crea una etiqueta de titulo de seccion.</summary>
    public static Label CrearTitulo(string texto) => new()
    {
        Text = texto,
        Font = new Font("Segoe UI", 12F, FontStyle.Bold),
        ForeColor = Paleta.Primario,
        AutoSize = true
    };

    /// <summary>Crea una etiqueta de campo.</summary>
    public static Label CrearEtiqueta(string texto) => new()
    {
        Text = texto,
        Font = new Font("Segoe UI", 9F),
        ForeColor = Paleta.TextoSuave,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(3, 8, 3, 3)
    };
}
