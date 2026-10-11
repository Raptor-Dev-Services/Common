using System.Text.Json.Serialization;

namespace Common.Workflow.Folios
{
    /// <summary>
    /// Ambito de un contador de folios: cuantos contadores independientes hay para una misma secuencia.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FolioScope>))]
    public enum FolioScope
    {
        /// <summary>Un solo contador en todo el producto (p. ej. tickets de soporte a plataforma).</summary>
        Global,

        /// <summary>Un contador por tenant.</summary>
        Tenant,

        /// <summary>Un contador por sucursal de un tenant.</summary>
        Branch,
    }

    /// <summary>
    /// Si el folio admite huecos o tiene que ser consecutivo.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FolioMode>))]
    public enum FolioMode
    {
        /// <summary>
        /// Por omision. El numero se asigna en una transaccion corta propia: si la transaccion de negocio
        /// falla, ese numero se pierde (hueco), pero las altas del ambito no se serializan.
        /// </summary>
        AllowGaps,

        /// <summary>
        /// Solo cuando una norma lo exige. El numero se asigna dentro de la transaccion de negocio: un
        /// rollback lo devuelve, a cambio de bloquear el ambito hasta el COMMIT.
        /// </summary>
        Gapless,
    }

    /// <summary>
    /// Cada cuanto reinicia el contador.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FolioReset>))]
    public enum FolioReset
    {
        /// <summary>Por omision: nunca reinicia; la clave no lleva periodo.</summary>
        Never,

        /// <summary>Reinicia cada ano local de la sucursal/tenant; el ano forma parte de la clave.</summary>
        Yearly,
    }
}
