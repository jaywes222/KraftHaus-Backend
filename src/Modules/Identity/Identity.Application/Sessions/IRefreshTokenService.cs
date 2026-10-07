namespace Identity.Application.Sessions;

public interface IRefreshTokenService
{
    string Generate();

    string Hash(string refreshToken);

    bool Verify(
        string refreshToken,
        string expectedHash);
}
