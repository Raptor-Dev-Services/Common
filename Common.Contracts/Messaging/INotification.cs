namespace Common.Messaging
{
    /// <summary>
    /// Marca un mensaje que se puede publicar con <c>IMediator.Publish</c> y que reciben
    /// todos los <c>INotificationHandler</c> registrados para su tipo.
    /// </summary>
    public interface INotification { }

    /// <summary>
    /// Marca la respuesta de un caso de uso. Es una <see cref="INotification"/>: el pipeline
    /// del mediador la publica al terminar para que la reciban los presenters.
    /// </summary>
    public interface IResponse : INotification { }
}
