using Common.Results;

namespace Common.Workflow.Forms
{
    /// <summary>
    /// Visibilidad calculada de un formulario.
    /// </summary>
    /// <param name="SubmitVisible">Si el boton de envio se ve.</param>
    /// <param name="Sections">Seccion -> visible.</param>
    /// <param name="Questions">Pregunta -> visible.</param>
    /// <param name="Comments">Comentario -> visible.</param>
    /// <param name="Iterations">Pasadas del punto fijo (la ultima es la que confirma que nada cambio).</param>
    /// <param name="Converged">Falso si se alcanzo el tope y aun habia cambios (esos elementos quedan ocultos).</param>
    public sealed record VisibilityResult(bool SubmitVisible,
        IReadOnlyDictionary<string, bool> Sections, IReadOnlyDictionary<string, bool> Questions,
        IReadOnlyDictionary<string, bool> Comments, int Iterations, bool Converged);

    /// <summary>
    /// Error de validacion de una pregunta.
    /// </summary>
    /// <param name="QuestionId">Pregunta.</param>
    /// <param name="Code"><c>required</c> (obligatoria vacia) o <c>validation</c> (condicion que no se cumple).</param>
    /// <param name="FailedCondition">La condicion que provoca el fallo; <c>null</c> en <c>required</c>.</param>
    public sealed record QuestionValidationError(string QuestionId, string Code, Condition? FailedCondition);

    /// <summary>
    /// Resultado de <see cref="FormConditionEngine.ComputeValidation"/>.
    /// </summary>
    /// <param name="Errors">Errores, en orden de pregunta y de condicion.</param>
    public sealed record ValidationResult(IReadOnlyList<QuestionValidationError> Errors)
    {
        /// <summary>Verdadero si no hay errores.</summary>
        public bool IsValid => Errors.Count == 0;
    }

    /// <summary>
    /// Decide, con las respuestas de un formulario, que se ve, que es valido y que destinos se crean.
    /// Puro y determinista: sin E/S, sin sesion, sin reloj. Es inmutable y seguro entre hilos.
    /// </summary>
    public sealed class FormConditionEngine
    {
        /// <summary>Codigo de error de una pregunta obligatoria, visible y vacia.</summary>
        public const string RequiredCode = "required";

        /// <summary>Codigo de error de una condicion de validacion que no se cumple.</summary>
        public const string ValidationCode = "validation";

        private readonly ConditionHandlerRegistry _handlers;
        private readonly FormEngineOptions _options;

        /// <summary>
        /// Crea el motor.
        /// </summary>
        /// <param name="handlers">Manejadores por tipo de pregunta.</param>
        /// <param name="options">Opciones; por omision, 100 pasadas como tope.</param>
        /// <exception cref="ArgumentException">Manejadores nulos o tope menor que 1.</exception>
        public FormConditionEngine(ConditionHandlerRegistry handlers, FormEngineOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(handlers);
            _options = options ?? new FormEngineOptions();
            ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaxIterations, 1, nameof(options));
            _handlers = handlers;
        }

        /// <summary>
        /// Valida la definicion al guardarla: sujetos inexistentes, operadores que el manejador no admite,
        /// valores y regex invalidos, ids repetidos, secciones inexistentes y ciclos de visibilidad.
        /// </summary>
        /// <param name="form">Formulario.</param>
        /// <returns>El mismo formulario, o un <see cref="DefinitionFailure{T}"/> con todos los errores.</returns>
        /// <exception cref="ArgumentNullException">Si <paramref name="form"/> es nulo.</exception>
        public Result<FormModel> ValidateDefinition(FormModel form)
        {
            var index = FormIndex.Build(form);
            var errors = new List<DefinitionError>();

            void Duplicates<T>(IReadOnlyList<T> items, Func<T, string> id, string list)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var i = 0; i < items.Count; i++)
                    if (!seen.Add(id(items[i])))
                        errors.Add(new DefinitionError("forms.duplicate_id", $"{list}[{i}].Id", $"El id '{id(items[i])}' esta repetido en {list}."));
            }
            Duplicates(index.Sections, s => s.Id, "Sections");
            Duplicates(index.Questions, q => q.Id, "Questions");
            Duplicates(index.Comments, c => c.Id, "Comments");
            Duplicates(index.Destinations, d => d.Id, "Destinations");

            CheckConditions(index, form.SubmitVisibility?.Conditions, "SubmitVisibility.Conditions", errors);
            for (var i = 0; i < index.Sections.Count; i++)
                CheckConditions(index, index.Sections[i].Visibility?.Conditions, $"Sections[{i}].Visibility.Conditions", errors);
            for (var i = 0; i < index.Questions.Count; i++)
            {
                var q = index.Questions[i];
                if (!index.SectionById.ContainsKey(q.SectionId ?? string.Empty))
                    errors.Add(new DefinitionError("forms.unknown_section", $"Questions[{i}].SectionId", $"La seccion '{q.SectionId}' no existe."));
                CheckConditions(index, q.Visibility?.Conditions, $"Questions[{i}].Visibility.Conditions", errors);
                CheckConditions(index, q.Validation?.Conditions, $"Questions[{i}].Validation.Conditions", errors);
            }
            for (var i = 0; i < index.Comments.Count; i++)
            {
                var c = index.Comments[i];
                if (!index.SectionById.ContainsKey(c.SectionId ?? string.Empty))
                    errors.Add(new DefinitionError("forms.unknown_section", $"Comments[{i}].SectionId", $"La seccion '{c.SectionId}' no existe."));
                CheckConditions(index, c.Visibility?.Conditions, $"Comments[{i}].Visibility.Conditions", errors);
            }
            for (var i = 0; i < index.Destinations.Count; i++)
                CheckConditions(index, index.Destinations[i].Creation?.Conditions, $"Destinations[{i}].Creation.Conditions", errors);

            errors.AddRange(VisibilityCycles.Find(index));

            return errors.Count == 0 ? Result.OK(form) : new DefinitionFailure<FormModel>(errors);
        }

        private void CheckConditions(FormIndex index, IReadOnlyList<Condition>? conditions, string path, List<DefinitionError> errors)
        {
            if (conditions is null) return;
            for (var i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                var at = $"{path}[{i}]";
                if (c is null || string.IsNullOrEmpty(c.SubjectId)) continue;   // sin sujeto: se ignora, como en GLPI

                if (!index.Exists(c.SubjectKind, c.SubjectId))
                {
                    errors.Add(new DefinitionError("forms.unknown_subject", at + ".SubjectId",
                        $"No existe {c.SubjectKind} '{c.SubjectId}'."));
                    continue;
                }
                if (c.Operator is ValueOperator.Visible or ValueOperator.NotVisible) continue;

                var handler = c.SubjectKind == SubjectKind.Question ? _handlers.Find(index.QuestionById[c.SubjectId].Type) : null;
                if (handler is null || !handler.SupportedOperators.Contains(c.Operator))
                {
                    errors.Add(new DefinitionError("forms.operator_not_supported", at + ".Operator",
                        $"El operador {c.Operator} no se admite sobre {c.SubjectKind} '{c.SubjectId}'."));
                    continue;
                }
                if (c.Operator is ValueOperator.MatchRegex or ValueOperator.NotMatchRegex)
                {
                    if (FormRegex.Validate(c.Value) is { } why)
                        errors.Add(new DefinitionError("forms.invalid_regex", at + ".Value", why + "."));
                    continue;
                }
                if (handler.ValidateValue(c.Operator, c.Value) is { } error)
                    errors.Add(error with { Path = at + ".Value" });
            }
        }

        /// <summary>
        /// Calcula la visibilidad por punto fijo: inicia visible solo lo <c>AlwaysVisible</c>, recorre
        /// formulario, preguntas, comentarios y secciones actualizando en sitio, y repite hasta que una pasada
        /// no cambie nada o se llegue al tope. Si no converge, lo que cambio en la ultima pasada queda oculto.
        /// </summary>
        /// <param name="form">Formulario.</param>
        /// <param name="answers">Respuestas.</param>
        /// <returns>La visibilidad.</returns>
        /// <exception cref="ArgumentNullException">Argumentos nulos.</exception>
        public VisibilityResult ComputeVisibility(FormModel form, FormAnswers answers)
        {
            var state = Evaluate(form, answers);
            return state.ToResult();
        }

        /// <summary>
        /// Valida las preguntas visibles: obligatoria vacia da <c>required</c>; si no, las condiciones de
        /// <c>ValidIf</c>/<c>InvalidIf</c> que provocan el fallo, una por error. Las ocultas no se validan.
        /// </summary>
        /// <param name="form">Formulario.</param>
        /// <param name="answers">Respuestas.</param>
        /// <returns>Los errores.</returns>
        /// <exception cref="ArgumentNullException">Argumentos nulos.</exception>
        public ValidationResult ComputeValidation(FormModel form, FormAnswers answers)
        {
            var state = Evaluate(form, answers);
            var errors = new List<QuestionValidationError>();
            foreach (var q in state.Index.Questions)
            {
                if (!state.IsQuestionVisible(q.Id)) continue;

                var answer = answers.Get(q.Id);
                var empty = _handlers.Find(q.Type)?.IsEmpty(answer) ?? answer.IsEmpty;
                if (q.IsMandatory && empty)
                {
                    errors.Add(new QuestionValidationError(q.Id, RequiredCode, null));
                    continue;
                }

                var rule = q.Validation;
                if (rule is null || rule.Strategy is not (ValidationStrategy.ValidIf or ValidationStrategy.InvalidIf)) continue;

                var conditions = rule.Conditions ?? [];
                var perCondition = new bool?[conditions.Count];
                var result = state.ComputeConditions(conditions, perCondition);
                var mustPass = rule.Strategy == ValidationStrategy.ValidIf;
                if (result == mustPass) continue;

                for (var i = 0; i < conditions.Count; i++)
                    if (perCondition[i] is { } r && r != mustPass)
                        errors.Add(new QuestionValidationError(q.Id, ValidationCode, conditions[i]));
            }
            return new ValidationResult(errors);
        }

        /// <summary>
        /// Destinos que se crean al enviar: <c>AlwaysCreated</c>, <c>CreatedIf</c> con condiciones verdaderas y
        /// <c>CreatedUnless</c> con condiciones falsas, calculando antes la visibilidad.
        /// </summary>
        /// <param name="form">Formulario.</param>
        /// <param name="answers">Respuestas.</param>
        /// <returns>Ids de los destinos, en orden de declaracion.</returns>
        /// <exception cref="ArgumentNullException">Argumentos nulos.</exception>
        public IReadOnlyList<string> ComputeDestinations(FormModel form, FormAnswers answers)
        {
            var state = Evaluate(form, answers);
            var created = new List<string>();
            foreach (var d in state.Index.Destinations)
            {
                var rule = d.Creation;
                var create = rule?.Strategy switch
                {
                    CreationStrategy.CreatedIf => state.ComputeConditions(rule.Conditions ?? [], null),
                    CreationStrategy.CreatedUnless => !state.ComputeConditions(rule.Conditions ?? [], null),
                    _ => true,
                };
                if (create) created.Add(d.Id);
            }
            return created;
        }

        private VisibilityState Evaluate(FormModel form, FormAnswers answers)
        {
            ArgumentNullException.ThrowIfNull(answers);
            var state = new VisibilityState(FormIndex.Build(form), answers, _handlers);
            state.RunFixedPoint(_options.MaxIterations);
            return state;
        }
    }

    /// <summary>
    /// Indices del formulario por id (si hay ids repetidos, gana el primero).
    /// </summary>
    internal sealed class FormIndex
    {
        public required FormModel Form { get; init; }
        public required IReadOnlyList<SectionModel> Sections { get; init; }        // por Rank, estable
        public required IReadOnlyList<QuestionModel> Questions { get; init; }
        public required IReadOnlyList<CommentModel> Comments { get; init; }
        public required IReadOnlyList<DestinationModel> Destinations { get; init; }
        public required Dictionary<string, SectionModel> SectionById { get; init; }
        public required Dictionary<string, QuestionModel> QuestionById { get; init; }
        public required Dictionary<string, CommentModel> CommentById { get; init; }
        public string? FirstSectionId => Sections.Count > 0 ? Sections[0].Id : null;

        public static FormIndex Build(FormModel form)
        {
            ArgumentNullException.ThrowIfNull(form);
            var sections = (form.Sections ?? []).Where(s => s is not null).OrderBy(s => s.Rank).ToArray();
            var questions = (form.Questions ?? []).Where(q => q is not null).ToArray();
            var comments = (form.Comments ?? []).Where(c => c is not null).ToArray();
            return new FormIndex
            {
                Form = form,
                Sections = sections,
                Questions = questions,
                Comments = comments,
                Destinations = (form.Destinations ?? []).Where(d => d is not null).ToArray(),
                SectionById = ById(sections, s => s.Id),
                QuestionById = ById(questions, q => q.Id),
                CommentById = ById(comments, c => c.Id),
            };
        }

        private static Dictionary<string, T> ById<T>(IEnumerable<T> items, Func<T, string> id)
        {
            var dict = new Dictionary<string, T>(StringComparer.Ordinal);
            foreach (var item in items) dict.TryAdd(id(item) ?? string.Empty, item);
            return dict;
        }

        public bool Exists(SubjectKind kind, string id) => kind switch
        {
            SubjectKind.Question => QuestionById.ContainsKey(id),
            SubjectKind.Section => SectionById.ContainsKey(id),
            SubjectKind.Comment => CommentById.ContainsKey(id),
            _ => false,
        };
    }

    /// <summary>
    /// Estado de una evaluacion: la cache de visibilidad del punto fijo y la evaluacion de condiciones.
    /// Vive solo durante una llamada.
    /// </summary>
    internal sealed class VisibilityState(FormIndex index, FormAnswers answers, ConditionHandlerRegistry handlers)
    {
        private readonly Dictionary<string, bool> _sections = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _questions = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _comments = new(StringComparer.Ordinal);
        private bool _submit;
        private int _iterations;
        private bool _converged;

        public FormIndex Index { get; } = index;

        public bool IsQuestionVisible(string id) => _questions.TryGetValue(id, out var v) && v;

        public void RunFixedPoint(int maxIterations)
        {
            // (1) Visible al inicio solo lo AlwaysVisible (y la primera seccion, que siempre lo es).
            _submit = IsAlways(Index.Form.SubmitVisibility);
            foreach (var q in Index.Questions) _questions.TryAdd(q.Id, IsAlways(q.Visibility));
            foreach (var c in Index.Comments) _comments.TryAdd(c.Id, IsAlways(c.Visibility));
            foreach (var s in Index.Sections) _sections.TryAdd(s.Id, s.Id == Index.FirstSectionId || IsAlways(s.Visibility));

            // (2)-(3) Pasadas en orden formulario, preguntas, comentarios, secciones, actualizando en sitio.
            var changed = new List<(SubjectKind? Kind, string Id)>();
            for (_iterations = 1; _iterations <= maxIterations; _iterations++)
            {
                changed.Clear();
                var submit = ComputeRule(Index.Form.SubmitVisibility);
                if (submit != _submit) { _submit = submit; changed.Add((null, string.Empty)); }
                foreach (var q in Index.Questions)
                    Update(_questions, q.Id, ComputeBlock(q.SectionId, q.Visibility), SubjectKind.Question, changed);
                foreach (var c in Index.Comments)
                    Update(_comments, c.Id, ComputeBlock(c.SectionId, c.Visibility), SubjectKind.Comment, changed);
                foreach (var s in Index.Sections)
                    Update(_sections, s.Id, s.Id == Index.FirstSectionId || ComputeRule(s.Visibility), SubjectKind.Section, changed);

                if (changed.Count == 0)
                {
                    _converged = true;
                    return;
                }
            }

            // REQ-004: sin convergencia se falla cerrado: lo que cambio en la ultima pasada queda oculto.
            _iterations = maxIterations;
            _converged = false;
            foreach (var (kind, id) in changed)
            {
                switch (kind)
                {
                    case null: _submit = false; break;
                    case SubjectKind.Question: _questions[id] = false; break;
                    case SubjectKind.Comment: _comments[id] = false; break;
                    default: _sections[id] = false; break;
                }
            }
        }

        private static void Update(Dictionary<string, bool> cache, string id, bool value, SubjectKind kind,
            List<(SubjectKind? Kind, string Id)> changed)
        {
            if (cache[id] == value) return;
            cache[id] = value;
            changed.Add((kind, id));
        }

        private static bool IsAlways(VisibilityRule? rule) =>
            rule is null || rule.Strategy is not (VisibilityStrategy.VisibleIf or VisibilityStrategy.HiddenIf);

        // Un bloque cuya seccion esta oculta es oculto, sin evaluar sus condiciones.
        private bool ComputeBlock(string sectionId, VisibilityRule? rule) =>
            (!_sections.TryGetValue(sectionId ?? string.Empty, out var parent) || parent) && ComputeRule(rule);

        private bool ComputeRule(VisibilityRule? rule) => rule?.Strategy switch
        {
            VisibilityStrategy.VisibleIf => ComputeConditions(rule.Conditions ?? [], null),
            VisibilityStrategy.HiddenIf => !ComputeConditions(rule.Conditions ?? [], null),
            _ => true,
        };

        /// <summary>
        /// AND sobre OR: se parte en grupos por cada <c>Or</c> y basta un grupo con todo verdadero. Sin corte:
        /// se evaluan todas (la validacion necesita cada resultado). Lista vacia o sin sujetos: falso.
        /// </summary>
        public bool ComputeConditions(IReadOnlyList<Condition> conditions, bool?[]? perCondition)
        {
            var any = false;
            var result = false;
            var group = true;
            for (var i = 0; i < conditions.Count; i++)
            {
                var c = conditions[i];
                if (c is null || string.IsNullOrEmpty(c.SubjectId)) continue;
                var value = ComputeCondition(c);
                if (perCondition is not null) perCondition[i] = value;

                if (any && c.Logic == LogicOperator.Or)
                {
                    result |= group;
                    group = true;
                }
                group &= value;
                any = true;
            }
            return any && (result || group);
        }

        private bool ComputeCondition(Condition c)
        {
            bool visible;
            switch (c.SubjectKind)
            {
                case SubjectKind.Question:
                    if (!Index.QuestionById.TryGetValue(c.SubjectId, out var question)) return false;
                    visible = _questions[question.Id];
                    switch (c.Operator)
                    {
                        case ValueOperator.Visible: return visible;
                        case ValueOperator.NotVisible: return !visible;
                        case ValueOperator.Empty when !visible: return true;    // oculta cuenta como vacia
                        case var _ when !visible: return false;                  // cualquier otro operador: falso
                    }
                    var handler = handlers.Find(question.Type);
                    if (handler is null || !handler.SupportedOperators.Contains(c.Operator)) return false;
                    var answer = answers.Get(question.Id);
                    return c.Operator switch
                    {
                        ValueOperator.Empty => handler.IsEmpty(answer),
                        ValueOperator.NotEmpty => !handler.IsEmpty(answer),
                        _ => handler.Apply(answer, c.Operator, c.Value),
                    };
                case SubjectKind.Section:
                    if (!_sections.TryGetValue(c.SubjectId, out visible)) return false;
                    break;
                case SubjectKind.Comment:
                    if (!_comments.TryGetValue(c.SubjectId, out visible)) return false;
                    break;
                default:
                    return false;
            }
            return c.Operator switch
            {
                ValueOperator.Visible => visible,
                ValueOperator.NotVisible => !visible,
                _ => false,
            };
        }

        public VisibilityResult ToResult()
        {
            var questions = new Dictionary<string, bool>(_questions, StringComparer.Ordinal);
            var comments = new Dictionary<string, bool>(_comments, StringComparer.Ordinal);
            var sections = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var s in Index.Sections)
            {
                if (sections.ContainsKey(s.Id)) continue;
                var visible = _sections[s.Id];
                // REQ-005: una seccion (que no sea la primera) sin preguntas ni comentarios visibles se oculta.
                if (visible && s.Id != Index.FirstSectionId)
                    visible = Index.Questions.Any(q => q.SectionId == s.Id && questions[q.Id])
                              || Index.Comments.Any(c => c.SectionId == s.Id && comments[c.Id]);
                sections[s.Id] = visible;
            }
            return new VisibilityResult(_submit, sections, questions, comments, _iterations, _converged);
        }
    }

    /// <summary>
    /// Deteccion de ciclos en el grafo de dependencias de visibilidad: arista X -> Y si la visibilidad de Y
    /// tiene una condicion con sujeto X, mas seccion -> bloque para cada bloque de la seccion.
    /// </summary>
    internal static class VisibilityCycles
    {
        private readonly record struct Node(SubjectKind Kind, string Id);

        public static IEnumerable<DefinitionError> Find(FormIndex index)
        {
            var edges = new Dictionary<Node, List<Node>>();
            var order = new List<Node>();
            void AddNode(Node n)
            {
                if (edges.TryAdd(n, [])) order.Add(n);
            }
            void Depends(Node target, VisibilityRule? rule)
            {
                if (rule is null || rule.Strategy is not (VisibilityStrategy.VisibleIf or VisibilityStrategy.HiddenIf)) return;
                foreach (var c in rule.Conditions ?? [])
                {
                    if (c is null || string.IsNullOrEmpty(c.SubjectId) || !index.Exists(c.SubjectKind, c.SubjectId)) continue;
                    var source = new Node(c.SubjectKind, c.SubjectId);
                    AddNode(source);
                    if (!edges[source].Contains(target)) edges[source].Add(target);
                }
            }

            foreach (var q in index.Questions) AddNode(new Node(SubjectKind.Question, q.Id));
            foreach (var c in index.Comments) AddNode(new Node(SubjectKind.Comment, c.Id));
            foreach (var s in index.Sections) AddNode(new Node(SubjectKind.Section, s.Id));

            foreach (var q in index.Questions)
            {
                var node = new Node(SubjectKind.Question, q.Id);
                Depends(node, q.Visibility);
                if (index.SectionById.ContainsKey(q.SectionId ?? string.Empty)) edges[new Node(SubjectKind.Section, q.SectionId!)].Add(node);
            }
            foreach (var c in index.Comments)
            {
                var node = new Node(SubjectKind.Comment, c.Id);
                Depends(node, c.Visibility);
                if (index.SectionById.ContainsKey(c.SectionId ?? string.Empty)) edges[new Node(SubjectKind.Section, c.SectionId!)].Add(node);
            }
            foreach (var s in index.Sections) Depends(new Node(SubjectKind.Section, s.Id), s.Visibility);

            // DFS iterativo en orden de declaracion; cada arista de retroceso es un ciclo.
            var state = new Dictionary<Node, int>();   // 0 = sin visitar, 1 = en la pila, 2 = terminado
            var errors = new List<DefinitionError>();
            foreach (var root in order)
            {
                if (state.GetValueOrDefault(root) != 0) continue;
                var path = new List<Node> { root };
                var cursors = new Stack<int>();
                cursors.Push(0);
                state[root] = 1;
                while (path.Count > 0)
                {
                    var node = path[^1];
                    var next = cursors.Pop();
                    var children = edges[node];
                    if (next >= children.Count)
                    {
                        state[node] = 2;
                        path.RemoveAt(path.Count - 1);
                        continue;
                    }
                    cursors.Push(next + 1);
                    var child = children[next];
                    switch (state.GetValueOrDefault(child))
                    {
                        case 0:
                            state[child] = 1;
                            path.Add(child);
                            cursors.Push(0);
                            break;
                        case 1:
                            var cycle = path.Skip(path.IndexOf(child)).Append(child).Select(n => n.Id);
                            var text = string.Join(" → ", cycle);
                            errors.Add(new DefinitionError("forms.visibility_cycle", $"{child.Kind}s[{child.Id}].Visibility",
                                $"La visibilidad depende de si misma: {text}."));
                            break;
                    }
                }
            }
            return errors;
        }
    }
}
