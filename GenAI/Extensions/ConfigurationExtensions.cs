using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace GenAI.Extensions
{
    /// <summary>Configuration source registrations.</summary>
    public static class ConfigurationExtensions
    {
        /// <summary>
        /// Adds Azure Key Vault as the highest-priority configuration source.
        /// Secrets found in the vault override appsettings.json; any key that is
        /// missing from the vault automatically falls back to appsettings.json,
        /// user-secrets or environment variables.
        /// <para>
        /// Key Vault secret names use "--" as the section separator,
        /// e.g. the secret "AzureAIFoundry--ApiKey" maps to "AzureAIFoundry:ApiKey".
        /// </para>
        /// <para>
        /// The vault is skipped entirely when "KeyVault:Uri" is not configured.
        /// If the vault is configured but unreachable, startup continues on the
        /// local configuration and a warning is logged.
        /// </para>
        /// </summary>
        public static WebApplicationBuilder AddAzureKeyVaultConfiguration(this WebApplicationBuilder builder)
        {
            var vaultUri = builder.Configuration["KeyVault:Uri"];
            if (string.IsNullOrWhiteSpace(vaultUri))
            {
                return builder;
            }

            try
            {
                var secretClient = new SecretClient(new Uri(vaultUri), new DefaultAzureCredential());

                // Probe the vault once so an unreachable/unauthorized vault degrades
                // to local configuration instead of failing app startup.
                _ = secretClient.GetPropertiesOfSecrets().Take(1).ToList();

                builder.Configuration.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
            }
            catch (Exception ex)
            {
                using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
                loggerFactory.CreateLogger(nameof(ConfigurationExtensions)).LogWarning(
                    ex,
                    "Azure Key Vault at {VaultUri} is unavailable; falling back to local configuration.",
                    vaultUri);
            }

            return builder;
        }
    }
}
