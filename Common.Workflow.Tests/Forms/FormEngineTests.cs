using System.Diagnostics;
using System.Text.Json;
using Common.Results;
using Common.Workflow.Forms;
using static Common.Workflow.Tests.Forms.FormFixture;

namespace Common.Workflow.Tests.Forms;

/// <summary>
/// REQ-CMN-C06-003 a 007 y 009: punto fijo, secciones, validacion, destinos y validacion de la definicion.
/// </summary>
public class FormEngineTests
{
    private static Condition S(string id, ValueOperator op) => new(id, SubjectKind.Section, op, null);

    private static Condition Cm(string id, ValueOperator op) => new(id, SubjectKind.Comment, op, null);

    [Fact]
    public void Sujeto_oculto_visible_y_not_visible_reflejan_su_visibilidad()
    {
        var form = Form(
            Question("oculta", "text", VisibleIf()),
            Question("a", "text", VisibleIf(Q("oculta", ValueOperator.NotVisible))),
            Question("b", "text", VisibleIf(Q("oculta", ValueOperator.Visible))),
            Question("c", "text", VisibleIf(Q("oculta", ValueOperator.NotEmpty))),
            Question("d", "text", VisibleIf(Q("oculta", ValueOperator.NotEquals, "zzz"))));

        var result = Engine.ComputeVisibility(form, Answers(("oculta", AnswerValue.Text("valor viejo"))));

        Assert.True(result.Questions["a"]);
        Assert.False(result.Questions["b"]);
        Assert.False(result.Questions["c"]);
        Assert.False(result.Questions["d"]);   // cualquier otro operador sobre oculta: falso
    }

    [Fact]
    public void Bloque_de_seccion_oculta_es_oculto_sin_evaluar_sus_condiciones()
    {
        var form = new FormModel("F", VisibilityRule.Always,
            [new SectionModel("s1", 1, VisibilityRule.Always), new SectionModel("s2", 2, HiddenIf(Q("q1", ValueOperator.NotEmpty)))],
            [Question("q1", "text"), Question("q2", "text", HiddenIf(), section: "s2")],
            [new CommentModel("c2", "s2", VisibilityRule.Always)], []);

        var hidden = Engine.ComputeVisibility(form, Answers(("q1", AnswerValue.Text("x"))));
        var shown = Engine.ComputeVisibility(form, FormAnswers.Empty);

        Assert.False(hidden.Sections["s2"]);
        Assert.False(hidden.Questions["q2"]);
        Assert.False(hidden.Comments["c2"]);
        Assert.True(shown.Sections["s2"]);
        Assert.True(shown.Questions["q2"]);
        Assert.True(shown.Comments["c2"]);
    }

    [Fact]
    public void Primera_seccion_siempre_visible_y_las_demas_sin_hijos_visibles_se_ocultan()
    {
        var form = new FormModel("F", VisibilityRule.Always,
            [
                new SectionModel("s3", 3, VisibilityRule.Always),                 // sin hijos visibles
                new SectionModel("s1", 1, VisibleIf()),                           // la primera por Rank: siempre visible
                new SectionModel("s2", 2, VisibilityRule.Always),                 // solo un comentario visible
            ],
            [Question("q1", "text", section: "s1"), Question("q3", "text", VisibleIf(), section: "s3")],
            [new CommentModel("c2", "s2", VisibilityRule.Always)], []);

        var result = Engine.ComputeVisibility(form, FormAnswers.Empty);

        Assert.True(result.Sections["s1"]);
        Assert.True(result.Questions["q1"]);
        Assert.True(result.Sections["s2"]);
        Assert.False(result.Sections["s3"]);
    }

    [Fact]
    public void Condiciones_sobre_secciones_y_comentarios_usan_su_visibilidad()
    {
        var form = new FormModel("F", HiddenIf(Cm("aviso", ValueOperator.Visible)),
            [new SectionModel("s1", 1, VisibilityRule.Always), new SectionModel("s2", 2, VisibleIf(Q("q1", ValueOperator.Equals, "si")))],
            [Question("q1", "text"), Question("q2", "text", section: "s2"),
             Question("q3", "text", VisibleIf(S("s2", ValueOperator.Visible))),
             Question("q4", "text", VisibleIf(S("s2", ValueOperator.Empty)))],
            [new CommentModel("aviso", "s1", VisibleIf(S("s2", ValueOperator.NotVisible)))], []);

        var si = Engine.ComputeVisibility(form, Answers(("q1", AnswerValue.Text("si"))));
        var no = Engine.ComputeVisibility(form, Answers(("q1", AnswerValue.Text("no"))));

        Assert.True(si.Questions["q3"]);
        Assert.False(si.Comments["aviso"]);
        Assert.True(si.SubmitVisible);
        Assert.False(si.Questions["q4"]);   // operador de respuesta sobre una seccion: falso
        Assert.False(no.Questions["q3"]);
        Assert.True(no.Comments["aviso"]);
        Assert.False(no.SubmitVisible);
    }

    [Fact]
    public void Sin_convergencia_oculta_tambien_al_boton_de_envio_y_respeta_el_tope_configurado()
    {
        var form = Form(Question("q9", "text", VisibleIf(Q("q9", ValueOperator.NotVisible))))
            with { SubmitVisibility = VisibleIf(Q("q9", ValueOperator.Visible)) };
        var engine = new FormConditionEngine(ConditionHandlerRegistry.WithBuiltIns(), new FormEngineOptions(MaxIterations: 7));

        var result = engine.ComputeVisibility(form, FormAnswers.Empty);

        Assert.False(result.Converged);
        Assert.Equal(7, result.Iterations);
        Assert.False(result.Questions["q9"]);
        Assert.False(result.SubmitVisible);
    }

    [Fact]
    public void Validacion_obligatoria_va_antes_de_las_condiciones_y_las_ocultas_no_se_validan()
    {
        var rule = new ValidationRule(ValidationStrategy.ValidIf, [Q("q2", ValueOperator.LengthGreaterThan, "3")]);
        var form = Form(
            Question("q1", "text"),
            Question("q2", "text", mandatory: true, validation: rule),
            Question("q3", "text", VisibleIf(Q("q1", ValueOperator.Equals, "mostrar")), mandatory: true, validation: rule));

        var empty = Engine.ComputeValidation(form, FormAnswers.Empty);
        var corto = Engine.ComputeValidation(form, Answers(("q2", AnswerValue.Text("ab"))));
        var ok = Engine.ComputeValidation(form, Answers(("q2", AnswerValue.Text("abcd"))));

        Assert.Equal([new QuestionValidationError("q2", "required", null)], empty.Errors);
        Assert.Equal([new QuestionValidationError("q2", "validation", rule.Conditions[0])], corto.Errors);
        Assert.True(ok.IsValid);
    }

    [Fact]
    public void Validacion_devuelve_cada_condicion_que_falla()
    {
        var c0 = Q("q", ValueOperator.Contains, "@");
        var c1 = Q("q", ValueOperator.LengthGreaterThan, "5", LogicOperator.Or);
        var c2 = Q("q", ValueOperator.Contains, ".mx");
        var invalidIf = new ValidationRule(ValidationStrategy.InvalidIf, [Q("r", ValueOperator.Contains, "spam"), Q("r", ValueOperator.Contains, "xx")]);
        var form = Form(
            Question("q", "text", validation: new ValidationRule(ValidationStrategy.ValidIf, [c0, c1, c2])),
            Question("r", "text", validation: invalidIf));

        var result = Engine.ComputeValidation(form, Answers(("q", AnswerValue.Text("ab")), ("r", AnswerValue.Text("spam xx"))));

        Assert.Equal(
        [
            new QuestionValidationError("q", "validation", c0),
            new QuestionValidationError("q", "validation", c1),
            new QuestionValidationError("q", "validation", c2),
            new QuestionValidationError("r", "validation", invalidIf.Conditions[0]),
            new QuestionValidationError("r", "validation", invalidIf.Conditions[1]),
        ], result.Errors);
    }

    [Fact]
    public void Destinos_siempre_y_con_condiciones_sobre_preguntas_ocultas()
    {
        var form = F1 with
        {
            Destinations =
            [
                new DestinationModel("siempre", new CreationRule(CreationStrategy.AlwaysCreated, [])),
                new DestinationModel("si_q2", new CreationRule(CreationStrategy.CreatedIf, [Q("q2", ValueOperator.NotEmpty)])),
            ],
        };

        // q2 queda oculta con q1=acceso: su texto viejo no cuenta.
        Assert.Equal(["siempre"], Engine.ComputeDestinations(form, Answers(("q1", AnswerValue.Choice("acceso")), ("q2", AnswerValue.Text("x")))));
        Assert.Equal(["siempre", "si_q2"], Engine.ComputeDestinations(form, Answers(("q1", AnswerValue.Choice("equipo")), ("q2", AnswerValue.Text("x")))));
    }

    [Fact]
    public void Condiciones_sin_sujeto_se_ignoran_y_las_que_apuntan_a_nada_son_falsas()
    {
        var form = Form(
            Question("a", "text", VisibleIf(new Condition("", SubjectKind.Question, ValueOperator.Empty, null))),
            Question("b", "text", HiddenIf(new Condition("", SubjectKind.Question, ValueOperator.Empty, null), Q("a", ValueOperator.NotVisible))),
            Question("c", "text", VisibleIf(Q("fantasma", ValueOperator.Empty))),
            Question("d", "text", VisibleIf(S("fantasma", ValueOperator.NotVisible))),
            Question("e", "text", VisibleIf(Cm("fantasma", ValueOperator.NotVisible))));

        var result = Engine.ComputeVisibility(form, FormAnswers.Empty);

        Assert.False(result.Questions["a"]);   // solo condiciones sin sujeto = lista vacia
        Assert.False(result.Questions["b"]);   // a oculta -> not_visible verdadero -> HiddenIf oculta
        Assert.False(result.Questions["c"]);
        Assert.False(result.Questions["d"]);
        Assert.False(result.Questions["e"]);
    }

    [Fact]
    public void Validar_definicion_devuelve_todos_los_errores()
    {
        var form = new FormModel("F", VisibleIf(Q("q1", ValueOperator.Visible)),
            [new SectionModel("s1", 1, VisibleIf(S("s1", ValueOperator.Equals))), new SectionModel("s1", 2, VisibilityRule.Always)],
            [
                Question("q1", "text", VisibleIf(Q("nadie", ValueOperator.Empty))),
                Question("q2", "file", VisibleIf(Q("q1", ValueOperator.GreaterThan, "3"))),
                Question("q3", "text", VisibleIf(Q("q1", ValueOperator.MatchRegex, "([a-")),
                    validation: new ValidationRule(ValidationStrategy.ValidIf, [Q("q1", ValueOperator.MatchRegex, new string('a', 501))])),
                Question("q4", "number", VisibleIf(Q("q4n", ValueOperator.Equals, "x")), section: "s9"),
                Question("q4n", "number", VisibleIf(Q("q4n", ValueOperator.Visible))),
                Question("q5", "text", VisibleIf(Q("q2", ValueOperator.Empty), Q("q1", ValueOperator.LengthGreaterThan, "tres"))),
                Question("q6", "date", VisibleIf(Q("q6d", ValueOperator.Equals, "ayer"))),
                Question("q6d", "date"),
                Question("q7", "desconocido"),
                Question("q8", "text", VisibleIf(Q("q7", ValueOperator.Equals, "x"))),
            ],
            [new CommentModel("c1", "s1", VisibleIf(Cm("c1", ValueOperator.Contains)))],
            [new DestinationModel("d1", new CreationRule(CreationStrategy.CreatedIf, [Q("q4n", ValueOperator.Equals, "1,5")]))]);

        var failure = Assert.IsType<DefinitionFailure<FormModel>>(Engine.ValidateDefinition(form));
        var codes = failure.Errors.Select(e => $"{e.Path} {e.Code}").ToList();

        Assert.Contains("Sections[1].Id forms.duplicate_id", codes);
        Assert.Contains("Sections[0].Visibility.Conditions[0].Operator forms.operator_not_supported", codes);
        Assert.Contains("Questions[0].Visibility.Conditions[0].SubjectId forms.unknown_subject", codes);
        Assert.Contains("Questions[1].Visibility.Conditions[0].Operator forms.operator_not_supported", codes);
        Assert.Contains("Questions[2].Visibility.Conditions[0].Value forms.invalid_regex", codes);
        Assert.Contains("Questions[2].Validation.Conditions[0].Value forms.invalid_regex", codes);
        Assert.Contains("Questions[3].SectionId forms.unknown_section", codes);
        Assert.Contains("Questions[3].Visibility.Conditions[0].Value forms.invalid_value", codes);
        Assert.Contains("Questions[5].Visibility.Conditions[1].Value forms.invalid_value", codes);
        Assert.Contains("Questions[6].Visibility.Conditions[0].Value forms.invalid_value", codes);
        Assert.Contains("Questions[9].Visibility.Conditions[0].Operator forms.operator_not_supported", codes);
        Assert.Contains("Comments[0].Visibility.Conditions[0].Operator forms.operator_not_supported", codes);
        Assert.Contains("Destinations[0].Creation.Conditions[0].Value forms.invalid_value", codes);
        Assert.Contains(failure.Errors, e => e.Code == "forms.visibility_cycle" && e.Message.Contains("q4n → q4n"));
        Assert.Contains(failure.Errors, e => e.Code == "forms.visibility_cycle" && e.Message.Contains("s1 → s1"));
        Assert.Contains(failure.Errors, e => e.Code == "forms.visibility_cycle" && e.Message.Contains("c1 \u2192 c1"));
        Assert.Equal(16, failure.Errors.Count);
    }

    [Fact]
    public void Ciclo_entre_seccion_y_su_propio_bloque()
    {
        var form = new FormModel("F", VisibilityRule.Always,
            [new SectionModel("s1", 1, VisibilityRule.Always), new SectionModel("s2", 2, VisibleIf(Q("q2", ValueOperator.NotEmpty)))],
            [Question("q1", "text"), Question("q2", "text", section: "s2")], [], []);

        var error = Assert.Single(Assert.IsType<DefinitionFailure<FormModel>>(Engine.ValidateDefinition(form)).Errors);
        Assert.Equal("forms.visibility_cycle", error.Code);
        Assert.Contains("q2 → s2 → q2", error.Message);
    }

    [Fact]
    public void Las_condiciones_de_validacion_y_creacion_no_forman_ciclos()
    {
        var form = F1 with
        {
            Questions =
            [
                .. F1.Questions,
                Question("q5", "text", validation: new ValidationRule(ValidationStrategy.ValidIf, [Q("q5", ValueOperator.NotEmpty)])),
            ],
        };

        var ok = Assert.IsAssignableFrom<ISuccess<FormModel>>(Engine.ValidateDefinition(form));
        Assert.Same(form, ok.Data);
    }

    [Fact]
    public void El_modelo_viaja_en_json_con_enums_como_texto_y_da_el_mismo_resultado()
    {
        var json = JsonSerializer.Serialize(F1);
        var back = JsonSerializer.Deserialize<FormModel>(json)!;
        var answers = Answers(("q1", AnswerValue.Choice("acceso")), ("q2", AnswerValue.Text("Caminadora 3")));

        Assert.Contains("\"Strategy\":\"VisibleIf\"", json);
        Assert.Contains("\"Operator\":\"NotEmpty\"", json);
        Assert.Contains("\"Logic\":\"And\"", json);
        Assert.Equal(Engine.ComputeVisibility(F1, answers).Questions, Engine.ComputeVisibility(back, answers).Questions);
        Assert.Equal(Engine.ComputeDestinations(F1, answers), Engine.ComputeDestinations(back, answers));
    }

    [Fact]
    public void Errores_del_programador()
    {
        Assert.Throws<ArgumentNullException>(() => new FormConditionEngine(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FormConditionEngine(ConditionHandlerRegistry.WithBuiltIns(), new FormEngineOptions(0)));
        Assert.Throws<ArgumentNullException>(() => Engine.ComputeVisibility(null!, FormAnswers.Empty));
        Assert.Throws<ArgumentNullException>(() => Engine.ComputeVisibility(F1, null!));
        Assert.Throws<ArgumentNullException>(() => Engine.ValidateDefinition(null!));
        Assert.Throws<ArgumentException>(() => ConditionHandlerRegistry.WithBuiltIns().Register(" ", new FakeHandler()));
        Assert.Throws<ArgumentNullException>(() => ConditionHandlerRegistry.WithBuiltIns().Register("x", null!));
        Assert.Contains("multiple_choice", ConditionHandlerRegistry.WithBuiltIns().QuestionTypes);
    }

    private sealed class FakeHandler : IConditionHandler
    {
        public IReadOnlySet<ValueOperator> SupportedOperators { get; } = new HashSet<ValueOperator>();
        public bool IsEmpty(AnswerValue answer) => true;
        public bool Apply(AnswerValue answer, ValueOperator op, string? value) => false;
        public DefinitionError? ValidateValue(ValueOperator op, string? value) => null;
    }

    [Fact]
    public void Evaluar_es_puro_entre_hilos()
    {
        var answers = Answers(("q1", AnswerValue.Choice("acceso")), ("q2", AnswerValue.Text("Caminadora 3")));
        var results = Enumerable.Range(0, 200).AsParallel().Select(_ => Engine.ComputeVisibility(F1, answers)).ToList();

        Assert.All(results, r => Assert.Equal(3, r.Iterations));
        Assert.All(results, r => Assert.True(r.Questions["q4"]));
    }

    [Fact]
    public void Rendimiento_200_preguntas_y_400_condiciones()
    {
        var questions = new List<QuestionModel> { Question("q0", "text") };
        for (var i = 1; i < 200; i++)
        {
            questions.Add(Question($"q{i}", i % 2 == 0 ? "text" : "number", VisibleIf(
                Q($"q{i - 1}", ValueOperator.NotEmpty),
                Q("q0", i % 2 == 0 ? ValueOperator.MatchRegex : ValueOperator.Contains, i % 2 == 0 ? @"^\w+$" : "x", LogicOperator.Or))));
        }
        var form = Form([.. questions]);
        var answers = FormAnswers.From(questions.Select(q => KeyValuePair.Create(q.Id,
            q.Type == "number" ? AnswerValue.Number(1) : AnswerValue.Text("x"))));
        Assert.Equal(398, questions.Sum(q => q.Visibility.Conditions.Count));

        for (var i = 0; i < 10; i++) Engine.ComputeVisibility(form, answers);   // calentamiento
        var samples = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            var watch = Stopwatch.StartNew();
            Engine.ComputeVisibility(form, answers);
            samples.Add(watch.Elapsed.TotalMilliseconds);
        }
        samples.Sort();

        // El objetivo del SRS es 5 ms p95; se deja holgura para maquinas de CI compartidas.
        Assert.True(samples[47] < 50, $"p95 = {samples[47]:F3} ms");
    }
}
