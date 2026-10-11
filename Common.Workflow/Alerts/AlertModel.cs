using System.Globalization;
using System.Text.Json.Serialization;

namespace Common.Workflow.Alerts
{
    /// <summary>
    /// Tipo de alerta. Conserva la numeracion de GLPI (<c>Alert::THRESHOLD</c>, <c>END</c>, <c>NOTICE</c>,
    /// <c>PERIODICITY</c>) porque es la columna <c>kind</c> del ledger.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AlertKind>))]
    public enum AlertKind : short
    {
        /// <summary>Valor por debajo de un umbral (consumibles, existencias).</summary>
        Threshold = 1,

        /// <summary>Fin de vigencia (contrato, garantia, certificado).</summary>
        End = 2,

        /// <summary>Preaviso antes del fin.</summary>
        Notice = 3,

        /// <summary>Ocurrencia periodica (calibracion, revision).</summary>
        Periodicity = 6,
    }

    /// <summary>
    /// Que debe hacer el producto con una decision.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AlertAction>))]
    public enum AlertAction
    {
        /// <summary>No hacer nada; la razon dice por que.</summary>
        None,

        /// <summary>Reclamar el aviso en el ledger y, si se gana, notificar.</summary>
        Notify,

        /// <summary>Borrar la entrada de umbral del ledger: el valor ya no esta por debajo.</summary>
        Clear,
    }

    /// <summary>
    /// Razones de una <see cref="AlertDecision"/>. Son contrato publico (van a la bitacora de corridas).
    /// </summary>
    public static class AlertReasons
    {
        /// <summary>Toca avisar por primera vez en el ciclo.</summary>
        public const string Due = "due";

        /// <summary>Ya se aviso en el ciclo y toca repetir.</summary>
        public const string Repeat = "repeat";

        /// <summary>Ya se aviso en el ciclo y no toca repetir.</summary>
        public const string AlreadySent = "already_sent";

        /// <summary>La ventana de aviso aun no abre.</summary>
        public const string NotYet = "not_yet";

        /// <summary>La fecha objetivo paso hace mas de <see cref="AlertPolicy.StopAfterDays"/> dias.</summary>
        public const string Stale = "stale";

        /// <summary>El valor del umbral no esta por debajo.</summary>
        public const string NotBelow = "not_below";

        /// <summary>El umbral esta desactivado (negativo).</summary>
        public const string Disabled = "disabled";

        /// <summary>El sujeto ya no tiene vigencia (<c>ValidUntil</c> o <c>Until</c> rebasados).</summary>
        public const string OutOfValidity = "out_of_validity";
    }

    /// <summary>
    /// Politica de aviso de un tipo de elemento.
    /// </summary>
    /// <param name="LeadDays">Dias antes de la fecha objetivo en que abre la ventana; 0 o mas.</param>
    /// <param name="RepeatEveryDays">Repetir cada N dias dentro del ciclo (1 o mas); nulo = una vez por ciclo.</param>
    /// <param name="StopAfterDays">
    /// Dejar de avisar N dias despues de la fecha objetivo (0 o mas); nulo = avisar tarde una vez, como GLPI.
    /// </param>
    public sealed record AlertPolicy(int LeadDays, int? RepeatEveryDays = null, int? StopAfterDays = null);

    /// <summary>
    /// Clave de una entrada del ledger: un aviso por elemento, tipo y ciclo.
    /// </summary>
    /// <param name="ItemType">Tipo de elemento (<c>contract</c>, <c>asset.warranty</c>...).</param>
    /// <param name="ItemId">Id publico del elemento, como texto.</param>
    /// <param name="Kind">Tipo de alerta.</param>
    /// <param name="CycleKey">
    /// Ciclo: la fecha objetivo en <c>yyyy-MM-dd</c> (con prefijo opcional del sujeto periodico) o
    /// <c>below</c> para umbrales.
    /// </param>
    public sealed record AlertKey(string ItemType, string ItemId, AlertKind Kind, string CycleKey)
    {
        /// <summary>Clave de ciclo de los umbrales: no tienen fecha.</summary>
        public const string BelowCycle = "below";

        /// <summary>
        /// Clave de ciclo de una fecha: ISO <c>yyyy-MM-dd</c>, cultura invariante.
        /// </summary>
        /// <param name="date">Fecha objetivo del ciclo.</param>
        public static string CycleOf(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Algo que puede vencer o agotarse. El producto lo arma con sus datos y C04 decide si toca avisar.
    /// </summary>
    /// <param name="ItemType">Tipo de elemento.</param>
    /// <param name="ItemId">Id publico del elemento.</param>
    /// <param name="Kind">Tipo de alerta.</param>
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "subject")]
    [JsonDerivedType(typeof(DateAlertSubject), "date")]
    [JsonDerivedType(typeof(PeriodicAlertSubject), "periodic")]
    [JsonDerivedType(typeof(ThresholdAlertSubject), "threshold")]
    public abstract record AlertSubject(string ItemType, string ItemId, AlertKind Kind);

    /// <summary>
    /// Sujeto con una fecha objetivo: fin (<see cref="AlertKind.End"/>) o preaviso (<see cref="AlertKind.Notice"/>).
    /// </summary>
    /// <param name="ItemType">Tipo de elemento.</param>
    /// <param name="ItemId">Id publico del elemento.</param>
    /// <param name="Kind"><see cref="AlertKind.End"/> o <see cref="AlertKind.Notice"/>.</param>
    /// <param name="TargetDate">Fecha objetivo (local de la sucursal).</param>
    /// <param name="CycleDate">
    /// Fecha que identifica el ciclo; nula = <paramref name="TargetDate"/>. El preaviso usa el fin del
    /// periodo para agrupar fin y preaviso del mismo periodo.
    /// </param>
    /// <param name="ValidUntil">Si se define, no se avisa a partir de esta fecha (el preaviso muere con el fin).</param>
    public sealed record DateAlertSubject(string ItemType, string ItemId, AlertKind Kind,
        DateOnly TargetDate, DateOnly? CycleDate = null, DateOnly? ValidUntil = null)
        : AlertSubject(ItemType, ItemId, Kind);

    /// <summary>
    /// Sujeto periodico: ocurrencias <c>FirstOccurrence.AddMonths(k * PeriodMonths)</c>, cada una calculada
    /// desde la primera (nunca acumulando).
    /// </summary>
    /// <param name="ItemType">Tipo de elemento.</param>
    /// <param name="ItemId">Id publico del elemento.</param>
    /// <param name="FirstOccurrence">Primera ocurrencia.</param>
    /// <param name="PeriodMonths">Periodo en meses; 1 o mas.</param>
    /// <param name="Until">Ultima fecha en que puede caer una ocurrencia; nula = sin fin.</param>
    /// <param name="CyclePrefix">
    /// Prefijo de la clave de ciclo (<c>prefijo:yyyy-MM-dd</c>) para distinguir dos series periodicas del
    /// mismo elemento, como la de un contrato y la de su preaviso. Nulo = sin prefijo.
    /// </param>
    public sealed record PeriodicAlertSubject(string ItemType, string ItemId,
        DateOnly FirstOccurrence, int PeriodMonths, DateOnly? Until = null, string? CyclePrefix = null)
        : AlertSubject(ItemType, ItemId, AlertKind.Periodicity);

    /// <summary>
    /// Sujeto de umbral: avisa mientras el valor este en o por debajo del umbral.
    /// </summary>
    /// <param name="ItemType">Tipo de elemento.</param>
    /// <param name="ItemId">Id publico del elemento.</param>
    /// <param name="CurrentValue">Valor actual (existencias libres, por ejemplo).</param>
    /// <param name="Threshold">Umbral; negativo = desactivado.</param>
    public sealed record ThresholdAlertSubject(string ItemType, string ItemId,
        decimal CurrentValue, decimal Threshold)
        : AlertSubject(ItemType, ItemId, AlertKind.Threshold);

    /// <summary>
    /// Entrada del ledger para un ciclo: cuando se aviso por primera y ultima vez, y cuantas veces.
    /// </summary>
    /// <param name="Key">Clave del aviso.</param>
    /// <param name="FirstSentOn">Fecha local del primer aviso.</param>
    /// <param name="LastSentOn">Fecha local del ultimo aviso.</param>
    /// <param name="SendCount">Avisos enviados en el ciclo.</param>
    public sealed record AlertLedgerEntry(AlertKey Key, DateOnly FirstSentOn, DateOnly LastSentOn, int SendCount);

    /// <summary>
    /// Decision de <see cref="AlertEvaluator.Evaluate"/>. No es un error: "no toca avisar" es un resultado normal.
    /// </summary>
    /// <param name="Key">Clave del ciclo evaluado.</param>
    /// <param name="Action">Que hacer.</param>
    /// <param name="Reason">Por que; uno de <see cref="AlertReasons"/>.</param>
    /// <param name="WindowOpensOn">
    /// Para sujetos de fecha y periodicos, el dia en que abre (o abrio) la ventana del ciclo evaluado; nulo
    /// en umbrales.
    /// </param>
    public sealed record AlertDecision(AlertKey Key, AlertAction Action, string Reason, DateOnly? WindowOpensOn);

    /// <summary>
    /// Resumen de una corrida de <see cref="AlertRunner.RunAsync"/>, para la bitacora de corridas.
    /// </summary>
    /// <param name="Evaluated">Sujetos evaluados.</param>
    /// <param name="Notified">Avisos reclamados y notificados.</param>
    /// <param name="Skipped">Sujetos sin aviso: no tocaba, o el reclamo lo gano otra corrida.</param>
    /// <param name="Cleared">Umbrales limpiados.</param>
    public sealed record AlertRunSummary(int Evaluated, int Notified, int Skipped, int Cleared);
}
