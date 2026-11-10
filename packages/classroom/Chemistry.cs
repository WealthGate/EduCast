using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace EduCast.Classroom;

public static partial class Chemistry
{
    private static readonly string[] Elements = ("H He Li Be B C N O F Ne Na Mg Al Si P S Cl Ar K Ca Sc Ti V Cr " +
        "Mn Fe Co Ni Cu Zn Ga Ge As Se Br Kr Rb Sr Y Zr Nb Mo Tc Ru Rh Pd Ag Cd In Sn Sb Te I Xe Cs Ba La Ce Pr Nd " +
        "Pm Sm Eu Gd Tb Dy Ho Er Tm Yb Lu Hf Ta W Re Os Ir Pt Au Hg Tl Pb Bi Po At Rn Fr Ra Ac Th Pa U Np Pu Am Cm " +
        "Bk Cf Es Fm Md No Lr Rf Db Sg Bh Hs Mt Ds Rg Cn Nh Fl Mc Lv Ts Og").Split(' ');
    private const string Subscripts = "₀₁₂₃₄₅₆₇₈₉";
    private const string Superscripts = "⁰¹²³⁴⁵⁶⁷⁸⁹";

    public static IReadOnlyList<string> ShellElements { get; } = Array.AsReadOnly(Elements[..20]);

    public static int AtomicNumber(string? symbol) => Array.IndexOf(Elements, symbol) + 1;

    public static int[] Shells(string? symbol)
    {
        var number = AtomicNumber(symbol);
        return number switch
        {
            < 1 or > 20 => [],
            <= 2 => [number],
            <= 10 => [2, number - 2],
            <= 18 => [2, 8, number - 10],
            _ => [2, 8, 8, number - 18]
        };
    }

    public static bool TryFormat(string? source, out string display, out bool? balanced, out string error)
    {
        display = "";
        balanced = null;
        error = "";
        if (string.IsNullOrWhiteSpace(source) || source.Length > MathParser.MaxLength)
        {
            error = "Enter a chemical formula or reaction.";
            return false;
        }
        var normalized = new string(source.Where(c => !char.IsWhiteSpace(c)).Select(c =>
        {
            var subscript = Subscripts.IndexOf(c);
            return subscript < 0 ? c : (char)('0' + subscript);
        }).ToArray());
        normalized = UnicodeCharge().Replace(normalized, match => "^" + new string(match.Value.Select(c => c switch
        {
            '⁺' => '+',
            '⁻' => '-',
            _ => (char)('0' + Superscripts.IndexOf(c))
        }).ToArray()));
        try
        {
            var parser = new Parser(normalized);
            var left = parser.Side();
            var arrow = parser.Match("<=>") || parser.Match("⇌") ? " ⇌ " :
                parser.Match("->") || parser.Match("→") ? " → " :
                parser.Match("<-") || parser.Match("←") ? " ← " : null;
            if (arrow is not null)
            {
                parser.Display.Append(arrow);
                var right = parser.Side();
                balanced = left.Count == right.Count && left.All(pair =>
                    right.TryGetValue(pair.Key, out var count) && count == pair.Value);
            }
            if (!parser.Done)
                throw new FormatException("Use + between species and a single -> or <=> for a reaction arrow.");
            display = parser.Display.ToString();
            return true;
        }
        catch (FormatException failure)
        {
            error = failure.Message;
            return false;
        }
    }

    [GeneratedRegex("[⁰¹²³⁴⁵⁶⁷⁸⁹]*[⁺⁻]")]
    private static partial Regex UnicodeCharge();

    private sealed class Parser(string text)
    {
        private int position;
        internal StringBuilder Display { get; } = new();
        internal bool Done => position == text.Length;
        private char Current => Done ? '\0' : text[position];

        internal bool Match(string token)
        {
            if (!text.AsSpan(position).StartsWith(token, StringComparison.Ordinal))
                return false;
            position += token.Length;
            return true;
        }

        internal Dictionary<string, BigInteger> Side()
        {
            var total = new Dictionary<string, BigInteger>();
            do
            {
                var coefficient = Number();
                Display.Append(coefficient);
                var species = new Dictionary<string, BigInteger>();
                var electron = Match("e");
                if (electron)
                    Display.Append('e');
                else
                {
                    Merge(species, Formula(0), 1);
                    while (Current is '·' or '.')
                    {
                        position++;
                        Display.Append('·');
                        var multiplier = Number();
                        Display.Append(multiplier);
                        Merge(species, Formula(0), Value(multiplier));
                    }
                }

                var charge = 0;
                if (Match("^"))
                {
                    var magnitude = Number();
                    if (Current is not ('+' or '-'))
                        throw new FormatException("Put the sign after the charge, as in Fe^3+ or Cl^-.");
                    var positive = text[position++] == '+';
                    charge = Value(magnitude) * (positive ? 1 : -1);
                    AppendScript(magnitude, Superscripts);
                    Display.Append(positive ? '⁺' : '⁻');
                }
                if (electron && charge != -1)
                    throw new FormatException("Enter an electron as e^-.");
                if (charge != 0)
                    species.Add("charge", charge);
                foreach (var state in new[] { "(aq)", "(s)", "(l)", "(g)" })
                    if (Match(state))
                    {
                        Display.Append(state);
                        break;
                    }
                Merge(total, species, Value(coefficient));
                if (!Match("+"))
                    break;
                Display.Append(" + ");
            } while (true);
            total.Remove("charge", out var netCharge);
            if (!netCharge.IsZero)
                total.Add("charge", netCharge);
            return total;
        }

        private Dictionary<string, BigInteger> Formula(int depth)
        {
            if (depth == 10)
                throw new FormatException("This formula has too many nested groups.");
            var counts = new Dictionary<string, BigInteger>();
            while (Current is >= 'A' and <= 'Z' || Current == '[' ||
                Current == '(' && position + 1 < text.Length && text[position + 1] is >= 'A' and <= 'Z' or '(' or '[')
            {
                if (Current is '(' or '[')
                {
                    var open = text[position++];
                    Display.Append(open);
                    var group = Formula(depth + 1);
                    var close = open == '(' ? ')' : ']';
                    if (Current != close)
                        throw new FormatException("Close each group with its matching bracket.");
                    position++;
                    Display.Append(close);
                    var multiplier = Number();
                    AppendScript(multiplier, Subscripts);
                    Merge(counts, group, Value(multiplier));
                    continue;
                }
                var start = position++;
                if (Current is >= 'a' and <= 'z')
                    position++;
                var element = text[start..position];
                if (AtomicNumber(element) == 0)
                    throw new FormatException($"Unknown element symbol: {element}. Check capitalization.");
                Display.Append(element);
                var count = Number();
                AppendScript(count, Subscripts);
                counts[element] = counts.GetValueOrDefault(element) + Value(count);
            }
            if (counts.Count == 0)
                throw new FormatException("Enter a formula using element symbols, such as H2O or Ca(OH)2.");
            return counts;
        }

        private string Number()
        {
            var start = position;
            while (Current is >= '0' and <= '9')
                position++;
            if (position == start)
                return "";
            if (position - start > 6 || text[start] == '0')
                throw new FormatException("Use positive whole numbers up to six digits.");
            return text[start..position];
        }

        private static int Value(string digits) => digits.Length == 0 ? 1 : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);

        private void AppendScript(string digits, string glyphs)
        {
            foreach (var digit in digits)
                Display.Append(glyphs[digit - '0']);
        }

        private static void Merge(Dictionary<string, BigInteger> total, Dictionary<string, BigInteger> term, int multiplier)
        {
            foreach (var (element, count) in term)
                total[element] = total.GetValueOrDefault(element) + count * multiplier;
        }
    }
}
