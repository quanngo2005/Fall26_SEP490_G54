namespace G54.BLL.Services;

public interface IPasswordEncoder
{
    string Encode(string rawPassword);
    bool Verify(string rawPassword, string encodedPassword);
}
