using System.Reflection;
using System.Reflection.Emit;
using System.Text.RegularExpressions;
using BagoumLib; //https://github.com/Bagoum/suzunoya
using BagoumLib.Reflection;
using FluentIL; //https://github.com/PCOL/FluentIL
using JetBrains.Annotations;
using static TypeBuilding.BuilderHelpers;
using static TypeBuilding.BuilderExtensions;

namespace TypeBuilding;

/// <summary>
/// An example base type for <see cref="CustomDataBuilder"/>.
/// </summary>
public class ExampleBaseCustomType {
    /// <summary>
    /// This field is not examined by <see cref="CustomDataBuilder"/>,
    ///  and is not accessible by the Has/Read/Write methods.
    /// </summary>
    public string nonRecordedField = null!;

    /// <summary>
    /// Base field checker method. Should return false.
    /// </summary>
    public virtual bool HasFloat(int id) => false;
    /// <summary>
    /// Base field reader method. Should throw an exception.
    /// </summary>
    public virtual float ReadFloat(int id) => throw new Exception();
    /// <summary>
    /// Base field writer method. Should throw an exception.
    /// </summary>
    public virtual void WriteFloat(int id, float val) => throw new Exception();

    /// <summary>
    /// Copy this object's values onto another object.
    /// This should copy any values not handled by <see cref="CustomDataBuilder"/>
    ///  (in this case, just <see cref="nonRecordedField"/>).
    /// </summary>
    public ExampleBaseCustomType CopyInto(ExampleBaseCustomType other) {
        other.nonRecordedField = nonRecordedField;
        return other;
    }

    /// <summary>
    /// Virtual method to copy this object's values onto another object.
    /// <br/>Subclasses should implement this by casting the argument to their own type
    /// and then calling their own <see cref="CopyInto"/>, which itself should call this base class <see cref="CopyInto"/>.
    /// </summary>
    public virtual ExampleBaseCustomType CopyIntoVirtual(ExampleBaseCustomType other) => CopyInto(other);

    /// <summary>
    /// Virtual method to clone an object.
    /// Should be `CopyInto(new T())` where T is the type of the overriding subclass.
    /// </summary>
    /// <returns></returns>
    public virtual ExampleBaseCustomType Clone() {
        return CopyInto(new ExampleBaseCustomType());
    }
}


/// <summary>
/// A description of a field on a custom data type.
/// </summary>
public record FieldDescriptor(string Name, Type Type) {
    /// <summary>
    /// If nonnull, will create an auto-property with the given name.
    /// </summary>
    public string? AutoProperty { get; init; } = null;

    /// <inheritdoc />
    public override string ToString() => 
        $"{Name}<{Type.RName()}>" + (AutoProperty != null ? $"({AutoProperty})" : "");
}

//TODO: add nontrivial properties and methods
/// <summary>
/// A description of a custom data type, described uniquely by its fields and its base type.
/// </summary>
public record TypeDescriptor(params FieldDescriptor[] Fields) {
    /// <summary>
    /// The base type of the custom data type being constructed.
    /// </summary>
    public Type? BaseType { get; init; }
    
    /// <inheritdoc />
    public virtual bool Equals(TypeDescriptor? other) => 
        Fields.AreSame(other?.Fields) && BaseType == other?.BaseType;

    /// <inheritdoc />
    public override int GetHashCode() => (BaseType, Fields.ElementWiseHashCode()).GetHashCode();

    /// <summary>
    /// Checks if this type is a subclass of the provided type.
    /// </summary>
    public bool IsSubclassOf(BuiltTypeDescriptor parent) {
        foreach (var field in parent.Fields)
            if (!Fields.Contains(field.Descriptor))
                return false;
        return BaseType.IsWeakSubclassOf(parent.BuiltType);
    }

    /// <summary>
    /// A custom data type with no fields and no base type.
    /// </summary>
    public static readonly TypeDescriptor Empty = new();

    /// <inheritdoc />
    public override string ToString() => string.Join(";", Fields.Select(f => f.ToString()));
}


/// <summary>
/// The metadata of a field on a built custom data type.
/// </summary>
public record BuiltFieldDescriptor(FieldDescriptor Descriptor, int ID, FieldBuilder Field);

/// <summary>
/// The metadata of a built custom data type.
/// </summary>
public record BuiltTypeDescriptor(TypeDescriptor Descriptor, Type BuiltType, ConstructorInfo Constructor,
    params BuiltFieldDescriptor[] Fields);

/// <summary>
/// A class that constructs simple types containing fields of various types.
/// <br/>If specified, the types also provide methods in the format FindT for each T
///  in <see cref="byIdAccessible"/> (eg. FindFloat), that allow looking up
///  a field by ID (converted from name via <see cref="GetVariableKey"/>) on any
///  subclass of <see cref="CustomDataBaseType"/>.
/// </summary>
[PublicAPI]
public class CustomDataBuilder {
    /// <summary>
    /// Type factory used to create runtime types.
    /// </summary>
    public TypeFactory Factory { get; }
    /// <summary>
    /// Dictionary mapping string, type pairs to IDs, as looked up by GetVariableKey.
    /// </summary>
    protected readonly Dictionary<Type, Dictionary<string, int>> variableNameToID = new();
    private readonly Type[] byIdAccessible;
    /// <summary>
    /// The starting type from which all types generated by this <see cref="CustomDataBuilder"/> are derived.
    /// </summary>
    public Type CustomDataBaseType { get; }
    /// <summary>
    /// A map containing all types that have been built.
    /// </summary>
    protected readonly Dictionary<TypeDescriptor, Type> customDataTypes = new();
    /// <summary>
    /// A map containing the build metadata for all types that have been built.
    /// </summary>
    protected readonly Dictionary<Type, BuiltTypeDescriptor> customDataDescriptors = new();

    /// <summary>
    /// Constructor that can be used when there is a fixed base type.
    /// The base type must have functions in the format {Read/Write/Has}T(int key) for all types T in byIdAccessible. It also must have a Clone and CopyInto function as well. See <see cref="ExampleBaseCustomType"/>.
    /// <br/>It is recommended to use a base type if possible.
    /// </summary>
    public CustomDataBuilder(Type baseType, string asmName, string? moduleName, params Type[] byIdAccessible) {
        Factory = new(asmName, moduleName ?? asmName);
        this.byIdAccessible = byIdAccessible;
        foreach (var t in byIdAccessible) {
            if (baseType.GetMethod(FieldReaderMethodName(t)) is null)
                throw new Exception(
                    $"Preconstructed base typedoes not have custom data getter function {FieldReaderMethodName(t)} for type {t.RName()}");
        }
        CustomDataBaseType = baseType;
        customDataDescriptors[baseType] = MakeForEmpty(baseType);
        customDataTypes[TypeDescriptor.Empty] = baseType;
    }
    
    private static BuiltTypeDescriptor MakeForEmpty(Type baseType) => 
        new(TypeDescriptor.Empty, baseType, baseType.GetConstructor(Type.EmptyTypes) ?? 
                                        throw new Exception("Preconstructed base type is missing empty constructor"));
    
    /// <summary>
    /// Constructor that can be used when there is no fixed base type.
    /// </summary>
    public CustomDataBuilder(string asmName, string? moduleName, params Type[] byIdAccessible) {
        Factory = new(asmName, moduleName ?? asmName);
        this.byIdAccessible = byIdAccessible;

        var bt = Factory.NewType("CustomDataBase")
            .Attributes(ClassType)
            .InheritsFrom<object>();
        var cons = bt.MakeConstructorWithFields(BaseClass);

        var copyInto = bt.NewMethod(CopyIntoMethod)
            .MethodAttributes(MethodAttributes.Public)
            .Param(bt.Define(), "copyee")
            .Returns(bt.Define())
            .Body(e => e.LdArg1().Ret());

        bt.NewMethod(CopyIntoVirtualMethod, m => m
            .MethodAttributes(VirtualMethod)
            .Param(bt.Define(), "copyee")
            .Returns(bt.Define())
            .Body(e => e.LdArg0().LdArg1().Call(copyInto).Ret())
        );

        bt.NewMethod(CloneMethod, m => m
            .MethodAttributes(VirtualMethod)
            .Returns(bt.Define())
            .Body(e => e
                .LdArg0() // this
                .Newobj(cons) // new CustomDataBaseType()
                .Call(copyInto) // this.CopyInto(new CustomDataBaseType())
                .Ret()));
        
        foreach (var t in byIdAccessible) {
            bt.NewMethod(FieldCheckerMethodName(t), m => m
                .MethodAttributes(VirtualMethod)
                .Param<int>("variableKey")
                .Returns(typeof(bool))
                .Body(e => e.LdcI4_0().Ret())
            );
            bt.NewMethod(FieldReaderMethodName(t), m => m
                .MethodAttributes(VirtualMethod)
                .Param<int>("variableKey")
                .Returns(t)
                .Body(e => 
                    e.EmitThrow($"Custom data object has no fields of type {t.RName()} and thus has no field to get with ID ",
                    e => e
                        .Emit(OpCodes.Ldarga_S, (byte)1)
                        .Call(intToString)
                    )));
            bt.NewMethod(FieldWriterMethodName(t), m => m
                .MethodAttributes(VirtualMethod)
                .Param<int>("variableKey")
                .Param(t, "value")
                .Returns(t)
                .Body(e => 
                    e.EmitThrow($"Custom data object has no fields of type {t.RName()} and thus has no field to set with ID ",
                        e => e
                            .Emit(OpCodes.Ldarga_S, (byte)1)
                            .Call(intToString)
                    )));
        }
        CustomDataBaseType = bt.CreateType();
        
        customDataDescriptors[CustomDataBaseType] = new(TypeDescriptor.Empty, CustomDataBaseType, cons.Define());
        customDataTypes[TypeDescriptor.Empty] = CustomDataBaseType;
    }

    /// <summary>
    /// Get the id for a field with the given name and type.
    /// The id can be passed to the generated field reader, writer, and checker methods.
    /// </summary>
    public int GetVariableKey(string variable, Type t) {
        var nameDict = variableNameToID.SetDefault(t);
        return nameDict.TryGetValue(variable, out var key) ? 
            key : 
            nameDict[variable] = nameDict.Count;
    }

    private const string CopyIntoMethod = nameof(ExampleBaseCustomType.CopyInto);
    private const string CopyIntoVirtualMethod = nameof(ExampleBaseCustomType.CopyIntoVirtual);
    private const string CloneMethod = nameof(ExampleBaseCustomType.Clone);
    private static Regex stringCleaner = new(@"[^a-zA-Z0-9_]", RegexOptions.Compiled);
    
    /// <summary>
    /// Method name for reading an arbitrary value of type T by its id. Generally "ReadT".
    /// </summary>
    public static string FieldReaderMethodName(Type t) => 
        $"Read{stringCleaner.Replace(t.RName(), "").FirstToUpper()}";

    /// <summary>
    /// Method name for writing an arbitrary value of type T by its id. Generally "WriteT".
    /// </summary>
    public static string FieldWriterMethodName(Type t) => 
        $"Write{stringCleaner.Replace(t.RName(), "").FirstToUpper()}";

    /// <summary>
    /// Method name for checking if a value of type T with the given id exists. Generally "HasT".
    /// </summary>
    public static string FieldCheckerMethodName(Type t) => 
        $"Has{stringCleaner.Replace(t.RName(), "").FirstToUpper()}";


    /// <summary>
    /// Create a custom type with the fields described in <paramref name="req"/>,
    /// and Read{T}(int id) functions that allow looking up fields by ID.
    /// <br/>A base type can be provided, but that type must derive from <see cref="CustomDataBaseType"/>, and it must have a descriptor registered in the builder. 
    /// </summary>
    public Type CreateCustomDataType(TypeDescriptor req, out BuiltTypeDescriptor built, Action<ITypeBuilder>? setup = null) {
        var baseType = req.BaseType ?? CustomDataBaseType;
        if (req.Fields.Length == 0) {
            built = customDataDescriptors[baseType];
            return baseType;
        }
        if (req.BaseType is null)
            req = req with { BaseType = baseType };
        if (customDataTypes.TryGetValue(req, out var customType)) {
            built = customDataDescriptors[customType];
            return customType;
        }
        if (baseType != CustomDataBaseType && !baseType.IsSubclassOf(CustomDataBaseType))
            throw new Exception(
                $"Provided base type {baseType.Name} is not a subclass of builder base type {CustomDataBaseType.Name}");
        var bt = Factory.NewType($"CustomData{customDataTypes.Count}")
            .Attributes(ClassType)
            .InheritsFrom(baseType);
        setup?.Invoke(bt);
        if (!customDataDescriptors.TryGetValue(baseType, out var baseDesc))
            throw new Exception($"Provided base type {baseType.Name} does not have a descriptor");
        var cons = bt.MakeConstructorWithFields(baseDesc.Constructor);
        if (!req.IsSubclassOf(baseDesc))
            throw new Exception($"{req} is not a subclass of {baseDesc.Descriptor}");
        var builtFields = new BuiltFieldDescriptor[req.Fields.Length];
        var fieldTypes = new HashSet<Type>();
        for (int ii = 0; ii < req.Fields.Length; ++ii) {
            var f = req.Fields[ii];
            FieldBuilder fieldDef;
            foreach (var baseField in baseDesc.Fields) {
                if (baseField.Descriptor == f) {
                    fieldDef = baseField.Field;
                    goto recordFieldBuilder;
                }
            }
            var fieldBuild = bt.NewField(f.Name, f.Type).Public();
            fieldDef = fieldBuild.Define();
            if (f.AutoProperty is {} propName)
                bt.AutoProperty(fieldBuild, propName);
            recordFieldBuilder:
            fieldTypes.Add(f.Type);
            builtFields[ii] = new(f, GetVariableKey(f.Name, f.Type), fieldDef);
        }
        
        var copyInto = bt.NewMethod(CopyIntoMethod)
            .MethodAttributes(MethodAttributes.Public)
            .Param(bt.Define(), "copyee")
            .Returns(bt.Define())
            .Body(e => {
                e.LdArg0()
                    .LdArg1()
                    .Call(baseDesc.BuiltType.GetMethod(CopyIntoMethod, 
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, [baseDesc.BuiltType]))
                    .Pop(); //base.CopyInto(target)
                foreach (var field in builtFields)
                    e.LdArg1() 
                        .LdArg0().LdFld(field.Field) //this.field
                        .StFld(field.Field);
                e.LdArg1().Ret();
            });
        
        bt.NewMethod(CopyIntoVirtualMethod, m => m
            .MethodAttributes(VirtualMethod)
            .Param(CustomDataBaseType, "copyee")
            .Returns(CustomDataBaseType)
            .Body(e => e.LdArg0().LdArg1().CastClass(bt.Define()).Call(copyInto).Ret())
        );

        bt.NewMethod(CloneMethod, m => m
            .MethodAttributes(VirtualMethod)
            .Returns(CustomDataBaseType)
            .Body(e => e
                .LdArg0() // this
                .Newobj(cons) // new CustomDataType()
                .Call(copyInto) // this.CopyInto(new CustomDataType())
                .Ret()));
        
        foreach (var type in byIdAccessible)
            if (fieldTypes.Contains(type)) {
                bt.NewMethod(FieldCheckerMethodName(type), m => m
                    .MethodAttributes(VirtualMethod)
                    .Param<int>("variableKey")
                    .Returns<bool>()
                    .Body(e => e
                        .EmitSwitch(e => e.LdArg1(), 
                            builtFields.SelectNotNull(f => f.Descriptor.Type == type ?
                                (f.ID, c => c.LdcI4_1().Ret()) : ((int, Emitter)?)null), 
                            def => def.LdcI4_0().Ret(),
                            true)
                        ));
                
                bt.NewMethod(FieldReaderMethodName(type), m => m
                    .MethodAttributes(VirtualMethod)
                    .Param<int>("variableKey")
                    .Returns(type)
                    .Body(e => e
                        .DeclareLocal(type, out var local)
                        .EmitSwitch(e => e.LdArg1(), 
                            builtFields.SelectNotNull(f => f.Descriptor.Type == type ?
                                (f.ID, c => c
                                    .LdArg0()
                                    .LdFld(f.Field)
                                    .StLoc(local)
                                ) : ((int, Emitter)?)null), def =>
                                def.EmitThrow(
                                    $"Custom data object does not have a {type.RName()} to get with ID ",
                                    e => e
                                        .Emit(OpCodes.Ldarga_S, (byte)1)
                                        .Call(intToString)
                                )
                        )
                        .LdLoc(local)
                        .Ret()));
                
                bt.NewMethod(FieldWriterMethodName(type), m => m
                    .MethodAttributes(VirtualMethod)
                    .Param<int>("variableKey")
                    .Param(type, "value")
                    .Returns(type)
                    .Body(e => e
                        .EmitSwitch(e => e.LdArg1(), 
                            builtFields.SelectNotNull(f => f.Descriptor.Type == type ?
                                (f.ID, c => c
                                    .LdArg0()
                                    .LdArg2()
                                    .StFld(f.Field)
                                    .LdArg2()
                                    .Ret()
                                ) : ((int, Emitter)?)null), def =>
                                def.LdArg2().Ret()
                                /*def.EmitThrow(
                                    $"Custom data object does not have a {type.RName()} to set with ID ",
                                    e => e
                                        .Emit(OpCodes.Ldarga_S, (byte)1)
                                        .Call(intToString)
                                )*/
                                    , true)
                        ));
            }

        var newType = bt.CreateType();
        customDataDescriptors[newType] = built = new(req, newType, cons.Define(), builtFields);
        return customDataTypes[req] = newType;
    }

}