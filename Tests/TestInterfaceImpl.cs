using System.Reflection;
using BagoumLib.Expressions;
using FluentIL;
using TypeBuilding;
using static Tests.Helpers;
using static TypeBuilding.BuilderHelpers;
using Ex = System.Linq.Expressions.Expression;

namespace Tests;

public class TestInterfaceImpl {
    [SetUp]
    public void Setup() {
        DebugOutput.Output = new ConsoleOutput();
    }

    public interface MyInterface {
        public int MyInt { set; }
        public int GetDifference(int from);
    }
    
    [Test]
    public void TestExistingInterface() {
        var bT = Factory.NewType().Public().Implements<MyInterface>();
        bT.SetupAsDefaultClass();
        var field = bT.NewField<int>("m_myInt");
        var prop = bT.AutoProperty(field, "MyInt");
        
        bT.NewMethod<int>("GetDifference").MethodAttributes(VirtualMethod)
            .Param<int>("from")
            .Body()
            .LdArg1()
            .LdArg0()
            .LdFld(field) //.Call(prop.Getter()) //either works
            .Sub()
            .Ret();

        var obj = (MyInterface)Activator.CreateInstance(bT.CreateType())!;
        obj.MyInt = 5;
        Assert.AreEqual(95, obj.GetDifference(100));
        Assert.AreEqual(5, obj.GetPropertyValue("MyInt"));
    }

    [Test]
    public void TestDynamicInterface() {
        var bInt = Factory.NewType().Attributes(InterfaceType);
        bInt.NewProperty<int>("MyInt").Setter().MethodAttributes(AbstractProperty);
        bInt.NewMethod<int>("GetDifference").Param<int>("from").MethodAttributes(AbstractMethod);
        var itype = bInt.CreateType();
        var bT = Factory.NewType().Public()
            .Implements(itype);
        bT.SetupAsDefaultClass();
        var field = bT.NewField<int>("m_myInt").Public();
        var prop = bT.AutoProperty(field, "MyInt");
        bT.NewMethod("GetDifference").MethodAttributes(VirtualMethod)
            .Param<int>("from")
            .Returns<int>()
            .Body()
            .LdArg1()
            .LdArg0()
            //.LdFld(field)
            .Call(prop.Getter()) //either works
            .Sub()
            .Ret();

        dynamic obj1 = Activator.CreateInstance(bT.CreateType())!;


        var prm = Ex.Parameter(typeof(object));
        //int1.MyInt = 7
        Ex.Lambda<Func<object, int>>(
                Ex.Assign(Ex.Property(Ex.TypeAs(prm, itype), itype.GetProperty("MyInt")!), Ex.Constant(7))
            , prm).Compile()((object)obj1);
        //int1.GetDifference(103)
        var res = Ex.Lambda<Func<object, int>>(
                Ex.Call(Ex.TypeAs(prm, itype), itype.GetMethod("GetDifference")!, Ex.Constant(103))
            , prm).Compile()((object)obj1);
        Assert.AreEqual(96, res);
        Assert.AreEqual(7, obj1.MyInt);
        
    }
    
}