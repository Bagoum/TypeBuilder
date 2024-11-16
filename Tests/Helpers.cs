using FluentIL;
using Ex = System.Linq.Expressions.Expression;

namespace Tests;

public static class Helpers {
    public static readonly TypeFactory Factory = new("DynamicAssemblyTest", "DynamicAssemblyTest");

    /// <summary>
    /// Creates a function that returns its argument.
    /// This can be used to differentiate struct and class types, since for struct types,
    /// the returned value will be a different object.
    /// </summary>
    public static Delegate Return(Type t) {
        var prm = Ex.Parameter(t);
        return Ex.Lambda(prm, prm).Compile();
    }
}