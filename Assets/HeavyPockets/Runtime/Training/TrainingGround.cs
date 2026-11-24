using UnityEngine;
#if UNITY_EDITOR
using Unity.Netcode;
#endif

namespace HeavyPockets
{
    internal sealed class TrainingGround : MonoBehaviour
    {
        internal const string ScenePath = "Assets/HeavyPockets/TrainingGround.unity";
        internal static bool Active { get; private set; }
        internal static readonly Vector2 Spawn = new Vector2(1f, -28f);
        internal static readonly Vector2 Chest = new Vector2(4f, -24f);
        internal static readonly Vector2 Extraction = new Vector2(62f, 42f);
        internal static readonly Bounds ChestBounds = new Bounds(new Vector3(Chest.x, .43f, Chest.y),
            new Vector3(2f * RaidMap.CrateX, .86f, 2f * RaidMap.CrateZ));
        internal static readonly OutdoorSolid[] ExitPosts =
        {
            new OutdoorSolid(OutdoorKind.SignalPost, Extraction.x - 2.6f, Extraction.y, .30f, 4.1f, .30f),
            new OutdoorSolid(OutdoorKind.SignalPost, Extraction.x + 2.6f, Extraction.y, .30f, 4.1f, .30f)
        };
        internal static readonly Bounds[] SiteGeometry =
        {
            ChestBounds, ExitPosts[0].Bounds, ExitPosts[1].Bounds,
            new Bounds(new Vector3(Extraction.x, 4.04f, Extraction.y), new Vector3(5.55f,.31f,.29f)),
            new Bounds(new Vector3(Extraction.x, 4.49f, Extraction.y-.06f), new Vector3(3.90f,1.13f,.19f))
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetScene() => Active = false;

#if UNITY_EDITOR
        private NetworkManager host;
        private RaidServer server;
        private RaidClient client;

        internal static void Launch()
        {
            Active = true;
            var root = new GameObject("Учебный полигон");
            PortCollisionWorld.Create(root.transform);
            root.AddComponent<TrainingGround>().Begin();
        }

        private void Begin()
        {
            host = gameObject.AddComponent<NetworkManager>();
            host.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = gameObject.AddComponent<TrainingTransport>(),
                EnableSceneManagement = false,
                ForceSamePrefabs = false,
                TickRate = 30
            };
            if (!host.StartHost()) throw new System.InvalidOperationException("The practice session could not start.");
            // Register the HUD's receiver before the authority issues the kit and the initial pose.
            client = gameObject.AddComponent<RaidClient>();
            client.Initialize(host, null);
            server = new RaidServer(host);
        }

        private void FixedUpdate()
        {
            if (server == null) return;
            client.Link.FlushHostCommands();
            server.Tick(Time.fixedDeltaTime);
        }

        private void OnDestroy()
        {
            server?.Dispose();
            WorldMechanisms.Reset();
            Active = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
#endif
    }
}
