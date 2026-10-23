using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Jellyfin.Api.ModelBinders;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Moq;
using Xunit;

namespace Jellyfin.Api.Tests.ModelBinders
{
    public sealed class CommaDelimitedCollectionModelBinderTests
    {
        [Fact]
        public async Task BindModelAsync_CorrectlyBindsValidCommaDelimitedStringArrayQuery()
        {
            string queryParamName = "test";
            IReadOnlyList<string> queryParamValues = new[] { "lol", "xd" };
            string queryParamString = "lol,xd";
            Type queryParamType = typeof(string[]);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());
            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues> { { queryParamName, new StringValues(queryParamString) } }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);

            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            Assert.Equal((IReadOnlyList<string>?)bindingContextMock.Object?.Result.Model, queryParamValues);
        }

        [Fact]
        public async Task BindModelAsync_CorrectlyBindsValidCommaDelimitedIntArrayQuery()
        {
            string queryParamName = "test";
            IReadOnlyList<int> queryParamValues = new[] { 42, 0 };
            string queryParamString = "42,0";
            Type queryParamType = typeof(int[]);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());
            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues> { { queryParamName, new StringValues(queryParamString) } }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);

            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            Assert.Equal((IReadOnlyList<int>?)bindingContextMock.Object.Result.Model, queryParamValues);
        }

        [Fact]
        public async Task BindModelAsync_CorrectlyBindsValidCommaDelimitedEnumArrayQuery()
        {
            string queryParamName = "test";
            IReadOnlyList<TestType> queryParamValues = new[] { TestType.How, TestType.Much };
            string queryParamString = "How,Much";
            Type queryParamType = typeof(TestType[]);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());
            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues> { { queryParamName, new StringValues(queryParamString) } }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);

            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            Assert.Equal((IReadOnlyList<TestType>?)bindingContextMock.Object.Result.Model, queryParamValues);
        }

        [Fact]
        public async Task BindModelAsync_CorrectlyBindsValidCommaDelimitedEnumArrayQueryWithDoubleCommas()
        {
            string queryParamName = "test";
            IReadOnlyList<TestType> queryParamValues = new[] { TestType.How, TestType.Much };
            string queryParamString = "How,,Much";
            Type queryParamType = typeof(TestType[]);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());
            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues> { { queryParamName, new StringValues(queryParamString) } }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);

            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            Assert.Equal((IReadOnlyList<TestType>?)bindingContextMock.Object.Result.Model, queryParamValues);
        }

        [Fact]
        public async Task BindModelAsync_CorrectlyBindsValidEnumArrayQuery()
        {
            string queryParamName = "test";
            IReadOnlyList<TestType> queryParamValues = new[] { TestType.How, TestType.Much };
            string queryParamString1 = "How";
            string queryParamString2 = "Much";
            Type queryParamType = typeof(TestType[]);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());

            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues>
                    {
                        { queryParamName, new StringValues(new[] { queryParamString1, queryParamString2 }) },
                    }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);

            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            Assert.Equal((IReadOnlyList<TestType>?)bindingContextMock.Object.Result.Model, queryParamValues);
        }

        [Fact]
        public async Task BindModelAsync_CorrectlyBindsEmptyEnumArrayQuery()
        {
            string queryParamName = "test";
            IReadOnlyList<TestType> queryParamValues = Array.Empty<TestType>();
            Type queryParamType = typeof(TestType[]);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());

            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues>
                    {
                        { queryParamName, new StringValues(value: null) },
                    }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);

            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            Assert.Equal((IReadOnlyList<TestType>?)bindingContextMock.Object.Result.Model, queryParamValues);
        }

        [Fact]
        public async Task BindModelAsync_EnumArrayQuery_BindValidOnly()
        {
            string queryParamName = "test";
            string queryParamString = "🔥,😢";
            Type queryParamType = typeof(IReadOnlyList<TestType>);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());
            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues> { { queryParamName, new StringValues(queryParamString) } }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);
            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            IReadOnlyList<TestType>? listResult = (IReadOnlyList<TestType>?)bindingContextMock.Object.Result.Model;
            Assert.NotNull(listResult);
            Assert.Empty(listResult);
        }

        [Fact]
        public async Task BindModelAsync_EnumArrayQuery_BindValidOnly_2()
        {
            string queryParamName = "test";
            string queryParamString1 = "How";
            string queryParamString2 = "😱";
            Type queryParamType = typeof(IReadOnlyList<TestType>);

            CommaDelimitedCollectionModelBinder modelBinder = new CommaDelimitedCollectionModelBinder(new NullLogger<CommaDelimitedCollectionModelBinder>());

            QueryStringValueProvider valueProvider = new QueryStringValueProvider(
                    new BindingSource(string.Empty, string.Empty, false, false),
                    new QueryCollection(new Dictionary<string, StringValues>
                    {
                        { queryParamName, new StringValues(new[] { queryParamString1, queryParamString2 }) },
                    }),
                    CultureInfo.InvariantCulture);
            Mock<ModelBindingContext> bindingContextMock = new Mock<ModelBindingContext>();
            bindingContextMock.Setup(b => b.ValueProvider).Returns(valueProvider);
            bindingContextMock.Setup(b => b.ModelName).Returns(queryParamName);
            bindingContextMock.Setup(b => b.ModelType).Returns(queryParamType);
            bindingContextMock.SetupProperty(b => b.Result);

            await modelBinder.BindModelAsync(bindingContextMock.Object);
            Assert.True(bindingContextMock.Object.Result.IsModelSet);
            IReadOnlyList<TestType>? listResult = (IReadOnlyList<TestType>?)bindingContextMock.Object.Result.Model;
            Assert.NotNull(listResult);
            Assert.Single(listResult);
        }
    }
}
