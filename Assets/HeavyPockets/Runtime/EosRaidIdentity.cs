#if HEAVY_POCKETS_EOS
using System;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Platform;

namespace HeavyPockets
{
    // One instance per dedicated server or client process, driven from Unity's main thread.
    internal sealed class EosRaidIdentity : IDisposable
    {
        private readonly bool server;
        private readonly string productId, sandboxId, deploymentId, gameClientId;
        private readonly ConnectInterface connect;
        private bool disposed;

        internal PlatformInterface Platform { get; }

        private EosRaidIdentity(bool server, PlatformInterface platform, string productId, string sandboxId,
            string deploymentId, string gameClientId)
        {
            this.server = server;
            this.productId = productId;
            this.sandboxId = sandboxId;
            this.deploymentId = deploymentId;
            this.gameClientId = gameClientId;
            Platform = platform;
            connect = platform.GetConnectInterface() ?? throw new InvalidOperationException("EOS Connect is unavailable.");
        }

        internal static EosRaidIdentity Create(bool server)
        {
            string productId = Required("HP_EOS_PRODUCT_ID");
            string sandboxId = Required("HP_EOS_SANDBOX_ID");
            string deploymentId = Required("HP_EOS_DEPLOYMENT_ID");
            string gameClientId = Required("HP_EOS_CLIENT_ID");
            string policyClientId = server ? Required("HP_EOS_SERVER_CLIENT_ID") : gameClientId;
            if (server && String.Equals(policyClientId, gameClientId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("EOS requires separate client and dedicated-server policies.");
            string policySecret = TakeSecret(server ? "HP_EOS_SERVER_CLIENT_SECRET" : "HP_EOS_CLIENT_SECRET");
            if (String.IsNullOrEmpty(policySecret))
                throw new InvalidOperationException("The EOS role credential is missing.");

            var init = new InitializeOptions { ProductName = "Heavy Pockets", ProductVersion = UnityEngine.Application.version };
            Result result = PlatformInterface.Initialize(ref init);
            if (result != Result.Success)
                throw new InvalidOperationException("Heavy Pockets could not acquire the EOS SDK lifecycle: " + result);

            PlatformInterface platform = null;
            try
            {
                var options = new Options
                {
                    ProductId = productId, SandboxId = sandboxId, DeploymentId = deploymentId,
                    IsServer = server, Flags = PlatformFlags.DisableOverlay,
                    ClientCredentials = new ClientCredentials { ClientId = policyClientId, ClientSecret = policySecret }
                };
                platform = PlatformInterface.Create(ref options);
                if (platform == null) throw new InvalidOperationException("EOS Platform creation failed.");
                return new EosRaidIdentity(server, platform, productId, sandboxId, deploymentId, gameClientId);
            }
            catch
            {
                platform?.Release();
                PlatformInterface.Shutdown();
                throw;
            }
        }

        internal void Login(Action<ProductUserId, string> success, Action<string> failure)
        {
            if (disposed || server) { failure("Client identity is unavailable."); return; }
            string idToken = TakeSecret("HP_EOS_EPIC_ID_TOKEN");
            string accessToken = TakeSecret("HP_EOS_EPIC_ACCESS_TOKEN");
            bool hasId = !String.IsNullOrEmpty(idToken);
            bool hasAccess = !String.IsNullOrEmpty(accessToken);
            if (hasId == hasAccess)
            {
                failure("Supply one Epic player credential for this client.");
                return;
            }
            var login = new LoginOptions
            {
                Credentials = new Credentials
                {
                    Type = hasId ? ExternalCredentialType.EpicIdToken : ExternalCredentialType.Epic,
                    Token = hasId ? idToken : accessToken
                }
            };
            connect.Login(ref login, null, (ref LoginCallbackInfo response) =>
            {
                if (disposed) return;
                if (response.ResultCode != Result.Success || response.LocalUserId == null)
                {
                    failure(response.ResultCode == Result.InvalidUser
                        ? "This Epic account has no Connect player in the selected product."
                        : "Epic login failed: " + response.ResultCode);
                    return;
                }
                var copy = new CopyIdTokenOptions { LocalUserId = response.LocalUserId };
                Result result = connect.CopyIdToken(ref copy, out IdToken? token);
                if (result != Result.Success || !token.HasValue || String.IsNullOrEmpty(token.Value.JsonWebToken))
                {
                    failure("EOS could not issue a Connect ID token: " + result);
                    return;
                }
                success(response.LocalUserId, token.Value.JsonWebToken);
            });
        }

        internal void Verify(string puid, string jwt, Action<ProductUserId> success, Action<string> failure)
        {
            if (!server || disposed || !ValidPlayerId(puid) || String.IsNullOrEmpty(jwt) || jwt.Length > 8192)
            {
                failure("Invalid Connect identity.");
                return;
            }
            ProductUserId claimedId = ProductUserId.FromString(puid);
            if (claimedId == null || !claimedId.IsValid()) { failure("Invalid Connect player ID."); return; }
            var verify = new VerifyIdTokenOptions { IdToken = new IdToken { ProductUserId = claimedId, JsonWebToken = jwt } };
            connect.VerifyIdToken(ref verify, null, (ref VerifyIdTokenCallbackInfo response) =>
            {
                if (disposed) return;
                if (response.ResultCode != Result.Success || response.ProductUserId == null ||
                    !String.Equals(response.ProductUserId.ToString(), puid, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(response.ProductId, productId, StringComparison.Ordinal) ||
                    !String.Equals(response.SandboxId, sandboxId, StringComparison.Ordinal) ||
                    !String.Equals(response.DeploymentId, deploymentId, StringComparison.Ordinal) ||
                    !String.Equals(response.ClientId, gameClientId, StringComparison.Ordinal) ||
                    !response.IsAccountInfoPresent || response.AccountIdType != ExternalAccountType.Epic)
                {
                    failure("Connect identity verification failed.");
                    return;
                }
                success(response.ProductUserId);
            });
        }

        // EOS documents FromString as a converter, not a validator. Accept only the
        // canonical 32-hex PUID format emitted by the supported Epic login flow.
        private static bool ValidPlayerId(string id)
        {
            if (id == null || id.Length != 32) return false;
            foreach (char c in id)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))) return false;
            return true;
        }

        private static string Required(string key)
        {
            string value = Environment.GetEnvironmentVariable(key);
            if (String.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Missing " + key + ".");
            return value;
        }

        private static string TakeSecret(string key)
        {
            string value = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, null);
            return value;
        }

        internal void Tick()
        {
            if (!disposed) Platform.Tick();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Platform.Release();
            PlatformInterface.Shutdown();
        }
    }
}
#endif
