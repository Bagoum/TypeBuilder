using System.Reflection;

namespace TypeBuilding;
public static class BuilderHelpers {
    public const TypeAttributes InterfaceType =
        TypeAttributes.Interface | TypeAttributes.Public | TypeAttributes.Abstract;
    public const TypeAttributes ClassType =
        TypeAttributes.Class | TypeAttributes.Public;

    //ignoring: FamANDAssem, Family, NewSlot
    public const MethodAttributes AbstractProperty =
        AbstractMethod | VirtualProperty;

    public const MethodAttributes VirtualProperty =
        MethodAttributes.SpecialName | VirtualMethod;
        //NB: Virtual is not required for auto-property, but is convenient to declare since
        // it also is used to check if a property implements an interface property.

    public const MethodAttributes AbstractMethod =
        MethodAttributes.Abstract | VirtualMethod;
    
    public const MethodAttributes VirtualMethod =
        MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.Public;

}
