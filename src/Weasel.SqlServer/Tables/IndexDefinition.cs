using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Baseline;

namespace Weasel.SqlServer.Tables
{
    public class IndexDefinition : INamed
    {
        private const string JsonbPathOps = "jsonb_path_ops";
        private static readonly string[] _reserved_words = {"trim", "lower", "upper"};

        private string _indexName;

        public IndexDefinition(string indexName)
        {
            _indexName = indexName;
        }

        protected IndexDefinition()
        {
        }

        public SortOrder SortOrder { get; set; } = SortOrder.Asc;

        public bool IsUnique { get; set; }

        public virtual string[] Columns { get; set; }

        /// <summary>
        ///     Pattern for surrounding the columns. Use a `?` character
        ///     for the location of the columns, like "? jsonb_path_ops"
        /// </summary>
        public string Mask { get; set; }

        /// <summary>
        ///     The constraint expression for a partial index.
        /// </summary>
        public string Predicate { get; set; }

        /// <summary>
        ///     Set a non-default fill factor on this index
        /// </summary>
        public int? FillFactor { get; set; }


        public string Name
        {
            get
            {
                if (_indexName.IsNotEmpty())
                {
                    return _indexName;
                }

                return deriveIndexName();
            }
            set => _indexName = value;
        }

        protected virtual string deriveIndexName()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        ///     Set the Index expression against the supplied columns
        /// </summary>
        /// <param name="columns"></param>
        /// <returns></returns>
        public IndexDefinition AgainstColumns(params string[] columns)
        {
            Columns = columns;
            return this;
        }

        public string ToDDL(Table parent)
        {
            var builder = new StringBuilder();

            builder.Append("CREATE ");

            if (IsUnique)
            {
                builder.Append("UNIQUE ");
            }

            builder.Append("INDEX ");

            builder.Append(Name);


            builder.Append(" ON ");
            builder.Append(parent.Identifier);

            builder.Append(" ");
            builder.Append(correctedExpression());

            if (Predicate.IsNotEmpty())
            {
                builder.Append(" WHERE ");
                builder.Append($"({Predicate})");
            }

            if (FillFactor.HasValue)
            {
                builder.Append($" WITH (fillfactor='{FillFactor}')");
            }

            builder.Append(";");


            return builder.ToString();
        }

        private string correctedExpression()
        {
            if (Columns == null || !Columns.Any())
            {
                throw new InvalidOperationException("IndexDefinition requires at least one field");
            }

            var expression = Columns.Select(x => { return _reserved_words.Contains(x) ? $"\"{x}\"" : x; }).Join(", ");
            if (Mask.IsNotEmpty())
            {
                expression = Mask.Replace("?", expression);
            }

            if (SortOrder != SortOrder.Asc)
            {
                expression += " DESC";
            }

            return $"({expression})";
        }
        
        public static IndexDefinition Parse(string definition)
        {
            var tokens = new Queue<string>(StringTokenizer.Tokenize(definition.TrimEnd(';')));

            IndexDefinition index = null;

            var isUnique = false;
            var expression = "";

            while (tokens.Any())
            {
                var current = tokens.Dequeue();
                switch (current.ToUpper())
                {
                    case "CREATE":
                    case "CONCURRENTLY":
                        continue;

                    case "INDEX":
                        var name = tokens.Dequeue();
                        index = new IndexDefinition(name) {Mask = string.Empty, IsUnique = isUnique};
                        break;

                    case "ON":
                        // Skip the table name
                        tokens.Dequeue();
                        break;

                    case "UNIQUE":
                        isUnique = true;
                        break;

                    case "USING":
                        expression = tokens.Dequeue();
                        expression = removeSortOrderFromExpression(expression, out var order);

                        index.SortOrder = order;

                        break;


                    case "WHERE":
                        var predicate = tokens.Dequeue();
                        index.Predicate = predicate;
                        break;

                    case "WITH":
                        var factor = tokens.Dequeue().TrimStart('(').TrimEnd(')');
                        var parts = factor.Split('=');

                        if (parts[0].Trim().EqualsIgnoreCase("fillfactor"))
                        {
                            index.FillFactor = int.Parse(parts[1].TrimStart('\'').TrimEnd('\''));
                        }
                        else
                        {
                            throw new NotSupportedException(
                                $"Weasel does not yet support the '{parts[0]}' storage parameter");
                        }

                        break;

                    default:
                        throw new NotImplementedException("NOT YET DEALING WITH " + current);
                }
            }

            expression = expression.Trim().Replace("::text", "");
            while (expression.StartsWith("(") && expression.EndsWith(")"))
            {
                expression = expression.Substring(1, expression.Length - 2);
            }

            index.Columns = new[] {expression}; // This might be problematic

            return index;
        }

        private static string removeSortOrderFromExpression(string expression, out SortOrder order)
        {
            if (expression.EndsWith("DESC)"))
            {
                order = SortOrder.Desc;
                return expression.Substring(0, expression.Length - 6) + ")";
            }

            if (expression.EndsWith("ASC)"))
            {
                order = SortOrder.Asc;
                return expression.Substring(0, expression.Length - 5) + ")";
            }

            order = SortOrder.Asc;
            return expression.Trim();
        }

        public bool Matches(IndexDefinition actual, Table parent)
        {
            var expectedExpression = correctedExpression();


            if (actual.Mask == expectedExpression)
            {
                actual.Mask = removeSortOrderFromExpression(expectedExpression, out var order);
            }

            var expectedSql = CanonicizeDdl(this, parent);

            var actualSql = CanonicizeDdl(actual, parent);

            return expectedSql == actualSql;
        }

        public void AssertMatches(IndexDefinition actual, Table parent)
        {
            var expectedExpression = correctedExpression();


            if (actual.Mask == expectedExpression)
            {
                actual.Mask = removeSortOrderFromExpression(expectedExpression, out var order);
            }

            var expectedSql = CanonicizeDdl(this, parent);

            var actualSql = CanonicizeDdl(actual, parent);

            if (expectedSql != actualSql)
            {
                throw new Exception(
                    $"Index did not match, expected{Environment.NewLine}{expectedSql}{Environment.NewLine}but got:{Environment.NewLine}{actualSql}");
            }
        }

        public static string CanonicizeDdl(IndexDefinition index, Table parent)
        {
            return index.ToDDL(parent)
                    .Replace("\"\"", "\"")
                    .Replace("  ", " ")
                    .Replace("(", "")
                    .Replace(")", "")
                    .Replace("INDEX CONCURRENTLY", "INDEX")
                    .Replace("::text", "")
                    .Replace(" ->> ", "->>")
                    .Replace("->", "->").TrimEnd(new[] {';'})
                ;
        }
    }
}