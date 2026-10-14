using System.Net.Http.Headers;
using OpenIddict.Client;

namespace Umbraco.Community.Compose.Dictionary;

/// <summary>
/// Attaches an Umbraco Compose client-credentials bearer token to outgoing requests, resolving the
/// client registration by its explicit <see cref="ComposeRegistrationId"/>.
/// </summary>
/// <remarks>
/// The Compose Core handler (<c>AuthenticationHttpMessageHandler</c>) authenticates without specifying
/// an issuer, so OpenIddict can only resolve the registration when Compose is the single registered
/// client. Resolving by a dedicated registration id instead works regardless of other OpenIddict client
/// registrations (for example Slack via Umbraco.Automate, or Umbraco Cloud), and regardless of whether
/// the official Compose integration also registered a client for the same issuer. The id is stamped onto
/// the registration in <c>AddUmbracoComposeDictionaryIngestion</c>.
/// </remarks>
internal sealed class ComposeAuthenticationMessageHandler(OpenIddictClientService client) : DelegatingHandler
{
    /// <summary>The registration id this package assigns to, and resolves, its Compose client registration.</summary>
    internal const string ComposeRegistrationId = "Umbraco.Community.Compose.Dictionary";

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        OpenIddictClientModels.ClientCredentialsAuthenticationResult result = await client
            .AuthenticateWithClientCredentialsAsync(new OpenIddictClientModels.ClientCredentialsAuthenticationRequest
            {
                RegistrationId = ComposeRegistrationId,
                CancellationToken = cancellationToken
            })
            .ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
