using System;
using Unity.Netcode.Transports.UTP;
#if UNITY_EDITOR || UNITY_SERVER || HEAVY_POCKETS_SERVER
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
#endif

namespace HeavyPockets
{
    // Private material exists only in the environment of the role that needs it.
    // These are development-machine invitations, not Internet player identities.
    internal sealed class LocalRaidCredentials
    {
        internal byte[] ClientTicket { get; private set; }

        private static string Take(string name)
        {
            string value = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, null);
            if (string.IsNullOrEmpty(value))
                throw new InvalidOperationException("No local raid credentials. Start Heavy Pockets using Play.cmd.");
            return value;
        }

        internal static LocalRaidCredentials Configure(UnityTransport transport, bool isServer)
        {
            if (!isServer)
                return ConfigureClient(transport, Take("HP_RAID_CA"), Take("HP_RAID_TICKET"));
#if UNITY_EDITOR || UNITY_SERVER || HEAVY_POCKETS_SERVER
            transport.UseEncryption = true;
            transport.SetServerSecrets(Take("HP_RAID_CERT"), Take("HP_RAID_KEY"));
            var credentials = new LocalRaidCredentials();
            foreach (string encoded in Take("HP_RAID_DIGESTS").Split(','))
            {
                byte[] digest = Convert.FromBase64String(encoded);
                if (digest.Length != 32 || credentials.remaining.Count >= 4)
                    throw new InvalidOperationException("Invalid local raid admission configuration.");
                credentials.remaining.Add(digest);
            }
            return credentials;
#else
            throw new InvalidOperationException("A dedicated server build is required.");
#endif
        }

        internal static LocalRaidCredentials ConfigureClient(UnityTransport transport, string ca, string ticket)
        {
            transport.UseEncryption = true;
            transport.SetClientSecrets("heavy-pockets.local", ca);
            var credentials = new LocalRaidCredentials { ClientTicket = Convert.FromBase64String(ticket) };
            if (credentials.ClientTicket.Length != 32)
                throw new InvalidOperationException("Invalid local invitation.");
            return credentials;
        }

#if UNITY_EDITOR || UNITY_SERVER || HEAVY_POCKETS_SERVER
        private readonly List<byte[]> remaining = new List<byte[]>(4);
        private readonly Stopwatch lifetime = Stopwatch.StartNew();

        // NGO invokes approval on the server simulation thread. A successful match
        // is removed before returning approval, including closely spaced connections.
        internal bool TryConsume(byte[] invitation)
        {
            if (invitation == null || invitation.Length != 32 || remaining.Count == 0 ||
                lifetime.Elapsed > TimeSpan.FromMinutes(5))
                return false;
            byte[] digest;
            using (var sha = SHA256.Create()) digest = sha.ComputeHash(invitation);
            for (int i = 0; i < remaining.Count; i++)
            {
                int difference = 0;
                for (int j = 0; j < digest.Length; j++) difference |= digest[j] ^ remaining[i][j];
                if (difference != 0) continue;
                remaining.RemoveAt(i);
                return true;
            }
            return false;
        }
#endif
    }
}
