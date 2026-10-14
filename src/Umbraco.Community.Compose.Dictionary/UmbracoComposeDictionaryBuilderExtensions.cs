using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenIddict.Client;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Community.Compose.Dictionary;
using Umbraco.Compose.Integrations.UmbracoCms.Core;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Umbraco.Cms.Core.DependencyInjection;
#pragma warning restore IDE0130 // Namespace does not match folder structure

/// <summary>
/// Extension methods for Umbraco Compose dictionary ingestion.
/// </summary>
public static class UmbracoComposeDictionaryBuilderExtensions
{
    extension(IUmbracoBuilder builder)
    {
        /// <summary>
        /// Adds Umbraco Compose dictionary ingestion.
        /// </summary>
        /// <remarks>
        /// Call this after <c>AddComposers()</c>. This registers Compose authentication itself (a standalone
        /// install needs it) and stamps a dedicated <see cref="ComposeAuthenticationMessageHandler.ComposeRegistrationId"/>
        /// onto its OpenIddict client registration. That makes it work automatically with or without the
        /// official Compose integration: when both register a client for the same Compose issuer, the two
        /// registrations have distinct ids (ours explicit, the official's a computed default), so OpenIddict
        /// accepts them, and this package resolves tokens by its own id. No configuration required.
        /// </remarks>
        /// <returns>The Umbraco builder.</returns>
        public IUmbracoBuilder AddUmbracoComposeDictionaryIngestion()
        {
            ArgumentNullException.ThrowIfNull(builder);

            builder.AddUmbracoComposeAuthentication();

            // Give our Compose client registration an explicit RegistrationId. The official integration (if
            // installed) registers a client for the same issuer with no id; OpenIddict computes a default id
            // from the issuer, so two such registrations would collide (error ID0347). Stamping an explicit
            // id makes the ids distinct and lets ComposeAuthenticationMessageHandler resolve ours by id.
            // This runs as a Configure (before OpenIddict's validating PostConfigure) and is registered at
            // builder time (before any composer-time Configure), so it stamps exactly our own registration.
            builder.Services.AddOptions<OpenIddictClientOptions>()
                .Configure<IOptions<UmbracoComposeOptions>>(static (options, composeOptions) =>
                {
                    if (!composeOptions.Value.IsValid)
                    {
                        return;
                    }

                    Uri issuer = composeOptions.Value.GetManagementBaseUrl();

                    OpenIddictClientRegistration? registration = options.Registrations.FirstOrDefault(r =>
                        issuer.Equals(r.Issuer) && string.IsNullOrEmpty(r.RegistrationId));

                    if (registration is not null)
                    {
                        registration.RegistrationId = ComposeAuthenticationMessageHandler.ComposeRegistrationId;
                    }
                });

            builder.Services.AddOptions<UmbracoComposeDictionaryOptions>()
                .BindConfiguration("Umbraco:Compose:Dictionary");

            Channel<DictionaryIngestQueueItem> channel = Channel.CreateUnbounded<DictionaryIngestQueueItem>(
                new UnboundedChannelOptions { SingleReader = true });
            builder.Services.AddSingleton(channel);
            builder.Services.AddSingleton(channel.Writer);

            builder.Services.AddScoped<DictionaryIngestQueueItemProcessor>();

            // Use our own authentication handler, which specifies the Compose issuer explicitly, rather
            // than the Compose Core handler, which omits it and fails when other OpenIddict client
            // registrations (e.g. Slack or Umbraco Cloud) are also present. See the handler's remarks.
            builder.Services.AddTransient<ComposeAuthenticationMessageHandler>();

            builder.Services.AddHttpClient(DictionaryIngestBackgroundService.IngestionClientName)
                .ConfigureHttpClient(static (services, client) =>
                    client.BaseAddress = services.GetRequiredService<IOptions<UmbracoComposeOptions>>().Value.GetIngestionUrl())
                .AddHttpMessageHandler<ComposeAuthenticationMessageHandler>()
                .SetProductInformation(typeof(DictionaryIngestBackgroundService).Assembly)
                .AddStandardResilienceHandler();

            builder.Services.AddHttpClient(DictionaryIngestBackgroundService.ManagementClientName)
                .ConfigureHttpClient(static (services, client) =>
                    client.BaseAddress = services.GetRequiredService<IOptions<UmbracoComposeOptions>>().Value.GetManagementUrl())
                .AddHttpMessageHandler<ComposeAuthenticationMessageHandler>()
                .SetProductInformation(typeof(DictionaryIngestBackgroundService).Assembly)
                .AddStandardResilienceHandler();

            builder.Services.AddHostedService<DictionaryIngestBackgroundService>();

            builder.AddComponent<DictionarySyncOnStartupComponent>();

            builder
                .AddNotificationHandler<DictionaryItemSavedNotification, DictionaryNotificationHandler>()
                .AddNotificationHandler<DictionaryItemDeletedNotification, DictionaryNotificationHandler>()
                .AddNotificationHandler<DictionaryItemMovedNotification, DictionaryNotificationHandler>();

            return builder;
        }
    }
}
