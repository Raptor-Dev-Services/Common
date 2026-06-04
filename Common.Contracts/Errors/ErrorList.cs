using Common.Exceptions;

namespace Common.Errors
{
    public sealed class ErrorList : List<string>
    {
        public bool IsEmpty => this.Count == 0;

        public BusinessRuleException AsException() => new(ToString());

        public override string ToString() =>
            this.Select(item => $"- {item}").Aggregate((x, y) => $"{x}\n{y}");
    }
}

