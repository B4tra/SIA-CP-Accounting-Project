using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace SIA.Web.Security;

/// <summary>
/// Mengisi <see cref="CircuitUserAccessor"/> saat circuit tersambung dan setiap kali
/// status autentikasi berubah.
/// </summary>
internal sealed class UserCircuitHandler : CircuitHandler, IDisposable
{
    private readonly AuthenticationStateProvider _authenticationStateProvider;
    private readonly CircuitUserAccessor _userAccessor;

    public UserCircuitHandler(AuthenticationStateProvider authenticationStateProvider, CircuitUserAccessor userAccessor)
    {
        _authenticationStateProvider = authenticationStateProvider;
        _userAccessor = userAccessor;
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationChanged;
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override async Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        _userAccessor.User = state.User;
    }

    private void OnAuthenticationChanged(Task<AuthenticationState> task)
    {
        _ = UpdateAsync(task);

        async Task UpdateAsync(Task<AuthenticationState> stateTask)
        {
            var state = await stateTask;
            _userAccessor.User = state.User;
        }
    }

    public void Dispose()
    {
        _authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationChanged;
    }
}
