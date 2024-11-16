using FluentIL;
using TypeBuilding;

namespace Tests;
public class TestCustomDataBuilder {
    private static readonly Type tf = typeof(float);
    private static readonly Type ti = typeof(int);

    [SetUp]
    public void Setup() {
        DebugOutput.Output = new ConsoleOutput();
    }
    
    [Test]
    public void TestValue() {
        var builder = new CustomDataBuilder("Testing", null, tf, ti);
        builder.GetVariableKey("ffff", typeof(float));
        var req1 = new TypeDescriptor(
            new("f1", tf) { AutoProperty = "PropF1" },
            new("f2", tf),
            new("f3", tf),
            new("i1", ti)
        );
        var myType1 = builder.CreateCustomDataType(req1, out _);
        Assert.AreSame(myType1, builder.CreateCustomDataType(req1, out _));
        dynamic bobj = Activator.CreateInstance(builder.CustomDataBaseType)!;
        dynamic obj = Activator.CreateInstance(myType1)!;
        obj.f1 = 5f;
        obj.WriteFloat(builder.GetVariableKey("f2", tf), 4f);
        obj.i1 = 1;
        obj.PropF1 += obj.f2;
        var obj2 = obj.Clone();
        Assert.AreEqual(obj2.f1, 9f);
        Assert.AreEqual(obj.ReadFloat(builder.GetVariableKey("f1", tf)), 9f);
        Assert.AreEqual(obj2.ReadInt(builder.GetVariableKey("i1", ti)), 1);
        Assert.AreEqual("Custom data object does not have a float to get with ID 6", Assert.Throws<KeyNotFoundException>(() => obj.ReadFloat(6))?.Message);
    }
    
    [Test]
    public void TestSubclass() {
        var builder = new CustomDataBuilder("Testing", null, tf, ti);
        var rBase = new TypeDescriptor(
            new("f1", tf),
            new("f2", tf)
        );
        var tBase = builder.CreateCustomDataType(rBase, out _);
        Assert.AreEqual("f1<float>;f3<float> is not a subclass of f1<float>;f2<float>", Assert.Throws<Exception>(() =>
            builder.CreateCustomDataType(new(new("f1", tf), new("f3", tf)) {BaseType = tBase }, out _))?.Message);

        var rDeriv = new TypeDescriptor(
            new("f1", tf),
            new("f2", tf),
            new("f3", tf)
        );
        var tNorm = builder.CreateCustomDataType(rDeriv, out _);
        var tDeriv = builder.CreateCustomDataType(rDeriv with {BaseType = tBase}, out _);
        Assert.AreNotEqual(tNorm, tDeriv);
        
        dynamic norm = Activator.CreateInstance(tNorm)!;
        dynamic deriv = Activator.CreateInstance(tDeriv)!;
        deriv.f3 = 6;
        Assert.AreEqual(deriv.ReadFloat(builder.GetVariableKey("f3", tf)), 6f);
        Assert.IsTrue(deriv.GetType().IsSubclassOf(tBase));
        Assert.IsFalse(norm.GetType().IsSubclassOf(tBase));
    }

    [Test]
    public void TestCustomBaseClass() {
        var builder = new CustomDataBuilder(typeof(ExampleBaseCustomType), "Testing", null, tf);

        var tBase = builder.CreateCustomDataType(new(new("f1", tf), new("f2", tf)), out _);
        var tDeriv = builder.CreateCustomDataType(new(new("f1", tf), new("f2", tf), new("f3", tf)) { BaseType = tBase }, out _);

        dynamic bobj = Activator.CreateInstance(tBase)!;
        dynamic obj = Activator.CreateInstance(tDeriv)!;
        Assert.IsTrue(obj is ExampleBaseCustomType);
        Assert.AreEqual(null, obj.nonRecordedField);
        obj.nonRecordedField = "world";
        obj.f1 = 1f;
        obj.f3 = 3f;
        dynamic obj2 = (obj as ExampleBaseCustomType)!.Clone();
        Assert.AreEqual("world", obj2.nonRecordedField);
        Assert.AreEqual(1f, obj2.f1);
        Assert.AreEqual(3f, obj2.f3);
        dynamic obj3 = Activator.CreateInstance(tDeriv)!;
        (obj as ExampleBaseCustomType)!.CopyIntoVirtual(obj3);
        Assert.AreEqual("world", obj3.nonRecordedField);
        Assert.AreEqual(1f, obj3.f1);
        Assert.AreEqual(3f, obj3.f3);
        Assert.IsFalse(bobj.HasFloat(builder.GetVariableKey("f3", typeof(float))));
        Assert.IsTrue((obj as ExampleBaseCustomType)!.HasFloat(builder.GetVariableKey("f3", typeof(float))));
        obj3.nonRecordedField = "foo";

    }
}