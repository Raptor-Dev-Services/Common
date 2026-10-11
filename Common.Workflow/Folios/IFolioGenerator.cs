using System.Data.Common;

namespace Common.Workflow.Folios
{
    /// <summary>
    /// Asigna el siguiente numero de un contador en modo <b>con huecos</b>. La implementacion real (PostgreSQL)
    /// vive en la infraestructura de cada producto; Common trae <see cref="InMemoryFolioGenerator"/> para pruebas.
    /// </summary>
    public interface IFolioGenerator
    {
        /// <summary>
        /// Modo con huecos: corre en su propia transaccion corta, fuera de la de negocio. Devuelve un numero
        /// positivo, unico y creciente dentro de la clave; el primero de un ambito nuevo es 1.
        /// </summary>
        /// <param name="key">Contador.</param>
        /// <param name="ct">Token de cancelacion.</param>
        /// <returns>El numero asignado.</returns>
        ValueTask<long> NextAsync(FolioKey key, CancellationToken ct);
    }

    /// <summary>
    /// Asigna el siguiente numero de un contador en modo <b>sin huecos</b>: solo cuando una norma lo exige.
    /// </summary>
    public interface IGaplessFolioGenerator
    {
        /// <summary>
        /// Modo sin huecos: usa la transaccion de negocio y bloquea el ambito hasta el COMMIT. Si la
        /// transaccion se revierte, el numero vuelve a estar disponible.
        /// </summary>
        /// <param name="key">Contador.</param>
        /// <param name="transaction">Transaccion de negocio; obligatoria.</param>
        /// <param name="ct">Token de cancelacion.</param>
        /// <returns>El numero asignado, firme cuando la transaccion confirme.</returns>
        ValueTask<long> NextInTransactionAsync(FolioKey key, DbTransaction transaction, CancellationToken ct);
    }
}
