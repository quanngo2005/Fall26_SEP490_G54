using G54.BLL.Common.Constants;

namespace G54.BLL.Common.Exceptions;

public class NotFoundException : AppException
{
    public NotFoundException(string message, string errorCode = ErrorCodes.NotFound)
        : base(message, errorCode, 404)
    {
    }

    public NotFoundException(string resourceName, object key)
        : base($"{resourceName} với khóa '{key}' không tồn tại.", ErrorCodes.NotFound, 404)
    {
    }
}
