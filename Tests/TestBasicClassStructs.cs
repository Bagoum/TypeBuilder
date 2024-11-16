using FluentIL;
using TypeBuilding;
using static Tests.Helpers;

namespace Tests;
public class TestBasicClassStructs {
    [SetUp]
    public void Setup() {
        DebugOutput.Output = new ConsoleOutput();
    }
    
    [Test]
    public void TestStruct() {
        var tb = Factory.NewType().Public();
        var fields = new[] {
            tb.NewField<int>("m_myInt").Public(),
            tb.NewField<string>("myString").Public()
        };
        tb.SetupAsDefaultStruct(fields);
        tb.AutoProperty(fields[0], "MyInt");
        var typ = tb.CreateType();
        Assert.IsTrue(typ.IsValueType);
        
        dynamic s1 = Activator.CreateInstance(typ, 12, "hello")!;
        Assert.AreEqual(14, s1.MyInt += 2);
        Assert.AreEqual(14, s1.m_myInt, 14);
        Assert.AreEqual("hello", s1.myString);
        //dynamic s2 = s1; is boxed so it doesn't correctly copy the struct value
        dynamic s2 = Return(typ).DynamicInvoke(s1);
        s1.m_myInt = 7;
        Assert.AreEqual(7, s1.MyInt);
        //changing s1 doesn't affect s2 because it is a struct type
        Assert.AreEqual(14, s2.MyInt);
        Assert.IsFalse(object.ReferenceEquals(s1, s2));
    }
    
    [Test]
    public void TestClass() {
        var tb = Factory.NewType().Public();
        var fields = new[] {
            tb.NewField<int>("m_myInt").Public(),
            tb.NewField<string>("myString").Public()
        };
        tb.SetupAsDefaultClass(fields);
        tb.AutoProperty(fields[0], "MyInt");
        var typ = tb.CreateType();
        Assert.IsFalse(typ.IsValueType);
        
        dynamic s1 = Activator.CreateInstance(typ, 12, "hello")!;
        Assert.AreEqual(14, s1.MyInt += 2);
        Assert.AreEqual(14, s1.m_myInt, 14);
        Assert.AreEqual("hello", s1.myString);
        dynamic s2 = Return(typ).DynamicInvoke(s1);
        s1.m_myInt = 7;
        Assert.AreEqual(7, s1.MyInt);
        Assert.AreEqual(7, s2.MyInt);
        Assert.IsTrue(object.ReferenceEquals(s1, s2));
    }
}