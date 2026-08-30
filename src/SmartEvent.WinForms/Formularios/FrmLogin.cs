using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Servicios;
using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Inicio de sesion.
///
/// Cumple los cuatro comportamientos que exige el examen para este formulario:
///   - autenticacion
///   - bloqueo temporal tras intentos fallidos
///   - mensajes seguros
///   - apertura del menu segun el rol
///
/// Sobre los MENSAJES SEGUROS: el texto que se muestra es siempre el mismo
/// tanto si el usuario no existe, como si esta inactivo, como si la contrasena
/// es incorrecta. Un mensaje distinto por caso permitiria averiguar que cuentas
/// existen, que es el primer paso de un ataque por fuerza bruta.
///
/// El BLOQUEO lo aplica SQL Server, no este formulario: el contador de intentos
/// vive en seg.Usuario y el procedimiento almacenado es quien decide. Cerrar y
/// volver a abrir la aplicacion no reinicia nada.
/// </summary>
internal sealed class FrmLogin : Form
{
    private readonly ServicioAutenticacion _autenticacion;
    private readonly IRegistradorSeguro _registro;

    private readonly TextBox _txtUsuario = new();
    private readonly TextBox _txtContrasena = new();
    private readonly Button _btnIngresar = new();
    private readonly Button _btnSalir = new();
    private readonly Label _lblMensaje = new();
    private readonly CheckBox _chkVerContrasena = new();
    private readonly System.Windows.Forms.Timer _temporizadorBloqueo = new();

    private CancellationTokenSource? _cancelacion;
    private int _segundosRestantes;

    public FrmLogin(ServicioAutenticacion autenticacion, IRegistradorSeguro registro)
    {
        _autenticacion = autenticacion ?? throw new ArgumentNullException(nameof(autenticacion));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));

        ConstruirInterfaz();
    }

    private void ConstruirInterfaz()
    {
        Text = "SmartEvent AI - Iniciar sesion";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 400);
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9F);

        // ---------- Franja superior ----------
        var cabecera = new Panel
        {
            Dock = DockStyle.Top,
            Height = 96,
            BackColor = AyudasUi.Paleta.Primario
        };

        cabecera.Controls.Add(new Label
        {
            Text = "SmartEvent AI",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(28, 20)
        });

        cabecera.Controls.Add(new Label
        {
            Text = "Reservas de salones y recursos para eventos",
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(200, 215, 235),
            AutoSize = true,
            Location = new Point(30, 58)
        });

        Controls.Add(cabecera);

        // ---------- Campos ----------
        var lblUsuario = AyudasUi.CrearEtiqueta("Usuario");
        lblUsuario.Location = new Point(30, 122);
        Controls.Add(lblUsuario);

        _txtUsuario.Location = new Point(30, 142);
        _txtUsuario.Size = new Size(360, 28);
        _txtUsuario.Font = new Font("Segoe UI", 11F);
        _txtUsuario.MaxLength = 50;
        _txtUsuario.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_txtUsuario);

        var lblContrasena = AyudasUi.CrearEtiqueta("Contrasena");
        lblContrasena.Location = new Point(30, 182);
        Controls.Add(lblContrasena);

        _txtContrasena.Location = new Point(30, 202);
        _txtContrasena.Size = new Size(360, 28);
        _txtContrasena.Font = new Font("Segoe UI", 11F);
        _txtContrasena.UseSystemPasswordChar = true;
        _txtContrasena.MaxLength = 128;
        _txtContrasena.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_txtContrasena);

        _chkVerContrasena.Text = "Mostrar contrasena";
        _chkVerContrasena.Location = new Point(30, 238);
        _chkVerContrasena.AutoSize = true;
        _chkVerContrasena.ForeColor = AyudasUi.Paleta.TextoSuave;
        _chkVerContrasena.CheckedChanged += (_, _) =>
            _txtContrasena.UseSystemPasswordChar = !_chkVerContrasena.Checked;
        Controls.Add(_chkVerContrasena);

        // ---------- Mensaje ----------
        _lblMensaje.Location = new Point(30, 266);
        _lblMensaje.Size = new Size(360, 44);
        _lblMensaje.ForeColor = AyudasUi.Paleta.Peligro;
        _lblMensaje.Font = new Font("Segoe UI", 9F);
        _lblMensaje.TextAlign = ContentAlignment.TopLeft;
        Controls.Add(_lblMensaje);

        // ---------- Botones ----------
        _btnIngresar.Text = "Ingresar";
        _btnIngresar.Location = new Point(30, 316);
        _btnIngresar.Size = new Size(230, 38);
        _btnIngresar.EstiloPrimario();
        _btnIngresar.Click += BtnIngresarClick;
        Controls.Add(_btnIngresar);

        _btnSalir.Text = "Salir";
        _btnSalir.Location = new Point(270, 316);
        _btnSalir.Size = new Size(120, 38);
        _btnSalir.EstiloSecundario();
        _btnSalir.Click += (_, _) => Close();
        Controls.Add(_btnSalir);

        AcceptButton = _btnIngresar;
        CancelButton = _btnSalir;

        // ---------- Temporizador del bloqueo ----------
        _temporizadorBloqueo.Interval = 1000;
        _temporizadorBloqueo.Tick += TemporizadorBloqueoTick;

        Shown += (_, _) => _txtUsuario.Focus();
    }

    /// <summary>
    /// Controlador del boton Ingresar.
    ///
    /// Es "async void" porque asi lo exige la firma de un evento de Windows
    /// Forms; es el UNICO caso en el que ese patron es correcto. Toda la
    /// operacion va dentro de AyudasUi.EjecutarAsync, que captura cualquier
    /// excepcion, de modo que nunca puede escaparse una sin controlar.
    /// </summary>
    private async void BtnIngresarClick(object? sender, EventArgs e)
    {
        var usuario = _txtUsuario.Text.Trim();
        var contrasena = _txtContrasena.Text;

        if (string.IsNullOrWhiteSpace(usuario) || string.IsNullOrWhiteSpace(contrasena))
        {
            MostrarMensaje("Ingrese su usuario y su contrasena.");
            return;
        }

        _cancelacion?.Dispose();
        _cancelacion = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        HabilitarControles(false);
        MostrarMensaje(string.Empty);

        var resultado = await AyudasUi.EjecutarAsync(
            this, _registro,
            () => _autenticacion.IniciarSesionAsync(usuario, contrasena, _cancelacion.Token),
            "No se pudo verificar sus credenciales. Compruebe la conexion con la base de datos.");

        HabilitarControles(true);

        if (resultado is null)
        {
            // Hubo un error ya informado por EjecutarAsync, o una cancelacion.
            return;
        }

        if (resultado.Autenticado)
        {
            // La contrasena se limpia en cuanto deja de hacer falta.
            _txtContrasena.Clear();
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        MostrarMensaje(resultado.Mensaje);
        _txtContrasena.Clear();
        _txtContrasena.Focus();

        if (resultado.EstaBloqueado)
        {
            IniciarCuentaAtras(resultado.SegundosBloqueo);
        }
    }

    /// <summary>
    /// Muestra la cuenta atras del bloqueo y deshabilita el boton mientras dure.
    ///
    /// Es solo una ayuda visual: el bloqueo real lo impone SQL Server. Aunque
    /// el usuario cerrara la aplicacion y volviera a abrirla, seguiria
    /// bloqueado hasta que expire BloqueadoHasta.
    /// </summary>
    private void IniciarCuentaAtras(int segundos)
    {
        _segundosRestantes = Math.Max(1, segundos);
        _btnIngresar.Enabled = false;
        _temporizadorBloqueo.Start();
        ActualizarTextoBloqueo();
    }

    private void TemporizadorBloqueoTick(object? sender, EventArgs e)
    {
        _segundosRestantes--;

        if (_segundosRestantes <= 0)
        {
            _temporizadorBloqueo.Stop();
            _btnIngresar.Enabled = true;
            _btnIngresar.Text = "Ingresar";
            MostrarMensaje("Ya puede intentar iniciar sesion nuevamente.");
            return;
        }

        ActualizarTextoBloqueo();
    }

    private void ActualizarTextoBloqueo()
    {
        var minutos = _segundosRestantes / 60;
        var segundos = _segundosRestantes % 60;
        _btnIngresar.Text = $"Bloqueado ({minutos:00}:{segundos:00})";
    }

    private void MostrarMensaje(string mensaje) => _lblMensaje.Text = mensaje;

    private void HabilitarControles(bool habilitado)
    {
        _txtUsuario.Enabled = habilitado;
        _txtContrasena.Enabled = habilitado;
        _chkVerContrasena.Enabled = habilitado;
        _btnIngresar.Enabled = habilitado && !_temporizadorBloqueo.Enabled;
        _btnIngresar.Text = habilitado ? "Ingresar" : "Verificando...";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancelacion?.Cancel();
            _cancelacion?.Dispose();
            _temporizadorBloqueo.Dispose();
        }

        base.Dispose(disposing);
    }
}
