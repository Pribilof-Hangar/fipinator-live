// FIPinator Live - FREE and open source (AGPL-3.0). Free to use, change, share and fork.
// https://github.com/Pribilof-Hangar/fipinator-live
using System.Globalization;

namespace Fipinator.Core;

/// <summary>
/// Small formulas for values worked out from other values, so gauges don't need code.
/// Supports numbers, value names, + - * / % and parentheses, and these functions:
/// <c>wrap360(x)</c> (0 to under 360), <c>wrap180(x)</c> (-180 to under 180),
/// <c>abs(x)</c>, <c>min(a, b)</c>, <c>max(a, b)</c>, <c>clamp(x, low, high)</c>, and
/// <c>choose(test, a, b)</c> (a when test is above 0, else b).
/// Example: <c>wrap360(vor1_radial + 180 - heading)</c>.
/// </summary>
public sealed class ValueExpression
{
    private readonly Node _root;

    private ValueExpression(string text, Node root)
    {
        Text = text;
        _root = root;
    }

    public string Text { get; }

    /// <summary>Value names the formula uses.</summary>
    public IReadOnlySet<string> Names => _root.Names();

    /// <exception cref="FormatException">The formula can't be read; the message says where.</exception>
    public static ValueExpression Parse(string text)
    {
        var parser = new Parser(text);
        var root = parser.ParseExpression();
        parser.ExpectEnd();
        return new ValueExpression(text, root);
    }

    public double Evaluate(Func<string, double> valueOf) => _root.Evaluate(valueOf);

    /// <summary>
    /// The same formula in FSX-style RPN (reverse Polish, as SPAD.neXt gauges use), with each value name
    /// replaced by <paramref name="nameToRpn"/>'s text; null if a name can't be translated. Avoids the % sign
    /// (SPAD.neXt's docs warn about it): a % b becomes a - b * flr(a / b), the same for the positive numbers
    /// gauges use.
    /// </summary>
    public string? ToRpn(Func<string, string?> nameToRpn) => Rpn(_root, nameToRpn);

    private static string? Rpn(Node node, Func<string, string?> name)
    {
        static string N(double v) => Math.Round(v, 6).ToString(CultureInfo.InvariantCulture);
        string? R(Node n) => Rpn(n, name);
        switch (node)
        {
            case Number n: return N(n.Value);
            case Name n: return name(n.Id);
            case Unary { Op: '-' } u: return R(u.Operand) is { } x ? $"0 {x} -" : null;
            case Unary u: return R(u.Operand);
            case Binary b:
            {
                if (R(b.Left) is not { } a || R(b.Right) is not { } c) return null;
                return b.Op == '%' ? $"{a} {c} {a} {c} / flr * -" : $"{a} {c} {b.Op}";
            }
            case Call call:
            {
                var args = call.Args.Select(R).ToArray();
                if (args.Any(x => x is null)) return null;
                return call.Function switch
                {
                    "wrap360" => $"{args[0]} {args[0]} 360 / flr 360 * -",
                    "wrap180" => $"{args[0]} 180 + {args[0]} 180 + 360 / flr 360 * - 180 -",
                    "abs" => $"{args[0]} abs",
                    "min" => $"{args[0]} {args[1]} min",
                    "max" => $"{args[0]} {args[1]} max",
                    "clamp" => $"{args[0]} {args[1]} {args[2]} max min {args[1]} {args[2]} min max",
                    "choose" => $"{args[0]} 0 > if{{ {args[1]} }} els{{ {args[2]} }}",
                    _ => null,
                };
            }
            default: return null;
        }
    }

    private abstract record Node
    {
        public abstract double Evaluate(Func<string, double> valueOf);
        public virtual IReadOnlySet<string> Names() => new HashSet<string>();
    }

    private sealed record Number(double Value) : Node
    {
        public override double Evaluate(Func<string, double> valueOf) => Value;
    }

    private sealed record Name(string Id) : Node
    {
        public override double Evaluate(Func<string, double> valueOf) => valueOf(Id);
        public override IReadOnlySet<string> Names() => new HashSet<string> { Id };
    }

    private sealed record Unary(char Op, Node Operand) : Node
    {
        public override double Evaluate(Func<string, double> valueOf) => Op == '-' ? -Operand.Evaluate(valueOf) : Operand.Evaluate(valueOf);
        public override IReadOnlySet<string> Names() => Operand.Names();
    }

    private sealed record Binary(char Op, Node Left, Node Right) : Node
    {
        public override double Evaluate(Func<string, double> valueOf)
        {
            var a = Left.Evaluate(valueOf);
            var b = Right.Evaluate(valueOf);
            return Op switch
            {
                '+' => a + b,
                '-' => a - b,
                '*' => a * b,
                '/' => b == 0 ? 0 : a / b,
                '%' => b == 0 ? 0 : a % b,
                _ => throw new InvalidOperationException(),
            };
        }

        public override IReadOnlySet<string> Names() => Left.Names().Union(Right.Names()).ToHashSet();
    }

    private sealed record Call(string Function, Node[] Args) : Node
    {
        public override double Evaluate(Func<string, double> valueOf)
        {
            var a = Args.Select(x => x.Evaluate(valueOf)).ToArray();
            return Function switch
            {
                "wrap360" => (a[0] % 360 + 360) % 360,
                "wrap180" => ((a[0] + 180) % 360 + 360) % 360 - 180,
                "abs" => Math.Abs(a[0]),
                "min" => Math.Min(a[0], a[1]),
                "max" => Math.Max(a[0], a[1]),
                "clamp" => Math.Clamp(a[0], Math.Min(a[1], a[2]), Math.Max(a[1], a[2])),
                "choose" => a[0] > 0 ? a[1] : a[2],
                _ => throw new InvalidOperationException(),
            };
        }

        public override IReadOnlySet<string> Names() => Args.SelectMany(x => x.Names()).ToHashSet();
    }

    private static readonly Dictionary<string, int> Functions = new()
    {
        ["wrap360"] = 1, ["wrap180"] = 1, ["abs"] = 1, ["min"] = 2, ["max"] = 2, ["clamp"] = 3, ["choose"] = 3,
    };

    private sealed class Parser(string text)
    {
        private int _pos;

        public Node ParseExpression()
        {
            var left = ParseTerm();
            while (Peek() is '+' or '-')
            {
                var op = text[_pos++];
                left = new Binary(op, left, ParseTerm());
            }
            return left;
        }

        public void ExpectEnd()
        {
            SkipSpaces();
            if (_pos < text.Length) throw Error($"unexpected '{text[_pos]}'");
        }

        private Node ParseTerm()
        {
            var left = ParseFactor();
            while (Peek() is '*' or '/' or '%')
            {
                var op = text[_pos++];
                left = new Binary(op, left, ParseFactor());
            }
            return left;
        }

        private Node ParseFactor()
        {
            var c = Peek();
            if (c is '-' or '+')
            {
                _pos++;
                return new Unary(c.Value, ParseFactor());
            }
            if (c == '(')
            {
                _pos++;
                var inner = ParseExpression();
                Expect(')');
                return inner;
            }
            if (c is { } d && (char.IsDigit(d) || d == '.'))
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
                if (!double.TryParse(text.AsSpan(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    throw Error($"'{text[start.._pos]}' isn't a number");
                return new Number(number);
            }
            if (c is { } l && (char.IsLetter(l) || l == '_'))
            {
                var start = _pos;
                while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_')) _pos++;
                var id = text[start.._pos];
                if (Peek() != '(') return new Name(id);
                if (!Functions.TryGetValue(id, out var arity)) throw Error($"unknown function '{id}'");
                _pos++;
                var args = new List<Node> { ParseExpression() };
                while (Peek() == ',')
                {
                    _pos++;
                    args.Add(ParseExpression());
                }
                Expect(')');
                if (args.Count != arity) throw Error($"'{id}' needs {arity} value(s), got {args.Count}");
                return new Call(id, [.. args]);
            }
            throw Error(c is null ? "the formula ends too early" : $"unexpected '{c}'");
        }

        private char? Peek()
        {
            SkipSpaces();
            return _pos < text.Length ? text[_pos] : null;
        }

        private void Expect(char c)
        {
            if (Peek() != c) throw Error($"expected '{c}'");
            _pos++;
        }

        private void SkipSpaces()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }

        private FormatException Error(string what) => new($"Can't read formula \"{text}\": {what} (at character {_pos + 1}).");
    }
}
