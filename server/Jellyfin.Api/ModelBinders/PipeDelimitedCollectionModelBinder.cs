using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Api.ModelBinders;

/// <summary>
/// Comma delimited collection model binder.
/// Returns an empty collection of specified type if there is no query parameter.
/// </summary>
public class PipeDelimitedCollectionModelBinder : IModelBinder
{
    private readonly ILogger<PipeDelimitedCollectionModelBinder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="PipeDelimitedCollectionModelBinder"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{PipeDelimitedCollectionModelBinder}"/> interface.</param>
    public PipeDelimitedCollectionModelBinder(ILogger<PipeDelimitedCollectionModelBinder> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ValueProviderResult valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        Type elementType = bindingContext.ModelType.GetElementType() ?? bindingContext.ModelType.GenericTypeArguments[0];
        TypeConverter converter = TypeDescriptor.GetConverter(elementType);

        if (valueProviderResult.Length > 1)
        {
            Array typedValues = GetParsedResult(valueProviderResult.Values, elementType, converter);
            bindingContext.Result = ModelBindingResult.Success(typedValues);
        }
        else
        {
            string? value = valueProviderResult.FirstValue;

            if (value is not null)
            {
                string[] splitValues = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
                Array typedValues = GetParsedResult(splitValues, elementType, converter);
                bindingContext.Result = ModelBindingResult.Success(typedValues);
            }
            else
            {
                Array emptyResult = Array.CreateInstance(elementType, 0);
                bindingContext.Result = ModelBindingResult.Success(emptyResult);
            }
        }

        return Task.CompletedTask;
    }

    private Array GetParsedResult(IReadOnlyList<string> values, Type elementType, TypeConverter converter)
    {
        object?[] parsedValues = new object?[values.Count];
        int convertedCount = 0;
        for (int i = 0; i < values.Count; i++)
        {
            try
            {
                parsedValues[i] = converter.ConvertFromString(values[i].Trim());
                convertedCount++;
            }
            catch (FormatException e)
            {
                _logger.LogDebug(e, "Error converting value.");
            }
        }

        Array typedValues = Array.CreateInstance(elementType, convertedCount);
        int typedValueIndex = 0;
        for (int i = 0; i < parsedValues.Length; i++)
        {
            if (parsedValues[i] is not null)
            {
                typedValues.SetValue(parsedValues[i], typedValueIndex);
                typedValueIndex++;
            }
        }

        return typedValues;
    }
}
