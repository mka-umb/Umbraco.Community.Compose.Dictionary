using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using Umbraco.Compose.Integrations.UmbracoCms.Core;

namespace Umbraco.Community.Compose.Dictionary;

/// <summary>
/// Attaches an Umbraco Compose client-credentials bearer token to outgoing requests, resolving the
/// client registration by its issuer explicitly.
/// </summary>
/// <remarks>
/// The Compose Core handler (<c>AuthenticationHttpMessageHandler</c>) authenticates without specifying
/// an issuer, so OpenIddict can only resolve the registration when Compose is the single registered
/// client. On a site that has other OpenIddict client registrations (for example Slack via
/// Umbraco.Automate, or Umbraco Cloud) that is ambiguous and fails with "No issuer was specified in
/// the authentication context". This handler sets the issuer to the Compose management base URL, which
/// matches the registration added by <c>AddUmbracoComposeAuthentication</c>, so it works regardless of
/// how many other client registrations exist.
/// </remarks>
internal sealed class ComposeAuthenticationMessageHandler(
    OpenIddictClientService client,
    IOptions<UmbracoComposeOptions> options) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        OpenIddictClientModels.ClientCredentialsAuthenticationResult result = await client
            .AuthenticateWithClientCredentialsAsync(new OpenIddictClientModels.ClientCredentialsAuthenticationRequest
            {
                Issuer = options.Value.GetManagementBaseUrl(),
                CancellationToken = cancellationToken
            })
            .ConfigureAwait(false);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
