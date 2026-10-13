using G54.BLL.Common.Constants;

namespace G54.BLL.Common.Exceptions;

public class ValidationException : AppException
{
    public ValidationException(string message, IReadOnlyList<string>? errors = null)
        : base(message, ErrorCodes.ValidationError, 400, errors)
    {
    }

    public ValidationException(IReadOnlyList<string> errors)
        : base(
            errors.Count > 0 ? errors[0] : ErrorCodes.GetDefaultMessage(ErrorCodes.ValidationError),
            ErrorCodes.ValidationError,
            400,
            errors)
    {
    }
}
