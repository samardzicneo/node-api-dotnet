
using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace NodeApi.Hosting;

/// <summary>
/// Base class for a dynamically generated interface adapter that enables a JS object to implement
/// a .NET interface. Holds a reference to the underlying JS value.
/// </summary>
[RequiresUnreferencedCode("Dynamic marshaling is not used in trimmed assembly.")]
[RequiresDynamicCode("Dynamic marshaling is not used in trimmed assembly.")]
public abstract class JSInterface
{
    private static readonly ConcurrentDictionary<Type, Type> s_interfaceTypes = new();
    private static readonly AssemblyBuilder s_assemblyBuilder =
        AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName(typeof(JSInterface).FullName!),
            AssemblyBuilderAccess.Run);
    private static readonly ModuleBuilder s_moduleBuilder =
        s_assemblyBuilder.DefineDynamicModule(typeof(JSInterface).Name);

    private readonly JSReference _jsReference;

    protected JSInterface(JSValue value)
    {
        _jsReference = new JSReference(value, isWeak: false);
    }

    public static JSValue? GetJSValue(object obj)
        => (obj as JSInterface)?.Value;

    /// <summary>
    /// Gets the underlying JS value. (The property name is prefixed with `__` to avoid
    /// possible conflicts with interface
    /// </summary>
    protected internal JSValue Value => _jsReference.GetValue()!.Value;

    /// <summary>
    /// Defines a class type that extends <see cref="JSInterface" /> and implements the requested
    /// interface type by forwarding all member access to the JS value.
    /// </summary>
    public static Type Implement(Type interfaceType, JSMarshaler marshaler)
    {
        return s_interfaceTypes.GetOrAdd(
            interfaceType,
            (t) => BuildInterfaceImplementation(interfaceType, marshaler));
    }

#pragma warning disable IDE0060 // Unused parameter 'marshaler'
    private static Type BuildInterfaceImplementation(Type interfaceType, JSMarshaler marshaler)
#pragma warning restore IDE0060 // Unused parameter
    {
        TypeBuilder typeBuilder = s_moduleBuilder.DefineType(
            "proxy_" +
            JSMarshaler.FullTypeName(interfaceType),
            TypeAttributes.Class | TypeAttributes.Sealed,
            typeof(JSInterface),
            new[] { interfaceType });

        ConstructorBuilder constructorBuilder = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.HasThis,
            new[] { typeof(JSValue) });
        constructorBuilder.DefineParameter(1, ParameterAttributes.None, "value");

        ////ConstructorInfo baseConstructor =
        ////    typeof(JSInterface).GetConstructor(BindingFlags.NonPublic, new[] { typeof(JSValue) })!;

        ILGenerator il = constructorBuilder.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        // TODO: Call base constructor
        ////il.Emit(OpCodes.Call, baseConstructor);
        il.Emit(OpCodes.Ret);

        foreach (MemberInfo member in interfaceType.GetMembers())
        {
            if (member is PropertyInfo property)
            {
                BuildPropertyImplementation(typeBuilder, property);
            }
            else if (member is MethodInfo method && !method.IsSpecialName)
            {
                BuildMethodImplementation(typeBuilder, method);
            }
            else if (member is EventInfo)
            {
                // TODO: Events
            }
        }

        Type implementationType = typeBuilder.CreateType();

        // TODO: Get implementation delegates from the marshaler and assign to static properties.

        return implementationType;
    }

    private static void BuildPropertyImplementation(
        TypeBuilder typeBuilder,
        PropertyInfo property)
    {
        if (property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true) return;

        PropertyBuilder propertyBuilder = typeBuilder.DefineProperty(
            property.Name,
            PropertyAttributes.None,
            property.PropertyType,
            property.GetIndexParameters().Select((p) => p.ParameterType).ToArray());

        MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.SpecialName |
            MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.NewSlot | MethodAttributes.HideBySig;

        if (property.GetMethod != null)
        {
            ParameterInfo[] parameters = property.GetMethod.GetParameters();
            MethodBuilder getMethodBuilder = typeBuilder.DefineMethod(
                property.DeclaringType!.Name + "." + property.GetMethod.Name,
                attributes,
                CallingConventions.HasThis,
                property.GetMethod.ReturnType,
                parameters.Select((p) => p.ParameterType).ToArray());
            BuildMethodParameters(getMethodBuilder, parameters);

            ILGenerator il = getMethodBuilder.GetILGenerator();

            // TODO: Getter IL

            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(getMethodBuilder, property.GetMethod);
            propertyBuilder.SetGetMethod(getMethodBuilder);
        }

        if (property.SetMethod != null)
        {
            ParameterInfo[] parameters = property.SetMethod.GetParameters();
            MethodBuilder setMethodBuilder = typeBuilder.DefineMethod(
                property.DeclaringType!.Name + "." + property.SetMethod.Name,
                attributes,
                CallingConventions.HasThis,
                property.SetMethod.ReturnType,
                parameters.Select((p) => p.ParameterType).ToArray());
            BuildMethodParameters(setMethodBuilder, parameters);

            ILGenerator il = setMethodBuilder.GetILGenerator();

            // TODO: Setter IL

            il.Emit(OpCodes.Ret);

            typeBuilder.DefineMethodOverride(setMethodBuilder, property.SetMethod);
            propertyBuilder.SetSetMethod(setMethodBuilder);
        }
    }

    private static void BuildMethodImplementation(
        TypeBuilder typeBuilder,
        MethodInfo method)
    {
        if (method.IsStatic) return;

        MethodAttributes attributes =
            MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final |
            MethodAttributes.NewSlot | MethodAttributes.HideBySig;
        ParameterInfo[] parameters = method.GetParameters();
        MethodBuilder methodBuilder = typeBuilder.DefineMethod(
            method.DeclaringType!.Name + "." + method.Name, // Explicit interface implementation
            attributes,
            CallingConventions.HasThis,
            method.ReturnType,
            parameters.Select((p) => p.ParameterType).ToArray());
        BuildMethodParameters(methodBuilder, parameters);

        ILGenerator il = methodBuilder.GetILGenerator();

        // TODO: Method IL
        // Define a static property for a delegate for each method.
        // Emit IL to invoke the delegate, passing args and returning result.
        // After building the type, get delegates from the marshaler and assign to static properties.

        il.Emit(OpCodes.Ret);

        typeBuilder.DefineMethodOverride(methodBuilder, method);
    }

    private static void BuildMethodParameters(
        MethodBuilder methodBuilder, ParameterInfo[] parameters)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            // Position here is offset by one because the return parameter is at 0.
            methodBuilder.DefineParameter(i + 1, parameters[i].Attributes, parameters[i].Name);
        }
    }
}