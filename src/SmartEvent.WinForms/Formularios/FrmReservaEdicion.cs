using System.ComponentModel;
using System.Globalization;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Dto;
using SmartEvent.Aplicacion.Servicios;
using SmartEvent.Aplicacion.Sesion;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Reglas;
using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Alta y edicion de una reserva, con su cabecera y su detalle.
///
/// Es el formulario central del examen. Cubre los requisitos F15 a F25:
/// cabecera, busqueda de cliente y salon, fecha y horario, grilla editable de
/// detalles, calculo en tiempo real, validaciones, guardar, analizar con IA,
/// confirmar y cancelar.
///
/// DOS IDEAS QUE CONVIENE SABER EXPLICAR:
///
/// 1. El calculo en tiempo real usa la MISMA clase de dominio
///    (CalculadoraTotales) que replica la aritmetica del procedimiento
///    almacenado. Pero al guardar NO se envia ningun total: el procedimiento
///    los recalcula y lo que persiste es su resultado. Por eso, despues de
///    guardar, el formulario recarga la reserva desde la base de datos y
///    muestra los importes que devolvio SQL Server.
///
/// 2. Una reserva CONFIRMADA bloquea todos los controles de edicion. La
///    interfaz lo hace por comodidad, pero aunque alguien la forzara, el
///    procedimiento rechazaria el guardado con el error 50011.
/// </summary>
internal sealed class FrmReservaEdicion : Form
{
    private readonly ServicioReservas _reservas;
    private readonly ServicioCatalogos _catalogos;
    private readonly IServicioAnalisisIa _ia;
    private readonly SesionUsuario _sesion;
    private readonly IRegistradorSeguro _registro;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-EC");

    // ---------- Estado ----------
    private int _idReserva;
    private EstadoReserva _estado = EstadoReserva.Borrador;
    private string _codigo = string.Empty;
    private List<Cliente> _clientes = [];
    private List<Salon> _salones = [];
    private List<Recurso> _recursos = [];
    private readonly BindingList<FilaDetalle> _detalles = [];
    private CancellationTokenSource? _cancelacion;
    private CancellationTokenSource? _cancelacionIa;
    private bool _cargando;

    /// <summary>
    /// Impide que RecalcularTotales se vuelva a invocar mientras ya se esta
    /// ejecutando. Es la red de seguridad frente a la reentrada que provoco el
    /// fallo descrito en RecalcularTotales.
    /// </summary>
    private bool _recalculando;

    /// <summary>Identificador de la reserva abierta. Lo usa FrmPrincipal para no duplicar ventanas.</summary>
    public int IdReservaActual => _idReserva;

    // ---------- Controles de cabecera ----------
    private readonly ComboBox _cboCliente = new();
    private readonly TextBox _txtBuscarCliente = new();
    private readonly Label _lblAvisoCliente = new();
    private readonly ComboBox _cboSalon = new();
    private readonly Label _lblInfoSalon = new();
    private readonly DateTimePicker _dtpFecha = new();
    private readonly DateTimePicker _dtpHoraInicio = new();
    private readonly DateTimePicker _dtpHoraFin = new();
    private readonly NumericUpDown _numInvitados = new();
    private readonly NumericUpDown _numDescuentoGlobal = new();
    private readonly TextBox _txtObservacion = new();
    private readonly Label _lblEstado = new();
    private readonly Label _lblCodigo = new();
    private readonly Label _lblDuracion = new();

    // ---------- Detalle ----------
    private readonly DataGridView _grilla = new();
    private readonly Button _btnAgregarLinea = new();
    private readonly Button _btnQuitarLinea = new();

    // ---------- Totales ----------
    private readonly Label _lblSubtotal = new();
    private readonly Label _lblDescuento = new();
    private readonly Label _lblImpuesto = new();
    private readonly Label _lblTotal = new();

    // ---------- Acciones ----------
    private readonly Button _btnGuardar = new();
    private readonly Button _btnAnalizarIa = new();
    private readonly Button _btnCancelarIa = new();
    private readonly Button _btnConfirmar = new();
    private readonly Button _btnCancelarReserva = new();
    private readonly Button _btnVerificarDisponibilidad = new();
    private readonly Label _lblEstadoOperacion = new();

    public FrmReservaEdicion(
        ServicioReservas reservas,
        ServicioCatalogos catalogos,
        IServicioAnalisisIa ia,
        SesionUsuario sesion,
        IRegistradorSeguro registro)
    {
        _reservas = reservas ?? throw new ArgumentNullException(nameof(reservas));
        _catalogos = catalogos ?? throw new ArgumentNullException(nameof(catalogos));
        _ia = ia ?? throw new ArgumentNullException(nameof(ia));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));

        ConstruirInterfaz();
    }

    /// <summary>Prepara el formulario para crear una reserva nueva.</summary>
    public void PrepararNueva()
    {
        _idReserva = 0;
        _estado = EstadoReserva.Borrador;
        _codigo = string.Empty;
        Text = "Nueva reserva";
    }

    /// <summary>Prepara el formulario para editar una reserva existente.</summary>
    public void PrepararEdicion(int idReserva)
    {
        _idReserva = idReserva;
        Text = "Reserva";
    }

    // =====================================================================
    // CONSTRUCCION DE LA INTERFAZ
    // =====================================================================

    private void ConstruirInterfaz()
    {
        Text = "Reserva";
        WindowState = FormWindowState.Maximized;
        BackColor = AyudasUi.Paleta.Fondo;
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(900, 660);

        var contenedor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(12),
            BackColor = AyudasUi.Paleta.Fondo
        };

        contenedor.RowStyles.Add(new RowStyle(SizeType.Absolute, 232)); // cabecera
        contenedor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // detalle
        contenedor.RowStyles.Add(new RowStyle(SizeType.Absolute, 116)); // totales
        contenedor.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // acciones

        contenedor.Controls.Add(ConstruirPanelCabecera(), 0, 0);
        contenedor.Controls.Add(ConstruirPanelDetalle(), 0, 1);
        contenedor.Controls.Add(ConstruirPanelTotales(), 0, 2);
        contenedor.Controls.Add(ConstruirPanelAcciones(), 0, 3);

        Controls.Add(contenedor);

        Shown += FrmReservaEdicionShown;
        FormClosing += (_, _) =>
        {
            AyudasUi.CancelarSeguro(_cancelacion);
            AyudasUi.CancelarSeguro(_cancelacionIa);
        };
    }

    /// <summary>
    /// Construye la cabecera de la reserva.
    ///
    /// DISTRIBUCION: todas las posiciones caben dentro de unos 820 pixeles de
    /// ancho, y la observacion se ancla a izquierda y derecha para crecer con la
    /// ventana. La primera version usaba coordenadas de hasta 1100 pixeles y en
    /// una ventana algo mas estrecha se cortaban por la derecha el boton de
    /// verificar disponibilidad y la tarifa del salon. Ese boton se movio a la
    /// barra de acciones inferior, donde siempre hay sitio.
    /// </summary>
    private Panel ConstruirPanelCabecera()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14)
        };

        // ---------- Titulo y estado ----------
        _lblCodigo.Text = "Nueva reserva";
        _lblCodigo.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        _lblCodigo.ForeColor = AyudasUi.Paleta.Primario;
        _lblCodigo.AutoSize = true;
        _lblCodigo.Location = new Point(14, 10);
        panel.Controls.Add(_lblCodigo);

        _lblEstado.Text = "BORRADOR";
        _lblEstado.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _lblEstado.AutoSize = false;
        _lblEstado.Size = new Size(130, 26);
        _lblEstado.TextAlign = ContentAlignment.MiddleCenter;
        _lblEstado.Location = new Point(300, 12);
        panel.Controls.Add(_lblEstado);

        // ================= FILA 1: cliente y salon =================

        panel.Controls.Add(CrearEtiqueta("Buscar cliente (nombre o identificacion)", 14, 48));

        _txtBuscarCliente.Location = new Point(14, 66);
        _txtBuscarCliente.Size = new Size(180, 26);
        _txtBuscarCliente.BorderStyle = BorderStyle.FixedSingle;
        _txtBuscarCliente.PlaceholderText = "Escriba para filtrar...";
        _txtBuscarCliente.TextChanged += (_, _) => FiltrarClientes();
        panel.Controls.Add(_txtBuscarCliente);

        panel.Controls.Add(CrearEtiqueta("Cliente *", 200, 48));

        _cboCliente.Location = new Point(200, 66);
        _cboCliente.Size = new Size(330, 26);
        _cboCliente.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboCliente.FlatStyle = FlatStyle.Flat;
        _cboCliente.DisplayMember = nameof(Cliente.Descripcion);
        _cboCliente.ValueMember = nameof(Cliente.IdCliente);
        panel.Controls.Add(_cboCliente);

        // Aviso cuando el filtro no encuentra a nadie. Sin esto, la lista se
        // quedaba vacia en silencio y el usuario no entendia por que el
        // formulario decia "seleccione un cliente".
        _lblAvisoCliente.Location = new Point(14, 94);
        _lblAvisoCliente.Size = new Size(516, 18);
        _lblAvisoCliente.Font = new Font("Segoe UI", 8F);
        _lblAvisoCliente.ForeColor = AyudasUi.Paleta.Peligro;
        panel.Controls.Add(_lblAvisoCliente);

        panel.Controls.Add(CrearEtiqueta("Salon *", 545, 48));

        _cboSalon.Location = new Point(545, 66);
        _cboSalon.Size = new Size(280, 26);
        _cboSalon.DropDownStyle = ComboBoxStyle.DropDownList;
        _cboSalon.FlatStyle = FlatStyle.Flat;
        _cboSalon.DisplayMember = nameof(Salon.Descripcion);
        _cboSalon.ValueMember = nameof(Salon.IdSalon);
        _cboSalon.SelectedIndexChanged += (_, _) => { ActualizarInfoSalon(); RecalcularTotales(); };
        panel.Controls.Add(_cboSalon);

        _lblInfoSalon.Location = new Point(545, 94);
        _lblInfoSalon.Size = new Size(300, 18);
        _lblInfoSalon.ForeColor = AyudasUi.Paleta.TextoSuave;
        _lblInfoSalon.Font = new Font("Segoe UI", 8F);
        panel.Controls.Add(_lblInfoSalon);

        // ================= FILA 2: fecha, horario, invitados =================

        panel.Controls.Add(CrearEtiqueta("Fecha del evento *", 14, 120));

        _dtpFecha.Location = new Point(14, 138);
        _dtpFecha.Size = new Size(180, 26);
        _dtpFecha.Format = DateTimePickerFormat.Short;
        _dtpFecha.MinDate = DateTime.Today;
        _dtpFecha.Value = DateTime.Today.AddDays(7);
        panel.Controls.Add(_dtpFecha);

        panel.Controls.Add(CrearEtiqueta("Hora inicio *", 200, 120));

        ConfigurarHora(_dtpHoraInicio, new TimeSpan(9, 0, 0));
        _dtpHoraInicio.Location = new Point(200, 138);
        _dtpHoraInicio.Size = new Size(95, 26);
        _dtpHoraInicio.ValueChanged += (_, _) => ActualizarDuracion();
        panel.Controls.Add(_dtpHoraInicio);

        panel.Controls.Add(CrearEtiqueta("Hora fin *", 301, 120));

        ConfigurarHora(_dtpHoraFin, new TimeSpan(13, 0, 0));
        _dtpHoraFin.Location = new Point(301, 138);
        _dtpHoraFin.Size = new Size(95, 26);
        _dtpHoraFin.ValueChanged += (_, _) => ActualizarDuracion();
        panel.Controls.Add(_dtpHoraFin);

        _lblDuracion.Location = new Point(404, 142);
        _lblDuracion.Size = new Size(240, 20);
        _lblDuracion.Font = new Font("Segoe UI", 8.5F);
        panel.Controls.Add(_lblDuracion);

        panel.Controls.Add(CrearEtiqueta("Invitados *", 650, 120));

        _numInvitados.Location = new Point(650, 138);
        _numInvitados.Size = new Size(85, 26);
        _numInvitados.Minimum = 1;
        _numInvitados.Maximum = 100_000;
        _numInvitados.Value = 50;
        _numInvitados.BorderStyle = BorderStyle.FixedSingle;
        _numInvitados.TextAlign = HorizontalAlignment.Right;
        _numInvitados.ValueChanged += (_, _) => ActualizarInfoSalon();
        panel.Controls.Add(_numInvitados);

        panel.Controls.Add(CrearEtiqueta("Desc. global %", 741, 120));

        _numDescuentoGlobal.Location = new Point(741, 138);
        _numDescuentoGlobal.Size = new Size(84, 26);
        _numDescuentoGlobal.Minimum = 0;
        _numDescuentoGlobal.Maximum = ReglasReserva.DescuentoMaximoPorcentaje;
        _numDescuentoGlobal.DecimalPlaces = 2;
        _numDescuentoGlobal.BorderStyle = BorderStyle.FixedSingle;
        _numDescuentoGlobal.TextAlign = HorizontalAlignment.Right;
        _numDescuentoGlobal.ValueChanged += (_, _) => { AvisarDescuentoAlto(); RecalcularTotales(); };
        panel.Controls.Add(_numDescuentoGlobal);

        // ================= FILA 3: observacion =================

        panel.Controls.Add(CrearEtiqueta("Observacion", 14, 170));

        _txtObservacion.Location = new Point(14, 188);
        _txtObservacion.Size = new Size(811, 26);
        _txtObservacion.MaxLength = 500;
        _txtObservacion.BorderStyle = BorderStyle.FixedSingle;
        // Crece con la ventana en lugar de quedarse corta o desbordarse.
        _txtObservacion.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_txtObservacion);

        return panel;
    }

    private Panel ConstruirPanelDetalle()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14),
            Margin = new Padding(0, 10, 0, 10)
        };

        var barra = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Color.White };

        barra.Controls.Add(new Label
        {
            Text = "Recursos y servicios",
            Font = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = AyudasUi.Paleta.Primario,
            AutoSize = true,
            Location = new Point(0, 8)
        });

        _btnAgregarLinea.Text = "Agregar linea";
        _btnAgregarLinea.Location = new Point(240, 4);
        _btnAgregarLinea.Size = new Size(140, 32);
        _btnAgregarLinea.EstiloSecundario();
        _btnAgregarLinea.Click += (_, _) => AgregarLinea();
        barra.Controls.Add(_btnAgregarLinea);

        _btnQuitarLinea.Text = "Quitar linea";
        _btnQuitarLinea.Location = new Point(388, 4);
        _btnQuitarLinea.Size = new Size(130, 32);
        _btnQuitarLinea.EstiloSecundario();
        _btnQuitarLinea.Click += (_, _) => QuitarLinea();
        barra.Controls.Add(_btnQuitarLinea);

        barra.Controls.Add(new Label
        {
            Text = "Un recurso no puede repetirse. El subtotal se calcula solo.",
            ForeColor = AyudasUi.Paleta.TextoSuave,
            Font = new Font("Segoe UI", 8.5F),
            AutoSize = true,
            Location = new Point(530, 14)
        });

        ConstruirGrilla();

        panel.Controls.Add(_grilla);
        panel.Controls.Add(barra);

        return panel;
    }

    /// <summary>
    /// Configura la grilla editable del detalle.
    ///
    /// El recurso se elige en una lista desplegable dentro de la celda, para que
    /// no se pueda escribir un identificador inexistente. Cantidad, precio y
    /// descuento son editables; el subtotal NO lo es: se calcula.
    /// </summary>
    private void ConstruirGrilla()
    {
        _grilla.Dock = DockStyle.Fill;
        _grilla.EstiloEstandar(soloLectura: false);
        _grilla.AutoGenerateColumns = false;
        _grilla.EditMode = DataGridViewEditMode.EditOnEnter;
        _grilla.AllowUserToAddRows = false;

        var columnaRecurso = new DataGridViewComboBoxColumn
        {
            Name = "IdRecurso",
            HeaderText = "Recurso",
            DataPropertyName = nameof(FilaDetalle.IdRecurso),
            DisplayMember = nameof(Recurso.Descripcion),
            ValueMember = nameof(Recurso.IdRecurso),
            FlatStyle = FlatStyle.Flat,
            FillWeight = 150,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.Nothing
        };
        _grilla.Columns.Add(columnaRecurso);

        _grilla.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Cantidad",
            HeaderText = "Cantidad",
            DataPropertyName = nameof(FilaDetalle.Cantidad),
            FillWeight = 50,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });

        _grilla.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PrecioUnitario",
            HeaderText = "Precio unitario",
            DataPropertyName = nameof(FilaDetalle.PrecioUnitario),
            FillWeight = 60,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "N2"
            }
        });

        _grilla.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PorcentajeDescuento",
            HeaderText = "Descuento %",
            DataPropertyName = nameof(FilaDetalle.PorcentajeDescuento),
            FillWeight = 50,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "N2"
            }
        });

        _grilla.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "SubtotalLinea",
            HeaderText = "Subtotal",
            DataPropertyName = nameof(FilaDetalle.SubtotalLinea),
            ReadOnly = true,
            FillWeight = 60,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Alignment = DataGridViewContentAlignment.MiddleRight,
                Format = "N2",
                BackColor = Color.FromArgb(248, 249, 250),
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            }
        });

        _grilla.DataSource = _detalles;

        // El recalculo en tiempo real se dispara al terminar de editar una celda
        // y tambien al cambiar la lista completa.
        _grilla.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0 && !_cargando)
            {
                RecalcularTotales();
            }
        };

        // Sin esto, un cambio en la lista desplegable no se confirma hasta que
        // la celda pierde el foco, y el total se veria desfasado.
        _grilla.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_grilla.IsCurrentCellDirty)
            {
                _grilla.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };

        // Impide que un texto invalido en una celda numerica lance una
        // excepcion sin controlar en medio de la edicion.
        _grilla.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
        };

        _detalles.ListChanged += (_, _) =>
        {
            if (!_cargando)
            {
                RecalcularTotales();
            }
        };
    }

    private Panel ConstruirPanelTotales()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(14)
        };

        panel.Controls.Add(new Label
        {
            Text = "Totales calculados en tiempo real. Al guardar, SQL Server los recalcula y su resultado es el definitivo.",
            ForeColor = AyudasUi.Paleta.TextoSuave,
            Font = new Font("Segoe UI", 8.5F, FontStyle.Italic),
            AutoSize = true,
            Location = new Point(14, 12)
        });

        ConfigurarEtiquetaTotal(panel, "Subtotal", _lblSubtotal, 14, 44, false);
        ConfigurarEtiquetaTotal(panel, "Descuento", _lblDescuento, 234, 44, false);
        ConfigurarEtiquetaTotal(panel, "Impuesto 15%", _lblImpuesto, 454, 44, false);
        ConfigurarEtiquetaTotal(panel, "TOTAL", _lblTotal, 674, 44, true);

        return panel;
    }

    private static void ConfigurarEtiquetaTotal(
        Panel panel, string titulo, Label etiqueta, int x, int y, bool destacado)
    {
        panel.Controls.Add(new Label
        {
            Text = titulo,
            Location = new Point(x, y),
            AutoSize = true,
            ForeColor = AyudasUi.Paleta.TextoSuave,
            Font = new Font("Segoe UI", 8.5F)
        });

        etiqueta.Text = "0,00";
        etiqueta.Location = new Point(x, y + 18);
        etiqueta.Size = new Size(200, 30);
        etiqueta.Font = new Font("Segoe UI", destacado ? 16F : 13F,
            destacado ? FontStyle.Bold : FontStyle.Regular);
        etiqueta.ForeColor = destacado ? AyudasUi.Paleta.Primario : Color.Black;
        panel.Controls.Add(etiqueta);
    }

    private Panel ConstruirPanelAcciones()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = AyudasUi.Paleta.Fondo };

        _btnGuardar.Text = "Guardar";
        _btnGuardar.Location = new Point(0, 12);
        _btnGuardar.Size = new Size(140, 38);
        _btnGuardar.EstiloPrimario();
        _btnGuardar.Click += async (_, _) => await GuardarAsync();
        panel.Controls.Add(_btnGuardar);

        _btnAnalizarIa.Text = "Analizar con IA";
        _btnAnalizarIa.Location = new Point(150, 12);
        _btnAnalizarIa.Size = new Size(160, 38);
        _btnAnalizarIa.EstiloSecundario();
        _btnAnalizarIa.Click += async (_, _) => await AnalizarConIaAsync();
        panel.Controls.Add(_btnAnalizarIa);

        _btnCancelarIa.Text = "Cancelar analisis";
        _btnCancelarIa.Location = new Point(316, 12);
        _btnCancelarIa.Size = new Size(150, 38);
        _btnCancelarIa.EstiloSecundario();
        _btnCancelarIa.Visible = false;
        _btnCancelarIa.Click += (_, _) => AyudasUi.CancelarSeguro(_cancelacionIa);
        panel.Controls.Add(_btnCancelarIa);

        _btnConfirmar.Text = "Confirmar reserva";
        _btnConfirmar.Location = new Point(320, 12);
        _btnConfirmar.Size = new Size(170, 38);
        _btnConfirmar.EstiloPrimario();
        _btnConfirmar.BackColor = AyudasUi.Paleta.Exito;
        _btnConfirmar.Click += async (_, _) => await ConfirmarAsync();
        panel.Controls.Add(_btnConfirmar);

        _btnCancelarReserva.Text = "Cancelar reserva";
        _btnCancelarReserva.Location = new Point(500, 12);
        _btnCancelarReserva.Size = new Size(160, 38);
        _btnCancelarReserva.EstiloPeligro();
        _btnCancelarReserva.Click += async (_, _) => await CancelarReservaAsync();
        panel.Controls.Add(_btnCancelarReserva);

        // Se coloca aqui, y no en la cabecera, porque en la cabecera quedaba
        // fuera del area visible cuando la ventana no era muy ancha.
        _btnVerificarDisponibilidad.Text = "Verificar disponibilidad";
        _btnVerificarDisponibilidad.Location = new Point(670, 12);
        _btnVerificarDisponibilidad.Size = new Size(190, 38);
        _btnVerificarDisponibilidad.EstiloSecundario();
        _btnVerificarDisponibilidad.Click += async (_, _) => await VerificarDisponibilidadAsync();
        panel.Controls.Add(_btnVerificarDisponibilidad);

        _lblEstadoOperacion.Location = new Point(870, 22);
        _lblEstadoOperacion.Size = new Size(420, 22);
        _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.TextoSuave;
        _lblEstadoOperacion.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        panel.Controls.Add(_lblEstadoOperacion);

        return panel;
    }

    private static Label CrearEtiqueta(string texto, int x, int y) => new()
    {
        Text = texto,
        Location = new Point(x, y),
        AutoSize = true,
        ForeColor = AyudasUi.Paleta.TextoSuave,
        Font = new Font("Segoe UI", 8.5F)
    };

    private static void ConfigurarHora(DateTimePicker selector, TimeSpan valorInicial)
    {
        selector.Format = DateTimePickerFormat.Custom;
        selector.CustomFormat = "HH:mm";
        selector.ShowUpDown = true;
        selector.Value = DateTime.Today.Add(valorInicial);
    }

    // =====================================================================
    // CARGA INICIAL
    // =====================================================================

    private async void FrmReservaEdicionShown(object? sender, EventArgs e)
    {
        _cancelacion = new CancellationTokenSource();

        await AyudasUi.EjecutarAsync(this, _registro, async () =>
        {
            _clientes = (await _catalogos.ConsultarClientesAsync(null, true, _cancelacion.Token)).ToList();
            _salones = (await _catalogos.ConsultarSalonesAsync(null, true, _cancelacion.Token)).ToList();
            _recursos = (await _catalogos.ConsultarRecursosAsync(null, true, _cancelacion.Token)).ToList();
        }, "No se pudieron cargar los catalogos.");

        _cboCliente.DataSource = _clientes;
        _cboSalon.DataSource = _salones;

        // La lista desplegable de la grilla se alimenta con los recursos activos.
        ((DataGridViewComboBoxColumn)_grilla.Columns["IdRecurso"]!).DataSource = _recursos;

        if (_idReserva > 0)
        {
            await CargarReservaAsync();
        }
        else
        {
            AgregarLinea();
            ActualizarInfoSalon();
            ActualizarDuracion();
            AplicarEstadoAControles();
        }
    }

    private async Task CargarReservaAsync()
    {
        if (_cancelacion is null) { return; }

        var reserva = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ObtenerAsync(_idReserva, _cancelacion.Token),
            "No se pudo cargar la reserva.");

        if (reserva is null)
        {
            AyudasUi.MostrarAviso("La reserva solicitada ya no existe.");
            Close();
            return;
        }

        _cargando = true;

        try
        {
            _estado = reserva.Estado;
            _codigo = reserva.Codigo;

            _cboCliente.SelectedValue = reserva.IdCliente;
            _cboSalon.SelectedValue = reserva.IdSalon;
            // Al editar una reserva ya existente hay que poder mostrar su fecha
            // aunque sea anterior a hoy. Se usa DateTimePicker.MinimumDateTime
            // (1/1/1753) y NO DateTime.MinValue (1/1/0001): el control rechaza
            // cualquier valor anterior a esa fecha con ArgumentOutOfRangeException.
            _dtpFecha.MinDate = DateTimePicker.MinimumDateTime;
            _dtpFecha.Value = reserva.FechaEvento.ToDateTime(TimeOnly.MinValue);
            _dtpHoraInicio.Value = DateTime.Today.Add(reserva.HoraInicio);
            _dtpHoraFin.Value = DateTime.Today.Add(reserva.HoraFin);
            _numInvitados.Value = reserva.NumeroInvitados;
            _numDescuentoGlobal.Value = reserva.PorcentajeDescuentoGlobal;
            _txtObservacion.Text = reserva.Observacion ?? string.Empty;

            _detalles.Clear();

            foreach (var detalle in reserva.Detalles)
            {
                var fila = new FilaDetalle
                {
                    IdRecurso = detalle.IdRecurso,
                    Cantidad = detalle.Cantidad,
                    PrecioUnitario = detalle.PrecioUnitario,
                    PorcentajeDescuento = detalle.PorcentajeDescuento
                };

                // La fila calcula su propio subtotal con la misma formula que
                // SQL Server, de modo que coincide con el valor persistido.
                fila.ActualizarSubtotal();

                _detalles.Add(fila);
            }
        }
        finally
        {
            _cargando = false;
        }

        Text = $"Reserva {reserva.Codigo}";
        _lblCodigo.Text = reserva.Codigo;

        // Se muestran los importes PERSISTIDOS, que son los que calculo SQL.
        MostrarTotales(reserva.Subtotal, reserva.Descuento, reserva.Impuesto, reserva.Total);

        ActualizarInfoSalon();
        ActualizarDuracion();
        AplicarEstadoAControles();
    }

    // =====================================================================
    // CALCULO EN TIEMPO REAL
    // =====================================================================

    /// <summary>
    /// Recalcula los totales de la CABECERA con la misma aritmetica que
    /// evt.sp_Reserva_Guardar. Se invoca en cada cambio de la grilla, del salon
    /// o del descuento global.
    ///
    /// ESTE METODO ES DE SOLO LECTURA SOBRE LAS FILAS, Y ESO ES DELIBERADO.
    ///
    /// En la primera version recorria el detalle asignando el subtotal de cada
    /// linea. Al ejecutar la aplicacion aparecio este fallo real:
    ///
    ///   AgregarLinea -> BindingList.ListChanged -> RecalcularTotales
    ///     -> fila.SubtotalLinea = ... -> PropertyChanged -> ListChanged otra vez
    ///     -> DataGridView.InvalidateCell(fila 0) cuando la grilla aun tiene 0 filas
    ///     -> ArgumentOutOfRangeException: rowIndex ('0') must be less than '0'
    ///
    /// Es decir, se modificaban los elementos de la lista DESDE DENTRO del
    /// evento que notifica que la lista cambio, y la grilla todavia no habia
    /// terminado de procesar el cambio anterior. La solucion correcta no es
    /// silenciar la excepcion, sino quitar la reentrada: cada FilaDetalle
    /// calcula su propio subtotal cuando cambian su cantidad, su precio o su
    /// descuento, y este metodo se limita a sumar y mostrar.
    /// </summary>
    private void RecalcularTotales()
    {
        if (_cargando || _recalculando) { return; }

        _recalculando = true;

        try
        {
            var salon = _cboSalon.SelectedItem as Salon;
            var tarifaBase = salon?.TarifaBase ?? 0m;

            var lineas = _detalles.Select(f =>
                new LineaCalculo(f.Cantidad, f.PrecioUnitario, f.PorcentajeDescuento));

            var totales = CalculadoraTotales.Calcular(tarifaBase, lineas, _numDescuentoGlobal.Value);

            MostrarTotales(totales.Subtotal, totales.Descuento, totales.Impuesto, totales.Total);
        }
        finally
        {
            _recalculando = false;
        }
    }

    private void MostrarTotales(decimal subtotal, decimal descuento, decimal impuesto, decimal total)
    {
        _lblSubtotal.Text = subtotal.ToString("C2", Cultura);
        _lblDescuento.Text = descuento > 0 ? "-" + descuento.ToString("C2", Cultura) : descuento.ToString("C2", Cultura);
        _lblImpuesto.Text = impuesto.ToString("C2", Cultura);
        _lblTotal.Text = total.ToString("C2", Cultura);
    }

    private void ActualizarInfoSalon()
    {
        if (_cboSalon.SelectedItem is not Salon salon)
        {
            _lblInfoSalon.Text = string.Empty;
            return;
        }

        var invitados = (int)_numInvitados.Value;
        var excede = invitados > salon.Capacidad;

        _lblInfoSalon.Text =
            $"Capacidad {salon.Capacidad} · Tarifa {salon.TarifaBase.ToString("C2", Cultura)}"
            + (excede ? $"  ⚠ Excede en {invitados - salon.Capacidad}" : string.Empty);

        _lblInfoSalon.ForeColor = excede ? AyudasUi.Paleta.Peligro : AyudasUi.Paleta.TextoSuave;
    }

    private void ActualizarDuracion()
    {
        var inicio = _dtpHoraInicio.Value.TimeOfDay;
        var fin = _dtpHoraFin.Value.TimeOfDay;

        if (fin <= inicio)
        {
            _lblDuracion.Text = "⚠ La hora de fin debe ser posterior";
            _lblDuracion.ForeColor = AyudasUi.Paleta.Peligro;
            return;
        }

        var horas = (fin - inicio).TotalHours;
        var valida = ReglasReserva.DuracionEsValida(inicio, fin);

        _lblDuracion.Text = valida
            ? $"Duracion: {horas:0.#} horas"
            : $"⚠ Duracion {horas:0.#} h (debe estar entre {ReglasReserva.DuracionMinimaHoras} y {ReglasReserva.DuracionMaximaHoras})";

        _lblDuracion.ForeColor = valida ? AyudasUi.Paleta.Exito : AyudasUi.Paleta.Peligro;
    }

    private void AvisarDescuentoAlto()
    {
        if (_numDescuentoGlobal.Value > ReglasReserva.DescuentoSinPrivilegioPorcentaje
            && !_sesion.EsAdministrador)
        {
            _lblEstadoOperacion.Text =
                $"⚠ Un descuento superior al {ReglasReserva.DescuentoSinPrivilegioPorcentaje}% requiere rol ADMINISTRADOR.";
            _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.Peligro;
        }
        else
        {
            _lblEstadoOperacion.Text = string.Empty;
        }
    }

    // =====================================================================
    // GRILLA
    // =====================================================================

    private void AgregarLinea()
    {
        if (_recursos.Count == 0)
        {
            AyudasUi.MostrarAviso("No hay recursos activos disponibles.");
            return;
        }

        // Se propone el primer recurso que aun no este en el detalle, para no
        // crear duplicados de entrada (regla D08).
        var usados = _detalles.Select(d => d.IdRecurso).ToHashSet();
        var disponible = _recursos.FirstOrDefault(r => !usados.Contains(r.IdRecurso));

        if (disponible is null)
        {
            AyudasUi.MostrarAviso("Ya se agregaron todos los recursos disponibles.");
            return;
        }

        var nueva = new FilaDetalle
        {
            IdRecurso = disponible.IdRecurso,
            Cantidad = 1,
            PrecioUnitario = disponible.PrecioUnitario,
            PorcentajeDescuento = 0m
        };

        // El subtotal se deja calculado ANTES de agregar la fila a la lista.
        // Asi la grilla recibe un elemento ya completo y no hace falta
        // modificarlo despues, que es lo que provocaba la reentrada.
        nueva.ActualizarSubtotal();

        _detalles.Add(nueva);

        RecalcularTotales();
    }

    private void QuitarLinea()
    {
        if (_grilla.CurrentRow?.DataBoundItem is not FilaDetalle fila)
        {
            AyudasUi.MostrarAviso("Seleccione la linea que desea quitar.");
            return;
        }

        _detalles.Remove(fila);
        RecalcularTotales();
    }

    // =====================================================================
    // ACCIONES
    // =====================================================================

    private SolicitudGuardarReserva ConstruirSolicitud() => new()
    {
        IdReserva = _idReserva > 0 ? _idReserva : null,
        IdCliente = (_cboCliente.SelectedItem as Cliente)?.IdCliente ?? 0,
        IdSalon = (_cboSalon.SelectedItem as Salon)?.IdSalon ?? 0,
        FechaEvento = DateOnly.FromDateTime(_dtpFecha.Value),
        HoraInicio = _dtpHoraInicio.Value.TimeOfDay,
        HoraFin = _dtpHoraFin.Value.TimeOfDay,
        NumeroInvitados = (int)_numInvitados.Value,
        Observacion = string.IsNullOrWhiteSpace(_txtObservacion.Text) ? null : _txtObservacion.Text.Trim(),
        PorcentajeDescuentoGlobal = _numDescuentoGlobal.Value,
        IdUsuario = _sesion.IdUsuario,
        Detalles = _detalles
            .Select(d => new LineaDetalleSolicitud(
                d.IdRecurso, d.Cantidad, d.PrecioUnitario, d.PorcentajeDescuento))
            .ToList()
    };

    /// <summary>
    /// Consulta previa de disponibilidad. Sirve para avisar al usuario ANTES de
    /// guardar; el control definitivo lo hace el procedimiento almacenado
    /// dentro de la transaccion.
    /// </summary>
    private async Task VerificarDisponibilidadAsync()
    {
        if (_cancelacion is null) { return; }

        var conflictos = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ValidarDisponibilidadAsync(ConstruirSolicitud(), _cancelacion.Token),
            "No se pudo verificar la disponibilidad.");

        if (conflictos is null) { return; }

        if (conflictos.Count == 0)
        {
            _lblEstadoOperacion.Text = "✓ Sin conflictos de disponibilidad.";
            _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.Exito;
            AyudasUi.MostrarInformacion("La reserva es viable: no hay cruces de horario ni faltantes de stock.");
            return;
        }

        _lblEstadoOperacion.Text = $"⚠ {conflictos.Count} conflicto(s) detectado(s).";
        _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.Peligro;

        AyudasUi.MostrarAviso(
            "Se detectaron los siguientes conflictos:" + Environment.NewLine + Environment.NewLine
            + string.Join(Environment.NewLine, conflictos.Select(c => "• " + c.Mensaje)));
    }

    private async Task GuardarAsync()
    {
        if (_cancelacion is null) { return; }

        var salon = _cboSalon.SelectedItem as Salon;
        var solicitud = ConstruirSolicitud();

        var resultado = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.GuardarAsync(solicitud, salon?.Capacidad ?? 0, _cancelacion.Token),
            "No se pudo guardar la reserva.");

        if (resultado is null) { return; }

        _idReserva = resultado.IdReserva;
        _codigo = resultado.Codigo;
        Text = $"Reserva {_codigo}";
        _lblCodigo.Text = _codigo;

        _lblEstadoOperacion.Text = "✓ " + resultado.Mensaje;
        _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.Exito;

        // Se recarga desde la base para mostrar los importes que calculo SQL
        // Server, que son los definitivos.
        await CargarReservaAsync();

        AyudasUi.MostrarInformacion(resultado.Mensaje);
    }

    /// <summary>
    /// Ejecuta el analisis de IA sin bloquear la interfaz y permitiendo
    /// cancelarlo, tal como exige el examen.
    /// </summary>
    private async Task AnalizarConIaAsync()
    {
        if (_idReserva == 0)
        {
            AyudasUi.MostrarAviso("Guarde la reserva antes de analizarla con IA.");
            return;
        }

        if (!_ia.EstaConfigurado
            && !AyudasUi.Confirmar(
                "No hay una clave de OpenAI configurada, por lo que el analisis fallara."
                + Environment.NewLine + Environment.NewLine
                + "Desea intentarlo de todas formas para ver el mensaje de contingencia?"))
        {
            return;
        }

        AyudasUi.Liberar(ref _cancelacionIa);
        _cancelacionIa = new CancellationTokenSource();

        _btnAnalizarIa.Enabled = false;
        _btnAnalizarIa.Text = "Analizando...";
        _btnCancelarIa.Visible = true;
        _lblEstadoOperacion.Text = "Consultando el servicio de analisis...";
        _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.TextoSuave;

        try
        {
            var reserva = await _reservas.ObtenerAsync(_idReserva, _cancelacionIa.Token);

            if (reserva is null)
            {
                AyudasUi.MostrarAviso("La reserva ya no existe.");
                return;
            }

            var ejecucion = await _reservas.AnalizarConIaAsync(reserva, _cancelacionIa.Token);

            if (!ejecucion.Exitoso)
            {
                _lblEstadoOperacion.Text = "⚠ El analisis no se completo. Quedo registrado en la auditoria.";
                _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.Peligro;

                AyudasUi.MostrarAviso(
                    ejecucion.MensajeUsuario
                    ?? "No se pudo completar el analisis. La reserva no sufrio ningun cambio.");
                return;
            }

            _lblEstadoOperacion.Text =
                $"✓ Analisis completado. Nivel de riesgo: {ejecucion.Resultado!.NivelRiesgo}.";
            _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.Exito;

            using var dialogo = new FrmAnalisisIa(
                ejecucion.Resultado, ejecucion.Proveedor, ejecucion.Modelo, ejecucion.DuracionMs);
            dialogo.ShowDialog(this);
        }
        catch (OperationCanceledException)
        {
            _lblEstadoOperacion.Text = "Analisis cancelado por el usuario.";
            _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.TextoSuave;
        }
        catch (Exception ex)
        {
            _registro.Error("Error no controlado al analizar con IA.", ex);
            AyudasUi.MostrarError(
                "No se pudo completar el analisis. El detalle quedo en el archivo de registro.");
        }
        finally
        {
            _btnAnalizarIa.Enabled = true;
            _btnAnalizarIa.Text = "Analizar con IA";
            _btnCancelarIa.Visible = false;
        }
    }

    private async Task ConfirmarAsync()
    {
        if (_cancelacion is null) { return; }

        if (_idReserva == 0)
        {
            AyudasUi.MostrarAviso("Guarde la reserva antes de confirmarla.");
            return;
        }

        if (!AyudasUi.Confirmar(
            "Al confirmar, la reserva ya no podra editarse y se notificara al cliente por correo."
            + Environment.NewLine + Environment.NewLine + "Desea continuar?"))
        {
            return;
        }

        var resultado = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.ConfirmarAsync(_idReserva, _cancelacion.Token),
            "No se pudo confirmar la reserva.");

        // Si el rechazo fue por falta de analisis de IA, se ofrece la
        // contingencia manual que contempla la regla D22.
        if (resultado is null)
        {
            await OfrecerContingenciaAsync();
            return;
        }

        _lblEstadoOperacion.Text = resultado.TodoCorrecto
            ? "✓ Reserva confirmada y cliente notificado."
            : "Reserva confirmada. Revise el estado del correo.";

        _lblEstadoOperacion.ForeColor = resultado.TodoCorrecto
            ? AyudasUi.Paleta.Exito
            : AyudasUi.Paleta.Peligro;

        await CargarReservaAsync();
        AyudasUi.MostrarInformacion(resultado.MensajeResumen);
    }

    /// <summary>
    /// Ofrece registrar una justificacion de contingencia cuando la
    /// confirmacion se rechazo por falta de analisis de IA.
    ///
    /// Es la via que el propio examen contempla: "analisis de IA exitoso O una
    /// justificacion manual de contingencia guardada en auditoria".
    /// </summary>
    private async Task OfrecerContingenciaAsync()
    {
        if (_cancelacion is null) { return; }

        if (!AyudasUi.Confirmar(
            "Si el analisis de IA no esta disponible, puede registrar una justificacion "
            + "de contingencia para poder confirmar la reserva."
            + Environment.NewLine + Environment.NewLine
            + "Esa justificacion queda auditada con su usuario y la fecha."
            + Environment.NewLine + Environment.NewLine
            + "Desea registrarla ahora?"))
        {
            return;
        }

        using var dialogo = new FrmTextoRequerido(
            "Justificacion de contingencia",
            "Explique por que se confirma esta reserva sin un analisis de IA exitoso. "
            + $"Minimo {ReglasReserva.LongitudMinimaJustificacionContingencia} caracteres.",
            ReglasReserva.LongitudMinimaJustificacionContingencia);

        if (dialogo.ShowDialog(this) != DialogResult.OK) { return; }

        var registrada = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.RegistrarContingenciaIaAsync(_idReserva, dialogo.TextoCapturado, _cancelacion.Token),
            "No se pudo registrar la justificacion de contingencia.");

        if (!registrada) { return; }

        AyudasUi.MostrarInformacion(
            "Justificacion registrada y auditada. Ahora puede confirmar la reserva.");
    }

    private async Task CancelarReservaAsync()
    {
        if (_cancelacion is null) { return; }

        if (_idReserva == 0)
        {
            AyudasUi.MostrarAviso("La reserva todavia no se ha guardado.");
            return;
        }

        using var dialogo = new FrmTextoRequerido(
            "Cancelar reserva",
            "Indique el motivo de la cancelacion. Quedara registrado en la auditoria y se "
            + $"incluira en el correo al cliente. Minimo {ReglasReserva.LongitudMinimaMotivoCancelacion} caracteres.",
            ReglasReserva.LongitudMinimaMotivoCancelacion);

        if (dialogo.ShowDialog(this) != DialogResult.OK) { return; }

        var resultado = await AyudasUi.EjecutarAsync(this, _registro,
            () => _reservas.CancelarAsync(_idReserva, dialogo.TextoCapturado, _cancelacion.Token),
            "No se pudo cancelar la reserva.");

        if (resultado is null) { return; }

        await CargarReservaAsync();
        AyudasUi.MostrarInformacion(resultado.MensajeResumen);
    }

    // =====================================================================
    // ESTADO DE LOS CONTROLES
    // =====================================================================

    /// <summary>
    /// Habilita o bloquea los controles segun el estado de la reserva.
    ///
    /// Refleja la regla D19: una reserva CONFIRMADA no puede editar cliente,
    /// salon, fecha, horario ni detalles; solo cancelarse o finalizarse. Y las
    /// reservas FINALIZADA o CANCELADA son terminales.
    /// </summary>
    private void AplicarEstadoAControles()
    {
        var editable = _estado == EstadoReserva.Borrador;
        var terminal = MaquinaEstadosReserva.EsTerminal(_estado);

        _lblEstado.Text = MaquinaEstadosReserva.ATexto(_estado);
        _lblEstado.BackColor = AyudasUi.ColorFondoEstado(_estado);
        _lblEstado.ForeColor = AyudasUi.ColorTextoEstado(_estado);

        _cboCliente.Enabled = editable;
        _txtBuscarCliente.Enabled = editable;
        _cboSalon.Enabled = editable;
        _dtpFecha.Enabled = editable;
        _dtpHoraInicio.Enabled = editable;
        _dtpHoraFin.Enabled = editable;
        _numInvitados.Enabled = editable;
        _numDescuentoGlobal.Enabled = editable;
        _txtObservacion.Enabled = editable;
        _grilla.ReadOnly = !editable;
        _btnAgregarLinea.Enabled = editable;
        _btnQuitarLinea.Enabled = editable;
        _btnVerificarDisponibilidad.Enabled = editable;

        _btnGuardar.Enabled = editable && _sesion.Tiene(Permiso.GestionarReservas);
        _btnAnalizarIa.Enabled = !terminal && _sesion.Tiene(Permiso.AnalizarConIa);
        _btnConfirmar.Enabled = editable && _sesion.Tiene(Permiso.ConfirmarReserva);
        _btnCancelarReserva.Enabled = !terminal && _sesion.Tiene(Permiso.CancelarReserva);

        if (terminal)
        {
            _lblEstadoOperacion.Text =
                $"Esta reserva esta {MaquinaEstadosReserva.ATexto(_estado)} y ya no admite cambios.";
            _lblEstadoOperacion.ForeColor = AyudasUi.Paleta.TextoSuave;
        }
    }

    /// <summary>
    /// Filtra la lista de clientes por nombre o identificacion.
    ///
    /// AVISA CUANDO NO HAY COINCIDENCIAS. En la primera version, si el texto no
    /// coincidia con nadie la lista se quedaba vacia en silencio y el usuario
    /// no entendia por que al guardar salia "Seleccione un cliente". Ahora el
    /// cuadro de busqueda se marca en rojo y aparece un aviso explicito.
    ///
    /// Ademas, si el filtro deja exactamente un cliente, se selecciona solo:
    /// es el caso habitual cuando se escribe una identificacion completa.
    /// </summary>
    private void FiltrarClientes()
    {
        var texto = _txtBuscarCliente.Text.Trim();
        var hayFiltro = !string.IsNullOrWhiteSpace(texto);

        var filtrados = hayFiltro
            ? _clientes.Where(c =>
                    c.Nombres.Contains(texto, StringComparison.OrdinalIgnoreCase)
                    || c.Identificacion.Contains(texto, StringComparison.OrdinalIgnoreCase))
                .ToList()
            : _clientes;

        var seleccionado = (_cboCliente.SelectedItem as Cliente)?.IdCliente;

        _cboCliente.DataSource = filtrados;

        // ---------- Sin coincidencias: avisar en lugar de callar ----------
        if (hayFiltro && filtrados.Count == 0)
        {
            _txtBuscarCliente.BackColor = Color.FromArgb(255, 235, 238);

            _lblAvisoCliente.Text =
                $"Ningun cliente coincide con \"{texto}\". Borre el filtro para ver los "
                + $"{_clientes.Count} cliente(s) activo(s), o registrelo en Catalogos.";

            _lblAvisoCliente.ForeColor = AyudasUi.Paleta.Peligro;
            return;
        }

        _txtBuscarCliente.BackColor = SystemColors.Window;

        // ---------- Una sola coincidencia: seleccionarla ----------
        if (hayFiltro && filtrados.Count == 1)
        {
            _cboCliente.SelectedIndex = 0;
            _lblAvisoCliente.Text = "Cliente encontrado y seleccionado.";
            _lblAvisoCliente.ForeColor = AyudasUi.Paleta.Exito;
            return;
        }

        _lblAvisoCliente.Text = hayFiltro
            ? $"{filtrados.Count} cliente(s) coinciden. Elija uno en la lista."
            : string.Empty;

        _lblAvisoCliente.ForeColor = AyudasUi.Paleta.TextoSuave;

        // Se conserva la seleccion previa si sigue estando en la lista filtrada.
        if (seleccionado.HasValue && filtrados.Exists(c => c.IdCliente == seleccionado.Value))
        {
            _cboCliente.SelectedValue = seleccionado.Value;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AyudasUi.Liberar(ref _cancelacion);
            AyudasUi.Liberar(ref _cancelacionIa);
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Fila editable de la grilla del detalle.
    ///
    /// Implementa INotifyPropertyChanged para que la grilla refresque el
    /// subtotal en cuanto cambian cantidad, precio o descuento: es lo que hace
    /// que el "calculo en tiempo real" se vea de verdad.
    /// </summary>
    private sealed class FilaDetalle : INotifyPropertyChanged
    {
        private int _idRecurso;
        private int _cantidad = 1;
        private decimal _precioUnitario;
        private decimal _porcentajeDescuento;
        private decimal _subtotalLinea;

        public event PropertyChangedEventHandler? PropertyChanged;

        public int IdRecurso
        {
            get => _idRecurso;
            set => Asignar(ref _idRecurso, value, nameof(IdRecurso));
        }

        public int Cantidad
        {
            get => _cantidad;
            set
            {
                if (Asignar(ref _cantidad, value, nameof(Cantidad)))
                {
                    ActualizarSubtotal();
                }
            }
        }

        public decimal PrecioUnitario
        {
            get => _precioUnitario;
            set
            {
                if (Asignar(ref _precioUnitario, value, nameof(PrecioUnitario)))
                {
                    ActualizarSubtotal();
                }
            }
        }

        public decimal PorcentajeDescuento
        {
            get => _porcentajeDescuento;
            set
            {
                if (Asignar(ref _porcentajeDescuento, value, nameof(PorcentajeDescuento)))
                {
                    ActualizarSubtotal();
                }
            }
        }

        /// <summary>
        /// Subtotal de la linea. Es de solo lectura desde fuera: lo calcula la
        /// propia fila. Nadie mas puede asignarlo, y por eso ya no puede
        /// producirse la reentrada que rompia la grilla.
        /// </summary>
        public decimal SubtotalLinea
        {
            get => _subtotalLinea;
            private set => Asignar(ref _subtotalLinea, value, nameof(SubtotalLinea));
        }

        /// <summary>
        /// Recalcula el subtotal con la misma formula que el procedimiento
        /// almacenado. Se invoca sola cuando cambia cantidad, precio o descuento.
        /// </summary>
        public void ActualizarSubtotal() =>
            SubtotalLinea = CalculadoraTotales.CalcularSubtotalLinea(
                _cantidad, _precioUnitario, _porcentajeDescuento);

        /// <summary>Devuelve true si el valor cambio realmente.</summary>
        private bool Asignar<T>(ref T campo, T valor, string nombre)
        {
            if (EqualityComparer<T>.Default.Equals(campo, valor))
            {
                return false;
            }

            campo = valor;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nombre));
            return true;
        }
    }
}
