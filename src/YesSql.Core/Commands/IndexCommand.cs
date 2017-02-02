﻿using System;
using System.Collections.Concurrent;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using YesSql.Core.Indexes;
using YesSql.Core.Sql;

namespace YesSql.Core.Commands
{
    public abstract class IndexCommand : IIndexCommand
    {
        protected readonly string _tablePrefix;

        private static readonly ConcurrentDictionary<RuntimeTypeHandle, PropertyInfo[]> TypeProperties = new ConcurrentDictionary<RuntimeTypeHandle, PropertyInfo[]>();
        private static readonly ConcurrentDictionary<RuntimeTypeHandle, string> InsertsList = new ConcurrentDictionary<RuntimeTypeHandle, string>();
        private static readonly ConcurrentDictionary<RuntimeTypeHandle, string> UpdatesList = new ConcurrentDictionary<RuntimeTypeHandle, string>();

        protected static PropertyInfo[] KeysProperties = new[] { typeof(IIndex).GetProperty("Id") };

        public abstract int ExecutionOrder { get; }

        public IndexCommand(IIndex index, string tablePrefix)
        {
            Index = index;
            _tablePrefix = tablePrefix;
        }

        public IIndex Index { get; }
        public Document Document { get; }

        public abstract Task ExecuteAsync(DbConnection connection, DbTransaction transaction, ISqlDialect dialect);

        protected static PropertyInfo[] TypePropertiesCache(Type type)
        {
            if (TypeProperties.TryGetValue(type.TypeHandle, out PropertyInfo[] pis))
            {
                return pis;
            }

            var properties = type.GetProperties().Where(IsWriteable).ToArray();
            TypeProperties[type.TypeHandle] = properties;
            return properties;
        }

        protected string Inserts(Type type, ISqlDialect dialect)
        {
            if (!InsertsList.TryGetValue(type.TypeHandle, out string result))
            {
                string values = "DEFAULT VALUES";

                var allProperties = TypePropertiesCache(type);

                if (allProperties.Any())
                {
                    var sbColumnList = new StringBuilder(null);

                    for (var i = 0; i < allProperties.Count(); i++)
                    {
                        var property = allProperties.ElementAt(i);
                        sbColumnList.Append(dialect.QuoteForColumnName(property.Name));
                        if (i < allProperties.Count() - 1)
                            sbColumnList.Append(", ");
                    }

                    var sbParameterList = new StringBuilder(null);
                    for (var i = 0; i < allProperties.Count(); i++)
                    {
                        var property = allProperties.ElementAt(i);
                        sbParameterList.Append("@" + property.Name);
                        if (i < allProperties.Count() - 1)
                            sbParameterList.Append(", ");
                    }

                    values = " (" + sbColumnList + ") values (" + sbParameterList + ");";
                }

                InsertsList[type.TypeHandle] = result = "insert into " + dialect.QuoteForTableName(_tablePrefix + type.Name) + values;
            }

            return String.Format(result, _tablePrefix);
        }

        protected string Updates(Type type, ISqlDialect dialect)
        {
            if (!UpdatesList.TryGetValue(type.TypeHandle, out string result))
            {

                var allProperties = TypePropertiesCache(type);
                var values = new StringBuilder(null);

                for (var i = 0; i < allProperties.Length; i++)
                {
                    var property = allProperties[i];
                    values.Append(dialect.QuoteForColumnName(property.Name) + " = @" + property.Name);
                    if (i < allProperties.Length - 1)
                        values.Append(", ");
                }

                UpdatesList[type.TypeHandle] = result = "update " + dialect.QuoteForTableName(_tablePrefix + type.Name) + " set " + values + " where Id = @Id;";
            }

            return String.Format(result, _tablePrefix);
        }

        private static bool IsWriteable(PropertyInfo pi)
        {
            return
                pi.Name != nameof(IIndex.Id) &&
                // don't read DocumentId when on a MapIndex as it might be used to 
                // read the DocumentId directly from an Index query
                pi.Name != "DocumentId"
                ;
        }
    }
}
