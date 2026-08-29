using Microsoft.Extensions.DependencyInjection;
using SmartEvent.Aplicacion.Contratos;
using SmartEvent.Infraestructura.Datos;
using SmartEvent.Infraestructura.Registro;

namespace SmartEvent.Infraestructura;

/// <summary>
/// Registro de la capa de infraestructura en el contenedor de dependencias.
///
/// Que este metodo exista aqui, y no en el proyecto de Windows Forms, es lo que
/// permite que la presentacion no necesite conocer NINGUNA clase concreta de
/// acceso a datos: solo llama a AgregarInfraestructura y a partir de ahi pide
/// interfaces.
/// </summary>
public static class RegistroServiciosInfraestructura
{
    public static IServiceCollection AgregarInfraestructura(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // El registrador es singleton: un unico archivo de log por dia y un
        // unico bloqueo de escritura compartido.
        servicios.AddSingleton<IRegistradorSeguro, RegistradorArchivo>();

        // La fabrica es singleton porque solo guarda configuracion; las
        // conexiones que crea son nuevas en cada llamada y las cierra quien las pide.
        servicios.AddSingleton<IFabricaConexion, FabricaConexionSql>();

        // Los repositorios no tienen estado, asi que pueden ser singleton.
        servicios.AddSingleton<IUsuarioRepositorio, UsuarioRepositorio>();
        servicios.AddSingleton<ICatalogoRepositorio, CatalogoRepositorio>();
        servicios.AddSingleton<IReservaRepositorio, ReservaRepositorio>();
        servicios.AddSingleton<IAuditoriaRepositorio, AuditoriaRepositorio>();

        return servicios;
    }
}
