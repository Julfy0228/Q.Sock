using Q.Sock.Server.Entities;

namespace Q.Sock.Server.Services;

public interface IExternalAuthenticationProvider
{
    string Id { get; }
    bool AllowsLocalRegistration { get; }
    Task<UserEntity?> Authenticate(string username, string password);
}

public sealed class ExternalAuthentication(
    IEnumerable<IExternalAuthenticationProvider> providers,
    IConfiguration configuration
)
{
    public IExternalAuthenticationProvider? Active
    {
        get
        {
            var all = providers.ToArray();
            var selected = configuration["Authentication:Provider"];
            if (!string.IsNullOrWhiteSpace(selected))
                return all.SingleOrDefault(provider => provider.Id == selected)
                    ?? throw new InvalidOperationException(
                        "Указанный провайдер входа не установлен."
                    );
            return all.Length switch
            {
                0 => null,
                1 => all[0],
                _ => throw new InvalidOperationException(
                    "Выберите провайдера входа в настройках Authentication:Provider."
                ),
            };
        }
    }

    public bool AllowsLocalRegistration => Active?.AllowsLocalRegistration != false;
}
