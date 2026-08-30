using Microsoft.Extensions.DependencyInjection;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Servicios;
using SmartEvent.Aplicacion.Sesion;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Ventana principal. Es un contenedor MDI, tal como pide el examen
/// ("MDI o un contenedor principal con navegacion por permisos").
///
/// Cumple los cuatro comportamientos exigidos:
///   - menu construido segun los permisos del rol
///   - usuario autenticado visible
///   - cierre de sesion
///   - estado de conectividad
///
/// El menu no se limita a deshabilitar opciones: las que el rol no puede usar
/// NO SE CREAN. Aun asi, los servicios vuelven a comprobar el permiso y SQL
/// Server tambien: son tres capas independientes.
/// </summary>
internal sealed class FrmPrincipal : Form
{
    private readonly IServiceProvider _servicios;
    private readonly SesionUsuario _sesion;
    private readonly ServicioAutenticacion _autenticacion;
    private readonly IRegistradorSeguro _registro;

    private readonly MenuStrip _menu = new();
    private readonly StatusStrip _barraEstado = new();
    private readonly ToolStripStatusLabel _lblUsuario = new();
    private readonly ToolStripStatusLabel _lblConectividad = new();
    private readonly ToolStripStatusLabel _lblRelleno = new();
    private readonly System.Windows.Forms.Timer _temporizadorConectividad = new();

    private CancellationTokenSource? _cancelacion;
    private bool _cerrandoSesion;

    public FrmPrincipal(
        IServiceProvider servicios,
        SesionUsuario sesion,
        ServicioAutenticacion autenticacion,
        IRegistradorSeguro registro)
    {
        _servicios = servicios ?? throw new ArgumentNullException(nameof(servicios));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _autenticacion = autenticacion ?? throw new ArgumentNullException(nameof(autenticacion));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));

        ConstruirInterfaz();
    }

    private void ConstruirInterfaz()
    {
        Text = $"SmartEvent AI  -  {_sesion.Usuario.NombreCompleto} ({_sesion.Usuario.DescripcionRol})";
        WindowState = FormWindowState.Maximized;
        IsMdiContainer = true;
        Font = new Font("Segoe UI", 9F);
        BackColor = AyudasUi.Paleta.Fondo;
        MinimumSize = new Size(1100, 700);

        ConstruirMenu();
        ConstruirBarraEstado();

        // Fondo del area MDI, para que no se vea el gris por defecto.
        foreach (var control in Controls)
        {
            if (control is MdiClient cliente)
            {
                cliente.BackColor = AyudasUi.Paleta.Fondo;
            }
        }

        Shown += FrmPrincipalShown;
        FormClosing += FrmPrincipalFormClosing;
    }

    // ===================== MENU POR PERMISOS =====================

    private void ConstruirMenu()
    {
        _menu.BackColor = Color.White;
        _menu.Font = new Font("Segoe UI", 9.5F);
        _menu.Padding = new Padding(8, 4, 0, 4);
        _menu.Renderer = new ToolStripProfessionalRenderer();

        // ---------- Reservas ----------
        var menuReservas = new ToolStripMenuItem("&Reservas");

        if (_sesion.Tiene(Permiso.GestionarReservas))
        {
            menuReservas.DropDownItems.Add(
                CrearOpcion("&Nueva reserva", Keys.Control | Keys.N, AbrirNuevaReserva));
        }

        if (_sesion.Tiene(Permiso.ConsultarReservas))
        {
            menuReservas.DropDownItems.Add(
                CrearOpcion("&Consultar reservas", Keys.Control | Keys.B, AbrirConsultaReservas));
        }

        if (menuReservas.DropDownItems.Count > 0)
        {
            menuReservas.DropDownItems.Add(new ToolStripSeparator());
            menuReservas.DropDownItems.Add(CrearOpcion("&Cerrar sesion", Keys.None, CerrarSesion));
            menuReservas.DropDownItems.Add(CrearOpcion("&Salir", Keys.Alt | Keys.F4, Close));
            _menu.Items.Add(menuReservas);
        }

        // ---------- Catalogos: solo ADMINISTRADOR ----------
        if (_sesion.Tiene(Permiso.GestionarCatalogos))
        {
            var menuCatalogos = new ToolStripMenuItem("&Catalogos");
            menuCatalogos.DropDownItems.Add(
                CrearOpcion("Clientes, salones y &recursos", Keys.Control | Keys.M, AbrirCatalogos));
            _menu.Items.Add(menuCatalogos);
        }

        // ---------- Auditoria: solo ADMINISTRADOR ----------
        if (_sesion.Tiene(Permiso.VerAuditoriaIntegraciones))
        {
            var menuAuditoria = new ToolStripMenuItem("&Auditoria");
            menuAuditoria.DropDownItems.Add(
                CrearOpcion("&Integraciones: correo y analisis de IA", Keys.Control | Keys.I,
                    AbrirAuditoriaIntegraciones));
            _menu.Items.Add(menuAuditoria);
        }

        // ---------- Ventana ----------
        var menuVentana = new ToolStripMenuItem("&Ventana");
        menuVentana.DropDownItems.Add(CrearOpcion("Organizar en &cascada", Keys.None,
            () => LayoutMdi(MdiLayout.Cascade)));
        menuVentana.DropDownItems.Add(CrearOpcion("Organizar en &mosaico", Keys.None,
            () => LayoutMdi(MdiLayout.TileHorizontal)));
        menuVentana.DropDownItems.Add(new ToolStripSeparator());
        menuVentana.DropDownItems.Add(CrearOpcion("Cerrar &todas", Keys.None, CerrarTodasLasVentanas));
        _menu.Items.Add(menuVentana);

        // ---------- Ayuda ----------
        var menuAyuda = new ToolStripMenuItem("A&yuda");
        menuAyuda.DropDownItems.Add(CrearOpcion("&Acerca de SmartEvent AI", Keys.None, MostrarAcercaDe));
        _menu.Items.Add(menuAyuda);

        MainMenuStrip = _menu;
        Controls.Add(_menu);
    }

    private static ToolStripMenuItem CrearOpcion(string texto, Keys atajo, Action accion)
    {
        var opcion = new ToolStripMenuItem(texto) { ShortcutKeys = atajo };
        opcion.Click += (_, _) => accion();
        return opcion;
    }

    // ===================== BARRA DE ESTADO =====================

    private void ConstruirBarraEstado()
    {
        _barraEstado.BackColor = AyudasUi.Paleta.Primario;
        _barraEstado.SizingGrip = false;

        _lblUsuario.Text = $"Usuario: {_sesion.Usuario.NombreUsuario}  |  Rol: {_sesion.Usuario.DescripcionRol}";
        _lblUsuario.ForeColor = Color.White;
        _lblUsuario.Font = new Font("Segoe UI", 9F, FontStyle.Bold);

        _lblRelleno.Spring = true;
        _lblRelleno.Text = string.Empty;

        _lblConectividad.Text = "Comprobando conexion...";
        _lblConectividad.ForeColor = Color.Gainsboro;

        _barraEstado.Items.Add(_lblUsuario);
        _barraEstado.Items.Add(_lblRelleno);
        _barraEstado.Items.Add(_lblConectividad);

        Controls.Add(_barraEstado);

        // Se comprueba la conectividad al arrancar y cada 30 segundos. Es un
        // requisito explicito del examen: "estado de conectividad".
        _temporizadorConectividad.Interval = 30_000;
        _temporizadorConectividad.Tick += async (_, _) => await ActualizarConectividadAsync();
    }

    private async void FrmPrincipalShown(object? sender, EventArgs e)
    {
        _cancelacion = new CancellationTokenSource();
        await ActualizarConectividadAsync();
        _temporizadorConectividad.Start();

        _registro.Informacion(
            $"Sesion iniciada en la ventana principal por '{_sesion.Usuario.NombreUsuario}'.");
    }

    /// <summary>
    /// Comprueba la conexion con la base de datos sin bloquear la interfaz.
    /// Nunca lanza: si falla, simplemente lo refleja en la barra de estado.
    /// </summary>
    private async Task ActualizarConectividadAsync()
    {
        if (_cancelacion is null || _cancelacion.IsCancellationRequested)
        {
            return;
        }

        try
        {
            var conectado = await _autenticacion.HayConexionAsync(_cancelacion.Token).ConfigureAwait(true);

            if (IsDisposed)
            {
                return;
            }

            _lblConectividad.Text = conectado
                ? "● Base de datos conectada"
                : "● Sin conexion con la base de datos";

            _lblConectividad.ForeColor = conectado
                ? Color.FromArgb(150, 245, 190)
                : Color.FromArgb(255, 190, 190);
        }
        catch (OperationCanceledException)
        {
            // La ventana se esta cerrando.
        }
    }

    // ===================== APERTURA DE FORMULARIOS =====================

    /// <summary>
    /// Abre un formulario MDI reutilizando el que ya este abierto, si lo hay.
    /// Evita que el usuario acumule diez copias de la misma consulta.
    /// </summary>
    private T AbrirMdi<T>() where T : Form
    {
        var existente = MdiChildren.OfType<T>().FirstOrDefault();

        if (existente is not null)
        {
            existente.WindowState = FormWindowState.Normal;
            existente.BringToFront();
            existente.Focus();
            return existente;
        }

        var formulario = _servicios.GetRequiredService<T>();
        formulario.MdiParent = this;
        formulario.Show();
        return formulario;
    }

    private void AbrirCatalogos() => AbrirMdi<FrmCatalogos>();

    private void AbrirConsultaReservas() => AbrirMdi<FrmReservasConsulta>();

    private void AbrirAuditoriaIntegraciones() => AbrirMdi<FrmAuditoriaIntegraciones>();

    private void AbrirNuevaReserva()
    {
        var formulario = _servicios.GetRequiredService<FrmReservaEdicion>();
        formulario.MdiParent = this;
        formulario.PrepararNueva();
        formulario.Show();
    }

    /// <summary>Abre la edicion de una reserva existente. La invoca FrmReservasConsulta.</summary>
    public void AbrirReserva(int idReserva)
    {
        var abierta = MdiChildren.OfType<FrmReservaEdicion>()
                                 .FirstOrDefault(f => f.IdReservaActual == idReserva);

        if (abierta is not null)
        {
            abierta.BringToFront();
            abierta.Focus();
            return;
        }

        var formulario = _servicios.GetRequiredService<FrmReservaEdicion>();
        formulario.MdiParent = this;
        formulario.PrepararEdicion(idReserva);
        formulario.Show();
    }

    private void CerrarTodasLasVentanas()
    {
        foreach (var hijo in MdiChildren.ToArray())
        {
            hijo.Close();
        }
    }

    // ===================== SESION =====================

    private void CerrarSesion()
    {
        if (!AyudasUi.Confirmar("Se cerrara la sesion actual. Se perderan los cambios sin guardar. Continuar?"))
        {
            return;
        }

        _cerrandoSesion = true;
        _autenticacion.CerrarSesion();
        Close();

        // Se reinicia la aplicacion para volver limpio al inicio de sesion.
        Application.Restart();
    }

    private void FrmPrincipalFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_cerrandoSesion
            && e.CloseReason == CloseReason.UserClosing
            && !AyudasUi.Confirmar("Desea salir de SmartEvent AI?"))
        {
            e.Cancel = true;
            return;
        }

        _temporizadorConectividad.Stop();
        _cancelacion?.Cancel();
    }

    private void MostrarAcercaDe() =>
        AyudasUi.MostrarInformacion(
            "SmartEvent AI  v1.0.0" + Environment.NewLine + Environment.NewLine
            + "Sistema de reservas de salones y recursos para eventos corporativos."
            + Environment.NewLine + Environment.NewLine
            + "Examen practico del II parcial, bloque II." + Environment.NewLine
            + "Desarrollo e Implementacion de Aplicaciones de Escritorio." + Environment.NewLine
            + "Instituto Superior Tecnologico Liceo Cristiano." + Environment.NewLine + Environment.NewLine
            + "Estudiante: Williams Joel Navarrete Merino" + Environment.NewLine
            + "Tecnologias: C#, .NET 8, Windows Forms, SQL Server, MailKit y la Responses API de OpenAI."
            + Environment.NewLine + Environment.NewLine
            + "Archivo de registro:" + Environment.NewLine + _registro.ArchivoActual);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancelacion?.Cancel();
            _cancelacion?.Dispose();
            _temporizadorConectividad.Dispose();
        }

        base.Dispose(disposing);
    }
}
