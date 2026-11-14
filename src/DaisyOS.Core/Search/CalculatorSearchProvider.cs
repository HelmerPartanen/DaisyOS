using System.Globalization;

namespace DaisyOS.Core.Search;

/// <summary>Small bounded arithmetic grammar. No script engine, processes or reflection.</summary>
public sealed class CalculatorSearchProvider : ISearchProvider
{
    public string Id => "calculator";
    public SearchProviderCapabilities Capabilities => new("Calculator", [SearchResultType.Calculator], Priority: 110);
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCalculate(query.RawText, out var value)) return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        return Task.FromResult<IReadOnlyList<SearchResult>>([new("result", Id, value, SearchResultType.Calculator, 1100,
            new CalculatorSearchPayload(value)) { Subtitle = query.RawText, Actions = [new("copy-result", Id, value, "Copy result")] }]);
    }
    public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Copying results requires the presentation clipboard.");
    public static bool TryCalculate(string expression, out string value)
    {
        value = "";
        if (expression.Length is 0 or > 256 || !expression.Any(c => char.IsAsciiDigit(c)) ||
            expression.Any(c => !char.IsAsciiDigit(c) && !char.IsWhiteSpace(c) && !".+-*/%^()".Contains(c))) return false;
        try
        {
            var parser = new Parser(expression);
            var result = parser.Expression();
            parser.Skip();
            if (parser.Position != expression.Length || !double.IsFinite(result)) return false;
            value = (result == 0 ? 0 : result).ToString("G12", CultureInfo.InvariantCulture);
            return true;
        }
        catch (ArithmeticException) { return false; }
        catch (FormatException) { return false; }
    }
    private sealed class Parser(string text)
    {
        public int Position;
        private int _depth;
        public void Skip() { while (Position < text.Length && char.IsWhiteSpace(text[Position])) Position++; }
        private bool Eat(char token) { Skip(); if (Position >= text.Length || text[Position] != token) return false; Position++; return true; }
        public double Expression()
        {
            var value = Multiply();
            while (true) { if (Eat('+')) value += Multiply(); else if (Eat('-')) value -= Multiply(); else return value; }
        }
        private double Multiply()
        {
            var value = Unary();
            while (true)
            {
                if (Eat('*')) value *= Unary();
                else if (Eat('/')) { var divisor = Unary(); if (divisor == 0) throw new ArithmeticException(); value /= divisor; }
                else if (Eat('%')) { var divisor = Unary(); if (divisor == 0) throw new ArithmeticException(); value %= divisor; }
                else return value;
            }
        }
        private double Unary()
        {
            if (++_depth > 32) throw new FormatException();
            try
            {
                if (Eat('+')) return Unary();
                if (Eat('-')) return -Unary();
                var value = Primary();
                return Eat('^') ? Math.Pow(value, Unary()) : value;
            }
            finally { _depth--; }
        }
        private double Primary()
        {
            if (Eat('(')) { var value = Expression(); if (!Eat(')')) throw new FormatException(); return value; }
            Skip();
            var start = Position;
            while (Position < text.Length && (char.IsAsciiDigit(text[Position]) || text[Position] == '.')) Position++;
            if (start == Position) throw new FormatException();
            return double.Parse(text.AsSpan(start, Position - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
    }
}
