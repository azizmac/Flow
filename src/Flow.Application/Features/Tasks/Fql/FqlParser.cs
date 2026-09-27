namespace Flow.Application.Features.Tasks.Fql;

/// <summary>
/// Рекурсивный спуск по грамматике из docs/TZ_task_views.md §7: OR слабее AND, AND слабее NOT, скобки группируют.
/// Ключевые слова — без учёта регистра. Ошибка — <see cref="FqlException"/> с местом в строке.
/// </summary>
public static class FqlParser
{
    private static readonly string[] Keywords = ["AND", "OR", "NOT", "IN", "IS", "EMPTY", "ORDER", "BY", "ASC", "DESC"];

    public static FqlQuery Parse(string? query)
    {
        var tokens = FqlTokenizer.Tokenize(query ?? "");
        var parser = new State(tokens);
        return parser.Query();
    }

    private sealed class State(IReadOnlyList<FqlToken> tokens)
    {
        private int _i;

        private FqlToken Current => tokens[_i];

        private FqlToken Next() => tokens[_i++];

        public FqlQuery Query()
        {
            FqlExpr? where = null;
            if (Current.Kind != FqlTokenKind.End && !Current.Is("ORDER"))
                where = Or();

            var orders = new List<FqlOrder>();
            if (Current.Is("ORDER"))
            {
                Next();
                Expect("BY");
                do
                {
                    var field = ExpectWord("поле сортировки");
                    var desc = false;
                    if (Current.Is("DESC")) { Next(); desc = true; }
                    else if (Current.Is("ASC")) Next();
                    orders.Add(new FqlOrder(field, desc));
                }
                while (TryTake(FqlTokenKind.Comma));
            }

            if (Current.Kind != FqlTokenKind.End)
                throw Error(Current.Is("AND") || Current.Is("OR") ? "Не хватает условия" : "Ожидались AND, OR или ORDER BY", Current);

            return new FqlQuery(where, orders);
        }

        private FqlExpr Or()
        {
            var items = new List<FqlExpr> { And() };
            while (Current.Is("OR"))
            {
                Next();
                items.Add(And());
            }

            return items.Count == 1 ? items[0] : new FqlOr(items);
        }

        private FqlExpr And()
        {
            var items = new List<FqlExpr> { Factor() };
            while (Current.Is("AND"))
            {
                Next();
                items.Add(Factor());
            }

            return items.Count == 1 ? items[0] : new FqlAnd(items);
        }

        private FqlExpr Factor()
        {
            if (Current.Is("NOT"))
            {
                Next();
                return new FqlNot(Factor());
            }

            if (TryTake(FqlTokenKind.LParen))
            {
                var inner = Or();
                if (!TryTake(FqlTokenKind.RParen))
                    throw Error("Не закрыта скобка", Current);
                return inner;
            }

            return Clause();
        }

        private FqlExpr Clause()
        {
            var field = ExpectWord("поле");
            if (Keywords.Any(field.Is))
                throw Error($"Ожидалось поле, а не «{field.Text}»", field);

            if (Current.Is("IS"))
            {
                Next();
                var not = false;
                if (Current.Is("NOT")) { Next(); not = true; }
                Expect("EMPTY");
                return new FqlClause(field, not ? FqlOperator.IsNotEmpty : FqlOperator.IsEmpty, []);
            }

            if (Current.Is("NOT"))
            {
                Next();
                Expect("IN");
                return new FqlClause(field, FqlOperator.NotIn, List());
            }

            if (Current.Is("IN"))
            {
                Next();
                return new FqlClause(field, FqlOperator.In, List());
            }

            if (Current.Kind != FqlTokenKind.Operator)
                throw Error($"После поля «{field.Text}» ожидался оператор: = != > >= < <= ~ IN IS", Current);

            var op = Next().Text switch
            {
                "=" => FqlOperator.Eq,
                "!=" => FqlOperator.NotEq,
                ">" => FqlOperator.Gt,
                ">=" => FqlOperator.Gte,
                "<" => FqlOperator.Lt,
                "<=" => FqlOperator.Lte,
                _ => FqlOperator.Contains
            };

            // «assignee = EMPTY» — то же, что IS EMPTY: так пишут чаще, чем по грамматике.
            if (op is FqlOperator.Eq or FqlOperator.NotEq && Current.Is("EMPTY"))
            {
                Next();
                return new FqlClause(field, op == FqlOperator.Eq ? FqlOperator.IsEmpty : FqlOperator.IsNotEmpty, []);
            }

            return new FqlClause(field, op, [Value()]);
        }

        private IReadOnlyList<FqlValue> List()
        {
            var open = Current;
            if (!TryTake(FqlTokenKind.LParen))
                throw Error("После IN ожидался список в скобках", open);

            var values = new List<FqlValue> { Value() };
            while (TryTake(FqlTokenKind.Comma))
                values.Add(Value());

            if (!TryTake(FqlTokenKind.RParen))
                throw Error("Не закрыт список значений", Current);

            return values;
        }

        private FqlValue Value()
        {
            var token = Current;
            if (token.Kind == FqlTokenKind.String)
            {
                Next();
                return new FqlValue(token.Text, true, token.Position, token.Length);
            }

            if (token.Kind != FqlTokenKind.Word || token.Is("AND") || token.Is("OR") || token.Is("ORDER"))
                throw Error("Ожидалось значение", token);

            Next();
            if (!TryTake(FqlTokenKind.LParen))
                return new FqlValue(token.Text, false, token.Position, token.Length);

            var args = new List<FqlValue>();
            if (Current.Kind != FqlTokenKind.RParen)
            {
                args.Add(Value());
                while (TryTake(FqlTokenKind.Comma))
                    args.Add(Value());
            }

            var close = Current;
            if (!TryTake(FqlTokenKind.RParen))
                throw Error($"Не закрыта скобка у {token.Text}()", close);

            return new FqlValue(token.Text, false, token.Position, close.Position + 1 - token.Position, token.Text, args);
        }

        private void Expect(string keyword)
        {
            if (!Current.Is(keyword))
                throw Error($"Ожидалось «{keyword}»", Current);
            Next();
        }

        private FqlToken ExpectWord(string what)
        {
            if (Current.Kind != FqlTokenKind.Word)
                throw Error($"Ожидалось {what}", Current);
            return Next();
        }

        private bool TryTake(FqlTokenKind kind)
        {
            if (Current.Kind != kind)
                return false;
            Next();
            return true;
        }

        private static FqlException Error(string message, FqlToken at) =>
            new(at.Kind == FqlTokenKind.End ? message + " — строка закончилась" : message, at.Position, at.Length);
    }
}
