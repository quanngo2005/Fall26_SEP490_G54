using G54.BLL.Common.Constants;

namespace G54.BLL.Common.Exceptions;

public class BusinessRuleException : AppException
{
    public BusinessRuleException(string errorCode, string? message = null, int statusCode = 400)
        : base(message ?? ErrorCodes.GetDefaultMessage(errorCode), errorCode, statusCode)
    {
    }
}
