using Common.Workflow.Alerts;

namespace Common.Workflow.Tests.Alerts;

/// <summary>
/// SRS-CMN-C04 REQ-007 y REQ-008: fechas de contrato y sus sujetos de alerta. Vectores V4-V6.
/// </summary>
public class ContractScheduleTests
{
    private static DateOnly D(string iso) => DateOnly.Parse(iso);

    private static readonly ContractTerms Tacit = new(D("2026-01-15"), 12, 2, 0, RenewalKind.Tacit);

    private static AlertDecision Decide(AlertSubject subject, AlertPolicy policy, InMemoryAlertLedger ledger, DateOnly today)
    {
        var key = AlertEvaluator.KeyFor(subject, policy, today);
        var entry = ledger.Entries.SingleOrDefault(e => e.Key == key);
        return AlertEvaluator.Evaluate(subject, policy, entry, today);
    }

    [Fact]
    public void V4_contrato_tacito_preaviso_toca_y_fin_aun_no()
    {
        var today = D("2026-10-31");
        var policy = new AlertPolicy(LeadDays: 15);

        var dates = ContractSchedule.Compute(Tacit, today);
        Assert.Equal(D("2027-01-15"), dates.CurrentPeriodEnd);
        Assert.Equal(D("2026-11-15"), dates.NoticeDate);

        var subjects = ContractSchedule.Subjects("contract", "C1", Tacit, ContractAlertKinds.End | ContractAlertKinds.Notice, today);
        var ledger = new InMemoryAlertLedger();

        var notice = Decide(subjects.Single(s => s.Kind == AlertKind.Notice), policy, ledger, today);
        Assert.Equal(AlertAction.Notify, notice.Action);
        Assert.Equal("2027-01-15", notice.Key.CycleKey);

        var end = Decide(subjects.Single(s => s.Kind == AlertKind.End), policy, ledger, today);
        Assert.Equal(AlertAction.None, end.Action);
        Assert.Equal(D("2026-12-31"), end.WindowOpensOn);
    }

    [Fact]
    public void V5_tacito_renovado_avisa_en_el_nuevo_ciclo_sin_limpiar()
    {
        var today = D("2027-10-31");
        var policy = new AlertPolicy(LeadDays: 15);
        var ledger = new InMemoryAlertLedger(
        [
            new(new("contract", "C1", AlertKind.Notice, "2027-01-15"), D("2026-10-31"), D("2026-10-31"), 1),
            new(new("contract", "C1", AlertKind.End, "2027-01-15"), D("2026-12-31"), D("2026-12-31"), 1),
        ]);

        var dates = ContractSchedule.Compute(Tacit, today);
        Assert.False(dates.IsExpired);
        Assert.Equal(D("2028-01-15"), dates.CurrentPeriodEnd);
        Assert.Equal(D("2027-11-15"), dates.NoticeDate);

        var subjects = ContractSchedule.Subjects("contract", "C1", Tacit, ContractAlertKinds.End | ContractAlertKinds.Notice, today);
        var notice = Decide(subjects.Single(s => s.Kind == AlertKind.Notice), policy, ledger, today);

        Assert.Equal(AlertAction.Notify, notice.Action);
        Assert.Equal("2028-01-15", notice.Key.CycleKey);
    }

    [Fact]
    public void V6_expreso_vencido_no_genera_preaviso()
    {
        var terms = Tacit with { Renewal = RenewalKind.Express, PeriodicityMonths = 12 };
        var today = D("2027-01-15");

        var dates = ContractSchedule.Compute(terms, today);
        Assert.True(dates.IsExpired);
        Assert.Equal(D("2028-01-15"), dates.RenewableUntil);
        Assert.Equal(D("2027-01-15"), dates.CurrentPeriodEnd);

        var subjects = ContractSchedule.Subjects("contract", "C1", terms, ContractAlertKinds.End | ContractAlertKinds.Notice, today);
        Assert.DoesNotContain(subjects, s => s.Kind == AlertKind.Notice);
        Assert.Contains(subjects, s => s.Kind == AlertKind.End);
    }

    [Theory]
    [InlineData("2027-01-14", "2027-01-15")]
    [InlineData("2027-01-15", "2028-01-15")]
    [InlineData("2025-06-01", "2027-01-15")]
    [InlineData("2040-03-01", "2041-01-15")]
    public void Tacito_avanza_al_siguiente_periodo_el_mismo_dia_del_fin(string today, string end)
    {
        Assert.Equal(D(end), ContractSchedule.Compute(Tacit, D(today)).CurrentPeriodEnd);
    }

    [Fact]
    public void Tacito_terminado_queda_acotado_y_vence()
    {
        var terms = Tacit with { TerminatedOn = D("2027-06-30") };

        var before = ContractSchedule.Compute(terms, D("2027-03-01"));
        Assert.Equal(D("2027-06-30"), before.CurrentPeriodEnd);
        Assert.False(before.IsExpired);

        var after = ContractSchedule.Compute(terms, D("2027-07-01"));
        Assert.True(after.IsExpired);
    }

    [Fact]
    public void Sin_renovacion_vence_al_fin_inicial()
    {
        var terms = Tacit with { Renewal = RenewalKind.None };
        Assert.False(ContractSchedule.Compute(terms, D("2027-01-14")).IsExpired);
        var dates = ContractSchedule.Compute(terms, D("2027-01-15"));
        Assert.True(dates.IsExpired);
        Assert.Equal(D("2027-01-15"), dates.InitialEnd);
        Assert.Null(dates.RenewableUntil);
    }

    [Fact]
    public void Sin_duracion_no_tiene_fin_y_la_periodicidad_arranca_con_su_periodo()
    {
        var terms = new ContractTerms(D("2026-01-15"), 0, 1, 6, RenewalKind.None);

        var dates = ContractSchedule.Compute(terms, D("2026-10-31"));
        Assert.Null(dates.InitialEnd);
        Assert.Null(dates.CurrentPeriodEnd);
        Assert.Null(dates.NoticeDate);
        Assert.False(dates.IsExpired);

        var subjects = ContractSchedule.Subjects("contract", "C2", terms, ContractAlertKinds.End | ContractAlertKinds.Notice | ContractAlertKinds.Periodicity, D("2026-10-31"));
        var periodic = subjects.OfType<PeriodicAlertSubject>().ToList();
        Assert.Equal(2, periodic.Count);
        Assert.DoesNotContain(subjects, s => s is DateAlertSubject);
        Assert.Equal(D("2026-07-15"), periodic[0].FirstOccurrence);
        Assert.Null(periodic[0].CyclePrefix);
        Assert.Equal(D("2026-06-15"), periodic[1].FirstOccurrence);
        Assert.Equal("notice", periodic[1].CyclePrefix);
    }

    [Fact]
    public void Tacito_genera_fin_y_preaviso_en_cada_periodo_sin_periodicidad()
    {
        var first = ContractSchedule.Subjects("contract", "C1", Tacit, ContractAlertKinds.End | ContractAlertKinds.Notice, D("2026-10-31"));
        var second = ContractSchedule.Subjects("contract", "C1", Tacit, ContractAlertKinds.End | ContractAlertKinds.Notice, D("2027-10-31"));

        Assert.Equal(2, first.Count);
        Assert.Equal(2, second.Count);
        Assert.Equal(D("2027-01-15"), first.OfType<DateAlertSubject>().Single(s => s.Kind == AlertKind.End).TargetDate);
        Assert.Equal(D("2028-01-15"), second.OfType<DateAlertSubject>().Single(s => s.Kind == AlertKind.End).TargetDate);
    }

    [Fact]
    public void Solo_genera_los_tipos_habilitados()
    {
        Assert.Empty(ContractSchedule.Subjects("contract", "C1", Tacit, ContractAlertKinds.None, D("2026-10-31")));
        Assert.Single(ContractSchedule.Subjects("contract", "C1", Tacit, ContractAlertKinds.End, D("2026-10-31")));
    }

    [Fact]
    public void Terminos_negativos_son_error_del_programador()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractSchedule.Compute(Tacit with { NoticeMonths = -1 }, D("2026-10-31")));
        Assert.Throws<ArgumentNullException>(() => ContractSchedule.Compute(null!, D("2026-10-31")));
    }
}
