using System.Linq.Expressions;

namespace ExportDocManager.Utils
{
    public static class TextSearchHelper
    {
        public static string NormalizeFilter(string value)
        {
            return NormalizeValue(value);
        }

        public static string NormalizeValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        public static string NormalizeUpperValue(string value)
        {
            return NormalizeValue(value).ToUpperInvariant();
        }

        public static string[] Tokenize(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return Array.Empty<string>();
            }

            return keyword
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public static IQueryable<T> ApplyKeywordSearch<T>(
            this IQueryable<T> query,
            string keyword,
            params Expression<Func<T, string>>[] selectors)
        {
            ArgumentNullException.ThrowIfNull(query);

            var tokens = Tokenize(keyword);
            if (tokens.Length == 0 || selectors == null || selectors.Length == 0)
            {
                return query;
            }

            var parameter = Expression.Parameter(typeof(T), "entity");
            Expression combinedExpression = null;

            foreach (var token in tokens)
            {
                Expression tokenExpression = null;
                var normalizedToken = token.ToUpperInvariant();
                var tokenConstant = Expression.Constant(normalizedToken);

                foreach (var selector in selectors)
                {
                    if (selector == null)
                    {
                        continue;
                    }

                    var body = ReplaceParameter(selector.Body, selector.Parameters[0], parameter);
                    var notNull = Expression.NotEqual(body, Expression.Constant(null, typeof(string)));
                    var normalizedBody = Expression.Call(body, nameof(string.ToUpper), Type.EmptyTypes);
                    var contains = Expression.Call(normalizedBody, nameof(string.Contains), Type.EmptyTypes, tokenConstant);
                    var fieldExpression = Expression.AndAlso(notNull, contains);

                    tokenExpression = tokenExpression == null
                        ? fieldExpression
                        : Expression.OrElse(tokenExpression, fieldExpression);
                }

                if (tokenExpression == null)
                {
                    continue;
                }

                combinedExpression = combinedExpression == null
                    ? tokenExpression
                    : Expression.AndAlso(combinedExpression, tokenExpression);
            }

            if (combinedExpression == null)
            {
                return query;
            }

            var predicate = Expression.Lambda<Func<T, bool>>(combinedExpression, parameter);
            return query.Where(predicate);
        }

        private static Expression ReplaceParameter(Expression expression, ParameterExpression source, ParameterExpression target)
        {
            return new ParameterReplaceVisitor(source, target).Visit(expression);
        }

        private sealed class ParameterReplaceVisitor : ExpressionVisitor
        {
            private readonly ParameterExpression _source;
            private readonly ParameterExpression _target;

            public ParameterReplaceVisitor(ParameterExpression source, ParameterExpression target)
            {
                _source = source;
                _target = target;
            }

            protected override Expression VisitParameter(ParameterExpression node)
            {
                return node == _source ? _target : base.VisitParameter(node);
            }
        }
    }
}
