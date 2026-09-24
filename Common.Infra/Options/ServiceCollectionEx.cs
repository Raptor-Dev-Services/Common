using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Common.Options
{
    /// <summary>
    /// Registro de opciones validadas.
    /// </summary>
    public static class ServiceCollectionEx
    {
        /// <summary>
        /// Registra <typeparamref name="TOptions"/> enlazado a <paramref name="section"/>, validado con
        /// DataAnnotations al arrancar la aplicacion.
        /// </summary>
        /// <typeparam name="TOptions">Tipo de las opciones.</typeparam>
        /// <param name="services">Coleccion de servicios.</param>
        /// <param name="section">Seccion de configuracion a enlazar.</param>
        /// <returns>El constructor de opciones, para agregar mas validaciones.</returns>
        public static OptionsBuilder<TOptions> AddValidatedOptions<TOptions>(
            this IServiceCollection services,
            IConfigurationSection section)
            where TOptions : class
        {
            return services.AddOptions<TOptions>()
                .Bind(section)
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
    }
}
