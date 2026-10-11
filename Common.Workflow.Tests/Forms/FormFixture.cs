using Common.Workflow.Forms;

namespace Common.Workflow.Tests.Forms;

/// <summary>
/// Formulario F1 "Solicitud de servicio" de §12 del SRS CMN-C06 y ayudas para armar casos.
/// </summary>
internal static class FormFixture
{
    public static readonly FormConditionEngine Engine = new(ConditionHandlerRegistry.WithBuiltIns());

    public static Condition Q(string id, ValueOperator op, string? value = null, LogicOperator logic = LogicOperator.And) =>
        new(id, SubjectKind.Question, op, value, logic);

    public static VisibilityRule VisibleIf(params Condition[] conditions) => new(VisibilityStrategy.VisibleIf, conditions);

    public static VisibilityRule HiddenIf(params Condition[] conditions) => new(VisibilityStrategy.HiddenIf, conditions);

    public static QuestionModel Question(string id, string type, VisibilityRule? visibility = null,
        string section = "s1", bool mandatory = false, ValidationRule? validation = null) =>
        new(id, section, type, mandatory, visibility ?? VisibilityRule.Always, validation ?? ValidationRule.None);

    public static readonly FormModel F1 = new("F1", VisibilityRule.Always,
        [new SectionModel("s1", 1, VisibilityRule.Always), new SectionModel("s2", 2, VisibleIf(Q("q1", ValueOperator.Equals, "acceso")))],
        [
            Question("q1", "single_choice", mandatory: true),
            Question("q2", "text", VisibleIf(Q("q1", ValueOperator.Equals, "equipo"))),
            Question("q3", "text", VisibleIf(Q("q2", ValueOperator.NotEmpty))),
            Question("q4", "text", section: "s2"),
        ],
        [],
        [
            new DestinationModel("d1", new CreationRule(CreationStrategy.CreatedUnless, [Q("q1", ValueOperator.Equals, "acceso")])),
            new DestinationModel("d2", new CreationRule(CreationStrategy.CreatedIf, [Q("q1", ValueOperator.Equals, "acceso")])),
        ]);

    /// <summary>Formulario de una sola seccion con las preguntas dadas.</summary>
    public static FormModel Form(params QuestionModel[] questions) =>
        new("F", VisibilityRule.Always, [new SectionModel("s1", 1, VisibilityRule.Always)], questions, [], []);

    public static FormAnswers Answers(params (string Id, AnswerValue Value)[] values) =>
        FormAnswers.From(values.Select(v => KeyValuePair.Create(v.Id, v.Value)));
}
