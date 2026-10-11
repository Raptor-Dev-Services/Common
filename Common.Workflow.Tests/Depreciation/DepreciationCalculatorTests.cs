using System.Text.Json;
using Common.Results;
using Common.Workflow.Depreciation;

namespace Common.Workflow.Tests.Depreciation;

/// <summary>
/// SRS-CMN-C07: depreciacion lineal y decreciente, valor en libros, tope, redondeo. Los vectores V1-V9
/// del SRS van con los mismos datos.
/// </summary>
public class DepreciationCalculatorTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static DepreciationSchedule Ok(DepreciationParameters parameters)
    {
        var result = DepreciationCalculator.Schedule(parameters);
        return Assert.IsType<SuccessResult<DepreciationSchedule>>(result).Data;
    }

    private static IReadOnlyList<DefinitionError> Errors(DepreciationParameters parameters)
    {
        var result = DepreciationCalculator.Schedule(parameters);
        Assert.IsAssignableFrom<IValidationFailure>(result);
        return Assert.IsType<DefinitionFailure<DepreciationSchedule>>(result).Errors;
    }

    private static void AssertTable(DepreciationSchedule schedule, params (int Year, decimal Charge, decimal Closing)[] expected)
    {
        Assert.Equal(expected.Length, schedule.Periods.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Year, schedule.Periods[i].FiscalYear);
            Assert.Equal(expected[i].Charge, schedule.Periods[i].Charge);
            Assert.Equal(expected[i].Closing, schedule.Periods[i].ClosingValue);
            if (i > 0) Assert.Equal(schedule.Periods[i - 1].ClosingValue, schedule.Periods[i].OpeningValue);
        }
    }

    private static readonly DepreciationParameters V1 = new(DepreciationMethod.StraightLine, 12_000m, D("2026-07-01"),
        LifeYears: 3m, FirstYearProration: FirstYearProration.Days360);

    private static readonly DepreciationParameters V3 = new(DepreciationMethod.DecliningBalance, 10_000m, D("2026-01-10"),
        LifeYears: 5m, DecliningCoefficient: 1.75m, AcquisitionDate: D("2026-01-10"));

    // --- Vectores ---

    [Fact]
    public void V1_lineal_days360_prorratea_el_primer_año_y_el_ultimo_toma_el_residuo()
    {
        var schedule = Ok(V1);

        Assert.Equal(0.5m, schedule.Periods[0].Factor);
        AssertTable(schedule, (2026, 2_000m, 10_000m), (2027, 4_000m, 6_000m), (2028, 4_000m, 2_000m), (2029, 2_000m, 0m));
        Assert.Equal(D("2029-12-31"), schedule.FullyDepreciatedOn);
        Assert.Equal(D("2026-07-01"), schedule.Periods[0].Start);
        Assert.Equal(D("2027-01-01"), schedule.Periods[1].Start);
        Assert.Equal(0.5m, schedule.Periods[^1].Factor);
    }

    [Theory]
    [InlineData("2027-12-30", BookValueMode.Stepwise, "10000")]
    [InlineData("2027-12-31", BookValueMode.Stepwise, "6000")]
    [InlineData("2026-09-30", BookValueMode.Monthly, "11000")]
    [InlineData("2027-06-30", BookValueMode.Monthly, "8000")]
    [InlineData("2027-06-29", BookValueMode.Monthly, "8333.33")]
    [InlineData("2026-06-30", BookValueMode.Monthly, "12000")]
    public void V2_valor_en_libros_a_una_fecha(string date, BookValueMode mode, string expected)
    {
        var schedule = Ok(V1);
        Assert.Equal(decimal.Parse(expected), schedule.BookValueAt(D(date), mode));
    }

    [Fact]
    public void V3_decreciente_cambia_a_lineal_y_la_suma_cuadra_al_centavo()
    {
        var schedule = Ok(V3);

        AssertTable(schedule, (2026, 3_500m, 6_500m), (2027, 2_275m, 4_225m), (2028, 1_478.75m, 2_746.25m),
            (2029, 1_373.13m, 1_373.12m), (2030, 1_373.12m, 0m));
        Assert.Equal(10_000.00m, schedule.Periods.Sum(p => p.Charge));
        Assert.Equal(DepreciationMethod.DecliningBalance, schedule.Periods[2].MethodApplied);
        Assert.Equal(DepreciationMethod.StraightLine, schedule.Periods[3].MethodApplied);
    }

    [Fact]
    public void V4_decreciente_prorratea_por_meses_desde_la_compra()
    {
        var schedule = Ok(V3 with { AcquisitionDate = D("2026-10-05") });

        Assert.Equal(0.25m, schedule.Periods[0].Factor);
        AssertTable(schedule, (2026, 875m, 9_125m), (2027, 3_193.75m, 5_931.25m), (2028, 2_075.94m, 3_855.31m),
            (2029, 1_927.66m, 1_927.65m), (2030, 1_927.65m, 0m));
        Assert.Equal(10_000.00m, schedule.Periods.Sum(p => p.Charge));
    }

    [Fact]
    public void V5_fiscal_computadora_por_meses_completos_de_uso()
    {
        // Tasa 0.30 = ejemplo del SRS (LISR art. 34 fr. VII); en el producto sale de su catalogo.
        var schedule = Ok(new DepreciationParameters(DepreciationMethod.StraightLine, 30_000m, D("2026-03-15"),
            AnnualRate: 0.30m, FirstYearProration: FirstYearProration.FullMonthsOfUse));

        Assert.Equal(0.75m, schedule.Periods[0].Factor);
        AssertTable(schedule, (2026, 6_750m, 23_250m), (2027, 9_000m, 14_250m), (2028, 9_000m, 5_250m), (2029, 5_250m, 0m));
    }

    [Fact]
    public void V6_fiscal_automovil_con_tope_deducible()
    {
        var schedule = Ok(new DepreciationParameters(DepreciationMethod.StraightLine, 250_000m, D("2026-01-01"),
            AnnualRate: 0.25m, DepreciableCap: 175_000m, FirstYearProration: FirstYearProration.FullMonthsOfUse));

        Assert.Equal(175_000m, schedule.DepreciableBase);
        Assert.Equal(75_000m, schedule.NonDeductibleAmount);
        Assert.Equal(250_000m, schedule.Cost);
        AssertTable(schedule, (2026, 43_750m, 131_250m), (2027, 43_750m, 87_500m), (2028, 43_750m, 43_750m), (2029, 43_750m, 0m));
    }

    [Fact]
    public void V9_decreciente_con_coeficiente_1_es_definicion_invalida()
    {
        var errors = Errors(V3 with { DecliningCoefficient = 1m });
        Assert.Contains(errors, e => e.Code == "depreciation.invalid_coefficient" && e.Path == "DecliningCoefficient");
    }

    // --- REQ-001: validacion ---

    [Theory]
    [InlineData("0", "0", "3", null, "depreciation.invalid_cost")]
    [InlineData("-5", "0", "3", null, "depreciation.invalid_cost")]
    [InlineData("1000", "-1", "3", null, "depreciation.invalid_salvage")]
    [InlineData("1000", "1000", "3", null, "depreciation.invalid_salvage")]
    [InlineData("1000", "0", null, null, "depreciation.life_or_rate")]
    [InlineData("1000", "0", "3", "0.1", "depreciation.life_or_rate")]
    [InlineData("1000", "0", null, "0", "depreciation.invalid_rate")]
    [InlineData("1000", "0", null, "1.01", "depreciation.invalid_rate")]
    [InlineData("1000", "0", "0", null, "depreciation.invalid_life")]
    public void Parametros_invalidos_devuelven_su_codigo(string cost, string salvage, string? life, string? rate, string code)
    {
        var errors = Errors(new DepreciationParameters(DepreciationMethod.StraightLine, decimal.Parse(cost), D("2026-01-01"),
            LifeYears: life is null ? null : decimal.Parse(life), AnnualRate: rate is null ? null : decimal.Parse(rate),
            SalvageValue: decimal.Parse(salvage)));
        Assert.Contains(errors, e => e.Code == code);
    }

    [Fact]
    public void Tasa_de_100_por_ciento_es_valida()
    {
        var schedule = Ok(new DepreciationParameters(DepreciationMethod.StraightLine, 1_000m, D("2026-01-01"),
            AnnualRate: 1m, FirstYearProration: FirstYearProration.None));
        AssertTable(schedule, (2026, 1_000m, 0m));
    }

    [Theory]
    [InlineData("4.5")]
    [InlineData("1")]
    public void Decreciente_exige_vida_entera_de_al_menos_dos_años(string life)
    {
        var errors = Errors(V3 with { LifeYears = decimal.Parse(life) });
        Assert.Contains(errors, e => e.Code == "depreciation.invalid_life");
    }

    [Fact]
    public void Decreciente_con_tasa_en_vez_de_vida_es_invalido()
    {
        var errors = Errors(V3 with { LifeYears = null, AnnualRate = 0.2m });
        Assert.Contains(errors, e => e.Code == "depreciation.invalid_life");
    }

    [Theory]
    [InlineData(2, 30)]
    [InlineData(2, 29)]
    [InlineData(13, 1)]
    [InlineData(4, 31)]
    public void Cierre_fiscal_inexistente_es_invalido(int month, int day)
    {
        var errors = Errors(V1 with { FiscalYearEnd = new FiscalYearEnd(month, day) });
        Assert.Contains(errors, e => e.Code == "depreciation.invalid_fiscal_year_end");
    }

    [Fact]
    public void Salvage_contra_la_base_con_tope_y_reporta_todos_los_errores()
    {
        var errors = Errors(new DepreciationParameters(DepreciationMethod.DecliningBalance, 250_000m, D("2026-01-01"),
            LifeYears: 2.5m, DecliningCoefficient: 0.5m, DepreciableCap: 175_000m, SalvageValue: 180_000m,
            FiscalYearEnd: new FiscalYearEnd(2, 30), Rounding: -1));

        var codes = errors.Select(e => e.Code).ToHashSet();
        Assert.Superset(new HashSet<string>
        {
            "depreciation.invalid_salvage", "depreciation.invalid_life", "depreciation.invalid_coefficient",
            "depreciation.invalid_fiscal_year_end", "depreciation.invalid_rounding",
        }, codes);
    }

    [Fact]
    public void Parametros_nulos_son_error_del_programador()
    {
        Assert.Throws<ArgumentNullException>(() => DepreciationCalculator.Schedule(null!));
    }

    // --- REQ-002: prorratas ---

    [Fact]
    public void Months_cuenta_el_mes_de_inicio()
    {
        var schedule = Ok(V1 with { InServiceDate = D("2026-10-20"), FirstYearProration = FirstYearProration.Months });
        Assert.Equal(0.25m, schedule.Periods[0].Factor);
        Assert.Equal(1_000m, schedule.Periods[0].Charge);
    }

    [Theory]
    [InlineData("2026-03-01", 10)]
    [InlineData("2026-03-02", 9)]
    [InlineData("2026-12-15", 0)]
    public void FullMonthsOfUse_cuenta_el_mes_si_empieza_el_dia_1(string start, int months)
    {
        var schedule = Ok(V1 with { InServiceDate = D(start), FirstYearProration = FirstYearProration.FullMonthsOfUse });
        Assert.Equal(months / 12m, schedule.Periods[0].Factor);
    }

    [Theory]
    [InlineData("2026-05-31", 210)] // + 7 meses cae justo en el cierre: 7 meses y 0 dias
    [InlineData("2026-01-01", 360)] // ejercicio completo: tope 1
    [InlineData("2026-12-31", 0)]   // alta el dia del cierre
    public void Days360_cuenta_meses_completos_hasta_el_cierre_inclusive(string start, int days)
    {
        var schedule = Ok(V1 with { InServiceDate = D(start) });
        Assert.Equal(days / 360m, schedule.Periods[0].Factor);
    }

    [Fact]
    public void Sin_prorrata_dura_exactamente_la_vida()
    {
        var schedule = Ok(V1 with { FirstYearProration = FirstYearProration.None });
        AssertTable(schedule, (2026, 4_000m, 8_000m), (2027, 4_000m, 4_000m), (2028, 4_000m, 0m));
    }

    [Fact]
    public void Arrancar_en_el_ejercicio_siguiente_deja_el_primero_en_cero()
    {
        var schedule = Ok(new DepreciationParameters(DepreciationMethod.StraightLine, 30_000m, D("2026-03-15"),
            AnnualRate: 0.30m, FirstYearProration: FirstYearProration.FullMonthsOfUse, StartInNextFiscalYear: true));

        AssertTable(schedule, (2026, 0m, 30_000m), (2027, 9_000m, 21_000m), (2028, 9_000m, 12_000m),
            (2029, 9_000m, 3_000m), (2030, 3_000m, 0m));
        Assert.Equal(0m, schedule.Periods[0].Factor);
    }

    [Fact]
    public void El_ultimo_ejercicio_absorbe_el_redondeo_sin_abrir_uno_de_mas()
    {
        var schedule = Ok(new DepreciationParameters(DepreciationMethod.StraightLine, 10_000m, D("2026-01-01"),
            LifeYears: 3m, FirstYearProration: FirstYearProration.None));

        AssertTable(schedule, (2026, 3_333.33m, 6_666.67m), (2027, 3_333.33m, 3_333.34m), (2028, 3_333.34m, 0m));
    }

    [Fact]
    public void Valor_residual_no_se_deprecia()
    {
        var schedule = Ok(V1 with { SalvageValue = 2_000m, FirstYearProration = FirstYearProration.None });

        AssertTable(schedule, (2026, 3_333.33m, 8_666.67m), (2027, 3_333.33m, 5_333.34m), (2028, 3_333.34m, 2_000m));
        Assert.Equal(2_000m, schedule.BookValueAt(D("2035-01-01")));
        Assert.Equal(10_000m, schedule.AccumulatedAt(D("2035-01-01")));
    }

    // --- REQ-004: cierre fiscal configurable ---

    [Fact]
    public void Cierre_fiscal_al_30_de_junio()
    {
        var schedule = Ok(V1 with { InServiceDate = D("2026-03-01"), FiscalYearEnd = new FiscalYearEnd(6, 30) });

        var first = schedule.Periods[0];
        Assert.Equal(2026, first.FiscalYear);
        Assert.Equal(D("2026-03-01"), first.Start);
        Assert.Equal(D("2026-06-30"), first.End);
        Assert.Equal(0.3306m, Math.Round(first.Factor, 4));
        Assert.Equal(119m / 360m, first.Factor);
        Assert.Equal(2027, schedule.Periods[1].FiscalYear);
        Assert.Equal(D("2026-07-01"), schedule.Periods[1].Start);
        Assert.Equal(D("2027-06-30"), schedule.Periods[1].End);
    }

    // --- REQ-005: valor en libros ---

    [Fact]
    public void Antes_del_alta_vale_el_costo_y_despues_del_fin_vale_cero()
    {
        var schedule = Ok(V1);
        Assert.Equal(12_000m, schedule.BookValueAt(D("2020-01-01"), BookValueMode.Stepwise));
        Assert.Equal(0m, schedule.AccumulatedAt(D("2020-01-01")));
        Assert.Equal(0m, schedule.BookValueAt(D("2029-12-31")));
        Assert.Equal(0m, schedule.BookValueAt(D("2040-01-01"), BookValueMode.Stepwise));
        Assert.Equal(12_000m, schedule.AccumulatedAt(D("2040-01-01")));
    }

    [Fact]
    public void Stepwise_antes_del_primer_cierre_devuelve_la_apertura()
    {
        var schedule = Ok(V1);
        Assert.Equal(12_000m, schedule.BookValueAt(D("2026-12-30"), BookValueMode.Stepwise));
        Assert.Equal(10_000m, schedule.BookValueAt(D("2026-12-31"), BookValueMode.Stepwise));
    }

    [Fact]
    public void Decreciente_arranca_en_la_compra_para_el_valor_en_libros()
    {
        var schedule = Ok(V3 with { InServiceDate = D("2026-03-01") });
        Assert.Equal(D("2026-01-10"), schedule.Periods[0].Start);
        Assert.Equal(10_000m, schedule.BookValueAt(D("2026-01-09")));
        Assert.True(schedule.BookValueAt(D("2026-02-28")) < 10_000m);
    }

    // --- Serializacion ---

    [Fact]
    public void Parametros_se_serializan_con_enums_como_texto()
    {
        var parameters = V1 with { FiscalYearEnd = new FiscalYearEnd(6, 30) };
        var json = JsonSerializer.Serialize(parameters);

        Assert.Contains("\"StraightLine\"", json);
        Assert.Contains("\"Days360\"", json);
        Assert.Equal(parameters, JsonSerializer.Deserialize<DepreciationParameters>(json));
    }
}
