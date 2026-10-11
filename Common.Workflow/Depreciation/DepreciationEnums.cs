using System.Text.Json.Serialization;

namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Metodo de depreciacion. Conserva la numeracion de GLPI (<c>sink_type</c>) para que una
    /// migracion de datos no tenga que traducir valores.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<DepreciationMethod>))]
    public enum DepreciationMethod
    {
        /// <summary>Saldo decreciente con cambio a linea recta (GLPI <c>sink_type = 1</c>).</summary>
        DecliningBalance = 1,

        /// <summary>Linea recta (GLPI <c>sink_type = 2</c>).</summary>
        StraightLine = 2,
    }

    /// <summary>
    /// Como se prorratea el primer ejercicio en linea recta. El saldo decreciente siempre prorratea
    /// por <see cref="Months"/> desde la fecha de compra.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<FirstYearProration>))]
    public enum FirstYearProration
    {
        /// <summary>
        /// <c>(30 * m + d) / 360</c>: <c>m</c> meses completos y <c>d</c> dias restantes desde el alta hasta el
        /// cierre del ejercicio, con tope 1 (GLPI <c>linearAmortise</c>).
        /// </summary>
        Days360,

        /// <summary>Meses del ejercicio contando el mes de inicio, entre 12 (GLPI <c>Amort</c>).</summary>
        Months,

        /// <summary>
        /// Meses completos de uso dentro del ejercicio, entre 12; si el alta es el dia 1, ese mes cuenta
        /// (LISR art. 31).
        /// </summary>
        FullMonthsOfUse,

        /// <summary>Sin prorrata: el primer ejercicio carga la anualidad completa.</summary>
        None,
    }

    /// <summary>
    /// Como se calcula el valor en libros dentro de un ejercicio.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<BookValueMode>))]
    public enum BookValueMode
    {
        /// <summary>
        /// Baja mes a mes: valor de apertura del ejercicio menos la parte del cargo de los meses ya
        /// transcurridos. Es el modo por omision.
        /// </summary>
        Monthly,

        /// <summary>Escalonado: el valor solo cambia el dia de cierre de cada ejercicio.</summary>
        Stepwise,
    }
}
