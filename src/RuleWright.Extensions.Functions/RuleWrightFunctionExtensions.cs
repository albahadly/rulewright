using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RuleWright.Core;
using RuleWright.Execution;

namespace RuleWright.Extensions.Functions;

/// <summary>
/// Convenience registration and discovery helpers over
/// <see cref="RuleWrightBuilder.RegisterFunction(IRuleFunction)"/>: register many functions at
/// once, register the whole built-in catalog, or scan an assembly for function implementations.
/// </summary>
public static class RuleWrightFunctionExtensions
{
    /// <summary>
    /// Registers each function in <paramref name="functions"/> on the builder, in order.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="functions">The functions to register.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="functions"/> is null.</exception>
    /// <exception cref="ArgumentException">A function's name is null/empty or already registered.</exception>
    public static RuleWrightBuilder RegisterFunctions(this RuleWrightBuilder builder, IEnumerable<IRuleFunction> functions)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (functions is null)
        {
            throw new ArgumentNullException(nameof(functions));
        }

        foreach (IRuleFunction function in functions)
        {
            builder.RegisterFunction(function);
        }

        return builder;
    }

    /// <summary>
    /// Registers the built-in function catalog (see <see cref="BuiltInFunctions"/>).
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="clock">Optional clock for the date-relativity predicates; defaults to <see cref="DateTime.UtcNow"/>.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <exception cref="ArgumentException">A built-in name collides with one already registered.</exception>
    public static RuleWrightBuilder RegisterBuiltInFunctions(this RuleWrightBuilder builder, Func<DateTime>? clock = null)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        return builder.RegisterFunctions(clock is null ? BuiltInFunctions.All : BuiltInFunctions.Create(clock));
    }

    /// <summary>
    /// Scans an assembly and registers every public, concrete <see cref="IRuleFunction"/> type
    /// that has a public parameterless constructor. Discovery lets a project keep its rule
    /// functions as ordinary classes and wire them up with one call instead of registering each
    /// by hand.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="assembly">The assembly to scan.</param>
    /// <returns>The same builder, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="assembly"/> is null.</exception>
    /// <exception cref="ArgumentException">Two discovered functions share a name, or a name is already registered.</exception>
    public static RuleWrightBuilder RegisterFunctionsFrom(this RuleWrightBuilder builder, Assembly assembly)
    {
        if (builder is null)
        {
            throw new ArgumentNullException(nameof(builder));
        }

        if (assembly is null)
        {
            throw new ArgumentNullException(nameof(assembly));
        }

        return builder.RegisterFunctions(Discover(assembly));
    }

    private static IEnumerable<IRuleFunction> Discover(Assembly assembly)
    {
        foreach (Type type in GetLoadableTypes(assembly))
        {
            if (type.IsPublic
                && !type.IsAbstract
                && !type.IsInterface
                && typeof(IRuleFunction).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                yield return (IRuleFunction)Activator.CreateInstance(type)!;
            }
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            // A dependency failed to load; register whatever types did resolve.
            return ex.Types.Where(type => type is not null)!;
        }
    }
}
