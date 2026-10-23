using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using Xunit;

namespace Jellyfin.Server.Implementations.Tests.TypedBaseItem
{
    public class BaseItemKindTests
    {
        public static TheoryData<Type> BaseItemKind_TestData()
        {
            TheoryData<Type> data = new TheoryData<Type>();

            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (Assembly assembly in loadedAssemblies)
            {
                if (IsProjectAssemblyName(assembly.FullName))
                {
                    IEnumerable<Type> baseItemTypes = assembly.GetTypes()
                        .Where(targetType => targetType.IsClass
                                             && !targetType.IsAbstract
                                             && targetType.IsSubclassOf(typeof(MediaBrowser.Controller.Entities.BaseItem)));
                    foreach (Type? baseItemType in baseItemTypes)
                    {
                        data.Add(baseItemType);
                    }
                }
            }

            return data;
        }

        [Theory]
        [MemberData(nameof(BaseItemKind_TestData))]
        public void EnumParse_GivenValidBaseItemType_ReturnsEnumValue(Type baseItemDescendantType)
        {
            BaseItemKind enumValue = Enum.Parse<BaseItemKind>(baseItemDescendantType.Name);
            Assert.True(Enum.IsDefined(enumValue));
        }

        [Theory]
        [MemberData(nameof(BaseItemKind_TestData))]
        public void GetBaseItemKind_WhenCalledAfterDefaultCtor_DoesNotThrow(Type baseItemDescendantType)
        {
            ConstructorInfo? defaultConstructor = baseItemDescendantType.GetConstructor(Type.EmptyTypes);
            BaseItem instance = (MediaBrowser.Controller.Entities.BaseItem)defaultConstructor!.Invoke(null);
            Exception exception = Record.Exception(() => instance.GetBaseItemKind());
            Assert.Null(exception);
        }

        private static bool IsProjectAssemblyName(string? name)
        {
            if (name is null)
            {
                return false;
            }

            return name.StartsWith("Jellyfin", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("Emby", StringComparison.OrdinalIgnoreCase)
                   || name.StartsWith("MediaBrowser", StringComparison.OrdinalIgnoreCase);
        }
    }
}
