using Common.Results;

namespace Common.Workflow.Depreciation
{
    /// <summary>
    /// Calcula la tabla de depreciacion de un activo (linea recta o saldo decreciente con cambio a lineal).
    /// Calculo puro en <see cref="decimal"/>: sin reloj, sin E/S y sin estado.
    /// </summary>
    public static class DepreciationCalculator
    {
        /// <summary>
        /// Valida los parametros y calcula la tabla por ejercicio.
        /// </summary>
        /// <param name="parameters">Parametros del activo.</param>
        /// <returns>
        /// La tabla, o un <see cref="DefinitionFailure{T}"/> con todos los errores de los parametros
        /// (codigos <c>depreciation.*</c>).
        /// </returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="parameters"/> es nulo.</exception>
        public static Result<DepreciationSchedule> Schedule(DepreciationParameters parameters)
        {
            ArgumentNullException.ThrowIfNull(parameters);

            var errors = Validate(parameters);
            if (errors.Count > 0)
                return new DefinitionFailure<DepreciationSchedule>(errors);

            var fiscalYearEnd = parameters.FiscalYearEnd ?? FiscalYearEnd.December31;
            var depreciableBase = DepreciableBaseOf(parameters);
            var periods = parameters.Method == DepreciationMethod.StraightLine
                ? StraightLine(parameters, fiscalYearEnd, depreciableBase)
                : DecliningBalance(parameters, fiscalYearEnd, depreciableBase);

            return Result.OK(new DepreciationSchedule(periods, parameters.Cost, depreciableBase,
                parameters.Cost - depreciableBase, periods[^1].End, parameters.Rounding));
        }

        private static decimal DepreciableBaseOf(DepreciationParameters p)
            => p.DepreciableCap is { } cap && cap < p.Cost ? cap : p.Cost;

        private static List<DefinitionError> Validate(DepreciationParameters p)
        {
            var errors = new List<DefinitionError>();

            if (!Enum.IsDefined(p.Method))
                errors.Add(new("depreciation.invalid_method", "Method", $"Metodo desconocido: {(int)p.Method}."));

            var costOk = p.Cost > 0;
            if (!costOk)
                errors.Add(new("depreciation.invalid_cost", "Cost", "El costo debe ser mayor que cero."));

            var capOk = p.DepreciableCap is null || p.DepreciableCap > 0;
            if (!capOk)
                errors.Add(new("depreciation.invalid_cap", "DepreciableCap", "El tope deducible debe ser mayor que cero."));

            if (p.SalvageValue < 0)
                errors.Add(new("depreciation.invalid_salvage", "SalvageValue", "El valor residual no puede ser negativo."));
            else if (costOk && capOk && p.SalvageValue >= DepreciableBaseOf(p))
                errors.Add(new("depreciation.invalid_salvage", "SalvageValue", "El valor residual debe ser menor que la base depreciable."));

            if ((p.LifeYears is null) == (p.AnnualRate is null))
                errors.Add(new("depreciation.life_or_rate", "LifeYears", "Se requiere exactamente uno de LifeYears o AnnualRate."));

            if (p.AnnualRate is { } rate && (rate <= 0 || rate > 1))
                errors.Add(new("depreciation.invalid_rate", "AnnualRate", "La tasa anual debe estar en (0, 1]."));

            if (p.LifeYears is { } life && life <= 0)
                errors.Add(new("depreciation.invalid_life", "LifeYears", "La vida util debe ser mayor que cero."));

            if (p.Method == DepreciationMethod.DecliningBalance)
            {
                if (p.LifeYears is not { } years)
                    errors.Add(new("depreciation.invalid_life", "LifeYears", "El saldo decreciente requiere LifeYears (no AnnualRate)."));
                else if (years > 0 && (years != decimal.Truncate(years) || years < 2))
                    errors.Add(new("depreciation.invalid_life", "LifeYears", "El saldo decreciente requiere una vida util entera de al menos 2 anios."));

                if (p.DecliningCoefficient <= 1)
                    errors.Add(new("depreciation.invalid_coefficient", "DecliningCoefficient", "El coeficiente del saldo decreciente debe ser mayor que 1."));
            }

            if (p.FiscalYearEnd is { IsValid: false } fye)
                errors.Add(new("depreciation.invalid_fiscal_year_end", "FiscalYearEnd", $"El cierre {fye.Month:00}-{fye.Day:00} no existe todos los anios."));

            if (p.Rounding is < 0 or > 28)
                errors.Add(new("depreciation.invalid_rounding", "Rounding", "Los decimales deben estar entre 0 y 28."));

            return errors;
        }

        // Linea recta: anualidad = (base - residual) / vida, o base * tasa. El primer ejercicio aplica la
        // prorrata, los siguientes la anualidad completa y el ultimo el residuo. El ultimo se decide por
        // "unidades de anualidad" consumidas (exactas), no por el valor redondeado, para que un centavo de
        // redondeo no abra un ejercicio de mas.
        private static List<DepreciationPeriod> StraightLine(DepreciationParameters p, FiscalYearEnd fye, decimal depreciableBase)
        {
            var salvage = p.SalvageValue;
            var depreciable = depreciableBase - salvage;
            decimal annuity, lifeUnits;
            if (p.LifeYears is { } years)
            {
                annuity = depreciable / years;
                lifeUnits = years;
            }
            else
            {
                annuity = depreciableBase * p.AnnualRate!.Value;
                lifeUnits = depreciable / annuity;
            }

            var periods = new List<DepreciationPeriod>();
            var value = depreciableBase;
            var start = p.InServiceDate;
            var end = fye.EndContaining(start);

            decimal factor;
            if (p.StartInNextFiscalYear)
            {
                periods.Add(new(end.Year, start, end, value, 0m, value, 0m, DepreciationMethod.StraightLine));
                (start, end) = (end.AddDays(1), fye.EndOf(end.Year + 1));
                factor = 1m;
            }
            else
            {
                factor = FirstYearFactor(p.FirstYearProration, start, end);
            }

            var used = 0m;
            while (true)
            {
                var remainingUnits = lifeUnits - used;
                var remainingValue = value - salvage;
                decimal charge, applied;
                bool last;
                if (remainingUnits <= factor)
                {
                    (charge, applied, last) = (remainingValue, remainingUnits, true);
                }
                else
                {
                    charge = Round(annuity * factor, p.Rounding);
                    applied = factor;
                    last = charge >= remainingValue;
                    if (last) charge = remainingValue;
                }

                periods.Add(new(end.Year, start, end, value, charge, value - charge, applied, DepreciationMethod.StraightLine));
                if (last) return periods;

                value -= charge;
                used += factor;
                factor = 1m;
                (start, end) = (end.AddDays(1), fye.EndOf(end.Year + 1));
            }
        }

        // Saldo decreciente (GLPI Amort): tasa = coef / vida; los primeros vida - floor(vida / coef)
        // ejercicios cargan valor * tasa (el primero por la prorrata en meses desde la compra), los
        // restantes una anualidad lineal sobre el valor al cambio, y el ultimo el residuo. La tabla dura
        // exactamente LifeYears ejercicios (mas el diferido, si StartInNextFiscalYear).
        private static List<DepreciationPeriod> DecliningBalance(DepreciationParameters p, FiscalYearEnd fye, decimal depreciableBase)
        {
            var salvage = p.SalvageValue;
            var life = p.LifeYears!.Value;
            var years = (int)life;
            var rate = p.DecliningCoefficient / life;
            var linearYears = (int)decimal.Floor(life / p.DecliningCoefficient);
            var decliningYears = years - linearYears;

            var periods = new List<DepreciationPeriod>();
            var value = depreciableBase;
            var start = p.AcquisitionDate ?? p.InServiceDate;
            var end = fye.EndContaining(start);

            decimal factor;
            if (p.StartInNextFiscalYear)
            {
                periods.Add(new(end.Year, start, end, value, 0m, value, 0m, DepreciationMethod.DecliningBalance));
                (start, end) = (end.AddDays(1), fye.EndOf(end.Year + 1));
                factor = 1m;
            }
            else
            {
                factor = FirstYearFactor(FirstYearProration.Months, start, end);
            }

            var linearAnnuity = 0m;
            for (var i = 0; i < years; i++)
            {
                var remainingValue = value - salvage;
                decimal charge;
                DepreciationMethod method;
                var applied = i == 0 ? factor : 1m;
                if (i == years - 1)
                {
                    charge = remainingValue;
                    method = linearYears > 0 ? DepreciationMethod.StraightLine : DepreciationMethod.DecliningBalance;
                }
                else if (i < decliningYears)
                {
                    charge = Round(value * rate * applied, p.Rounding);
                    method = DepreciationMethod.DecliningBalance;
                }
                else
                {
                    if (i == decliningYears)
                        linearAnnuity = Round(remainingValue / linearYears, p.Rounding);
                    charge = linearAnnuity;
                    method = DepreciationMethod.StraightLine;
                }

                var last = i == years - 1 || charge >= remainingValue;
                if (last) charge = remainingValue;

                periods.Add(new(end.Year, start, end, value, charge, value - charge, applied, method));
                if (last) break;

                value -= charge;
                (start, end) = (end.AddDays(1), fye.EndOf(end.Year + 1));
            }

            return periods;
        }

        private static decimal FirstYearFactor(FirstYearProration proration, DateOnly start, DateOnly end)
        {
            var factor = proration switch
            {
                FirstYearProration.Days360 => Days360(start, end),
                FirstYearProration.Months => ((end.Year * 12 + end.Month) - (start.Year * 12 + start.Month) + 1) / 12m,
                FirstYearProration.FullMonthsOfUse => FullMonthsOfUse(start, end) / 12m,
                _ => 1m,
            };
            return Math.Min(1m, factor);
        }

        // (30 * m + d) / 360: m meses completos desde start (cada uno desde el ancla, sin acumular) y
        // d dias que quedan hasta el cierre. 2026-03-01 -> 2026-06-30 = (3 * 30 + 29) / 360.
        private static decimal Days360(DateOnly start, DateOnly end)
        {
            var months = 0;
            while (start.AddMonths(months + 1) <= end) months++;
            var days = end.DayNumber - start.AddMonths(months).DayNumber;
            return (30m * months + days) / 360m;
        }

        // Meses de calendario completos entre start y end; si start es dia 1, su mes cuenta.
        private static int FullMonthsOfUse(DateOnly start, DateOnly end)
        {
            var first = start.Day == 1 ? start : new DateOnly(start.Year, start.Month, 1).AddMonths(1);
            var count = 0;
            while (first.AddMonths(count + 1).AddDays(-1) <= end) count++;
            return count;
        }

        private static decimal Round(decimal amount, int decimals)
            => Math.Round(amount, decimals, MidpointRounding.AwayFromZero);
    }
}
