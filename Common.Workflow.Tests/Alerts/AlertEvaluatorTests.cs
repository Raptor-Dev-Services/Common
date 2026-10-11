using System.Text.Json;
using Common.Results;
using Common.Workflow.Alerts;
using Common.Workflow.Depreciation;

namespace Common.Workflow.Tests.Alerts;

/// <summary>
/// SRS-CMN-C04: "toca avisar?" para fecha, periodicidad y umbral (REQ-001 a 006). Vectores V1-V3, V7-V12
/// con los mismos datos del SRS.
/// </summary>
public class AlertEvaluatorTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static readonly AlertPolicy Default = new(LeadDays: 30);

    // Garantia del activo A1: el fin sale de C07 (2025-12-01 + 12 meses = 2026-12-01).
    private static DateAlertSubject Warranty_A1()
    {
        var expiry = Assert.IsType<WarrantyExpiry.On>(Warranty.ExpiresOn(D("2025-12-01"), 12));
        return new DateAlertSubject("asset.warranty", "A1", AlertKind.End, expiry.Date);
    }

    private static AlertLedgerEntry Entry(AlertKey key, string lastSent, int count = 1)
        => new(key, D(lastSent), D(lastSent), count);

    // --- Vectores ---

    [Fact]
    public void V1_antes_de_la_ventana_no_toca_y_dice_cuando_abre()
    {
        var decision = AlertEvaluator.Evaluate(Warranty_A1(), Default, null, D("2026-10-31"));

        Assert.Equal(AlertAction.None, decision.Action);
        Assert.Equal(AlertReasons.NotYet, decision.Reason);
        Assert.Equal(D("2026-11-01"), decision.WindowOpensOn);
    }

    [Fact]
    public void V2_el_dia_que_abre_la_ventana_toca_avisar_con_la_fecha_como_ciclo()
    {
        var decision = AlertEvaluator.Evaluate(Warranty_A1(), Default, null, D("2026-11-01"));

        Assert.Equal(AlertAction.Notify, decision.Action);
        Assert.Equal(AlertReasons.Due, decision.Reason);
        Assert.Equal(new AlertKey("asset.warranty", "A1", AlertKind.End, "2026-12-01"), decision.Key);
    }

    [Fact]
    public void V3_ya_avisado_en_el_ciclo_no_repite()
    {
        var subject = Warranty_A1();
        var key = AlertEvaluator.KeyFor(subject, Default, D("2026-11-02"));

        var decision = AlertEvaluator.Evaluate(subject, Default, Entry(key, "2026-11-01"), D("2026-11-02"));

        Assert.Equal(AlertAction.None, decision.Action);
        Assert.Equal(AlertReasons.AlreadySent, decision.Reason);
    }

    [Theory]
    [InlineData("2026-12-19", AlertAction.None, "already_sent", "2026-06-30")]
    [InlineData("2026-12-20", AlertAction.Notify, "due", "2026-12-30")]
    public void V7_calibracion_periodica_toma_la_ultima_ocurrencia_abierta(string today, AlertAction action, string reason, string cycle)
    {
        var subject = new PeriodicAlertSubject("equipment.calibration", "E7", D("2026-06-30"), 6);
        var policy = new AlertPolicy(LeadDays: 10);
        var ledger = new Dictionary<AlertKey, AlertLedgerEntry>
        {
            [new("equipment.calibration", "E7", AlertKind.Periodicity, "2026-06-30")] =
                Entry(new("equipment.calibration", "E7", AlertKind.Periodicity, "2026-06-30"), "2026-06-20"),
        };

        var key = AlertEvaluator.KeyFor(subject, policy, D(today));
        var decision = AlertEvaluator.Evaluate(subject, policy, ledger.GetValueOrDefault(key), D(today));

        Assert.Equal(action, decision.Action);
        Assert.Equal(reason, decision.Reason);
        Assert.Equal(cycle, decision.Key.CycleKey);
    }

    [Theory]
    [InlineData("2026-02-28", "2026-02-28")]
    [InlineData("2026-03-31", "2026-03-31")]
    public void V8_periodicidad_mensual_se_calcula_desde_la_primera_sin_acumular(string today, string cycle)
    {
        var subject = new PeriodicAlertSubject("contract", "C1", D("2026-01-31"), 1);

        var decision = AlertEvaluator.Evaluate(subject, new AlertPolicy(LeadDays: 0), null, D(today));

        Assert.Equal(AlertAction.Notify, decision.Action);
        Assert.Equal(cycle, decision.Key.CycleKey);
    }

    [Theory]
    [InlineData("2026-10-11", AlertAction.None, "already_sent")]
    [InlineData("2026-10-12", AlertAction.Notify, "repeat")]
    public void V9_umbral_repite_cada_siete_dias(string today, AlertAction action, string reason)
    {
        var subject = new ThresholdAlertSubject("consumable", "TONER-1", 3m, 5m);
        var policy = new AlertPolicy(LeadDays: 30, RepeatEveryDays: 7);
        var key = new AlertKey("consumable", "TONER-1", AlertKind.Threshold, "below");

        var decision = AlertEvaluator.Evaluate(subject, policy, Entry(key, "2026-10-05"), D(today));

        Assert.Equal(key, decision.Key);
        Assert.Equal(action, decision.Action);
        Assert.Equal(reason, decision.Reason);
    }

    [Fact]
    public void V10_umbral_que_se_recupera_limpia_la_entrada()
    {
        var subject = new ThresholdAlertSubject("consumable", "TONER-1", 8m, 5m);
        var key = new AlertKey("consumable", "TONER-1", AlertKind.Threshold, "below");

        var decision = AlertEvaluator.Evaluate(subject, Default, Entry(key, "2026-10-05"), D("2026-10-12"));

        Assert.Equal(AlertAction.Clear, decision.Action);
    }

    [Fact]
    public void V11_umbral_negativo_esta_desactivado()
    {
        var subject = new ThresholdAlertSubject("consumable", "TONER-1", 0m, -1m);

        var decision = AlertEvaluator.Evaluate(subject, Default, null, D("2026-10-11"));

        Assert.Equal(AlertAction.None, decision.Action);
        Assert.Equal(AlertReasons.Disabled, decision.Reason);
    }

    [Fact]
    public void V12_fecha_vencida_hace_mucho_no_avisa_con_StopAfterDays()
    {
        var subject = new DateAlertSubject("contract", "C9", AlertKind.End, D("2023-06-30"));

        var decision = AlertEvaluator.Evaluate(subject, new AlertPolicy(30, StopAfterDays: 30), null, D("2026-10-11"));

        Assert.Equal(AlertAction.None, decision.Action);
        Assert.Equal(AlertReasons.Stale, decision.Reason);
    }

    // --- Criterios de aceptacion ---

    [Fact]
    public void Sin_StopAfterDays_avisa_tarde_una_vez()
    {
        var subject = new DateAlertSubject("contract", "C9", AlertKind.End, D("2023-06-30"));

        var decision = AlertEvaluator.Evaluate(subject, Default, null, D("2026-10-11"));

        Assert.Equal(AlertAction.Notify, decision.Action);
        Assert.Equal(AlertReasons.Due, decision.Reason);
    }

    [Theory]
    [InlineData("2023-07-30", AlertAction.Notify)]
    [InlineData("2023-07-31", AlertAction.None)]
    public void StopAfterDays_corta_el_dia_siguiente_al_limite(string today, AlertAction action)
    {
        var subject = new DateAlertSubject("contract", "C9", AlertKind.End, D("2023-06-30"));
        var decision = AlertEvaluator.Evaluate(subject, new AlertPolicy(30, StopAfterDays: 30), null, D(today));
        Assert.Equal(action, decision.Action);
    }

    [Theory]
    [InlineData("2027-01-14", AlertAction.Notify, "due")]
    [InlineData("2027-01-15", AlertAction.None, "out_of_validity")]
    public void Preaviso_deja_de_avisar_al_llegar_su_vigencia(string today, AlertAction action, string reason)
    {
        var subject = new DateAlertSubject("contract", "C1", AlertKind.Notice, D("2026-11-15"), D("2027-01-15"), D("2027-01-15"));

        var decision = AlertEvaluator.Evaluate(subject, Default, null, D(today));

        Assert.Equal(action, decision.Action);
        Assert.Equal(reason, decision.Reason);
        Assert.Equal("2027-01-15", decision.Key.CycleKey);
    }

    [Fact]
    public void Repeticion_en_sujeto_de_fecha()
    {
        var subject = Warranty_A1();
        var policy = new AlertPolicy(30, RepeatEveryDays: 3);
        var key = AlertEvaluator.KeyFor(subject, policy, D("2026-11-04"));

        Assert.Equal(AlertReasons.AlreadySent, AlertEvaluator.Evaluate(subject, policy, Entry(key, "2026-11-01"), D("2026-11-03")).Reason);
        Assert.Equal(AlertReasons.Repeat, AlertEvaluator.Evaluate(subject, policy, Entry(key, "2026-11-01"), D("2026-11-04")).Reason);
    }

    [Fact]
    public void Umbral_sin_entrada_y_por_encima_no_hace_nada()
    {
        var decision = AlertEvaluator.Evaluate(new ThresholdAlertSubject("consumable", "X", 8m, 5m), Default, null, D("2026-10-12"));
        Assert.Equal(AlertAction.None, decision.Action);
        Assert.Equal(AlertReasons.NotBelow, decision.Reason);
    }

    [Theory]
    [InlineData("5", AlertAction.Notify)]
    [InlineData("5.01", AlertAction.None)]
    public void Umbral_avisa_en_el_valor_igual(string value, AlertAction action)
    {
        var decision = AlertEvaluator.Evaluate(new ThresholdAlertSubject("consumable", "X", decimal.Parse(value), 5m), Default, null, D("2026-10-12"));
        Assert.Equal(action, decision.Action);
    }

    [Fact]
    public void Periodicidad_antes_de_la_primera_ventana_no_toca()
    {
        var subject = new PeriodicAlertSubject("equipment.calibration", "E7", D("2026-06-30"), 6);

        var decision = AlertEvaluator.Evaluate(subject, new AlertPolicy(10), null, D("2026-06-19"));

        Assert.Equal(AlertReasons.NotYet, decision.Reason);
        Assert.Equal(D("2026-06-20"), decision.WindowOpensOn);
        Assert.Equal("2026-06-30", decision.Key.CycleKey);
    }

    [Fact]
    public void Periodicidad_no_pasa_de_Until()
    {
        var subject = new PeriodicAlertSubject("equipment.calibration", "E7", D("2026-06-30"), 6, Until: D("2026-12-29"));

        var decision = AlertEvaluator.Evaluate(subject, new AlertPolicy(10), null, D("2027-01-05"));

        Assert.Equal("2026-06-30", decision.Key.CycleKey);
    }

    [Fact]
    public void Periodicidad_salta_ocurrencias_perdidas_a_la_ultima()
    {
        var subject = new PeriodicAlertSubject("contract", "C1", D("2020-01-31"), 1);
        var decision = AlertEvaluator.Evaluate(subject, new AlertPolicy(0), null, D("2026-10-11"));
        Assert.Equal("2026-09-30", decision.Key.CycleKey);
    }

    [Fact]
    public void Prefijo_de_ciclo_distingue_dos_series_del_mismo_elemento()
    {
        var subject = new PeriodicAlertSubject("contract", "C1", D("2026-01-31"), 1, CyclePrefix: "notice");
        Assert.Equal("notice:2026-02-28", AlertEvaluator.KeyFor(subject, new AlertPolicy(0), D("2026-02-28")).CycleKey);
    }

    [Theory]
    [InlineData(-1, null, null, "alerts.invalid_lead")]
    [InlineData(0, 0, null, "alerts.invalid_repeat")]
    [InlineData(0, -3, null, "alerts.invalid_repeat")]
    [InlineData(0, null, -1, "alerts.invalid_stop")]
    public void Politica_invalida_devuelve_su_codigo(int lead, int? repeat, int? stop, string code)
    {
        var policy = new AlertPolicy(lead, repeat, stop);

        var result = AlertEvaluator.ValidatePolicy(policy);

        var failure = Assert.IsType<DefinitionFailure<AlertPolicy>>(result);
        Assert.Contains(failure.Errors, e => e.Code == code);
        Assert.Throws<ArgumentException>(() => AlertEvaluator.Evaluate(Warranty_A1(), policy, null, D("2026-11-01")));
    }

    [Fact]
    public void Politica_valida_pasa()
    {
        Assert.IsType<SuccessResult<AlertPolicy>>(AlertEvaluator.ValidatePolicy(new AlertPolicy(0, 1, 0)));
    }

    [Fact]
    public void Sujetos_mal_formados_son_error_del_programador()
    {
        Assert.Throws<ArgumentException>(() => AlertEvaluator.Evaluate(
            new DateAlertSubject("x", "1", AlertKind.Threshold, D("2026-01-01")), Default, null, D("2026-01-01")));
        Assert.Throws<ArgumentException>(() => AlertEvaluator.Evaluate(
            new PeriodicAlertSubject("x", "1", D("2026-01-01"), 0), Default, null, D("2026-01-01")));
        Assert.Throws<ArgumentException>(() => AlertEvaluator.Evaluate(
            new ThresholdAlertSubject("", "1", 1m, 2m), Default, null, D("2026-01-01")));
        Assert.Throws<ArgumentNullException>(() => AlertEvaluator.Evaluate(null!, Default, null, D("2026-01-01")));
    }

    [Fact]
    public void Entrada_de_otro_ciclo_es_error_del_programador()
    {
        var other = new AlertKey("asset.warranty", "A1", AlertKind.End, "2025-12-01");
        Assert.Throws<ArgumentException>(() => AlertEvaluator.Evaluate(Warranty_A1(), Default, Entry(other, "2025-11-01"), D("2026-11-02")));
    }

    [Fact]
    public void Sujetos_se_serializan_con_discriminador_y_enums_como_texto()
    {
        AlertSubject subject = new DateAlertSubject("contract", "C1", AlertKind.Notice, D("2026-11-15"), D("2027-01-15"));
        var json = JsonSerializer.Serialize(subject);

        Assert.Contains("\"subject\":\"date\"", json);
        Assert.Contains("\"Notice\"", json);
        Assert.Equal(subject, JsonSerializer.Deserialize<AlertSubject>(json));
    }
}
