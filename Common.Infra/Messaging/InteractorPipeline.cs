using Common.Results;
using Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace Common.Messaging
{
    /// <summary>
    /// Behavior del mediador que registra cada caso de uso y publica su respuesta.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Registra la peticion (Information) y, si sale bien, la respuesta (Information), ambas
    /// enmascaradas con <see cref="SensitiveDataMasker"/>. Si la respuesta es un <see cref="IFailure"/>,
    /// registra su mensaje como Warning en lugar de la respuesta.
    /// </para>
    /// <para>
    /// Despues publica la respuesta con <see cref="IMediator.Publish{TNotification}"/>, para que la
    /// reciban los presenters (<see cref="INotificationHandler{TNotification}"/> de <typeparamref name="TResponse"/>),
    /// exito o fallo. La publicacion no recibe el token de cancelacion.
    /// </para>
    /// <para>
    /// Si el handler o un presenter lanzan, registra la excepcion (Error si es
    /// <see cref="BusinessRuleException"/>, Critical en otro caso, con el mensaje de la excepcion mas
    /// interna) y la relanza.
    /// </para>
    /// <para>
    /// Solo se aplica a peticiones cuya respuesta es <see cref="IResponse"/>. <c>AddMediator</c> lo registra
    /// para todas.
    /// </para>
    /// </remarks>
    /// <typeparam name="TRequest">Tipo de la peticion.</typeparam>
    /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
    /// <param name="Mediator">Mediador con el que se publica la respuesta.</param>
    /// <param name="Logger">Logger donde se registran peticion, respuesta y errores.</param>
    public record InteractorPipeline<TRequest, TResponse>(IMediator Mediator, ILogger<InteractorPipeline<TRequest, TResponse>> Logger) : IPipelineBehavior<TRequest, TResponse>
            where TRequest : IRequest<TResponse>
            where TResponse : IResponse
    {
        private readonly Type _requestType = typeof(TRequest);
        /// <summary>
        /// Registra la peticion, invoca el resto de la cadena, registra y publica la respuesta.
        /// </summary>
        /// <param name="request">Peticion en curso.</param>
        /// <param name="next">Resto de la cadena (otros behaviors o el handler).</param>
        /// <param name="cancellationToken">Token de cancelacion. No se pasa a la publicacion.</param>
        /// <returns>La respuesta del handler, sin cambios.</returns>
        public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
        {
            TResponse response;
            // Enmascarado ANTES de destructurar. Sin esto, `{@Request}` escribia el objeto
            // entero en claro: una peticion de login dejaba la contrasena en el log, y su
            // respuesta los dos tokens. El nivel configurado en produccion es Information,
            // asi que llegaba a consola --capturada por el runtime del contenedor-- y a Seq.
            Logger.LogInformation("{@Request}", SensitiveDataMasker.Enmascarar(request));
            try
            {
                response = await next().ConfigureAwait(false);
                if (response is IFailure failure)
                {
                    var typeOfResponse = typeof(TResponse);
                    var genericArgs = typeOfResponse.GetGenericArguments();
                    var responseTypeName = genericArgs.Length > 0 ? genericArgs[0].Name : typeOfResponse.Name;
                    Logger.LogWarning("{ResponseType}: {FailureMessage}", responseTypeName, failure.Message);
                }
                else
                {
                    //object? data = ((dynamic)response).Data;
                    // La respuesta importa tanto o mas que la peticion: es la que lleva los
                    // tokens recien emitidos.
                    Logger.LogInformation("{@Response}", SensitiveDataMasker.Enmascarar(response));
                }
                await Mediator.Publish(response).ConfigureAwait(false);
            }
            catch (BusinessRuleException ex)
            {
                Logger.LogError(ex, "Error: {ErrorMessage}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                var innerEx = ex;
                while (innerEx.InnerException != null) innerEx = innerEx.InnerException!;
                Logger.LogCritical(ex, "Error critico: {ErrorMessage}", innerEx.Message);
                throw;
            }
            return response;
        }
    }
}


