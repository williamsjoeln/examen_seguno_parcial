using Microsoft.Extensions.DependencyInjection;
using SmartEvent.Aplicacion.Servicios;
using SmartEvent.Aplicacion.Sesion;

namespace SmartEvent.Aplicacion;

/// <summary>
/// Registro de la capa de aplicacion en el contenedor de dependencias.
/// </summary>
public static class RegistroServiciosAplicacion
{
    public static IServiceCollection AgregarAplicacion(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // La sesion es singleton porque representa al unico usuario que tiene
        // abierta la aplicacion de escritorio en este momento.
        servicios.AddSingleton<SesionUsuario>();

        // Los servicios no guardan estado propio, solo dependencias.
        servicios.AddSingleton<ServicioAutenticacion>();
        servicios.AddSingleton<ServicioCatalogos>();
        servicios.AddSingleton<ServicioReservas>();

        return servicios;
    }
}
