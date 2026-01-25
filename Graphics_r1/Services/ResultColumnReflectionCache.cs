using PileDesign.Models.Results;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;

namespace PileDesign.Services
{
    public static class ResultColumnReflectionCache
    {
        private static readonly ConcurrentDictionary<Type, ResultColumnDescriptor[]> Cache = new();

        public static ResultColumnDescriptor[] GetColumns(Type t) =>
            Cache.GetOrAdd(t, Build);

        private static ResultColumnDescriptor[] Build(Type t) =>
            t.GetProperties(BindingFlags.Instance | BindingFlags.Public)
             .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ResultColumnAttribute>()))
             .Where(x => x.Attr != null)
             .Select(x => new ResultColumnDescriptor
             {
                 Header = x.Attr!.Header,
                 Order = x.Attr.Order,
                 Property = x.Prop,
                 Format = x.Attr.Format
             })
             .OrderBy(c => c.Order)
             .ToArray();
    }
}