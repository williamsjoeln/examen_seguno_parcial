using System.Data;
using Microsoft.Data.SqlClient;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Excepciones;

namespace SmartEvent.Infraestructura.Datos;

/// <summary>
/// Acceso a datos de los catalogos de clientes, salones y recursos.
///
/// Todos los metodos invocan procedimientos almacenados con parametros
/// tipados. La deteccion de duplicados y la restriccion de no inactivar
/// elementos en uso viven en SQL Server, no aqui: asi se cumplen aunque se
/// llame al procedimiento desde fuera de la aplicacion.
/// </summary>
public sealed class CatalogoRepositorio : ICatalogoRepositorio
{
    private readonly IFabricaConexion _fabrica;
    private readonly IRegistradorSeguro _registro;

    public CatalogoRepositorio(IFabricaConexion fabrica, IRegistradorSeguro registro)
    {
        _fabrica = fabrica ?? throw new ArgumentNullException(nameof(fabrica));
        _registro = registro ?? throw new ArgumentNullException(nameof(registro));
    }

    // =========================== CLIENTES ===========================

    public async Task<IReadOnlyList<Cliente>> ConsultarClientesAsync(
        string? texto, bool soloActivos, CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Cliente_Consultar", conexion);

            comando.Agregar("@Texto", SqlDbType.NVarChar, 150, Normalizar(texto));
            comando.Agregar("@SoloActivos", SqlDbType.Bit, soloActivos);

            var lista = new List<Cliente>();
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                lista.Add(new Cliente
                {
                    IdCliente         = lector.Entero("IdCliente"),
                    Identificacion    = lector.Texto("Identificacion"),
                    Nombres           = lector.Texto("Nombres"),
                    Email             = lector.Texto("Email"),
                    Telefono          = lector.TextoNulo("Telefono"),
                    Estado            = lector.Booleano("Estado"),
                    FechaCreacion     = lector.FechaHora("FechaCreacion"),
                    FechaModificacion = lector.FechaHoraNula("FechaModificacion")
                });
            }

            return lista;
        }
        catch (SqlException ex)
        {
            _registro.Error("Error de SQL al consultar clientes.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public async Task<int> GuardarClienteAsync(Cliente cliente, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(cliente);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Cliente_Guardar", conexion);

            comando.Agregar("@IdCliente", SqlDbType.Int, cliente.IdCliente > 0 ? cliente.IdCliente : null);
            comando.Agregar("@Identificacion", SqlDbType.VarChar, 20, cliente.Identificacion.Trim());
            comando.Agregar("@Nombres", SqlDbType.NVarChar, 150, cliente.Nombres.Trim());
            comando.Agregar("@Email", SqlDbType.VarChar, 150, cliente.Email.Trim());
            comando.Agregar("@Telefono", SqlDbType.VarChar, 20, Normalizar(cliente.Telefono));

            var salidaId = comando.AgregarSalida("@IdResultado", SqlDbType.Int);
            comando.AgregarSalida("@Mensaje", SqlDbType.NVarChar, 300);

            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            var id = salidaId.Salida<int>()
                ?? throw new ExcepcionNegocio("No se pudo guardar el cliente. Verifique los datos e intente nuevamente.");

            _registro.Informacion($"Cliente guardado. Id={id}.");
            return id;
        }
        catch (SqlException ex)
        {
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public Task CambiarEstadoClienteAsync(int idCliente, bool estado, CancellationToken cancelacion) =>
        CambiarEstadoAsync("evt.sp_Cliente_CambiarEstado", "@IdCliente", idCliente, estado, cancelacion);

    // =========================== SALONES ===========================

    public async Task<IReadOnlyList<Salon>> ConsultarSalonesAsync(
        string? texto, bool soloActivos, CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Salon_Consultar", conexion);

            comando.Agregar("@Texto", SqlDbType.NVarChar, 150, Normalizar(texto));
            comando.Agregar("@SoloActivos", SqlDbType.Bit, soloActivos);

            var lista = new List<Salon>();
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                lista.Add(new Salon
                {
                    IdSalon           = lector.Entero("IdSalon"),
                    Nombre            = lector.Texto("Nombre"),
                    Ubicacion         = lector.TextoNulo("Ubicacion"),
                    Capacidad         = lector.Entero("Capacidad"),
                    TarifaBase        = lector.Decimal("TarifaBase"),
                    Estado            = lector.Booleano("Estado"),
                    FechaCreacion     = lector.FechaHora("FechaCreacion"),
                    FechaModificacion = lector.FechaHoraNula("FechaModificacion")
                });
            }

            return lista;
        }
        catch (SqlException ex)
        {
            _registro.Error("Error de SQL al consultar salones.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public async Task<int> GuardarSalonAsync(Salon salon, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(salon);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Salon_Guardar", conexion);

            comando.Agregar("@IdSalon", SqlDbType.Int, salon.IdSalon > 0 ? salon.IdSalon : null);
            comando.Agregar("@Nombre", SqlDbType.NVarChar, 100, salon.Nombre.Trim());
            comando.Agregar("@Ubicacion", SqlDbType.NVarChar, 150, Normalizar(salon.Ubicacion));
            comando.Agregar("@Capacidad", SqlDbType.Int, salon.Capacidad);
            comando.Agregar("@TarifaBase", SqlDbType.Decimal, salon.TarifaBase).SetPrecision(12, 2);

            var salidaId = comando.AgregarSalida("@IdResultado", SqlDbType.Int);
            comando.AgregarSalida("@Mensaje", SqlDbType.NVarChar, 300);

            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            var id = salidaId.Salida<int>()
                ?? throw new ExcepcionNegocio("No se pudo guardar el salon. Verifique los datos e intente nuevamente.");

            _registro.Informacion($"Salon guardado. Id={id}.");
            return id;
        }
        catch (SqlException ex)
        {
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public Task CambiarEstadoSalonAsync(int idSalon, bool estado, CancellationToken cancelacion) =>
        CambiarEstadoAsync("evt.sp_Salon_CambiarEstado", "@IdSalon", idSalon, estado, cancelacion);

    // =========================== RECURSOS ===========================

    public async Task<IReadOnlyList<Recurso>> ConsultarRecursosAsync(
        string? texto, bool soloActivos, CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Recurso_Consultar", conexion);

            comando.Agregar("@Texto", SqlDbType.NVarChar, 150, Normalizar(texto));
            comando.Agregar("@SoloActivos", SqlDbType.Bit, soloActivos);

            var lista = new List<Recurso>();
            await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);

            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                lista.Add(new Recurso
                {
                    IdRecurso         = lector.Entero("IdRecurso"),
                    Nombre            = lector.Texto("Nombre"),
                    Tipo              = lector.Texto("Tipo"),
                    StockTotal        = lector.Entero("StockTotal"),
                    PrecioUnitario    = lector.Decimal("PrecioUnitario"),
                    Estado            = lector.Booleano("Estado"),
                    FechaCreacion     = lector.FechaHora("FechaCreacion"),
                    FechaModificacion = lector.FechaHoraNula("FechaModificacion")
                });
            }

            return lista;
        }
        catch (SqlException ex)
        {
            _registro.Error("Error de SQL al consultar recursos.", ex);
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public async Task<int> GuardarRecursoAsync(Recurso recurso, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(recurso);

        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando("evt.sp_Recurso_Guardar", conexion);

            comando.Agregar("@IdRecurso", SqlDbType.Int, recurso.IdRecurso > 0 ? recurso.IdRecurso : null);
            comando.Agregar("@Nombre", SqlDbType.NVarChar, 100, recurso.Nombre.Trim());
            comando.Agregar("@Tipo", SqlDbType.NVarChar, 40, recurso.Tipo.Trim());
            comando.Agregar("@StockTotal", SqlDbType.Int, recurso.StockTotal);
            comando.Agregar("@PrecioUnitario", SqlDbType.Decimal, recurso.PrecioUnitario).SetPrecision(12, 2);

            var salidaId = comando.AgregarSalida("@IdResultado", SqlDbType.Int);
            comando.AgregarSalida("@Mensaje", SqlDbType.NVarChar, 300);

            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            var id = salidaId.Salida<int>()
                ?? throw new ExcepcionNegocio("No se pudo guardar el recurso. Verifique los datos e intente nuevamente.");

            _registro.Informacion($"Recurso guardado. Id={id}.");
            return id;
        }
        catch (SqlException ex)
        {
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    public Task CambiarEstadoRecursoAsync(int idRecurso, bool estado, CancellationToken cancelacion) =>
        CambiarEstadoAsync("evt.sp_Recurso_CambiarEstado", "@IdRecurso", idRecurso, estado, cancelacion);

    // =========================== APOYO ===========================

    /// <summary>
    /// Los tres catalogos comparten la misma forma de activacion e
    /// inactivacion LOGICA: nunca se borra un registro, solo se marca. Asi no
    /// se rompe el historial de reservas.
    /// </summary>
    private async Task CambiarEstadoAsync(
        string procedimiento, string nombreParametroId, int id, bool estado, CancellationToken cancelacion)
    {
        try
        {
            await using var conexion = await _fabrica.AbrirAsync(cancelacion).ConfigureAwait(false);
            await using var comando = _fabrica.CrearComando(procedimiento, conexion);

            comando.Agregar(nombreParametroId, SqlDbType.Int, id);
            comando.Agregar("@Estado", SqlDbType.Bit, estado);
            comando.AgregarSalida("@Mensaje", SqlDbType.NVarChar, 300);

            await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);

            _registro.Informacion($"{procedimiento}: id={id} estado={(estado ? "activo" : "inactivo")}.");
        }
        catch (SqlException ex)
        {
            throw TraductorErroresSql.Traducir(ex);
        }
    }

    private static string? Normalizar(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
