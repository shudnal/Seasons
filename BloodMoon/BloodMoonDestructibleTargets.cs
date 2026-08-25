using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using static Seasons.Seasons;

namespace Seasons.BloodMoon
{
    internal static class BloodMoonDestructibleTargets
    {
        internal static IEnumerable<MethodBase> EnumerateWorldDamageMethods()
        {
            HashSet<MethodBase> methods = new HashSet<MethodBase>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in GetTypes(assembly))
                {
                    if (type == null || type.IsInterface || type.IsAbstract || typeof(Character).IsAssignableFrom(type) ||
                        !typeof(IDestructible).IsAssignableFrom(type))
                        continue;

                    MethodInfo method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(candidate =>
                        {
                            ParameterInfo[] parameters = candidate.GetParameters();
                            return (candidate.Name == "Damage" || candidate.Name.EndsWith(".Damage", StringComparison.Ordinal)) &&
                                candidate.ReturnType == typeof(void) && parameters.Length == 1 && parameters[0].ParameterType == typeof(HitData);
                        });
                    if (method != null)
                        methods.Add(method);
                }
            }

            if (methods.Count == 0)
                LogWarning("[BloodMoon.Combat] No non-Character IDestructible.Damage(HitData) methods were discovered; world-damage protection is unavailable.");
            return methods;
        }

        private static IEnumerable<Type> GetTypes(Assembly assembly)
        {
            if (assembly == null || assembly.IsDynamic)
                return Array.Empty<Type>();
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types?.Where(type => type != null) ?? Enumerable.Empty<Type>();
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }
}
