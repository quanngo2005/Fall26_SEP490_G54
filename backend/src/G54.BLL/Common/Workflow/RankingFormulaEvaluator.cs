using System.Globalization;
using G54.BLL.Dtos.SystemParameter;

namespace G54.BLL.Common.Workflow;

/// <summary>
/// Safe, deterministic recursive-descent AST evaluator for ranking score formulas (GB-910).
/// Never compiles or invokes dynamic code; supports only whitelisted variables, numbers, arithmetic,
/// comparisons, logical AND/OR, and bounded pure functions (<c>MIN, MAX, ROUND, ABS, CLAMP, IF</c>).
/// </summary>
public static class RankingFormulaEvaluator
{
    public const int MaxFormulaLength = 500;
    public const int MaxAstDepth = 10;

    private static readonly HashSet<string> AllowedVariables =
        new(RankingRuleCatalog.FormulaVariables, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, string> CanonicalVariables =
        RankingRuleCatalog.FormulaVariables.ToDictionary(v => v, v => v, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> AllowedFunctions =
        new(RankingRuleCatalog.FormulaFunctions, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Validate(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula))
        {
            return ["Công thức điểm xét hạng không được để trống (ví dụ: TotalScore)."];
        }

        if (formula.Length > MaxFormulaLength)
        {
            return [$"Công thức điểm xét hạng tối đa {MaxFormulaLength} ký tự."];
        }

        var errors = new List<string>();
        var tokens = Tokenize(formula, errors);
        if (errors.Count > 0)
        {
            return errors;
        }

        var parser = new FormulaParser(tokens, errors);
        var node = parser.ParseRoot();
        if (errors.Count == 0 && node is not null && node.IsBoolean)
        {
            errors.Add("Biểu thức gốc của công thức phải trả về giá trị số, không phải biểu thức logic đúng/sai.");
        }

        return errors;
    }

    public static decimal Evaluate(string? formula, IReadOnlyDictionary<string, decimal> variables, decimal fallbackTotalScore)
    {
        if (string.IsNullOrWhiteSpace(formula)
            || string.Equals(formula.Trim(), "TotalScore", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Round(fallbackTotalScore, 2, MidpointRounding.AwayFromZero);
        }

        var errors = new List<string>();
        var tokens = Tokenize(formula, errors);
        if (errors.Count > 0)
        {
            return Math.Round(fallbackTotalScore, 2, MidpointRounding.AwayFromZero);
        }

        var parser = new FormulaParser(tokens, errors);
        var root = parser.ParseRoot();
        if (errors.Count > 0 || root is null || root.IsBoolean)
        {
            return Math.Round(fallbackTotalScore, 2, MidpointRounding.AwayFromZero);
        }

        var result = root.EvalNumber(variables);
        return Math.Clamp(Math.Round(result, 2, MidpointRounding.AwayFromZero), 0m, 1000m);
    }

    private enum TokenKind
    {
        Number,
        Identifier,
        Plus,
        Minus,
        Star,
        Slash,
        LParen,
        RParen,
        Comma,
        Gte,
        Lte,
        Gt,
        Lt,
        Eq,
        Neq,
        And,
        Or,
        End,
    }

    private sealed record Token(TokenKind Kind, string Text, decimal NumericValue = 0m);

    private static List<Token> Tokenize(string input, List<string> errors)
    {
        var list = new List<Token>();
        var i = 0;
        while (i < input.Length)
        {
            var ch = input[i];
            if (char.IsWhiteSpace(ch))
            {
                i++;
                continue;
            }

            if (char.IsDigit(ch) || (ch == '.' && i + 1 < input.Length && char.IsDigit(input[i + 1])))
            {
                var start = i;
                var hasDot = false;
                while (i < input.Length && (char.IsDigit(input[i]) || (!hasDot && input[i] == '.')))
                {
                    if (input[i] == '.') hasDot = true;
                    i++;
                }

                var slice = input[start..i];
                if (!decimal.TryParse(slice, NumberStyles.Number, CultureInfo.InvariantCulture, out var num))
                {
                    errors.Add($"Số không hợp lệ '{slice}' trong công thức.");
                    return list;
                }

                list.Add(new Token(TokenKind.Number, slice, num));
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                while (i < input.Length && (char.IsLetterOrDigit(input[i]) || input[i] == '_'))
                {
                    i++;
                }

                var ident = input[start..i];
                if (string.Equals(ident, "AND", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new Token(TokenKind.And, ident));
                }
                else if (string.Equals(ident, "OR", StringComparison.OrdinalIgnoreCase))
                {
                    list.Add(new Token(TokenKind.Or, ident));
                }
                else
                {
                    list.Add(new Token(TokenKind.Identifier, ident));
                }

                continue;
            }

            if (i + 1 < input.Length)
            {
                var two = input.Substring(i, 2);
                TokenKind? twoKind = two switch
                {
                    ">=" => TokenKind.Gte,
                    "<=" => TokenKind.Lte,
                    "==" => TokenKind.Eq,
                    "!=" or "<>" => TokenKind.Neq,
                    "&&" => TokenKind.And,
                    "||" => TokenKind.Or,
                    _ => null,
                };
                if (twoKind.HasValue)
                {
                    list.Add(new Token(twoKind.Value, two));
                    i += 2;
                    continue;
                }
            }

            TokenKind? oneKind = ch switch
            {
                '+' => TokenKind.Plus,
                '-' => TokenKind.Minus,
                '*' => TokenKind.Star,
                '/' => TokenKind.Slash,
                '(' => TokenKind.LParen,
                ')' => TokenKind.RParen,
                ',' => TokenKind.Comma,
                '>' => TokenKind.Gt,
                '<' => TokenKind.Lt,
                '=' => TokenKind.Eq,
                _ => null,
            };

            if (oneKind.HasValue)
            {
                list.Add(new Token(oneKind.Value, ch.ToString()));
                i++;
                continue;
            }

            errors.Add($"Ký tự không hợp lệ '{ch}' tại vị trí {i + 1} trong công thức.");
            return list;
        }

        list.Add(new Token(TokenKind.End, string.Empty));
        return list;
    }

    private abstract record AstNode(bool IsBoolean)
    {
        public virtual decimal EvalNumber(IReadOnlyDictionary<string, decimal> vars) => 0m;

        public virtual bool EvalBool(IReadOnlyDictionary<string, decimal> vars) => false;
    }

    private sealed record NumberNode(decimal Value) : AstNode(false)
    {
        public override decimal EvalNumber(IReadOnlyDictionary<string, decimal> vars) => Value;
    }

    private sealed record VariableNode(string CanonicalName) : AstNode(false)
    {
        public override decimal EvalNumber(IReadOnlyDictionary<string, decimal> vars)
            => vars.TryGetValue(CanonicalName, out var v) ? v : 0m;
    }

    private sealed record UnaryMinusNode(AstNode Operand) : AstNode(false)
    {
        public override decimal EvalNumber(IReadOnlyDictionary<string, decimal> vars) => -Operand.EvalNumber(vars);
    }

    private sealed record BinaryArithmeticNode(TokenKind Op, AstNode Left, AstNode Right) : AstNode(false)
    {
        public override decimal EvalNumber(IReadOnlyDictionary<string, decimal> vars)
        {
            var l = Left.EvalNumber(vars);
            var r = Right.EvalNumber(vars);
            return Op switch
            {
                TokenKind.Plus => l + r,
                TokenKind.Minus => l - r,
                TokenKind.Star => l * r,
                TokenKind.Slash => r == 0m ? 0m : l / r,
                _ => 0m,
            };
        }
    }

    private sealed record ComparisonNode(TokenKind Op, AstNode Left, AstNode Right) : AstNode(true)
    {
        public override bool EvalBool(IReadOnlyDictionary<string, decimal> vars)
        {
            var l = Left.EvalNumber(vars);
            var r = Right.EvalNumber(vars);
            return Op switch
            {
                TokenKind.Gte => l >= r,
                TokenKind.Lte => l <= r,
                TokenKind.Gt => l > r,
                TokenKind.Lt => l < r,
                TokenKind.Eq => l == r,
                TokenKind.Neq => l != r,
                _ => false,
            };
        }
    }

    private sealed record LogicalNode(TokenKind Op, AstNode Left, AstNode Right) : AstNode(true)
    {
        public override bool EvalBool(IReadOnlyDictionary<string, decimal> vars)
            => Op == TokenKind.And
                ? Left.EvalBool(vars) && Right.EvalBool(vars)
                : Left.EvalBool(vars) || Right.EvalBool(vars);
    }

    private sealed record FunctionCallNode(string FunctionName, IReadOnlyList<AstNode> Args) : AstNode(false)
    {
        public override decimal EvalNumber(IReadOnlyDictionary<string, decimal> vars)
        {
            return FunctionName switch
            {
                "MIN" => Args.Min(a => a.EvalNumber(vars)),
                "MAX" => Args.Max(a => a.EvalNumber(vars)),
                "ABS" => Math.Abs(Args[0].EvalNumber(vars)),
                "ROUND" => Math.Round(
                    Args[0].EvalNumber(vars),
                    Args.Count > 1 ? Math.Clamp((int)Args[1].EvalNumber(vars), 0, 4) : 0,
                    MidpointRounding.AwayFromZero),
                "CLAMP" => Math.Clamp(Args[0].EvalNumber(vars), Math.Min(Args[1].EvalNumber(vars), Args[2].EvalNumber(vars)), Math.Max(Args[1].EvalNumber(vars), Args[2].EvalNumber(vars))),
                "IF" => Args[0].EvalBool(vars) ? Args[1].EvalNumber(vars) : Args[2].EvalNumber(vars),
                _ => 0m,
            };
        }
    }

    private sealed class FormulaParser(List<Token> tokens, List<string> errors)
    {
        private int _pos;

        private Token Current => tokens[Math.Min(_pos, tokens.Count - 1)];

        public AstNode? ParseRoot()
        {
            var expr = ParseOr(0);
            if (errors.Count == 0 && Current.Kind != TokenKind.End)
            {
                errors.Add($"Cú pháp không hợp lệ gần '{Current.Text}'.");
            }

            return expr;
        }

        private AstNode? ParseOr(int depth)
        {
            if (!CheckDepth(depth)) return null;
            var left = ParseAnd(depth + 1);
            while (left is not null && Current.Kind == TokenKind.Or)
            {
                var op = Consume();
                var right = ParseAnd(depth + 1);
                if (right is null) return null;
                EnsureBooleanOperands(op.Text, left, right);
                left = new LogicalNode(TokenKind.Or, left, right);
            }

            return left;
        }

        private AstNode? ParseAnd(int depth)
        {
            if (!CheckDepth(depth)) return null;
            var left = ParseComparison(depth + 1);
            while (left is not null && Current.Kind == TokenKind.And)
            {
                var op = Consume();
                var right = ParseComparison(depth + 1);
                if (right is null) return null;
                EnsureBooleanOperands(op.Text, left, right);
                left = new LogicalNode(TokenKind.And, left, right);
            }

            return left;
        }

        private AstNode? ParseComparison(int depth)
        {
            if (!CheckDepth(depth)) return null;
            var left = ParseAdditive(depth + 1);
            if (left is not null && Current.Kind is TokenKind.Gte or TokenKind.Lte or TokenKind.Gt or TokenKind.Lt or TokenKind.Eq or TokenKind.Neq)
            {
                var op = Consume();
                var right = ParseAdditive(depth + 1);
                if (right is null) return null;
                EnsureNumberOperands(op.Text, left, right);
                return new ComparisonNode(op.Kind, left, right);
            }

            return left;
        }

        private AstNode? ParseAdditive(int depth)
        {
            if (!CheckDepth(depth)) return null;
            var left = ParseMultiplicative(depth + 1);
            while (left is not null && Current.Kind is TokenKind.Plus or TokenKind.Minus)
            {
                var op = Consume();
                var right = ParseMultiplicative(depth + 1);
                if (right is null) return null;
                EnsureNumberOperands(op.Text, left, right);
                left = new BinaryArithmeticNode(op.Kind, left, right);
            }

            return left;
        }

        private AstNode? ParseMultiplicative(int depth)
        {
            if (!CheckDepth(depth)) return null;
            var left = ParseUnary(depth + 1);
            while (left is not null && Current.Kind is TokenKind.Star or TokenKind.Slash)
            {
                var op = Consume();
                var right = ParseUnary(depth + 1);
                if (right is null) return null;
                EnsureNumberOperands(op.Text, left, right);
                left = new BinaryArithmeticNode(op.Kind, left, right);
            }

            return left;
        }

        private AstNode? ParseUnary(int depth)
        {
            if (!CheckDepth(depth)) return null;
            if (Current.Kind == TokenKind.Plus)
            {
                Consume();
                return ParseUnary(depth + 1);
            }

            if (Current.Kind == TokenKind.Minus)
            {
                Consume();
                var inner = ParseUnary(depth + 1);
                if (inner is null) return null;
                if (inner.IsBoolean)
                {
                    errors.Add("Không thể đặt dấu âm trước biểu thức logic.");
                }

                return new UnaryMinusNode(inner);
            }

            return ParsePrimary(depth + 1);
        }

        private AstNode? ParsePrimary(int depth)
        {
            if (!CheckDepth(depth)) return null;
            var tok = Current;
            if (tok.Kind == TokenKind.Number)
            {
                Consume();
                return new NumberNode(tok.NumericValue);
            }

            if (tok.Kind == TokenKind.LParen)
            {
                Consume();
                var inner = ParseOr(depth + 1);
                if (Current.Kind != TokenKind.RParen)
                {
                    errors.Add("Thiếu dấu đóng ngoặc ')'.");
                    return null;
                }

                Consume();
                return inner;
            }

            if (tok.Kind == TokenKind.Identifier)
            {
                Consume();
                if (Current.Kind == TokenKind.LParen)
                {
                    return ParseFunction(tok.Text, depth + 1);
                }

                if (!AllowedVariables.Contains(tok.Text))
                {
                    errors.Add($"Biến '{tok.Text}' không tồn tại trong danh mục cho phép ({string.Join(", ", RankingRuleCatalog.FormulaVariables)}).");
                    return null;
                }

                return new VariableNode(CanonicalVariables[tok.Text]);
            }

            errors.Add(tok.Kind == TokenKind.End
                ? "Công thức bị kết thúc đột ngột, thiếu toán hạng."
                : $"Toán tử hoặc ký hiệu '{tok.Text}' đặt sai vị trí.");
            return null;
        }

        private FunctionCallNode? ParseFunction(string rawName, int depth)
        {
            var fnName = rawName.ToUpperInvariant();
            if (!AllowedFunctions.Contains(fnName))
            {
                errors.Add($"Hàm '{rawName}' không được phép sử dụng. Chỉ hỗ trợ: {string.Join(", ", RankingRuleCatalog.FormulaFunctions)}.");
                return null;
            }

            Consume(); // '('
            var args = new List<AstNode>();
            if (Current.Kind != TokenKind.RParen)
            {
                while (true)
                {
                    var arg = ParseOr(depth + 1);
                    if (arg is null) return null;
                    args.Add(arg);
                    if (Current.Kind == TokenKind.Comma)
                    {
                        Consume();
                        continue;
                    }

                    break;
                }
            }

            if (Current.Kind != TokenKind.RParen)
            {
                errors.Add($"Hàm '{fnName}' thiếu dấu đóng ngoặc ')'.");
                return null;
            }

            Consume();
            ValidateFunctionSignature(fnName, args);
            return errors.Count > 0 ? null : new FunctionCallNode(fnName, args);
        }

        private void ValidateFunctionSignature(string fnName, List<AstNode> args)
        {
            switch (fnName)
            {
                case "MIN" or "MAX":
                    if (args.Count < 2 || args.Any(a => a.IsBoolean))
                    {
                        errors.Add($"Hàm {fnName} cần ít nhất 2 tham số kiểu số.");
                    }

                    break;
                case "ABS":
                    if (args.Count != 1 || args[0].IsBoolean)
                    {
                        errors.Add("Hàm ABS cần đúng 1 tham số kiểu số.");
                    }

                    break;
                case "ROUND":
                    if (args.Count is < 1 or > 2 || args.Any(a => a.IsBoolean))
                    {
                        errors.Add("Hàm ROUND(giá_trị, [số_lẻ]) cần 1 hoặc 2 tham số kiểu số.");
                    }

                    break;
                case "CLAMP":
                    if (args.Count != 3 || args.Any(a => a.IsBoolean))
                    {
                        errors.Add("Hàm CLAMP(giá_trị, min, max) cần đúng 3 tham số kiểu số.");
                    }

                    break;
                case "IF":
                    if (args.Count != 3 || !args[0].IsBoolean || args[1].IsBoolean || args[2].IsBoolean)
                    {
                        errors.Add("Hàm IF(điều_kiện_so_sánh, giá_trị_đúng, giá_trị_sai) cần tham số đầu là phép so sánh và 2 tham số sau là kiểu số.");
                    }

                    break;
            }
        }

        private void EnsureNumberOperands(string op, AstNode left, AstNode right)
        {
            if (left.IsBoolean || right.IsBoolean)
            {
                errors.Add($"Toán tử '{op}' chỉ áp dụng cho hai giá trị số.");
            }
        }

        private void EnsureBooleanOperands(string op, AstNode left, AstNode right)
        {
            if (!left.IsBoolean || !right.IsBoolean)
            {
                errors.Add($"Toán tử logic '{op}' chỉ áp dụng cho hai phép so sánh.");
            }
        }

        private bool CheckDepth(int depth)
        {
            if (depth <= MaxAstDepth * 4) return true;
            if (errors.Count == 0)
            {
                errors.Add($"Công thức lồng nhau quá sâu (vượt giới hạn {MaxAstDepth} cấp).");
            }

            return false;
        }

        private Token Consume() => tokens[_pos++];
    }
}
