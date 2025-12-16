using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FileStoringService.Filters;

public class WorkIdParameterFilter : IParameterFilter
{
    public void Apply(OpenApiParameter parameter, ParameterFilterContext context)
    {
        if (parameter.Name?.Contains("workId", StringComparison.OrdinalIgnoreCase) == true ||
            parameter.Name?.Contains("id", StringComparison.OrdinalIgnoreCase) == true)
        {
            parameter.Description = "Work identifier. Can be in one of the following formats:\n" +
                                    "- GUID format: 123e4567-e89b-12d3-a456-426614174000\n" +
                                    "- Short hex format: 123e4567\n" +
                                    "- Numeric ID: 305419896\n\n" +
                                    "The service will automatically normalize and find the work.";
            parameter.Required = true;
            parameter.Example = new Microsoft.OpenApi.Any.OpenApiString("123e4567-e89b-12d3-a456-426614174000");
            
            if (parameter.Schema == null)
            {
                parameter.Schema = new OpenApiSchema { Type = "string" };
            }
        }
    }
}