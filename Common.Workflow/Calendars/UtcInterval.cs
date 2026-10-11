namespace Common.Workflow.Calendars
{
    /// <summary>
    /// Intervalo semiabierto <c>[inicio, fin)</c> en UTC.
    /// </summary>
    /// <param name="StartUtc">Inicio, incluido.</param>
    /// <param name="EndUtc">Fin, excluido.</param>
    public readonly record struct UtcInterval(DateTime StartUtc, DateTime EndUtc)
    {
        /// <summary>
        /// Duracion real del intervalo.
        /// </summary>
        public TimeSpan Duration => EndUtc - StartUtc;
    }
}
