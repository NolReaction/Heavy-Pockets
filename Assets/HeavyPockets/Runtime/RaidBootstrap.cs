using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace HeavyPockets
{
    public sealed class RaidBootstrap : MonoBehaviour
    {
        private NetworkManager network;
#if (UNITY_EDITOR_WIN || UNITY_EDITOR_OSX) && !HEAVY_POCKETS_EOS && !UNITY_SERVER && !HEAVY_POCKETS_SERVER
        private EditorRaidSession editorSession;
        private bool startingEditorSession;
#endif
#if UNITY_SERVER || UNITY_EDITOR || HEAVY_POCKETS_SERVER
        private RaidServer server;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Launch()
        {
#if UNITY_EDITOR
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path == TrainingGround.ScenePath)
            {
                TrainingGround.Launch();
                return;
            }
#endif
            var root = new GameObject("Heavy Pockets session");
            DontDestroyOnLoad(root);
            PortCollisionWorld.Create(root.transform);
            root.AddComponent<RaidBootstrap>().Begin();
        }

        private static string Option(string name, string fallback = null)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        internal static void ConfigureTiming()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 30f;
        }

        private void Begin()
        {
            try
            {
                if (!ushort.TryParse(Option("-port", "17777"), out ushort port) || port == 0)
                    throw new ArgumentException("Expected -port 1..65535.");
                ConfigureTiming();
                network = gameObject.AddComponent<NetworkManager>();
                var transport = gameObject.AddComponent<UnityTransport>();
                network.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    EnableSceneManagement = false,
                    ForceSamePrefabs = false,
                    TickRate = 30,
                    ConnectionApproval = true,
#if HEAVY_POCKETS_EOS
                    ProtocolVersion = 34
#else
                    ProtocolVersion = 33
#endif
                };
#if HEAVY_POCKETS_EOS
                gameObject.AddComponent<EosRaidNetwork>().Begin(network, transport, port);
#else
                if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("-requireEac", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Protected play requires the EAC build and Epic bootstrapper.");
                transport.SetConnectionData(true, "127.0.0.1", port, "127.0.0.1");
                string auditPath = null;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                auditPath = Option("-auditPath");
#endif
#if UNITY_SERVER || HEAVY_POCKETS_SERVER
                Serve(transport, port, auditPath);
#else
#if UNITY_EDITOR
                if (Array.Exists(Environment.GetCommandLineArgs(), arg => arg == "-server"))
                {
                    Serve(transport, port, auditPath);
                    return;
                }
#endif
#if UNITY_EDITOR_WIN || UNITY_EDITOR_OSX
                if (!Application.isBatchMode && Environment.GetEnvironmentVariable("HP_RAID_CA") == null &&
                    Environment.GetEnvironmentVariable("HP_RAID_TICKET") == null)
                {
                    editorSession = new EditorRaidSession();
                    startingEditorSession = true;
                    UnityEditor.Compilation.CompilationPipeline.compilationStarted += EndPlayBeforeCompile;
                    UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += StopEditorSession;
                    StartCoroutine(editorSession.WaitUntilReady((sessionPort, ca, ticket) =>
                    {
                        transport.SetConnectionData(true, "127.0.0.1", sessionPort, "127.0.0.1");
                        StartLocalClient(LocalRaidCredentials.ConfigureClient(transport, ca, ticket), auditPath);
                        startingEditorSession = false;
                    }, FailStartup));
                    return;
                }
#endif
                StartLocalClient(LocalRaidCredentials.Configure(transport, false), auditPath);
#endif
#endif
            }
            catch (Exception exception)
            {
                FailStartup(exception);
            }
        }

        private static void FailStartup(Exception exception)
        {
            Debug.LogException(exception);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(2);
#endif
        }

#if !HEAVY_POCKETS_EOS && !UNITY_SERVER && !HEAVY_POCKETS_SERVER
        private void StartLocalClient(LocalRaidCredentials credentials, string auditPath)
        {
            network.NetworkConfig.ConnectionData = credentials.ClientTicket;
            network.OnClientConnectedCallback += id => Debug.Log("HEAVY POCKETS CLIENT CONNECTED " + id);
            if (!network.StartClient())
                throw new InvalidOperationException("The UDP client could not start.");
            var client = gameObject.AddComponent<RaidClient>();
            client.Initialize(network, auditPath);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            string role = Option("-probeRole");
            if (role != null)
            {
                string directory = Option("-probeDir");
                if ((role != "A" && role != "B") || string.IsNullOrEmpty(directory))
                    throw new ArgumentException("Use -probeRole A|B -probeDir <directory>.");
                gameObject.AddComponent<RaidProbe>().Initialize(network, client.Link, role, directory);
            }
#endif
        }
#endif

#if (UNITY_EDITOR_WIN || UNITY_EDITOR_OSX) && !HEAVY_POCKETS_EOS && !UNITY_SERVER && !HEAVY_POCKETS_SERVER
        private void OnGUI()
        {
            if (startingEditorSession)
                GUI.Box(new Rect((Screen.width - 250) / 2f, Screen.height / 2f + 50, 250, 28), "Запуск локального рейда…");
        }

        private void OnDestroy() => StopEditorSession();

        // Stop the editor raid before a script reload invalidates its network manager and gait graphs.
        private void EndPlayBeforeCompile(object context) => UnityEditor.EditorApplication.isPlaying = false;

        private void StopEditorSession()
        {
            UnityEditor.Compilation.CompilationPipeline.compilationStarted -= EndPlayBeforeCompile;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= StopEditorSession;
            editorSession?.Dispose();
            editorSession = null;
        }
#endif

#if !HEAVY_POCKETS_EOS && (UNITY_SERVER || UNITY_EDITOR || HEAVY_POCKETS_SERVER)
        private void Serve(UnityTransport transport, ushort port, string auditPath)
        {
            var credentials = LocalRaidCredentials.Configure(transport, true);
            network.ConnectionApprovalCallback = (request, response) =>
            {
                response.Approved = credentials.TryConsume(request.Payload);
                response.CreatePlayerObject = false;
                if (!response.Approved)
                    response.Reason = "Local raid invitation rejected.";
            };
            if (!network.StartServer())
                throw new InvalidOperationException("Cannot listen on 127.0.0.1:" + port);
            bool pairedSpawn = false;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            pairedSpawn = Array.IndexOf(Environment.GetCommandLineArgs(), "-twoPlayerTest") >= 0;
#endif
            server = new RaidServer(network, auditPath, pairedSpawn: pairedSpawn);
            Debug.Log("HEAVY POCKETS SERVER READY 127.0.0.1:" + port);
        }

        private void FixedUpdate() => server?.Tick(Time.fixedDeltaTime);
        private void OnApplicationQuit()
        {
            server?.Dispose();
            server = null;
        }
#endif
    }
}
