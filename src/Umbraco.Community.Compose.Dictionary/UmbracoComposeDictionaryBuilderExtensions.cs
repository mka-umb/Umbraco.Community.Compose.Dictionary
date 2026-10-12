using System.Threading.Channels;
using Microsoft.Extensions.Configuration;
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
        /// Call this after <c>AddComposers()</c>. By default this registers Compose authentication itself,
        /// which a standalone install needs. When the official Compose integration is also installed it
        /// registers authentication in a composer (at <c>Build()</c>, after this method), so both end up
        /// registering a client for the same issuer. That is de-duplicated automatically, so the default
        /// works with or without the official integration — no configuration required. Set
        /// <c>Umbraco:Compose:Dictionary:RegisterComposeAuthentication</c> to <c>false</c> only to stop
        /// this package from registering authentication at all (for example to rely solely on the official
        /// integration's registration).
        /// </remarks>
        /// <returns>The Umbraco builder.</returns>
        public IUmbracoBuilder AddUmbracoComposeDictionaryIngestion()
        {
            ArgumentNullException.ThrowIfNull(builder);

            bool registerComposeAuthentication = builder.Config
                .GetValue<bool?>("Umbraco:Compose:Dictionary:RegisterComposeAuthentication") ?? true;

            if (registerComposeAuthentication)
            {
                builder.AddUmbracoComposeAuthentication();
            }

            // Collapse duplicate Compose client registrations to one. When both this package and the
            // official Compose integration register authentication, two registrations exist for the same
            // issuer, which OpenIddict cannot resolve without a RegistrationId. De-duplicating here keeps
            // RegisterComposeAuthentication optional: the default works whether or not the official
            // integration is installed. Runs as PostConfigure so it sees registrations added by composers.
            builder.Services.AddOptions<OpenIddictClientOptions>()
                .PostConfigure<IOptions<UmbracoComposeOptions>>(static (options, composeOptions) =>
                {
                    if (!composeOptions.Value.IsValid)
                    {
                        return;
                    }

                    Uri issuer = composeOptions.Value.GetManagementBaseUrl();
                    bool kept = false;

                    options.Registrations.RemoveAll(registration =>
                    {
                        if (!issuer.Equals(registration.Issuer))
                        {
                            return false;
                        }

                        if (kept)
                        {
                            return true;
                        }

                        kept = true;
                        return false;
                    });
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
