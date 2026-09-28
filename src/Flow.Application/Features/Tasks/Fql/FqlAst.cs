namespace Flow.Application.Features.Tasks.Fql;

public enum FqlOperator { Eq, NotEq, Gt, Gte, Lt, Lte, In, NotIn, IsEmpty, IsNotEmpty, Contains }

public abstract record FqlExpr;

public sealed record FqlAnd(IReadOnlyList<FqlExpr> Items) : FqlExpr;

public sealed record FqlOr(IReadOnlyList<FqlExpr> Items) : FqlExpr;

public sealed record FqlNot(FqlExpr Item) : FqlExpr;

/// <summary>Условие «поле оператор значения»; у IN — список, у IS [NOT] EMPTY — пусто.</summary>
public sealed record FqlClause(FqlToken Field, FqlOperator Operator, IReadOnlyList<FqlValue> Values) : FqlExpr;

/// <summary>
/// Значение: слово, строка в кавычках или вызов функции (<c>me()</c>, <c>childrenOf(PROJ-1)</c>) — тогда Function
/// задано, а Args — её аргументы. Position/Length — для ошибок биндинга.
/// </summary>
public sealed record FqlValue(string Text, bool Quoted, int Position, int Length, string? Function = null, IReadOnlyList<FqlValue>? Args = null)
{
    public bool IsFunction(string name) => string.Equals(Function, name, StringComparison.OrdinalIgnoreCase);
}

public sealed record FqlOrder(FqlToken Field, bool Descending);

public sealed record FqlQuery(FqlExpr? Where, IReadOnlyList<FqlOrder> Orders);
