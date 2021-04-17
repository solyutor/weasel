using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Baseline;

namespace Weasel.Postgresql.Tables
{
    public class TableColumn
    {
        public TableColumn(string name, string type)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentOutOfRangeException(nameof(name));
            if (string.IsNullOrEmpty(type))
                throw new ArgumentOutOfRangeException(nameof(type));
            
            Name = name.ToLower().Trim().Replace(' ', '_');
            Type = type.ToLower();
        }

        public IList<ColumnCheck> ColumnChecks { get; } = new List<ColumnCheck>();


        public string Name { get; }


        public string Type { get; set; }
        public Table Parent { get; internal set; }

        public string RawType()
        {
            return Type.Split('(')[0].Trim();
        }

        public string CheckDeclarations()
        {
            IEnumerable<ColumnCheck> checks = ColumnChecks;

            if (IsPrimaryKey && Parent.PrimaryKeyColumns().Count() == 1)
            {
                checks = checks.Where(x => !(x is INullConstraint))
                    .Concat(new ColumnCheck[]
                        {new PrimaryKey(Parent.PrimaryKeyConstraintName, new TableColumn[] {this})});
            }
            
            return checks.Select(x => x.FullDeclaration()).Join(" ");
        }

        protected bool Equals(TableColumn other)
        {
            return string.Equals(Name, other.Name) &&
                   string.Equals(TypeMappings.ConvertSynonyms(RawType()), TypeMappings.ConvertSynonyms(other.RawType()));
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
                return false;
            if (ReferenceEquals(this, obj))
                return true;
            if (!obj.GetType().CanBeCastTo<TableColumn>())
                return false;
            return Equals((TableColumn)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Name.GetHashCode() * 397) ^ Type.GetHashCode();
            }
        }

        public string ToDeclaration(int length)
        {
            // TODO -- use collation
            
            return $"{Name.PadRight(length)}{Type} {CheckDeclarations()}";
        }

        public override string ToString()
        {
            return $"{Name} {Type} {CheckDeclarations()}";
        }

        // TODO -- this needs to get a lot smarter
        //public bool CanAdd { get; set; } = false;

        /*
        public virtual string AddColumnSql(Table table)
        {
            return $"alter table {table.Identifier} add column {ToDeclaration(Name.Length + 1)};";
        }

        public virtual string AlterColumnTypeSql(Table table)
        {
            return $"alter table {table.Identifier} alter column {Name.PadRight(Name.Length)} type {Type};";
        }
        */



        internal bool IsPrimaryKey { get; set; }


    }

    public abstract class ColumnCheck
    {
        /// <summary>
        /// The database name for the check. This can be null
        /// </summary>
        public string Name { get; set; } // TODO -- validate good name

        public abstract string Declaration();

        public string FullDeclaration()
        {
            if (Name.IsEmpty()) return Declaration();

            return $"CONSTRAINT {Name} {Declaration()}";
        }
    }

    internal interface INullConstraint{}
    
    public class AllowNulls : ColumnCheck, INullConstraint
    {
        public override string Declaration() => "NULL";
    }

    public class NotNull : ColumnCheck, INullConstraint
    {
        public override string Declaration() => "NOT NULL";
    }

    /*
    public class DefaultValue : ColumnCheck
    {
        
    }

    public class GeneratedAlwaysAsStored : ColumnCheck
    {
        // GENERATED ALWAYS AS ( generation_expr ) STORED
    }
    
    public class GeneratedAsIdentity : ColumnCheck
    {
        // GENERATED { ALWAYS | BY DEFAULT } AS IDENTITY [ ( sequence_options ) ]
    }
    
    */
}