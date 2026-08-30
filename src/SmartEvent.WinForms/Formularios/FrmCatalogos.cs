using System.Globalization;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Servicios;
using SmartEvent.Dominio.Entidades;
using SmartEvent.WinForms.Comun;

namespace SmartEvent.WinForms.Formularios;

/// <summary>
/// Mantenimiento de clientes, salones y recursos.
///
/// Cumple lo que exige el examen para este formulario: CRUD de los tres
/// catalogos, filtros, validaciones, deteccion de duplicados e inactivacion
/// logica.
///
/// Sobre la INACTIVACION LOGICA: no existe ningun boton de borrar. Un cliente
/// o un salon no se eliminan nunca, porque hay reservas historicas que los
/// referencian; se marcan como inactivos y dejan de aparecer para nuevas
/// reservas. Ademas, SQL Server impide inactivar un salon o un recurso que
/// tenga reservas en BORRADOR o CONFIRMADA.
///
/// Sobre los DUPLICADOS: la comprobacion vive en los procedimientos
/// almacenados, no aqui. Si dos usuarios guardaran el mismo nombre a la vez,
/// una validacion previa en C# no lo evitaria; la restriccion UNIQUE de la
/// tabla, si.
/// </summary>
internal sealed class FrmCatalogos : Form
{
    private readonly ServicioCatalogos _catalogos;
    private readonly IRegistradorSeguro _registro;

    private readonly TabControl _pestanas = new();
    private CancellationTokenSource? _cancelacion;

    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-EC");

    // ---------- Clientes ----------
    private readonly TextBox _txtBuscarCliente = new();
    private readonly CheckBox _chkSoloActivosCliente = new();
    private readonly DataGridView _grillaClientes = new();
    private readonly TextBox _txtClienteIdentificacion = new();
    private readonly TextBox _txtClienteNombres = new();
    private readonly TextBox _txtClienteEmail = new();
    private readonly TextBox _txtClienteTelefono = new();
    private readonly Button _btnClienteGuardar = new();
    private readonly Button _btnClienteNuevo = new();
    private readonly Button _btnClienteEstado = new();
    private int _idClienteSeleccionado;
    private bool _estadoClienteSeleccionado = true;

    // ---------- Salones ----------
    private readonly TextBox _txtBuscarSalon = new();
    private readonly CheckBox _chkSoloActivosSalon = new();
    private readonly DataGridView _grillaSalones = new();
    private readonly TextBox _txtSalonNombre = new();
    private readonly TextBox _txtSalonUbicacion = new();
    private readonly NumericUpDown _numSalonCapacidad = new();
    private readonly NumericUpDown _numSalonTarifa = new();
    private readonly Button _btnSalonGuardar = new();
    private readonly Button _btnSalonNuevo = new();
    private readonly Button _btnSalonEstado = new();
    private int _idSalonSeleccionado;
    private bool _estadoSalonSeleccionado = true;

    // ---------- Recursos ----------
    private readonly TextBox _txtBuscarRecurso = new();
    private readonly CheckBox _chkSoloActivosRecurso = new();
    private readonly DataGridView _grillaRecursos = new();
    private readonly TextBox _txtRecursoNombre = new();
    private readonly TextBox _txtRecursoTipo = new();
    private readonly NumericUpDown _numRecursoStock = new();
    private readonly NumericUpDown _numRecursoPrecio = new();
    private readonly Button _btnRecursoGuardar = new();
    private readonly Button _btnRecursoNuevo = new();
    private readonly Button _btnRecursoEstado = new();
    private int _idRecursoSeleccionado;
    private bool _estadoRecursoSeleccionado = true;

    public FrmCatalogos(ServicioCatalogos catalogos, IRegistradorSeguro registro)
    {
        _catalogos = catalogos ?? throw new ArgumentNullException(nameof(catalogos));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));

        ConstruirInterfaz();
    }

    private void ConstruirInterfaz()
    {
        Text = "Catalogos";
        WindowState = FormWindowState.Maximized;
        BackColor = AyudasUi.Paleta.Fondo;
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(1000, 620);

        _pestanas.Dock = DockStyle.Fill;
        _pestanas.Font = new Font("Segoe UI", 10F);
        _pestanas.Padding = new Point(16, 6);

        _pestanas.TabPages.Add(ConstruirPestanaClientes());
        _pestanas.TabPages.Add(ConstruirPestanaSalones());
        _pestanas.TabPages.Add(ConstruirPestanaRecursos());

        Controls.Add(_pestanas);

        Shown += async (_, _) =>
        {
            _cancelacion = new CancellationTokenSource();
            await CargarClientesAsync();
            await CargarSalonesAsync();
            await CargarRecursosAsync();
        };
    }

    // =====================================================================
    // CLIENTES
    // =====================================================================

    private TabPage ConstruirPestanaClientes()
    {
        var pagina = new TabPage("Clientes") { BackColor = AyudasUi.Paleta.Fondo, Padding = new Padding(12) };

        // ---------- Filtros ----------
        var panelFiltros = new Panel { Dock = DockStyle.Top, Height = 46 };

        panelFiltros.Controls.Add(new Label
        {
            Text = "Buscar:", Location = new Point(0, 12), AutoSize = true,
            ForeColor = AyudasUi.Paleta.TextoSuave
        });

        _txtBuscarCliente.Location = new Point(56, 8);
        _txtBuscarCliente.Size = new Size(300, 26);
        _txtBuscarCliente.BorderStyle = BorderStyle.FixedSingle;
        _txtBuscarCliente.PlaceholderText = "Identificacion, nombre o correo";
        _txtBuscarCliente.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CargarClientesAsync(); }
        };
        panelFiltros.Controls.Add(_txtBuscarCliente);

        var btnBuscar = new Button { Text = "Buscar", Location = new Point(366, 7), Size = new Size(100, 28) };
        btnBuscar.EstiloSecundario();
        btnBuscar.Click += async (_, _) => await CargarClientesAsync();
        panelFiltros.Controls.Add(btnBuscar);

        _chkSoloActivosCliente.Text = "Solo activos";
        _chkSoloActivosCliente.Location = new Point(480, 12);
        _chkSoloActivosCliente.AutoSize = true;
        _chkSoloActivosCliente.CheckedChanged += async (_, _) => await CargarClientesAsync();
        panelFiltros.Controls.Add(_chkSoloActivosCliente);

        // ---------- Grilla ----------
        _grillaClientes.Dock = DockStyle.Fill;
        _grillaClientes.EstiloEstandar();
        _grillaClientes.SelectionChanged += GrillaClientesSelectionChanged;

        // ---------- Panel de edicion ----------
        var panelEdicion = ConstruirPanelEdicionCliente();

        pagina.Controls.Add(_grillaClientes);
        pagina.Controls.Add(panelEdicion);
        pagina.Controls.Add(panelFiltros);

        return pagina;
    }

    private Panel ConstruirPanelEdicionCliente()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 168,
            BackColor = Color.White,
            Padding = new Padding(14),
            BorderStyle = BorderStyle.FixedSingle
        };

        panel.Controls.Add(new Label
        {
            Text = "Datos del cliente", Location = new Point(14, 10), AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = AyudasUi.Paleta.Primario
        });

        AgregarCampo(panel, "Identificacion *", _txtClienteIdentificacion, 14, 40, 220, 20);
        AgregarCampo(panel, "Nombres *", _txtClienteNombres, 254, 40, 400, 150);
        AgregarCampo(panel, "Correo electronico *", _txtClienteEmail, 14, 92, 380, 150);
        AgregarCampo(panel, "Telefono", _txtClienteTelefono, 414, 92, 240, 20);

        _btnClienteNuevo.Text = "Nuevo";
        _btnClienteNuevo.Location = new Point(690, 58);
        _btnClienteNuevo.Size = new Size(120, 34);
        _btnClienteNuevo.EstiloSecundario();
        _btnClienteNuevo.Click += (_, _) => LimpiarCliente();
        panel.Controls.Add(_btnClienteNuevo);

        _btnClienteGuardar.Text = "Guardar";
        _btnClienteGuardar.Location = new Point(690, 98);
        _btnClienteGuardar.Size = new Size(120, 34);
        _btnClienteGuardar.EstiloPrimario();
        _btnClienteGuardar.Click += async (_, _) => await GuardarClienteAsync();
        panel.Controls.Add(_btnClienteGuardar);

        _btnClienteEstado.Text = "Inactivar";
        _btnClienteEstado.Location = new Point(824, 98);
        _btnClienteEstado.Size = new Size(130, 34);
        _btnClienteEstado.EstiloSecundario();
        _btnClienteEstado.Enabled = false;
        _btnClienteEstado.Click += async (_, _) => await CambiarEstadoClienteAsync();
        panel.Controls.Add(_btnClienteEstado);

        return panel;
    }

    private async Task CargarClientesAsync()
    {
        if (_cancelacion is null) { return; }

        var lista = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.ConsultarClientesAsync(
                _txtBuscarCliente.Text, _chkSoloActivosCliente.Checked, _cancelacion.Token),
            "No se pudieron cargar los clientes.");

        if (lista is null) { return; }

        _grillaClientes.DataSource = lista
            .Select(c => new
            {
                c.IdCliente,
                c.Identificacion,
                c.Nombres,
                c.Email,
                c.Telefono,
                Estado = c.Estado ? "Activo" : "Inactivo",
                EstadoBool = c.Estado
            })
            .ToList();

        ConfigurarColumnasCliente();
    }

    private void ConfigurarColumnasCliente()
    {
        if (_grillaClientes.Columns.Count == 0) { return; }

        _grillaClientes.Columns["IdCliente"]!.Visible = false;
        _grillaClientes.Columns["EstadoBool"]!.Visible = false;
        _grillaClientes.Columns["Identificacion"]!.HeaderText = "Identificacion";
        _grillaClientes.Columns["Nombres"]!.HeaderText = "Nombres";
        _grillaClientes.Columns["Email"]!.HeaderText = "Correo electronico";
        _grillaClientes.Columns["Telefono"]!.HeaderText = "Telefono";
        _grillaClientes.Columns["Estado"]!.HeaderText = "Estado";
        _grillaClientes.Columns["Identificacion"]!.FillWeight = 60;
        _grillaClientes.Columns["Nombres"]!.FillWeight = 120;
        _grillaClientes.Columns["Email"]!.FillWeight = 120;
        _grillaClientes.Columns["Telefono"]!.FillWeight = 50;
        _grillaClientes.Columns["Estado"]!.FillWeight = 40;
    }

    private void GrillaClientesSelectionChanged(object? sender, EventArgs e)
    {
        if (_grillaClientes.CurrentRow?.DataBoundItem is null) { return; }

        var fila = _grillaClientes.CurrentRow;

        _idClienteSeleccionado = (int)fila.Cells["IdCliente"].Value;
        _estadoClienteSeleccionado = (bool)fila.Cells["EstadoBool"].Value;

        _txtClienteIdentificacion.Text = fila.Cells["Identificacion"].Value?.ToString() ?? string.Empty;
        _txtClienteNombres.Text = fila.Cells["Nombres"].Value?.ToString() ?? string.Empty;
        _txtClienteEmail.Text = fila.Cells["Email"].Value?.ToString() ?? string.Empty;
        _txtClienteTelefono.Text = fila.Cells["Telefono"].Value?.ToString() ?? string.Empty;

        _btnClienteEstado.Enabled = true;
        _btnClienteEstado.Text = _estadoClienteSeleccionado ? "Inactivar" : "Activar";
    }

    private void LimpiarCliente()
    {
        _idClienteSeleccionado = 0;
        _txtClienteIdentificacion.Clear();
        _txtClienteNombres.Clear();
        _txtClienteEmail.Clear();
        _txtClienteTelefono.Clear();
        _btnClienteEstado.Enabled = false;
        _grillaClientes.ClearSelection();
        _txtClienteIdentificacion.Focus();
    }

    private async Task GuardarClienteAsync()
    {
        if (_cancelacion is null) { return; }

        var cliente = new Cliente
        {
            IdCliente = _idClienteSeleccionado,
            Identificacion = _txtClienteIdentificacion.Text.Trim(),
            Nombres = _txtClienteNombres.Text.Trim(),
            Email = _txtClienteEmail.Text.Trim(),
            Telefono = string.IsNullOrWhiteSpace(_txtClienteTelefono.Text) ? null : _txtClienteTelefono.Text.Trim()
        };

        var correcto = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.GuardarClienteAsync(cliente, _cancelacion.Token),
            "No se pudo guardar el cliente.");

        if (correcto == 0) { return; }

        AyudasUi.MostrarInformacion("Cliente guardado correctamente.");
        LimpiarCliente();
        await CargarClientesAsync();
    }

    private async Task CambiarEstadoClienteAsync()
    {
        if (_cancelacion is null || _idClienteSeleccionado == 0) { return; }

        var nuevoEstado = !_estadoClienteSeleccionado;
        var accion = nuevoEstado ? "activar" : "inactivar";

        if (!AyudasUi.Confirmar($"Desea {accion} este cliente?")) { return; }

        var correcto = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.CambiarEstadoClienteAsync(_idClienteSeleccionado, nuevoEstado, _cancelacion.Token),
            $"No se pudo {accion} el cliente.");

        if (!correcto) { return; }

        await CargarClientesAsync();
    }

    // =====================================================================
    // SALONES
    // =====================================================================

    private TabPage ConstruirPestanaSalones()
    {
        var pagina = new TabPage("Salones") { BackColor = AyudasUi.Paleta.Fondo, Padding = new Padding(12) };

        var panelFiltros = new Panel { Dock = DockStyle.Top, Height = 46 };

        panelFiltros.Controls.Add(new Label
        {
            Text = "Buscar:", Location = new Point(0, 12), AutoSize = true,
            ForeColor = AyudasUi.Paleta.TextoSuave
        });

        _txtBuscarSalon.Location = new Point(56, 8);
        _txtBuscarSalon.Size = new Size(300, 26);
        _txtBuscarSalon.BorderStyle = BorderStyle.FixedSingle;
        _txtBuscarSalon.PlaceholderText = "Nombre o ubicacion";
        _txtBuscarSalon.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CargarSalonesAsync(); }
        };
        panelFiltros.Controls.Add(_txtBuscarSalon);

        var btnBuscar = new Button { Text = "Buscar", Location = new Point(366, 7), Size = new Size(100, 28) };
        btnBuscar.EstiloSecundario();
        btnBuscar.Click += async (_, _) => await CargarSalonesAsync();
        panelFiltros.Controls.Add(btnBuscar);

        _chkSoloActivosSalon.Text = "Solo activos";
        _chkSoloActivosSalon.Location = new Point(480, 12);
        _chkSoloActivosSalon.AutoSize = true;
        _chkSoloActivosSalon.CheckedChanged += async (_, _) => await CargarSalonesAsync();
        panelFiltros.Controls.Add(_chkSoloActivosSalon);

        _grillaSalones.Dock = DockStyle.Fill;
        _grillaSalones.EstiloEstandar();
        _grillaSalones.SelectionChanged += GrillaSalonesSelectionChanged;

        pagina.Controls.Add(_grillaSalones);
        pagina.Controls.Add(ConstruirPanelEdicionSalon());
        pagina.Controls.Add(panelFiltros);

        return pagina;
    }

    private Panel ConstruirPanelEdicionSalon()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom, Height = 168, BackColor = Color.White,
            Padding = new Padding(14), BorderStyle = BorderStyle.FixedSingle
        };

        panel.Controls.Add(new Label
        {
            Text = "Datos del salon", Location = new Point(14, 10), AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = AyudasUi.Paleta.Primario
        });

        AgregarCampo(panel, "Nombre *", _txtSalonNombre, 14, 40, 300, 100);
        AgregarCampo(panel, "Ubicacion", _txtSalonUbicacion, 334, 40, 320, 150);

        ConfigurarNumerico(_numSalonCapacidad, 1, 100_000, 0);
        AgregarCampoNumerico(panel, "Capacidad *", _numSalonCapacidad, 14, 92, 140);

        ConfigurarNumerico(_numSalonTarifa, 0, 1_000_000, 2);
        AgregarCampoNumerico(panel, "Tarifa base (USD) *", _numSalonTarifa, 174, 92, 180);

        _btnSalonNuevo.Text = "Nuevo";
        _btnSalonNuevo.Location = new Point(690, 58);
        _btnSalonNuevo.Size = new Size(120, 34);
        _btnSalonNuevo.EstiloSecundario();
        _btnSalonNuevo.Click += (_, _) => LimpiarSalon();
        panel.Controls.Add(_btnSalonNuevo);

        _btnSalonGuardar.Text = "Guardar";
        _btnSalonGuardar.Location = new Point(690, 98);
        _btnSalonGuardar.Size = new Size(120, 34);
        _btnSalonGuardar.EstiloPrimario();
        _btnSalonGuardar.Click += async (_, _) => await GuardarSalonAsync();
        panel.Controls.Add(_btnSalonGuardar);

        _btnSalonEstado.Text = "Inactivar";
        _btnSalonEstado.Location = new Point(824, 98);
        _btnSalonEstado.Size = new Size(130, 34);
        _btnSalonEstado.EstiloSecundario();
        _btnSalonEstado.Enabled = false;
        _btnSalonEstado.Click += async (_, _) => await CambiarEstadoSalonAsync();
        panel.Controls.Add(_btnSalonEstado);

        return panel;
    }

    private async Task CargarSalonesAsync()
    {
        if (_cancelacion is null) { return; }

        var lista = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.ConsultarSalonesAsync(
                _txtBuscarSalon.Text, _chkSoloActivosSalon.Checked, _cancelacion.Token),
            "No se pudieron cargar los salones.");

        if (lista is null) { return; }

        _grillaSalones.DataSource = lista
            .Select(s => new
            {
                s.IdSalon,
                s.Nombre,
                s.Ubicacion,
                s.Capacidad,
                s.TarifaBase,
                Estado = s.Estado ? "Activo" : "Inactivo",
                EstadoBool = s.Estado
            })
            .ToList();

        if (_grillaSalones.Columns.Count > 0)
        {
            _grillaSalones.Columns["IdSalon"]!.Visible = false;
            _grillaSalones.Columns["EstadoBool"]!.Visible = false;
            _grillaSalones.Columns["TarifaBase"]!.HeaderText = "Tarifa base";
            _grillaSalones.Columns["TarifaBase"]!.DefaultCellStyle.Format = "C2";
            _grillaSalones.Columns["TarifaBase"]!.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;
            _grillaSalones.Columns["Capacidad"]!.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;
        }
    }

    private void GrillaSalonesSelectionChanged(object? sender, EventArgs e)
    {
        if (_grillaSalones.CurrentRow?.DataBoundItem is null) { return; }

        var fila = _grillaSalones.CurrentRow;

        _idSalonSeleccionado = (int)fila.Cells["IdSalon"].Value;
        _estadoSalonSeleccionado = (bool)fila.Cells["EstadoBool"].Value;

        _txtSalonNombre.Text = fila.Cells["Nombre"].Value?.ToString() ?? string.Empty;
        _txtSalonUbicacion.Text = fila.Cells["Ubicacion"].Value?.ToString() ?? string.Empty;
        _numSalonCapacidad.Value = Convert.ToDecimal(fila.Cells["Capacidad"].Value, Cultura);
        _numSalonTarifa.Value = Convert.ToDecimal(fila.Cells["TarifaBase"].Value, Cultura);

        _btnSalonEstado.Enabled = true;
        _btnSalonEstado.Text = _estadoSalonSeleccionado ? "Inactivar" : "Activar";
    }

    private void LimpiarSalon()
    {
        _idSalonSeleccionado = 0;
        _txtSalonNombre.Clear();
        _txtSalonUbicacion.Clear();
        _numSalonCapacidad.Value = 1;
        _numSalonTarifa.Value = 0;
        _btnSalonEstado.Enabled = false;
        _grillaSalones.ClearSelection();
        _txtSalonNombre.Focus();
    }

    private async Task GuardarSalonAsync()
    {
        if (_cancelacion is null) { return; }

        var salon = new Salon
        {
            IdSalon = _idSalonSeleccionado,
            Nombre = _txtSalonNombre.Text.Trim(),
            Ubicacion = string.IsNullOrWhiteSpace(_txtSalonUbicacion.Text) ? null : _txtSalonUbicacion.Text.Trim(),
            Capacidad = (int)_numSalonCapacidad.Value,
            TarifaBase = _numSalonTarifa.Value
        };

        var correcto = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.GuardarSalonAsync(salon, _cancelacion.Token),
            "No se pudo guardar el salon.");

        if (correcto == 0) { return; }

        AyudasUi.MostrarInformacion("Salon guardado correctamente.");
        LimpiarSalon();
        await CargarSalonesAsync();
    }

    private async Task CambiarEstadoSalonAsync()
    {
        if (_cancelacion is null || _idSalonSeleccionado == 0) { return; }

        var nuevoEstado = !_estadoSalonSeleccionado;
        var accion = nuevoEstado ? "activar" : "inactivar";

        if (!AyudasUi.Confirmar($"Desea {accion} este salon?")) { return; }

        var correcto = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.CambiarEstadoSalonAsync(_idSalonSeleccionado, nuevoEstado, _cancelacion.Token),
            $"No se pudo {accion} el salon.");

        if (!correcto) { return; }

        await CargarSalonesAsync();
    }

    // =====================================================================
    // RECURSOS
    // =====================================================================

    private TabPage ConstruirPestanaRecursos()
    {
        var pagina = new TabPage("Recursos y servicios")
        {
            BackColor = AyudasUi.Paleta.Fondo,
            Padding = new Padding(12)
        };

        var panelFiltros = new Panel { Dock = DockStyle.Top, Height = 46 };

        panelFiltros.Controls.Add(new Label
        {
            Text = "Buscar:", Location = new Point(0, 12), AutoSize = true,
            ForeColor = AyudasUi.Paleta.TextoSuave
        });

        _txtBuscarRecurso.Location = new Point(56, 8);
        _txtBuscarRecurso.Size = new Size(300, 26);
        _txtBuscarRecurso.BorderStyle = BorderStyle.FixedSingle;
        _txtBuscarRecurso.PlaceholderText = "Nombre o tipo";
        _txtBuscarRecurso.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; await CargarRecursosAsync(); }
        };
        panelFiltros.Controls.Add(_txtBuscarRecurso);

        var btnBuscar = new Button { Text = "Buscar", Location = new Point(366, 7), Size = new Size(100, 28) };
        btnBuscar.EstiloSecundario();
        btnBuscar.Click += async (_, _) => await CargarRecursosAsync();
        panelFiltros.Controls.Add(btnBuscar);

        _chkSoloActivosRecurso.Text = "Solo activos";
        _chkSoloActivosRecurso.Location = new Point(480, 12);
        _chkSoloActivosRecurso.AutoSize = true;
        _chkSoloActivosRecurso.CheckedChanged += async (_, _) => await CargarRecursosAsync();
        panelFiltros.Controls.Add(_chkSoloActivosRecurso);

        _grillaRecursos.Dock = DockStyle.Fill;
        _grillaRecursos.EstiloEstandar();
        _grillaRecursos.SelectionChanged += GrillaRecursosSelectionChanged;

        pagina.Controls.Add(_grillaRecursos);
        pagina.Controls.Add(ConstruirPanelEdicionRecurso());
        pagina.Controls.Add(panelFiltros);

        return pagina;
    }

    private Panel ConstruirPanelEdicionRecurso()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Bottom, Height = 168, BackColor = Color.White,
            Padding = new Padding(14), BorderStyle = BorderStyle.FixedSingle
        };

        panel.Controls.Add(new Label
        {
            Text = "Datos del recurso", Location = new Point(14, 10), AutoSize = true,
            Font = new Font("Segoe UI", 10F, FontStyle.Bold), ForeColor = AyudasUi.Paleta.Primario
        });

        AgregarCampo(panel, "Nombre *", _txtRecursoNombre, 14, 40, 300, 100);
        AgregarCampo(panel, "Tipo *", _txtRecursoTipo, 334, 40, 200, 40);

        ConfigurarNumerico(_numRecursoStock, 0, 1_000_000, 0);
        AgregarCampoNumerico(panel, "Stock total *", _numRecursoStock, 14, 92, 140);

        ConfigurarNumerico(_numRecursoPrecio, 0, 1_000_000, 2);
        AgregarCampoNumerico(panel, "Precio unitario (USD) *", _numRecursoPrecio, 174, 92, 180);

        _btnRecursoNuevo.Text = "Nuevo";
        _btnRecursoNuevo.Location = new Point(690, 58);
        _btnRecursoNuevo.Size = new Size(120, 34);
        _btnRecursoNuevo.EstiloSecundario();
        _btnRecursoNuevo.Click += (_, _) => LimpiarRecurso();
        panel.Controls.Add(_btnRecursoNuevo);

        _btnRecursoGuardar.Text = "Guardar";
        _btnRecursoGuardar.Location = new Point(690, 98);
        _btnRecursoGuardar.Size = new Size(120, 34);
        _btnRecursoGuardar.EstiloPrimario();
        _btnRecursoGuardar.Click += async (_, _) => await GuardarRecursoAsync();
        panel.Controls.Add(_btnRecursoGuardar);

        _btnRecursoEstado.Text = "Inactivar";
        _btnRecursoEstado.Location = new Point(824, 98);
        _btnRecursoEstado.Size = new Size(130, 34);
        _btnRecursoEstado.EstiloSecundario();
        _btnRecursoEstado.Enabled = false;
        _btnRecursoEstado.Click += async (_, _) => await CambiarEstadoRecursoAsync();
        panel.Controls.Add(_btnRecursoEstado);

        return panel;
    }

    private async Task CargarRecursosAsync()
    {
        if (_cancelacion is null) { return; }

        var lista = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.ConsultarRecursosAsync(
                _txtBuscarRecurso.Text, _chkSoloActivosRecurso.Checked, _cancelacion.Token),
            "No se pudieron cargar los recursos.");

        if (lista is null) { return; }

        _grillaRecursos.DataSource = lista
            .Select(r => new
            {
                r.IdRecurso,
                r.Nombre,
                r.Tipo,
                r.StockTotal,
                r.PrecioUnitario,
                Estado = r.Estado ? "Activo" : "Inactivo",
                EstadoBool = r.Estado
            })
            .ToList();

        if (_grillaRecursos.Columns.Count > 0)
        {
            _grillaRecursos.Columns["IdRecurso"]!.Visible = false;
            _grillaRecursos.Columns["EstadoBool"]!.Visible = false;
            _grillaRecursos.Columns["StockTotal"]!.HeaderText = "Stock";
            _grillaRecursos.Columns["PrecioUnitario"]!.HeaderText = "Precio unitario";
            _grillaRecursos.Columns["PrecioUnitario"]!.DefaultCellStyle.Format = "C2";
            _grillaRecursos.Columns["PrecioUnitario"]!.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;
            _grillaRecursos.Columns["StockTotal"]!.DefaultCellStyle.Alignment =
                DataGridViewContentAlignment.MiddleRight;
        }
    }

    private void GrillaRecursosSelectionChanged(object? sender, EventArgs e)
    {
        if (_grillaRecursos.CurrentRow?.DataBoundItem is null) { return; }

        var fila = _grillaRecursos.CurrentRow;

        _idRecursoSeleccionado = (int)fila.Cells["IdRecurso"].Value;
        _estadoRecursoSeleccionado = (bool)fila.Cells["EstadoBool"].Value;

        _txtRecursoNombre.Text = fila.Cells["Nombre"].Value?.ToString() ?? string.Empty;
        _txtRecursoTipo.Text = fila.Cells["Tipo"].Value?.ToString() ?? string.Empty;
        _numRecursoStock.Value = Convert.ToDecimal(fila.Cells["StockTotal"].Value, Cultura);
        _numRecursoPrecio.Value = Convert.ToDecimal(fila.Cells["PrecioUnitario"].Value, Cultura);

        _btnRecursoEstado.Enabled = true;
        _btnRecursoEstado.Text = _estadoRecursoSeleccionado ? "Inactivar" : "Activar";
    }

    private void LimpiarRecurso()
    {
        _idRecursoSeleccionado = 0;
        _txtRecursoNombre.Clear();
        _txtRecursoTipo.Clear();
        _numRecursoStock.Value = 0;
        _numRecursoPrecio.Value = 0;
        _btnRecursoEstado.Enabled = false;
        _grillaRecursos.ClearSelection();
        _txtRecursoNombre.Focus();
    }

    private async Task GuardarRecursoAsync()
    {
        if (_cancelacion is null) { return; }

        var recurso = new Recurso
        {
            IdRecurso = _idRecursoSeleccionado,
            Nombre = _txtRecursoNombre.Text.Trim(),
            Tipo = _txtRecursoTipo.Text.Trim(),
            StockTotal = (int)_numRecursoStock.Value,
            PrecioUnitario = _numRecursoPrecio.Value
        };

        var correcto = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.GuardarRecursoAsync(recurso, _cancelacion.Token),
            "No se pudo guardar el recurso.");

        if (correcto == 0) { return; }

        AyudasUi.MostrarInformacion("Recurso guardado correctamente.");
        LimpiarRecurso();
        await CargarRecursosAsync();
    }

    private async Task CambiarEstadoRecursoAsync()
    {
        if (_cancelacion is null || _idRecursoSeleccionado == 0) { return; }

        var nuevoEstado = !_estadoRecursoSeleccionado;
        var accion = nuevoEstado ? "activar" : "inactivar";

        if (!AyudasUi.Confirmar($"Desea {accion} este recurso?")) { return; }

        var correcto = await AyudasUi.EjecutarAsync(this, _registro,
            () => _catalogos.CambiarEstadoRecursoAsync(_idRecursoSeleccionado, nuevoEstado, _cancelacion.Token),
            $"No se pudo {accion} el recurso.");

        if (!correcto) { return; }

        await CargarRecursosAsync();
    }

    // =====================================================================
    // APOYO
    // =====================================================================

    private static void AgregarCampo(
        Panel panel, string etiqueta, TextBox caja, int x, int y, int ancho, int longitudMaxima)
    {
        panel.Controls.Add(new Label
        {
            Text = etiqueta, Location = new Point(x, y), AutoSize = true,
            ForeColor = AyudasUi.Paleta.TextoSuave, Font = new Font("Segoe UI", 8.5F)
        });

        caja.Location = new Point(x, y + 18);
        caja.Size = new Size(ancho, 26);
        caja.BorderStyle = BorderStyle.FixedSingle;
        caja.MaxLength = longitudMaxima;
        panel.Controls.Add(caja);
    }

    private static void AgregarCampoNumerico(
        Panel panel, string etiqueta, NumericUpDown numerico, int x, int y, int ancho)
    {
        panel.Controls.Add(new Label
        {
            Text = etiqueta, Location = new Point(x, y), AutoSize = true,
            ForeColor = AyudasUi.Paleta.TextoSuave, Font = new Font("Segoe UI", 8.5F)
        });

        numerico.Location = new Point(x, y + 18);
        numerico.Size = new Size(ancho, 26);
        numerico.BorderStyle = BorderStyle.FixedSingle;
        panel.Controls.Add(numerico);
    }

    private static void ConfigurarNumerico(NumericUpDown numerico, decimal minimo, decimal maximo, int decimales)
    {
        numerico.Minimum = minimo;
        numerico.Maximum = maximo;
        numerico.DecimalPlaces = decimales;
        numerico.ThousandsSeparator = true;
        numerico.TextAlign = HorizontalAlignment.Right;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancelacion?.Cancel();
            _cancelacion?.Dispose();
        }

        base.Dispose(disposing);
    }
}
