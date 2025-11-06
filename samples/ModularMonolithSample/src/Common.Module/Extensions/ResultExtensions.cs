using Foundatio.Mediator;
using Microsoft.AspNetCore.Http;

namespace Common.Module.Extensions;

public static class ResultExtensions
{
    public static Microsoft.AspNetCore.Http.IResult ToHttpResult(this Result result)
    {
        return result.Status switch
        {
            ResultStatus.Success => Results.NoContent(),
            ResultStatus.NoContent => Results.NoContent(),
            ResultStatus.NotFound => Results.NotFound(new { message = result.Message }),
            ResultStatus.Invalid => Results.BadRequest(new { message = result.Message, errors = result.ValidationErrors }),
            ResultStatus.BadRequest => Results.BadRequest(new { message = result.Message, errors = result.ValidationErrors }),
            ResultStatus.Conflict => Results.Conflict(new { message = result.Message }),
            ResultStatus.Error => Results.Problem(result.Message),
            ResultStatus.CriticalError => Results.Problem(result.Message),
            _ => Results.Problem("An unexpected error occurred")
        };
    }

    public static Microsoft.AspNetCore.Http.IResult ToHttpResult<T>(this Result<T> result)
    {
        return result.Status switch
        {
            ResultStatus.Success => Results.Ok(result.Value),
            ResultStatus.Created => Results.Ok(result.Value),
            ResultStatus.NotFound => Results.NotFound(new { message = result.Message }),
            ResultStatus.Invalid => Results.BadRequest(new { message = result.Message, errors = result.ValidationErrors }),
            ResultStatus.BadRequest => Results.BadRequest(new { message = result.Message, errors = result.ValidationErrors }),
            ResultStatus.Conflict => Results.Conflict(new { message = result.Message }),
            ResultStatus.Error => Results.Problem(result.Message),
            ResultStatus.CriticalError => Results.Problem(result.Message),
            _ => Results.Problem("An unexpected error occurred")
        };
    }

    public static Microsoft.AspNetCore.Http.IResult ToCreatedResult<T>(this Result<T> result, string location)
    {
        return result.Status switch
        {
            ResultStatus.Success => Results.Created(location, result.Value),
            ResultStatus.Created => Results.Created(location, result.Value),
            ResultStatus.NotFound => Results.NotFound(new { message = result.Message }),
            ResultStatus.Invalid => Results.BadRequest(new { message = result.Message, errors = result.ValidationErrors }),
            ResultStatus.BadRequest => Results.BadRequest(new { message = result.Message, errors = result.ValidationErrors }),
            ResultStatus.Conflict => Results.Conflict(new { message = result.Message }),
            ResultStatus.Error => Results.Problem(result.Message),
            ResultStatus.CriticalError => Results.Problem(result.Message),
            _ => Results.Problem("An unexpected error occurred")
        };
    }
}
