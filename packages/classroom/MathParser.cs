using System.Collections.Immutable;
using System.Globalization;

namespace EduCast.Classroom;

public abstract record MathNode;
public sealed record Literal(string Value) : MathNode;
public sealed record Binary(string Operator, MathNode Left, MathNode Right) : MathNode;
public sealed record Prefix(string Operator, MathNode Operand) : MathNode;
public sealed record Group(MathNode Child, char Open, char Close) : MathNode;
public sealed record Fraction(MathNode Top, MathNode Bottom) : MathNode;
public sealed record Scripts(MathNode Basis, MathNode? Superscript, MathNode? Subscript) : MathNode;
public sealed record Radical(MathNode Child, MathNode? Index = null) : MathNode;
public sealed record Function(string Name, MathNode Argument) : MathNode;
public sealed record Matrix(ImmutableArray<ImmutableArray<MathNode>> Rows) : MathNode;

public static class MathParser
{
    public const int MaxLength = 280;
    private const int MaxNesting = 12;
    private static readonly HashSet<string> Functions = new(StringComparer.Ordinal)
        { "sin", "cos", "tan", "abs", "ln", "log", "exp", "asin", "acos", "atan" };
    private static readonly Dictionary<string, string> Symbols = new(StringComparer.Ordinal)
    {
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ",
        ["theta"] = "θ", ["lambda"] = "λ", ["mu"] = "μ", ["pi"] = "π",
        ["sigma"] = "σ", ["phi"] = "φ", ["omega"] = "ω", ["infty"] = "∞"
    };

    public static bool TryParse(string source, out MathNode? node, out string error)
    {
        node = null;
        error = "";
        if (string.IsNullOrWhiteSpace(source) || source.Length > MaxLength || source.Contains('\0'))
        {
            error = $"Enter an expression of 1–{MaxLength} characters.";
            return false;
        }
        try
        {
            var parser = new Parser(source);
            node = parser.Expression();
            if (parser.Peek() != '\0')
                throw parser.Invalid("Unexpected character");
            return true;
        }
        catch (FormatException exception)
        {
            node = null;
            error = exception.Message;
            return false;
        }
    }

    public static bool TryParseGraph(string source, out MathNode? node, out string error)
    {
        if (!TryParse(source, out node, out error))
            return false;
        if (node is Binary { Operator: "=", Left: Literal { Value: "y" }, Right: var right })
            node = right;
        if (IsNumeric(node!))
            return true;
        node = null;
        error = "Use x, numbers, π, e, powers, roots, or a numeric function such as sin(x).";
        return false;
    }

    public static double Evaluate(MathNode node, double x) => node switch
    {
        Literal { Value: "x" } => x,
        Literal { Value: "π" } => Math.PI,
        Literal { Value: "e" } => Math.E,
        Literal literal => double.TryParse(literal.Value, NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value) ? value : double.NaN,
        Group group => Evaluate(group.Child, x),
        Prefix prefix => (prefix.Operator == "−" ? -1 : 1) * Evaluate(prefix.Operand, x),
        Fraction fraction => Evaluate(fraction.Top, x) / Evaluate(fraction.Bottom, x),
        Scripts { Superscript: not null } script =>
            Math.Pow(Evaluate(script.Basis, x), Evaluate(script.Superscript, x)),
        Radical radical => Root(Evaluate(radical.Child, x),
            radical.Index is null ? 2 : Evaluate(radical.Index, x)),
        Binary binary => EvaluateBinary(binary.Operator, Evaluate(binary.Left, x), Evaluate(binary.Right, x)),
        Function function => EvaluateFunction(function.Name, Evaluate(function.Argument, x)),
        _ => double.NaN
    };

    private static double Root(double number, double index) =>
        number < 0 && index % 2 == 1 ? -Math.Pow(-number, 1 / index) : Math.Pow(number, 1 / index);

    private static bool IsNumeric(MathNode node) => node switch
    {
        Literal { Value: "x" or "π" or "e" } => true,
        Literal literal => double.TryParse(literal.Value, NumberStyles.Float,
            CultureInfo.InvariantCulture, out var number) && double.IsFinite(number),
        Group group => IsNumeric(group.Child),
        Prefix { Operator: "+" or "−" } prefix => IsNumeric(prefix.Operand),
        Fraction fraction => IsNumeric(fraction.Top) && IsNumeric(fraction.Bottom),
        Radical radical => IsNumeric(radical.Child) && (radical.Index is null || IsNumeric(radical.Index)),
        Scripts { Superscript: not null, Subscript: null } script =>
            IsNumeric(script.Basis) && IsNumeric(script.Superscript),
        Binary { Operator: "+" or "−" or "·" or "×" or "" } binary =>
            IsNumeric(binary.Left) && IsNumeric(binary.Right),
        Function function => IsNumeric(function.Argument),
        _ => false
    };

    private static double EvaluateBinary(string op, double a, double b) => op switch
    {
        "+" => a + b, "−" => a - b, "" or "·" or "×" => a * b, _ => double.NaN
    };

    private static double EvaluateFunction(string name, double value) => name switch
    {
        "sin" => Math.Sin(value), "cos" => Math.Cos(value), "tan" => Math.Tan(value),
        "asin" => Math.Asin(value), "acos" => Math.Acos(value), "atan" => Math.Atan(value),
        "ln" => Math.Log(value), "log" => Math.Log10(value), "exp" => Math.Exp(value),
        "abs" => Math.Abs(value), _ => double.NaN
    };

    private sealed class Parser(string source)
    {
        private int position;

        public char Peek()
        {
            while (position < source.Length && char.IsWhiteSpace(source[position]))
                position++;
            return position < source.Length ? source[position] : '\0';
        }

        private bool Consume(char character)
        {
            if (Peek() != character)
                return false;
            position++;
            return true;
        }

        private bool Take(string token)
        {
            Peek();
            if (!source.AsSpan(position).StartsWith(token, StringComparison.Ordinal))
                return false;
            position += token.Length;
            return true;
        }

        private void Expect(char character)
        {
            if (!Consume(character))
                throw Invalid($"Expected '{character}'");
        }

        public FormatException Invalid(string message) =>
            new($"{message} at character {position + 1}.");

        public MathNode Expression(int depth = 0)
        {
            if (depth > MaxNesting)
                throw Invalid("The expression is nested too deeply");
            var result = Sum(depth);
            while (true)
            {
                var op = Take("<=") ? "≤" : Take(">=") ? "≥" : Take("!=") ? "≠" :
                    Peek() is '=' or '<' or '>' or '≤' or '≥' or '≠' or '≈' ? source[position++].ToString() : "";
                if (op.Length == 0)
                    return result;
                result = new Binary(op, result, Sum(depth));
            }
        }

        private MathNode Sum(int depth)
        {
            var result = Product(depth);
            while (true)
            {
                var op = Take("+-") || Consume('±') ? "±" : Consume('+') ? "+" :
                    Consume('-') || Consume('−') ? "−" : "";
                if (op.Length == 0)
                    return result;
                result = new Binary(op, result, Product(depth));
            }
        }

        private MathNode Product(int depth)
        {
            var result = Juxtaposition(depth);
            while (true)
            {
                if (Consume('/') || Consume('÷'))
                    result = new Fraction(Unwrap(result), Unwrap(Juxtaposition(depth)));
                else if (Consume('*') || Consume('·'))
                    result = new Binary("·", result, Juxtaposition(depth));
                else if (Consume('×'))
                    result = new Binary("×", result, Juxtaposition(depth));
                else
                    return result;
            }
        }

        private MathNode Juxtaposition(int depth)
        {
            var result = Term(depth);
            while (StartsAtom(Peek()))
                result = new Binary("", result, Term(depth));
            return result;
        }

        private MathNode Term(int depth)
        {
            if (depth > MaxNesting)
                throw Invalid("The expression is nested too deeply");
            if (Consume('-') || Consume('−'))
                return new Prefix("−", Term(depth + 1));
            if (Consume('+'))
                return new Prefix("+", Term(depth + 1));
            var basis = Atom(depth);
            MathNode? superscript = null;
            MathNode? subscript = null;
            while (true)
            {
                if (Consume('^'))
                {
                    if (superscript is not null)
                        throw Invalid("Group a nested power in parentheses");
                    superscript = ScriptArgument(depth);
                }
                else if (Consume('_'))
                {
                    if (subscript is not null)
                        throw Invalid("Group a nested subscript in parentheses");
                    subscript = ScriptArgument(depth);
                }
                else
                    break;
            }
            return superscript is null && subscript is null ? basis : new Scripts(basis, superscript, subscript);
        }

        private MathNode ScriptArgument(int depth)
        {
            if (Consume('-') || Consume('−'))
                return new Prefix("−", Unwrap(Atom(depth)));
            return Unwrap(Atom(depth));
        }

        private MathNode Atom(int depth)
        {
            var character = Peek();
            if (character is '(' or '[' or '{')
            {
                position++;
                var close = character == '(' ? ')' : character == '[' ? ']' : '}';
                var child = Expression(depth + 1);
                Expect(close);
                return new Group(child, character, close);
            }

            var start = position;
            if (char.IsAsciiDigit(character) || character == '.')
            {
                while (position < source.Length &&
                    (char.IsAsciiDigit(source[position]) || source[position] == '.'))
                    position++;
                var number = source[start..position];
                if (double.TryParse(number, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out _))
                    return new Literal(number);
                throw Invalid("Invalid decimal number");
            }

            if (!char.IsLetter(character) && character != '∞')
                throw Invalid("Expected a number, symbol or group");
            do { position++; } while (position < source.Length && char.IsLetter(source[position]));
            var word = source[start..position];
            if (word == "sqrt" && Consume('('))
            {
                var child = Expression(depth + 1);
                Expect(')');
                return new Radical(child);
            }
            if (word == "root" && Consume('('))
            {
                var index = Expression(depth + 1);
                Expect(',');
                var child = Expression(depth + 1);
                Expect(')');
                return new Radical(child, index);
            }
            if (word == "matrix" && Consume('('))
                return ReadMatrix(depth);
            if (Functions.Contains(word) && Consume('('))
            {
                var argument = Expression(depth + 1);
                Expect(')');
                return new Function(word, argument);
            }
            return new Literal(Symbols.GetValueOrDefault(word, word));
        }

        private Matrix ReadMatrix(int depth)
        {
            var rows = ImmutableArray.CreateBuilder<ImmutableArray<MathNode>>();
            do
            {
                var columns = ImmutableArray.CreateBuilder<MathNode>();
                do { columns.Add(Expression(depth + 1)); } while (Consume(','));
                if (columns.Count > 6 || rows.Count == 6 ||
                    (rows.Count > 0 && columns.Count != rows[0].Length))
                    throw Invalid("Use a rectangular matrix up to 6 by 6");
                rows.Add(columns.ToImmutable());
            } while (Consume(';'));
            Expect(')');
            return new Matrix(rows.ToImmutable());
        }

        private static bool StartsAtom(char c) => c is '(' or '[' or '{' or '.' or '∞' ||
            char.IsAsciiDigit(c) || char.IsLetter(c);

        private static MathNode Unwrap(MathNode node) => node is Group { Open: '(' or '{' } group
            ? group.Child : node;
    }
}
