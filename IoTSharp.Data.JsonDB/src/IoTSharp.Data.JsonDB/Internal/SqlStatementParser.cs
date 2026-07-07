#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IoTSharp.Data.JsonDB.Internal
{
    internal static class SqlStatementParser
    {
        public static SqlStatement Parse(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql))
            {
                throw new ArgumentException("SQL source is required.", nameof(sql));
            }

            var trimmed = sql.Trim();
            if (trimmed.StartsWith("select ", StringComparison.OrdinalIgnoreCase))
            {
                return ParseSelect(trimmed);
            }

            if (trimmed.StartsWith("update ", StringComparison.OrdinalIgnoreCase))
            {
                return ParseUpdate(trimmed);
            }

            if (trimmed.StartsWith("delete from ", StringComparison.OrdinalIgnoreCase))
            {
                return ParseDelete(trimmed);
            }

            if (trimmed.StartsWith("insert into ", StringComparison.OrdinalIgnoreCase))
            {
                return ParseInsert(trimmed);
            }

            throw new ArgumentException("Unsupported SQL statement.", nameof(sql));
        }

        private static SqlSelectStatement ParseSelect(string sql)
        {
            var body = sql["select".Length..].TrimStart();
            var fromIndex = FindClauseIndex(body, "from");
            if (fromIndex < 0)
            {
                throw new ArgumentException("Select statements require a FROM clause.", nameof(sql));
            }

            var fieldsText = body[..fromIndex].Trim();
            var tail = body[(fromIndex + "from".Length)..].TrimStart();
            var clauses = ParseCommonTail(tail);

            return new SqlSelectStatement(
                clauses.Table,
                ParseSelectItems(fieldsText),
                ParseOptionalExpression(clauses.Where),
                ParseExpressionList(clauses.GroupBy),
                ParseOptionalExpression(clauses.Having),
                ParseOrderBy(clauses.OrderBy),
                ParseLimit(clauses.Limit));
        }

        private static SqlUpdateStatement ParseUpdate(string sql)
        {
            var body = sql["update".Length..].TrimStart();
            var setIndex = FindClauseIndex(body, "set");
            if (setIndex < 0)
            {
                throw new ArgumentException("Update statements require a SET clause.", nameof(sql));
            }

            var table = body[..setIndex].Trim();
            var tail = body[(setIndex + "set".Length)..].TrimStart();
            var clauses = ParseClauseSegments(table, tail);

            return new SqlUpdateStatement(
                clauses.Table,
                ParseAssignments(clauses.MainBody),
                ParseOptionalExpression(clauses.Where),
                ParseOrderBy(clauses.OrderBy),
                ParseLimit(clauses.Limit));
        }

        private static SqlDeleteStatement ParseDelete(string sql)
        {
            var tail = sql["delete from".Length..].TrimStart();
            var clauses = ParseCommonTail(tail);

            return new SqlDeleteStatement(
                clauses.Table,
                ParseOptionalExpression(clauses.Where),
                ParseOrderBy(clauses.OrderBy),
                ParseLimit(clauses.Limit));
        }

        private static SqlInsertStatement ParseInsert(string sql)
        {
            var body = sql["insert into".Length..].TrimStart();
            var setIndex = FindClauseIndex(body, "set");
            if (setIndex < 0)
            {
                throw new ArgumentException("Insert statements require a SET clause.", nameof(sql));
            }

            var table = body[..setIndex].Trim();
            var assignments = body[(setIndex + "set".Length)..].Trim();
            return new SqlInsertStatement(table, ParseAssignments(assignments));
        }

        private static SqlTail ParseCommonTail(string tail)
        {
            return ParseClauseSegments(null, tail);
        }

        private static SqlTail ParseClauseSegments(string? table, string tail)
        {
            var whereIndex = FindClauseIndex(tail, "where");
            var groupByIndex = FindClauseIndex(tail, "group by");
            var havingIndex = FindClauseIndex(tail, "having");
            var orderIndex = FindClauseIndex(tail, "order by");
            var limitIndex = FindClauseIndex(tail, "limit");
            var firstClauseIndex = MinPositive(whereIndex, groupByIndex, havingIndex, orderIndex, limitIndex);

            var resolvedTable = string.IsNullOrWhiteSpace(table)
                ? (firstClauseIndex >= 0 ? tail[..firstClauseIndex].Trim() : tail.Trim())
                : table.Trim();

            var mainBody = string.IsNullOrWhiteSpace(table)
                ? string.Empty
                : (firstClauseIndex >= 0 ? tail[..firstClauseIndex].Trim() : tail.Trim());

            var where = SliceClause(tail, whereIndex, "where", groupByIndex, havingIndex, orderIndex, limitIndex);
            var groupBy = SliceClause(tail, groupByIndex, "group by", havingIndex, orderIndex, limitIndex);
            var having = SliceClause(tail, havingIndex, "having", orderIndex, limitIndex);
            var orderBy = SliceClause(tail, orderIndex, "order by", limitIndex);
            var limit = SliceClause(tail, limitIndex, "limit");

            return new SqlTail(resolvedTable, mainBody, where, groupBy, having, orderBy, limit);
        }

        private static IReadOnlyList<SqlSelectItem> ParseSelectItems(string text)
        {
            var items = new List<SqlSelectItem>();
            foreach (var segment in SplitTopLevel(text, ','))
            {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed == "*")
                {
                    items.Add(new SqlSelectItem(null, "*", true, SqlAggregateFunction.None, Array.Empty<SqlExpression>()));
                    continue;
                }

                var aliasIndex = FindClauseIndex(trimmed, "as");
                var expressionText = aliasIndex >= 0 ? trimmed[..aliasIndex].Trim() : trimmed;
                var alias = aliasIndex >= 0 ? trimmed[(aliasIndex + "as".Length)..].Trim() : GetDefaultAlias(expressionText);
                if (TryParseAggregate(expressionText, out var aggregateFunction, out var aggregateArguments))
                {
                    items.Add(new SqlSelectItem(
                        aggregateArguments.FirstOrDefault(),
                        alias,
                        false,
                        aggregateFunction,
                        aggregateArguments));
                    continue;
                }

                items.Add(new SqlSelectItem(
                    SqlExpressionParser.Parse(expressionText),
                    alias,
                    false,
                    SqlAggregateFunction.None,
                    Array.Empty<SqlExpression>()));
            }

            return items;
        }

        private static bool TryParseAggregate(
            string expressionText,
            out SqlAggregateFunction aggregateFunction,
            out IReadOnlyList<SqlExpression> arguments)
        {
            aggregateFunction = SqlAggregateFunction.None;
            arguments = Array.Empty<SqlExpression>();
            var trimmed = expressionText.Trim();
            var openIndex = trimmed.IndexOf('(');
            if (openIndex <= 0 || trimmed[^1] != ')')
            {
                return false;
            }

            aggregateFunction = NormalizeAggregateFunction(trimmed[..openIndex]);
            if (aggregateFunction == SqlAggregateFunction.None)
            {
                return false;
            }

            var argument = trimmed[(openIndex + 1)..^1].Trim();
            if (aggregateFunction == SqlAggregateFunction.Count && argument == "*")
            {
                return true;
            }

            if (argument.Length == 0)
            {
                throw new ArgumentException($"{trimmed[..openIndex]} requires an argument.", nameof(expressionText));
            }

            arguments = SplitTopLevel(argument, ',')
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .Select(SqlExpressionParser.Parse)
                .ToArray();
            return true;
        }

        private static SqlAggregateFunction NormalizeAggregateFunction(string value)
        {
            return value.Trim().ToLowerInvariant() switch
            {
                "count" => SqlAggregateFunction.Count,
                "sum" => SqlAggregateFunction.Sum,
                "total" => SqlAggregateFunction.Total,
                "avg" => SqlAggregateFunction.Avg,
                "min" => SqlAggregateFunction.Min,
                "max" => SqlAggregateFunction.Max,
                "group_concat" => SqlAggregateFunction.GroupConcat,
                "string_agg" => SqlAggregateFunction.StringAgg,
                _ => SqlAggregateFunction.None,
            };
        }

        private static IReadOnlyList<SqlAssignment> ParseAssignments(string text)
        {
            var assignments = new List<SqlAssignment>();
            foreach (var segment in SplitTopLevel(text, ','))
            {
                var equalsIndex = FindAssignmentOperator(segment);
                if (equalsIndex < 0)
                {
                    throw new ArgumentException($"Invalid assignment '{segment}'.", nameof(text));
                }

                var path = segment[..equalsIndex].Trim();
                var expressionText = segment[(equalsIndex + 1)..].Trim();
                if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(expressionText))
                {
                    throw new ArgumentException($"Invalid assignment '{segment}'.", nameof(text));
                }

                assignments.Add(new SqlAssignment(path, SqlExpressionParser.Parse(expressionText)));
            }

            return assignments;
        }

        private static IReadOnlyList<SqlOrderByItem> ParseOrderBy(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<SqlOrderByItem>();
            }

            var items = new List<SqlOrderByItem>();
            foreach (var segment in SplitTopLevel(text, ','))
            {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                var expressionText = trimmed;
                var descending = false;
                var numeric = false;
                foreach (var suffix in new[] { " descnum", " ascnum", " desc", " asc" })
                {
                    if (trimmed.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        expressionText = trimmed[..^suffix.Length].TrimEnd();
                        descending = suffix.Contains("desc", StringComparison.OrdinalIgnoreCase);
                        numeric = suffix.Contains("num", StringComparison.OrdinalIgnoreCase);
                        break;
                    }
                }

                items.Add(new SqlOrderByItem(SqlExpressionParser.Parse(expressionText), descending, numeric));
            }

            return items;
        }

        private static IReadOnlyList<SqlExpression> ParseExpressionList(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return Array.Empty<SqlExpression>();
            }

            return SplitTopLevel(text, ',')
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .Select(SqlExpressionParser.Parse)
                .ToArray();
        }

        private static SqlLimit? ParseLimit(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return parts.Length switch
            {
                1 => new SqlLimit(int.Parse(parts[0], CultureInfo.InvariantCulture), null),
                2 => new SqlLimit(int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture)),
                _ => throw new ArgumentException("Invalid LIMIT clause.", nameof(text))
            };
        }

        private static SqlExpression? ParseOptionalExpression(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? null : SqlExpressionParser.Parse(text);
        }

        private static string GetDefaultAlias(string expressionText)
        {
            return expressionText;
        }

        private static int FindAssignmentOperator(string text)
        {
            var quote = '\0';
            var depth = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var current = text[i];
                if (quote != '\0')
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                    continue;
                }

                if (current == ')')
                {
                    depth--;
                    continue;
                }

                if (depth == 0 && current == '=' && (i == 0 || text[i - 1] is not '!' and not '<' and not '>') && (i == text.Length - 1 || text[i + 1] != '='))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string? SliceClause(string text, int clauseIndex, string clauseKeyword, params int[] nextClauseIndexes)
        {
            if (clauseIndex < 0)
            {
                return null;
            }

            var start = clauseIndex + clauseKeyword.Length;
            var end = nextClauseIndexes.Where(index => index > clauseIndex).DefaultIfEmpty(text.Length).Min();
            return text[start..end].Trim();
        }

        private static int MinPositive(params int[] values)
        {
            return values.Where(static value => value >= 0).DefaultIfEmpty(-1).Min();
        }

        internal static IEnumerable<string> SplitTopLevel(string text, char separator)
        {
            var quote = '\0';
            var depth = 0;
            var lastIndex = 0;
            for (var i = 0; i < text.Length; i++)
            {
                var current = text[i];
                if (quote != '\0')
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                    continue;
                }

                if (current == ')')
                {
                    depth--;
                    continue;
                }

                if (depth == 0 && current == separator)
                {
                    yield return text[lastIndex..i];
                    lastIndex = i + 1;
                }
            }

            yield return text[lastIndex..];
        }

        private static int FindClauseIndex(string text, string clause)
        {
            var quote = '\0';
            var depth = 0;
            for (var i = 0; i <= text.Length - clause.Length; i++)
            {
                var current = text[i];
                if (quote != '\0')
                {
                    if (current == '\\')
                    {
                        i++;
                        continue;
                    }

                    if (current == quote)
                    {
                        quote = '\0';
                    }

                    continue;
                }

                if (current is '\'' or '"')
                {
                    quote = current;
                    continue;
                }

                if (current == '(')
                {
                    depth++;
                    continue;
                }

                if (current == ')')
                {
                    depth--;
                    continue;
                }

                if (depth == 0 && text.AsSpan(i).StartsWith(clause, StringComparison.OrdinalIgnoreCase) && IsBoundary(text, i - 1) && IsBoundary(text, i + clause.Length))
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsBoundary(string text, int index)
        {
            if (index < 0 || index >= text.Length)
            {
                return true;
            }

            return char.IsWhiteSpace(text[index]);
        }

        private sealed record SqlTail(
            string Table,
            string MainBody,
            string? Where,
            string? GroupBy,
            string? Having,
            string? OrderBy,
            string? Limit);
    }
}
