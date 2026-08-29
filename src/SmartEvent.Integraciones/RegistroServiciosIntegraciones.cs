using Microsoft.Extensions.DependencyInjection;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Integraciones.Correo;
using SmartEvent.Integraciones.Ia;

namespace SmartEvent.Integraciones;

/// <summary>
/// Registro de la capa de integraciones en el contenedor de dependencias.
/// </summary>
public static class RegistroServiciosIntegraciones
{
    public static IServiceCollection AgregarIntegraciones(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddSingleton<IServicioCorreo, ServicioCorreoMailKit>();

        // IHttpClientFactory gestiona el ciclo de vida de los HttpClient y
        // recicla las conexiones. Crear un HttpClient nuevo en cada llamada
        // agota los sockets del sistema; guardarlo en un campo estatico impide
        // detectar cambios de DNS. La fabrica resuelve las dos cosas.
        servicios.AddHttpClient(ServicioAnalisisIaResponses.NombreClienteHttp, cliente =>
        {
            cliente.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            cliente.DefaultRequestHeaders.UserAgent.ParseAdd("SmartEventAI/1.0");

            // El tiempo de espera real lo controla el CancellationTokenSource
            // del servicio; este es solo un tope de seguridad superior.
            cliente.Timeout = TimeSpan.FromMinutes(3);
        });

        servicios.AddSingleton<IServicioAnalisisIa, ServicioAnalisisIaResponses>();

        return servicios;
    }
}
