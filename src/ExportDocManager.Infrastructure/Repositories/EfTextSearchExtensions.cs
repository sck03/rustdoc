using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Utils;

public static class EfTextSearchExtensions
{
    private const string LikeEscape = "\\";
    private static readonly MethodInfo LikeMethod = ResolveLikeMethod(typeof(DbFunctionsExtensions), nameof(DbFunctionsExtensions.Like));
    private static readonly MethodInfo ILikeMethod = ResolveLikeMethod(typeof(NpgsqlDbFunctionsExtensions), nameof(NpgsqlDbFunctionsExtensions.ILike));

    public static IQueryable<T> ApplyKeywordSearch<T>(
        this IQueryable<T> query,
        DbContext context,
        string? keyword,
        params Expression<Func<T, string?>>[]? selectors)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(context);
        string[] tokens = TextSearchHelper.Tokenize(keyword);
        if (tokens.Length == 0 || selectors == null || selectors.Length == 0)
        {
            return query;
        }

        MethodInfo comparisonMethod = context.Database.IsNpgsql() ? ILikeMethod : LikeMethod;
        var parameter = Expression.Parameter(typeof(T), "entity");
        Expression? combinedExpression = null;
        foreach (string token in tokens)
        {
            Expression? tokenExpression = null;
            var pattern = Expression.Constant($"%{EscapeLikePattern(token)}%");
            foreach (var selector in selectors.Where(selector => selector != null))
            {
                Expression body = ReplaceParameter(selector.Body, selector.Parameters[0], parameter);
                Expression notNull = Expression.NotEqual(body, Expression.Constant(null, typeof(string)));
                Expression comparison = Expression.Call(
                    comparisonMethod,
                    Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                    body,
                    pattern,
                    Expression.Constant(LikeEscape));
                Expression fieldExpression = Expression.AndAlso(notNull, comparison);
                tokenExpression = tokenExpression == null
                    ? fieldExpression
                    : Expression.OrElse(tokenExpression, fieldExpression);
            }

            if (tokenExpression != null)
            {
                combinedExpression = combinedExpression == null
                    ? tokenExpression
                    : Expression.AndAlso(combinedExpression, tokenExpression);
            }
        }

        return combinedExpression == null
            ? query
            : query.Where(Expression.Lambda<Func<T, bool>>(combinedExpression, parameter));
    }

    public static string EscapeLikePattern(string value) =>
        (value ?? string.Empty)
            .Replace(LikeEscape, LikeEscape + LikeEscape, StringComparison.Ordinal)
            .Replace("%", LikeEscape + "%", StringComparison.Ordinal)
            .Replace("_", LikeEscape + "_", StringComparison.Ordinal);

    private static MethodInfo ResolveLikeMethod(Type declaringType, string name) =>
        declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == name && method.GetParameters().Length == 4);

    private static Expression ReplaceParameter(
        Expression expression,
        ParameterExpression source,
        ParameterExpression target) =>
        new ParameterReplaceVisitor(source, target).Visit(expression)
        ?? throw new InvalidOperationException("无法生成文本搜索表达式。");

    private sealed class ParameterReplaceVisitor(
        ParameterExpression source,
        ParameterExpression target) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) =>
            node == source ? target : base.VisitParameter(node);
    }
}
