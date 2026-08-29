using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Aplicacion.Sesion;
using SmartEvent.Dominio.Entidades;
using SmartEvent.Dominio.Enumeraciones;
using SmartEvent.Dominio.Excepciones;
using SmartEvent.Dominio.Reglas;

namespace SmartEvent.Aplicacion.Servicios;

/// <summary>
/// Casos de uso de los catalogos de clientes, salones y recursos.
///
/// Aplica dos capas de control antes de tocar la base de datos:
///   1. PERMISOS: mantener catalogos requiere rol ADMINISTRADOR. La comprobacion
///      esta aqui ademas de en el menu, por si algun formulario olvidara
///      ocultar un boton.
///   2. VALIDACION de formato, para dar un mensaje inmediato y claro.
///
/// La deteccion de duplicados y la prohibicion de inactivar elementos que estan
/// en uso viven en los procedimientos almacenados, no aqui: asi se cumplen
/// aunque se invoquen desde fuera de la aplicacion.
/// </summary>
public sealed class ServicioCatalogos
{
    private readonly ICatalogoRepositorio _catalogos;
    private readonly SesionUsuario _sesion;

    public ServicioCatalogos(ICatalogoRepositorio catalogos, SesionUsuario sesion)
    {
        _catalogos = catalogos ?? throw new ArgumentNullException(nameof(catalogos));
        _sesion = sesion ?? throw new ArgumentNullException(nameof(sesion));
    }

    // =========================== CLIENTES ===========================

    /// <summary>Consultar clientes esta permitido a cualquier rol autenticado.</summary>
    public Task<IReadOnlyList<Cliente>> ConsultarClientesAsync(
        string? texto, bool soloActivos, CancellationToken cancelacion) =>
        _catalogos.ConsultarClientesAsync(texto, soloActivos, cancelacion);

    public async Task<int> GuardarClienteAsync(Cliente cliente, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        _sesion.Exigir(Permiso.GestionarCatalogos);

        var problemas = new List<string>();

        if (string.IsNullOrWhiteSpace(cliente.Identificacion) || cliente.Identificacion.Trim().Length < 5)
        {
            problemas.Add("La identificacion debe tener al menos 5 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(cliente.Nombres) || cliente.Nombres.Trim().Length < 3)
        {
            problemas.Add("El nombre debe tener al menos 3 caracteres.");
        }

        if (!ReglasReserva.EmailEsValido(cliente.Email))
        {
            problemas.Add("El correo electronico no tiene un formato valido.");
        }

        Exigir(problemas);

        return await _catalogos.GuardarClienteAsync(cliente, cancelacion).ConfigureAwait(false);
    }

    public Task CambiarEstadoClienteAsync(int idCliente, bool estado, CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.GestionarCatalogos);
        return _catalogos.CambiarEstadoClienteAsync(idCliente, estado, cancelacion);
    }

    // =========================== SALONES ===========================

    public Task<IReadOnlyList<Salon>> ConsultarSalonesAsync(
        string? texto, bool soloActivos, CancellationToken cancelacion) =>
        _catalogos.ConsultarSalonesAsync(texto, soloActivos, cancelacion);

    public async Task<int> GuardarSalonAsync(Salon salon, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(salon);
        _sesion.Exigir(Permiso.GestionarCatalogos);

        var problemas = new List<string>();

        if (string.IsNullOrWhiteSpace(salon.Nombre) || salon.Nombre.Trim().Length < 3)
        {
            problemas.Add("El nombre del salon debe tener al menos 3 caracteres.");
        }

        if (salon.Capacidad <= 0)
        {
            problemas.Add("La capacidad debe ser mayor que cero.");
        }

        if (salon.TarifaBase < 0m)
        {
            problemas.Add("La tarifa base no puede ser negativa.");
        }

        Exigir(problemas);

        return await _catalogos.GuardarSalonAsync(salon, cancelacion).ConfigureAwait(false);
    }

    public Task CambiarEstadoSalonAsync(int idSalon, bool estado, CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.GestionarCatalogos);
        return _catalogos.CambiarEstadoSalonAsync(idSalon, estado, cancelacion);
    }

    // =========================== RECURSOS ===========================

    public Task<IReadOnlyList<Recurso>> ConsultarRecursosAsync(
        string? texto, bool soloActivos, CancellationToken cancelacion) =>
        _catalogos.ConsultarRecursosAsync(texto, soloActivos, cancelacion);

    public async Task<int> GuardarRecursoAsync(Recurso recurso, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(recurso);
        _sesion.Exigir(Permiso.GestionarCatalogos);

        var problemas = new List<string>();

        if (string.IsNullOrWhiteSpace(recurso.Nombre) || recurso.Nombre.Trim().Length < 3)
        {
            problemas.Add("El nombre del recurso debe tener al menos 3 caracteres.");
        }

        if (string.IsNullOrWhiteSpace(recurso.Tipo) || recurso.Tipo.Trim().Length < 3)
        {
            problemas.Add("El tipo debe tener al menos 3 caracteres.");
        }

        if (recurso.StockTotal < 0)
        {
            problemas.Add("El stock no puede ser negativo.");
        }

        if (recurso.PrecioUnitario < 0m)
        {
            problemas.Add("El precio unitario no puede ser negativo.");
        }

        Exigir(problemas);

        return await _catalogos.GuardarRecursoAsync(recurso, cancelacion).ConfigureAwait(false);
    }

    public Task CambiarEstadoRecursoAsync(int idRecurso, bool estado, CancellationToken cancelacion)
    {
        _sesion.Exigir(Permiso.GestionarCatalogos);
        return _catalogos.CambiarEstadoRecursoAsync(idRecurso, estado, cancelacion);
    }

    // =========================== APOYO ===========================

    private static void Exigir(List<string> problemas)
    {
        if (problemas.Count > 0)
        {
            throw new ExcepcionNegocio(
                "Revise los siguientes datos:" + Environment.NewLine
                + string.Join(Environment.NewLine, problemas.Select(p => "• " + p)));
        }
    }
}
