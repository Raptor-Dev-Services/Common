namespace Common.Messaging
{
    /// <summary>
    /// Peticion que se envia con <see cref="IMediator.Send{TResponse}"/> y que atiende un
    /// unico <see cref="IRequestHandler{TRequest, TResponse}"/>.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
    public interface IRequest<out TResponse>
    { }

    /// <summary>
    /// Siguiente paso de la cadena de <see cref="IPipelineBehavior{TRequest, TResponse}"/>:
    /// el siguiente behavior o, al final, el handler.
    /// </summary>
    /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
    /// <returns>La respuesta del resto de la cadena.</returns>
    public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

    /// <summary>
    /// Atiende una peticion de tipo <typeparamref name="TRequest"/>.
    /// </summary>
    /// <typeparam name="TRequest">Tipo de la peticion.</typeparam>
    /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
    public interface IRequestHandler<in TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        /// <summary>
        /// Atiende la peticion.
        /// </summary>
        /// <param name="request">Peticion a atender.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La respuesta.</returns>
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Recibe las notificaciones de tipo <typeparamref name="TNotification"/> que se publican
    /// con <see cref="IMediator.Publish{TNotification}"/>.
    /// </summary>
    /// <typeparam name="TNotification">Tipo de la notificacion.</typeparam>
    public interface INotificationHandler<in TNotification>
        where TNotification : INotification
    {
        /// <summary>
        /// Procesa la notificacion.
        /// </summary>
        /// <param name="notification">Notificacion publicada.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>Tarea que termina al procesarla.</returns>
        Task Handle(TNotification notification, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Paso que envuelve la atencion de una peticion (log, validacion, transacciones).
    /// </summary>
    /// <typeparam name="TRequest">Tipo de la peticion.</typeparam>
    /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
    public interface IPipelineBehavior<in TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        /// <summary>
        /// Ejecuta el paso. Debe invocar <paramref name="next"/> para que la peticion llegue al handler.
        /// </summary>
        /// <param name="request">Peticion en curso.</param>
        /// <param name="next">Resto de la cadena.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La respuesta.</returns>
        Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Mediador en proceso: envia peticiones a su handler y publica notificaciones.
    /// </summary>
    public interface IMediator
    {
        /// <summary>
        /// Envia una peticion a su handler, pasando por los <see cref="IPipelineBehavior{TRequest, TResponse}"/> registrados.
        /// </summary>
        /// <typeparam name="TResponse">Tipo de la respuesta.</typeparam>
        /// <param name="request">Peticion a enviar.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>La respuesta del handler.</returns>
        Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Publica una notificacion a todos sus handlers.
        /// </summary>
        /// <typeparam name="TNotification">Tipo de la notificacion.</typeparam>
        /// <param name="notification">Notificacion a publicar.</param>
        /// <param name="cancellationToken">Token de cancelacion.</param>
        /// <returns>Tarea que termina cuando todos los handlers terminan.</returns>
        Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
            where TNotification : INotification;
    }
}
