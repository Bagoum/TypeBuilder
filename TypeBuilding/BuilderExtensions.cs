using System.Reflection;
using FluentIL;
using JetBrains.Annotations;
using static TypeBuilding.BuilderHelpers;

namespace TypeBuilding;
public delegate void Emitter(IEmitter il);
[PublicAPI]
public static class BuilderExtensions {
    public static ConstructorInfo FindConstr<T>(params Type[] prms) => typeof(T).GetConstructor(prms) ??
        throw new Exception($"Couldn't find a constructor for type {typeof(T)} with types " +
                            $"{string.Join<Type>(",", prms)}");
    
    public static readonly ConstructorInfo KeyNotFound = FindConstr<KeyNotFoundException>(typeof(string));
    public static readonly ConstructorInfo BaseClass = FindConstr<object>();

    public static IConstructorBuilder MakeConstructorWithFields(this ITypeBuilder tb, ConstructorInfo? baseConstr,
        params IFieldBuilder[] fields) {
        var cons = tb.NewConstructor().Public();
        
        var body = cons.Body();
        if (baseConstr != null)
            body.LdArg0().Call(baseConstr);
        for (int ii = 0; ii < fields.Length; ++ii) {
            var f = fields[ii];
            cons.Param(f.FieldType, f.FieldName);
            body.LdArg0().LdArg(1 + ii).StFld(f);
        }
        body.Ret();
        return cons;
    }

    public static IConstructorBuilder SetupAsDefaultClass(this ITypeBuilder tb, params IFieldBuilder[] fields) => tb.InheritsFrom<object>().MakeConstructorWithFields(BaseClass, fields);

    public static IConstructorBuilder SetupAsDefaultStruct(this ITypeBuilder tb, params IFieldBuilder[] fields) => tb.InheritsFrom<ValueType>().MakeConstructorWithFields(null, fields);

    //https://docs.microsoft.com/en-us/dotnet/api/system.reflection.emit.typebuilder?view=net-6.0
    public static IPropertyBuilder GetFromField(this IPropertyBuilder pb, IFieldBuilder field) {
        pb.Getter().MethodAttributes(VirtualProperty).Body()
            .LdArg0()
            .LdFld(field)
            .Ret();
        return pb;
    }

    public static IPropertyBuilder SetToField(this IPropertyBuilder pb, IFieldBuilder field) {
        pb.Setter().MethodAttributes(VirtualProperty).Body()
            .LdArg0()
            .LdArg1()
            .StFld(field)
            .Ret();
        return pb;
    }

    public static IPropertyBuilder AutoProperty(this ITypeBuilder tb, IFieldBuilder field, string name) =>
        tb.NewProperty(name, field.FieldType)
            .GetFromField(field)
            .SetToField(field);
    
    /// <summary>
    /// Create a jumptable switch statement.
    /// </summary>
    /// <param name="il">IL generator</param>
    /// <param name="arg">Argument to switch</param>
    /// <param name="_cases">Cases for the switch statement. Keys may be negative.</param>
    /// <param name="deflt">Default case</param>
    /// <param name="isReturnSwitch">True iff all switch cases (including default) end in Ret.</param>
    /// <returns></returns>
    public static IEmitter EmitSwitch(this IEmitter il,
        Emitter arg, IEnumerable<(int key, Emitter emitter)> _cases, Emitter deflt, bool isReturnSwitch = false) {
        var cases = _cases.OrderBy(x => x.key).ToList();
        if (cases.Count == 0) {
            deflt(il);
            return il;
        }
        il.DefineLabel(out var defaultCase).DefineLabel(out var endOfSwitch);
        var minKey = cases.Min(x => x.key);
        var maxKey = cases.Max(x => x.key);
        var nCases = maxKey - minKey + 1;
        var jump = new ILabel[nCases];
        for (int ii = 0; ii < jump.Length; ++ii)
            il.DefineLabel(out jump[ii]);
        arg(il);
        il.LdcI4(minKey)
            .Sub()
            .Switch(jump)
            .BrS(defaultCase);
        int li = 0;
        foreach (var (key, emitter) in cases) {
            var relKey = key - minKey;
            if (li > relKey)
                throw new Exception("Misordering in switch emission, are the cases ordered?");
            while (li < relKey) {
                //Failure cases
                il.MarkLabel(jump[li]);
                il.BrS(defaultCase);
                ++li;
            }
            il.MarkLabel(jump[li]);
            emitter(il);
            if (!isReturnSwitch)
                il.BrS(endOfSwitch);
            ++li;
        }
        il.MarkLabel(defaultCase);
        deflt(il);
        if (!isReturnSwitch)
            il.MarkLabel(endOfSwitch);
        return il;
    }

    public static IEmitter EmitThrow(this IEmitter il, string msg) => il
        .LdStr(msg)
        .Newobj(KeyNotFound)
        .Throw();

    public static readonly MethodInfo stringConcat =
        typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string) }) ?? throw new Exception();
    public static readonly MethodInfo intToString =
        typeof(int).GetMethod("ToString", Array.Empty<Type>()) ?? throw new Exception();
    public static IEmitter EmitThrow(this IEmitter il, string msg, Emitter suffix) {
        il.LdStr(msg);
        suffix(il);
        il
            .Call(stringConcat)
            .Newobj(KeyNotFound)
            .Throw();
        return il;
    }
}
