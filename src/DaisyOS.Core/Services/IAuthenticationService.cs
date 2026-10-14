namespace DaisyOS.Core.Services;

public interface IAuthenticationService
{
    bool Authenticate(string password);
}