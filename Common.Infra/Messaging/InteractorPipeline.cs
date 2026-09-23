using Common.Results;
using Common.Exceptions;
using Microsoft.Extensions.Logging;

namespace Common.Messaging
{
    public record InteractorPipeline<TRequest, TResponse>(IMediator Mediator, ILogger<InteractorPipeline<TRequest, TResponse>> Logger) : IPipelineBehavior<TRequest, TResponse>
            where TRequest : IRequest<TResponse>
            where TResponse : IResponse
    {
        private readonly Type _requestType = typeof(TRequest);
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
                Logger.LogCritical(ex, "Error cr�tico: {ErrorMessage}", innerEx.Message);
                throw;
            }
            return response;
        }
    }
}


