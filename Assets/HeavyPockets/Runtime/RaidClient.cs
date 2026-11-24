using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace HeavyPockets
{
    public sealed partial class RaidClient : MonoBehaviour
    {
        private sealed class Runner
        {
            public Transform Model;
            public FreightRunnerGait Gait;
            public readonly PoseTrack Track = new PoseTrack();
        }

        // Render position, heading and locomotion at one point on the server timeline.
        // The extra 25 ms absorbs packet/frame jitter without predicting through walls.
        private struct RenderPose
        {
            public double Time;
            public Vector2 Position;
            public float Yaw;
            public RaidLocomotion Motion;

            public static RenderPose Between(RenderPose from, RenderPose to, double clock)
            {
                float duration = (float)(to.Time - from.Time);
                float t = Mathf.Clamp01((float)(clock - from.Time) / duration);
                var motion = new RaidLocomotion
                {
                    Height = Mathf.Lerp(from.Motion.Height, to.Motion.Height, t),
                    Crouch = Mathf.Lerp(from.Motion.Crouch, to.Motion.Crouch, t),
                    Sprinting = t < .5f ? from.Motion.Sprinting : to.Motion.Sprinting,
                    Sliding = t < .5f ? from.Motion.Sliding : to.Motion.Sliding
                };
                if (from.Motion.Airborne || to.Motion.Airborne)
                    SampleJump(from.Motion, to.Motion, duration, t, ref motion);
                if (motion.Airborne) motion.Sliding = false;
                return new RenderPose
                {
                    Time = clock, Position = Vector2.Lerp(from.Position, to.Position, t),
                    Yaw = Mathf.LerpAngle(from.Yaw, to.Yaw, t), Motion = motion
                };
            }

            private static void SampleJump(RaidLocomotion from, RaidLocomotion to, float duration,
                float t, ref RaidLocomotion motion)
            {
                const float gravity = RaidLocomotion.Gravity;
                if (from.Airborne && to.Airborne)
                {
                    // The velocity change gives the elapsed physics time independently of delivery jitter.
                    // Bound it at ceilings, where the head contact can cancel upward speed in one tick.
                    float flightTime = Mathf.Clamp((from.VerticalSpeed - to.VerticalSpeed) / gravity, 0f, duration + .017f);
                    motion.Height = Mathf.Lerp(from.Height, to.Height, t) + .5f * gravity * flightTime * flightTime * t * (1f - t);
                    motion.VerticalSpeed = Mathf.Lerp(from.VerticalSpeed, to.VerticalSpeed, t);
                    motion.Airborne = true;
                    return;
                }

                bool landing = from.Airborne;
                RaidLocomotion sample = landing ? from : to;
                float surface = landing ? to.Height : from.Height;
                float height = sample.Height - surface;
                float flightTimeAtContact = !landing && height < 0f
                    ? -sample.VerticalSpeed / gravity // Walking off a ledge begins with zero vertical speed.
                    : TimeToSurface(height, landing ? sample.VerticalSpeed : -sample.VerticalSpeed);
                duration = Mathf.Max(duration, flightTimeAtContact);
                float elapsed = landing ? t * duration : (t - 1f) * duration;
                if (Mathf.Abs(elapsed) >= flightTimeAtContact)
                {
                    motion.Height = surface;
                    return;
                }
                motion.Height = Mathf.Max(0f, sample.Height + (sample.VerticalSpeed - .5f * gravity * elapsed) * elapsed);
                motion.VerticalSpeed = sample.VerticalSpeed - gravity * elapsed;
                motion.Airborne = true;
            }

            private static float TimeToSurface(float height, float velocity) =>
                (velocity + Mathf.Sqrt(Mathf.Max(0f, velocity * velocity + 2f * RaidLocomotion.Gravity * height))) /
                RaidLocomotion.Gravity;
        }

        private sealed class PoseTrack
        {
            private readonly RenderPose[] samples = new RenderPose[5];
            private int count;
            private double lastArrival;
            private float cadence;

            public void Push(RaidEvent update, bool snap)
            {
                double now = Time.unscaledTimeAsDouble;
                float gap = (float)(now - lastArrival);
                double stamp = now;
                if (snap || count == 0 || gap > .4f)
                {
                    count = 0;
                    cadence = .1f; // Three fixed server steps at 30 Hz.
                }
                else
                {
                    // Reveals/spawns can happen between regular snapshots.
                    double previous = samples[count - 1].Time;
                    if (count == 1)
                        stamp = previous + Mathf.Clamp(gap, .001f, .15f);
                    else
                    {
                        cadence = Mathf.Lerp(cadence, Mathf.Clamp(gap, 0f, .2f), .04f);
                        double expected = previous + cadence;
                        stamp = expected + Mathf.Clamp((float)(now - expected) * .08f, -.004f, .004f);
                    }
                }
                lastArrival = now;
                if (count == samples.Length)
                {
                    Array.Copy(samples, 1, samples, 0, count - 1);
                    count--;
                }
                samples[count++] = new RenderPose { Time = stamp, Position = update.Position, Yaw = update.Yaw, Motion = update.Motion };
            }

            public RenderPose Sample()
            {
                double clock = Time.unscaledTimeAsDouble - .125;
                if (clock <= samples[0].Time) return samples[0];
                for (int i = 1; i < count; i++)
                    if (clock < samples[i].Time) return RenderPose.Between(samples[i - 1], samples[i], clock);
                return samples[count - 1];
            }
        }

        partial void ObserveRunnerStart(Runner runner, Vector3 position, float yaw);
        partial void ObserveRunnerHide(Runner runner);

        private readonly Dictionary<ulong, Runner> runners = new Dictionary<ulong, Runner>();
        private RaidScenery scenery;
        private GameObject worldStage;
        private bool renderedTraining, awaitingInitialMap, waitingForWorld, loadingMap, votedForMap;
        private int readyFrame;
        private byte mapVotes, mapPlayers;
        private readonly Queue<RaidEvent> pendingWorldEvents = new Queue<RaidEvent>();
        private Camera view;
        private int viewportWidth, viewportHeight;
        private float viewAspect = 16f / 9f;
        private Transform ownModel;
        private FreightRunnerGait ownGait;
        private readonly PoseTrack ownTrack = new PoseTrack();
        internal RaidLocomotion OwnMotion { get; private set; }
        private float displayHeight, displayCrouch;
        private MoveButtons probeButtons;
        private bool jumpPending;
        private Vector2 displayPosition;
        private float boomLength;
        private float shoulderPhase = 1f;
        private byte lastShoulderSample = RaidCameraBoom.LastSample;
        internal bool IsThirdPerson { get; private set; } = true;
        internal bool IsLeftShoulder { get; private set; }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal byte ProbeShoulderSample => RaidCameraBoom.SampleShoulder(shoulderPhase, CameraMode);
#endif
        private RaidCameraMode CameraMode => !IsThirdPerson ? RaidCameraMode.FirstPerson :
            IsLeftShoulder ? RaidCameraMode.LeftShoulder : RaidCameraMode.RightShoulder;
        private Vector2 lastProbeDirection;
        private bool pendingProbeView;
        internal bool OwnAvatarVisible => ownModel != null && ownModel.gameObject.activeInHierarchy;
        private float yaw, pitch, nextSend;
        private bool crateOpen, inventoryOpen, searchComplete, extracted, disconnected;
        private bool inventoryControlHeld;
        private float openRequestUntil;
        private bool OpeningContainer => Time.unscaledTime < openRequestUntil;
        private bool lockAfterClose, closeRequested, bagFromMenu, captureOnSpawn;
        private bool inventoryLooking;
        private float searchFinishAt;
        private RaidScreen screen;
        private uint round;
        private bool queued, changingQueue;
        private readonly InventoryPlacementView inventory = new InventoryPlacementView();
        private LootItem[] extractedItems = Array.Empty<LootItem>();
        private float exitSeconds;
        private RaidHud hud;

        public NetworkManager Manager { get; private set; }
        public RaidClientLink Link { get; private set; }
        public bool ScriptedInput { get; set; }
        public bool HasOwnPose { get; private set; }
        public Vector2 OwnPosition { get; private set; }
        internal byte ScaleKilograms { get; private set; }
        public float OwnYaw { get; private set; }

        public void Initialize(NetworkManager manager, string auditPath)
        {
            Manager = manager;
            Application.runInBackground = true;
            ScriptedInput = (Debug.isDebugBuild || Application.isEditor)
                && Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("-probeRole", StringComparison.OrdinalIgnoreCase));
            Link = new RaidClientLink(manager, auditPath);
            Link.Received += Receive;
            manager.OnClientDisconnectCallback += OnDisconnected;
            hud = new RaidHud(RequestTransfer, RequestPlacement, () => CloseInventory(true));
            hud.MenuAction = UseMenu;
            hud.MoveGear = RequestGearMove;
            hud.SelectGear = SelectEquipment;
            renderedTraining = TrainingGround.Active;
            if ((Application.isBatchMode && !TrainingGround.Active) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            var eye = new GameObject("Runner camera");
            eye.transform.SetParent(transform, false);
            view = eye.AddComponent<Camera>();
            eye.AddComponent<RaidCameraSampling>();
            view.tag = "MainCamera";
            view.fieldOfView = RaidCameraBoom.VerticalFov;
            view.nearClipPlane = RaidCameraBoom.NearClip;
            view.farClipPlane = RaidCameraBoom.FarClip;
            UpdateViewport();
            view.clearFlags = CameraClearFlags.Skybox;
            view.cullingMask &= ~(1 << AnimationStudio.Layer);
            eye.AddComponent<AudioListener>();
            eye.transform.position = new Vector3(0, 1.58f, -1f);
            // A late join can be assigned a map different from the scene used to start the client.
            awaitingInitialMap = waitingForWorld = loadingMap = true;
        }

        private void BeginMapLoad()
        {
            loadingMap = waitingForWorld = true;
            readyFrame = -1;
            StartCoroutine(LoadMapView());
        }

        private void ChangeMap(bool training)
        {
            renderedTraining = training;
            PortCollisionWorld.Select(training, transform);
            if (view == null) return;
            ReleaseCursor();
            worldStage.SetActive(false);
            scenery.Dispose();
            Destroy(worldStage);
            scenery = null;
            BeginMapLoad();
        }

        private System.Collections.IEnumerator LoadMapView()
        {
            // Paint the loading screen; release old renderers before creating the next map.
            yield return null;
            yield return null;
            yield return RaidScenery.PreloadMapAssets(renderedTraining);
            while (!RaidScenery.ReleasedAllCells && !disconnected) yield return null;
            if (disconnected) yield break;
            worldStage = new GameObject(renderedTraining ? "Training ground" : "Freight harbor");
            worldStage.transform.SetParent(transform, false);
            scenery = new RaidScenery(worldStage.transform);
            yield return scenery.BuildWorld();
            waitingForWorld = false;
            while (!waitingForWorld && pendingWorldEvents.Count > 0) Receive(pendingWorldEvents.Dequeue());
        }

        // Visual acceptance uses the same camera and HUD as a human player.
        internal void LookAtForCapture(Vector3 target)
        {
            if (!ScriptedInput || view == null) return;
            Vector3 angles = Quaternion.LookRotation(target - EyePosition()).eulerAngles;
            SetProbeMotion(Vector2.zero, angles.y, Mathf.DeltaAngle(0f, angles.x));
            view.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
        }

        internal void SetProbeMotion(Vector2 direction, float heading, float elevation, MoveButtons buttons = MoveButtons.None)
        {
            if (!ScriptedInput) return;
            yaw = Mathf.Repeat(heading, 360f);
            pitch = Mathf.Clamp(elevation, -RaidCameraBoom.MaxPitch, RaidCameraBoom.MaxPitch);
            lastProbeDirection = direction;
            probeButtons = buttons & ~MoveButtons.Jump;
            pendingProbeView = false;
            SendMovement(direction, buttons);
        }

        private void SendMovement(Vector2 direction, MoveButtons buttons = MoveButtons.None)
        {
            lastShoulderSample = RaidCameraBoom.SampleShoulder(shoulderPhase, CameraMode);
            Link.Move(direction, yaw, pitch, CameraMode, viewAspect, lastShoulderSample, buttons);
            nextSend = Time.unscaledTime + .05f;
        }

        private void UpdateViewport()
        {
            if (view == null || (viewportWidth == Screen.width && viewportHeight == Screen.height)) return;
            viewportWidth = Mathf.Max(1, Screen.width);
            viewportHeight = Mathf.Max(1, Screen.height);
            float windowAspect = (float)viewportWidth / viewportHeight;
            viewAspect = Mathf.Clamp(windowAspect, RaidCameraBoom.MinAspect, RaidCameraBoom.MaxAspect);
            float width = Mathf.Min(1f, viewAspect / windowAspect), height = Mathf.Min(1f, windowAspect / viewAspect);
            view.rect = new Rect((1f - width) * 0.5f, (1f - height) * 0.5f, width, height);
            view.aspect = viewAspect;
        }

        private void Receive(RaidEvent e)
        {
            if (disconnected) return;
            if (waitingForWorld)
            {
                pendingWorldEvents.Enqueue(e);
                if (awaitingInitialMap && e.Kind == RaidEventKind.RaidStarted)
                {
                    awaitingInitialMap = false;
                    renderedTraining = e.TrainingMap;
                    PortCollisionWorld.Select(renderedTraining, transform);
                    BeginMapLoad();
                }
                return;
            }
            switch (e.Kind)
            {
                case RaidEventKind.Gear:
                    ReceiveGear(e);
                    break;
                case RaidEventKind.PouchLevel:
                    basePouchLevel = e.PouchLevel;
                    break;
                case RaidEventKind.MapVote:
                    mapVotes = e.MapVotes;
                    mapPlayers = e.MapPlayers;
                    votedForMap = e.Voted;
                    break;
                case RaidEventKind.RaidStarted:
                    if (renderedTraining != e.TrainingMap) ChangeMap(e.TrainingMap);
                    StartRound(e.Round);
                    break;
                case RaidEventKind.Mechanism:
                    if (WorldMechanisms.TryGet(e.MechanismId, out var passage))
                        passage.SetMotion(e.FromSide, e.TargetSide, e.StartedAt);
                    break;
                case RaidEventKind.ContainerLid:
                    scenery?.OpenContainerLid(e.ContainerId, e.StartedAt);
                    break;
                case RaidEventKind.ScaleLoad:
                    ScaleKilograms = e.ScaleKilograms;
                    break;
                case RaidEventKind.RaidQueue:
                    if (e.Round == round && extracted) { queued = e.Queued; changingQueue = false; }
                    break;
                case RaidEventKind.Pose:
                    if (e.Actor == Manager.LocalClientId)
                    {
                        OwnPosition = e.Position;
                        OwnYaw = e.Yaw;
                        OwnMotion = e.Motion;
                        ownTrack.Push(e, !HasOwnPose);
                        if (!HasOwnPose && view != null)
                        {
                            yaw = e.Yaw;
                            pitch = TrainingGround.Active ? 7f : OwnPosition.y < 0f ? 2f : 24f;
                            displayPosition = OwnPosition;
                            ownModel = scenery.InstantiatePlayer(e.Actor, new Vector3(e.Position.x, e.Motion.Height, e.Position.y), yaw);
                            ownGait = ownModel.GetComponent<FreightRunnerGait>();
                            UpdateCamera(true);
                            if (captureOnSpawn && !loadingMap && !ScriptedInput && Application.isFocused) CaptureCursor();
                        }
                        if (!loadingMap) captureOnSpawn = false;
                        HasOwnPose = true;
                    }
                    else if (scenery != null && !extracted && !disconnected)
                    {
                        Vector3 target = new Vector3(e.Position.x, e.Motion.Height, e.Position.y);
                        bool spawned = !runners.TryGetValue(e.Actor, out Runner runner);
                        if (spawned)
                        {
                            Transform model = scenery.InstantiatePlayer(e.Actor, target, e.Yaw);
                            runner = new Runner { Model = model, Gait = model.GetComponent<FreightRunnerGait>() };
                            model.gameObject.SetActive(true);
                            ObserveRunnerStart(runner, target, e.Yaw);
                            runners.Add(e.Actor, runner);
                        }
                        runner.Track.Push(e, spawned);
                    }
                    break;
                case RaidEventKind.Left:
                    if (runners.TryGetValue(e.Actor, out Runner departed))
                    {
                        departed.Model.gameObject.SetActive(false);
                        ObserveRunnerHide(departed);
                        Destroy(departed.Model.gameObject);
                        runners.Remove(e.Actor);
                    }
                    break;
                case RaidEventKind.Crate:
                    openRequestUntil = 0f;
                    if (closeRequested) break;
                    if (screen != RaidScreen.Play)
                    {
                        closeRequested = true;
                        Link.Close();
                        break;
                    }
                    if (!crateOpen)
                    {
                        hud?.ResetSelection();
                        if (!ScriptedInput && view != null && Application.isFocused)
                            ReleaseCursor();
                    }
                    crateOpen = true;
                    inventoryOpen = true;
                    searchComplete = e.SearchComplete;
                    searchFinishAt = searchComplete ? 0f : Time.unscaledTime + e.SearchRemaining;
                    inventory.Snapshot(false, e.Items, e.Unknown);
                    hud?.TransferSnapshot(false);
                    break;
                case RaidEventKind.Bag:
                    inventory.Snapshot(true, e.Items);
                    hud?.TransferSnapshot(true);
                    break;
                case RaidEventKind.ExitProgress:
                    exitSeconds = e.Seconds;
                    break;
                case RaidEventKind.Result:
                    CloseMenuViews();
                    gearMoves.Reset();
                    extracted = true;
                    ScaleKilograms = 0;
                    screen = RaidScreen.Receipt;
                    bagFromMenu = queued = changingQueue = false;
                    extractedItems = e.Items;
                    HideInventory(false);
                    ClearOtherRunners();
                    ReleaseCursor();
                    break;
                case RaidEventKind.Status:
                    GearReply(e);
                    inventory.Acknowledge(e.Operation, e.Status);
                    hud?.TransferReply(e.Operation, e.Status);
                    HandleStatus(e.Status);
                    break;
            }
        }

        private void HandleStatus(RaidStatus status)
        {
            if (status == RaidStatus.Opened) openRequestUntil = Time.unscaledTime + RaidContainers.LidSeconds + 2f;
            if (status == RaidStatus.Blocked || status == RaidStatus.OutOfRange || status == RaidStatus.Closed || status == RaidStatus.CrateLost)
                openRequestUntil = 0f;
            switch (status)
            {
                case RaidStatus.Closed:
                    if (!closeRequested) HideInventory(lockAfterClose);
                    closeRequested = false;
                    break;
                case RaidStatus.CrateLost:
                    if (crateOpen && inventoryOpen)
                    {
                        crateOpen = searchComplete = false;
                        searchFinishAt = 0f;
                        inventory.Close(false);
                        hud?.ResetSelection();
                    }
                    break;
            }
        }

        private void OnDisconnected(ulong id)
        {
            CloseMenuViews();
            disconnected = true;
            loadingMap = false;
            pendingWorldEvents.Clear();
            ScaleKilograms = 0;
            ClearGear();
            HideInventory(false);
            ClearOtherRunners();
            ReleaseCursor();
        }

        private void StartRound(uint nextRound)
        {
            if (nextRound == round) return;
            CloseMenuViews();
            captureOnSpawn = round != 0 || TrainingGround.Active;
            round = nextRound;
            mapVotes = 0;
            votedForMap = false;
            scenery?.ResetContainerLids();
            screen = RaidScreen.Play;
            bagFromMenu = closeRequested = queued = changingQueue = extracted = false;
            HasOwnPose = false;
            ScaleKilograms = 0;
            OwnMotion = default;
            displayHeight = displayCrouch = 0f;
            jumpPending = false;
            lastProbeDirection = Vector2.zero;
            probeButtons = MoveButtons.None;
            exitSeconds = nextSend = 0f;
            ClearOtherRunners();
            if (ownModel != null)
            {
                ownModel.gameObject.SetActive(false);
                Destroy(ownModel.gameObject);
                ownModel = null;
            }
            HideInventory(false);
            inventory.Close(true);
            ClearGear();
            inventory.Snapshot(true, Array.Empty<LootItem>());
            extractedItems = Array.Empty<LootItem>();
        }

        private void ClearOtherRunners()
        {
            foreach (Runner runner in runners.Values)
            {
                runner.Model.gameObject.SetActive(false);
                Destroy(runner.Model.gameObject);
            }
            runners.Clear();
        }

        private void Update()
        {
            if (waitingForWorld) return;
            scenery?.TickLighting(Time.deltaTime);
            scenery?.TickContainerLids();
            scenery?.TickStreaming(HasOwnPose ? OwnPosition : RaidMap.Spawn(0));
            if (loadingMap)
            {
                if (!HasOwnPose || scenery == null || !scenery.StreamingSettled) return;
                // Render the complete spawn view once behind the overlay to upload visible geometry and materials.
                if (readyFrame < 0) { readyFrame = Time.frameCount; return; }
                if (Time.frameCount == readyFrame) return;
                loadingMap = false;
                if (captureOnSpawn && !ScriptedInput && Application.isFocused && screen == RaidScreen.Play) CaptureCursor();
                captureOnSpawn = false;
            }
            if (Link == null || !HasOwnPose || disconnected)
                return;
            WeighPlatform();
            UpdateViewport();
            shoulderPhase = RaidCameraBoom.AdvanceShoulder(shoulderPhase, CameraMode, Time.unscaledDeltaTime);
            if (!extracted && (ScriptedInput || InPauseMenu && !settingsLook) &&
                (pendingProbeView || (Time.unscaledTime >= nextSend &&
                lastShoulderSample != RaidCameraBoom.SampleShoulder(shoulderPhase, CameraMode))))
            {
                pendingProbeView = false;
                bool moving = ScriptedInput && !InPauseMenu;
                SendMovement(moving ? lastProbeDirection : Vector2.zero, moving ? probeButtons : MoveButtons.None);
            }
            if (extracted || InPauseMenu)
            {
                if (!ScriptedInput && Application.isFocused)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) EscapePressed();
                    else
                    {
                        int step = Input.GetKeyDown(KeyCode.DownArrow) ? 1 : Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 0;
                        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                        if (screen == RaidScreen.Animations)
                        {
                            int side = Input.GetKeyDown(KeyCode.RightArrow) ? 1 : Input.GetKeyDown(KeyCode.LeftArrow) ? -1 : 0;
                            hud.AnimationPreviewKey(step, side, enter || Input.GetKeyDown(KeyCode.Space));
                        }
                        else if (ViewingSettings)
                            UpdateSettingsLook();
                        else if (enter || step != 0) hud.MenuKey(InventoryState(), step, enter);
                    }
                }
                if (extracted) return;
                if (screen == RaidScreen.Animations) hud.UpdateAnimationPreview();
            }
            else if (!ScriptedInput && view != null)
                ReadControls();
            if (crateOpen && inventoryOpen && (ScriptedInput || Application.isFocused))
                hud.PumpTransfers(InventoryState(), Time.unscaledTime);
            if (view == null)
                return;
            UpdateCamera(false);
            foreach (Runner runner in runners.Values)
            {
                RenderPose pose = runner.Track.Sample();
                runner.Model.position = new Vector3(pose.Position.x, pose.Motion.Height, pose.Position.y);
                runner.Gait.SetLookHeading(pose.Yaw);
                runner.Gait.SetLocomotion(pose.Motion.Crouch, pose.Motion.Sprinting,
                    pose.Motion.Airborne, pose.Motion.VerticalSpeed, pose.Motion.Sliding);
            }
        }

        private void WeighPlatform()
        {
            var scale = scenery?.FloorScale;
            if (!scale || !scale.isActiveAndEnabled) return;
            scale.WeighLoad(ScaleKilograms, Time.deltaTime);
        }

        private Vector3 EyePosition() => RaidCameraBoom.Eye(displayPosition) +
            Vector3.up * (displayHeight - displayCrouch * RaidLocomotion.CrouchEyeDrop);

        internal void TogglePerspective()
        {
            IsThirdPerson = !IsThirdPerson;
            RefreshViewMode();
        }

        internal void ToggleShoulder()
        {
            if (!IsThirdPerson) return;
            IsLeftShoulder = !IsLeftShoulder;
            // Report the requested side immediately; rendering continues from the
            // existing phase, including when X is pressed halfway across the rig.
            nextSend = 0f;
            pendingProbeView = ScriptedInput;
        }

        private void RefreshViewMode()
        {
            nextSend = 0f;
            pendingProbeView = ScriptedInput;
            if (view != null && HasOwnPose)
                UpdateCamera(true);
        }

        private void UpdateCamera(bool snap)
        {
            // Slide the visual anchor just like the authoritative character. Straight
            // interpolation around the end of the partition could cut across its corner.
            RenderPose pose = ownTrack.Sample();
            Vector2 expected = pose.Position;
            // A step off a ledge moves horizontally before gravity lowers the soles.
            float travelFeet = Mathf.Max(displayHeight, pose.Motion.Height);
            displayHeight = pose.Motion.Height;
            displayCrouch = pose.Motion.Crouch;
            displayPosition = pose.Motion.Airborne ? expected : RaidMap.Move(displayPosition, expected - displayPosition,
                travelFeet + RaidMap.StandingHeight - displayCrouch * RaidLocomotion.CrouchEyeDrop, travelFeet);
            // After a prolonged stall recover a separated camera anchor. For ordinary
            // cornering keep sliding along the wall until the sampled route clears it.
            if ((displayPosition - expected).sqrMagnitude > 1f) displayPosition = OwnPosition;
            Quaternion look = Quaternion.Euler(pitch, yaw, 0);
            Vector3 camera = RaidCameraBoom.Position(displayPosition, yaw, pitch, CameraMode, viewAspect,
                shoulderPhase, ref boomLength, Time.unscaledDeltaTime, snap,
                displayHeight - displayCrouch * RaidLocomotion.CrouchEyeDrop);
            view.transform.SetPositionAndRotation(camera, look);
            ownModel.position = new Vector3(displayPosition.x, displayHeight, displayPosition.y);
            scenery.BendGroundcover(ownModel.position);
            ownGait.SetLookHeading(ScriptedInput ? pose.Yaw : yaw, snap);
            ownGait.SetLocomotion(displayCrouch, pose.Motion.Sprinting, pose.Motion.Airborne,
                pose.Motion.VerticalSpeed, pose.Motion.Sliding);
            bool showModel = IsThirdPerson && boomLength > 0.55f;
            if (ownModel.gameObject.activeSelf != showModel) ownModel.gameObject.SetActive(showModel);
        }

        internal void ToggleInventory()
        {
            if (disconnected || extracted || screen != RaidScreen.Play) return;
            if (inventoryOpen)
                CloseInventory(true);
            else
            {
                inventoryOpen = true;
                hud?.ResetSelection();
                if (!ScriptedInput && view != null && Application.isFocused)
                    ReleaseCursor();
            }
        }

        private void CloseInventory(bool returnToGame)
        {
            lockAfterClose = returnToGame;
            if ((crateOpen || OpeningContainer) && !closeRequested) { closeRequested = true; Link.Close(); }
            bool back = bagFromMenu;
            bagFromMenu = false;
            if (back) screen = RaidScreen.Menu;
            HideInventory(returnToGame && !back);
        }

        private void HideInventory(bool returnToGame)
        {
            crateOpen = inventoryOpen = searchComplete = inventoryLooking = false;
            openRequestUntil = 0f;
            searchFinishAt = 0f;
            inventory.Close(disconnected || extracted);
            hud?.ResetSelection();
            if (returnToGame && screen == RaidScreen.Play && !ScriptedInput && view != null && Application.isFocused)
                CaptureCursor();
        }

        private static void CaptureCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private uint RequestTransfer(bool fromBag, uint itemId)
        {
            if (gearMoves.IsPending(itemId)) return 0;
            if (!crateOpen || !inventoryOpen || extracted || disconnected)
                return 0;
            return fromBag ? Link.Store(itemId) : Link.Take(itemId);
        }

        private void RequestPlacement(LootItem item, bool fromBag, bool toBag, int x, int y, bool rotated)
        {
            if (gearMoves.IsPending(item.Id)) return;
            if (inventoryOpen && !disconnected && !extracted && (crateOpen || (fromBag && toBag)))
            {
                uint operation = Link.Place(item, fromBag, toBag, x, y, rotated);
                inventory.Place(operation, item, fromBag, toBag, x, y, rotated);
            }
        }

        private void ReadControls()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool usingInventory = inventoryOpen || OpeningContainer;
            if (Application.isFocused)
            {
                ReadGearKeys();
                if (Input.GetKeyDown(KeyCode.X)) ToggleShoulder();
                if (Input.GetKeyDown(KeyCode.V)) TogglePerspective();
                if (Input.GetKeyDown(KeyCode.Escape))
                    EscapePressed();
                else if (Input.GetKeyDown(KeyCode.Tab))
                    ToggleInventory();
                else if (inventoryOpen)
                {
                    if (Input.GetMouseButtonDown(1))
                        inventoryLooking = !hud.IsHoldingItem && !hud.PointerOverInventory(InventoryState());
                    if (!Input.GetMouseButton(1)) inventoryLooking = false;
                    if (inventoryLooking) RunnerInput.Look(ref yaw, ref pitch);
                    if (crateOpen)
                        hud.PhysicalTransferInput(InventoryState(), Input.GetKeyDown(KeyCode.F), Input.GetKey(KeyCode.F));
                    if (Input.GetKeyDown(KeyCode.R))
                        hud.RotateHeld();
                    else if (Input.GetKeyDown(KeyCode.E))
                    {
                        if (crateOpen) CloseInventory(true);
                        else if (!closeRequested && RaidContainers.Closest(OwnPosition,OwnMotion,out byte target)) RequestOpen(target);
                    }
                    else if (crateOpen && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
                        hud.TransferSelection(InventoryState());
                    else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow))
                        hud.StepSelection(inventory.Crate, inventory.Bag, Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 1);
                    else if (crateOpen && (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.RightArrow)))
                        hud.SelectColumn(inventory.Crate, inventory.Bag, Input.GetKeyDown(KeyCode.RightArrow));
                }
                else
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        if (view != null)
                        {
                            var ray = Cursor.lockState == CursorLockMode.Locked
                                ? view.ViewportPointToRay(new Vector3(.5f,.5f)) : view.ScreenPointToRay(Input.mousePosition);
                            if (RaidContainers.UnderPointer(ray,OwnPosition,OwnMotion,out byte target)) RequestOpen(target);
                        }
                        CaptureCursor();
                    }
                    if (Cursor.lockState == CursorLockMode.Locked)
                    {
                        RunnerInput.Look(ref yaw, ref pitch);
                        if (Input.GetKeyDown(KeyCode.E)) Interact();
                    }
                }
            }
            bool control = Application.isFocused && screen == RaidScreen.Play &&
                ((Cursor.lockState == CursorLockMode.Locked) || inventoryOpen);
            if (!control) jumpPending = false;
            else if (Input.GetKeyDown(KeyCode.Space)) jumpPending = true;
            inventoryControlHeld = RunnerInput.ConsumeInventoryControl(ctrl, usingInventory || inventoryOpen || OpeningContainer, inventoryControlHeld);
            bool crouchPressed = control && !inventoryControlHeld && (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl));
            if (crouchPressed || jumpPending || Time.unscaledTime >= nextSend)
            {
                Vector2 direction = MovementFromKeys(RunnerInput.Keys(), Application.isFocused, Cursor.lockState == CursorLockMode.Locked);
                MoveButtons buttons = control ? RunnerInput.Buttons(jumpPending) : MoveButtons.None;
                if (inventoryControlHeld) buttons &= ~MoveButtons.Crouch;
                SendMovement(direction, buttons);
                jumpPending = false;
            }
        }

        private void RequestOpen(byte target)
        {
            // Open requests can be throttled before the server starts the lid animation.
            openRequestUntil = Time.unscaledTime + RaidContainers.LidSeconds + 2f;
            Link.Open(target);
        }

        internal void Interact()
        {
            if (!HasOwnPose || extracted || disconnected || screen != RaidScreen.Play) return;
            var passage = WorldMechanisms.Closest(OwnPosition, OwnMotion);
            if (passage != null) Link.SetMechanism(passage.Id, !passage.IsOpen);
            else if (RaidContainers.Closest(OwnPosition, OwnMotion, out byte target)) RequestOpen(target);
            else Link.Open();
        }

        internal Vector2 MovementFromKeys(Vector2 keys, bool focused, bool cursorLocked)
        {
            if (!focused || !HasOwnPose || extracted || disconnected || screen != RaidScreen.Play ||
                (!cursorLocked && !inventoryOpen)) return Vector2.zero;
            return RunnerInput.WorldDirection(keys, yaw);
        }

        private void OnGUI()
        {
            if (hud == null || view == null)
                return;
            hud.Draw(InventoryState());
        }

        private RaidHud.State InventoryState()
        {
            return WithGear(new RaidHud.State
            {
                Connected = HasOwnPose,
                Disconnected = disconnected,
                Screen = screen, Queued = queued, ChangingQueue = changingQueue,
                LoadingMap = loadingMap, VotedForMap = votedForMap, MapVotes = mapVotes, MapPlayers = mapPlayers,
                BasePouchLevel = basePouchLevel,
                ExtractedItems = extractedItems,
                BagItems = inventory.Bag,
                Crate = crateOpen,
                Inventory = inventoryOpen,
                SearchFinishAt = searchFinishAt,
                WorldLook = inventoryLooking,
                Unknown = inventory.Unknown,
                CrateItems = inventory.Crate,
                Position = OwnPosition,
                Motion = OwnMotion,
                Seconds = exitSeconds
            });
        }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
        internal bool ProbeCrateOpen => crateOpen;
        internal uint ProbeSelectedItem => hud.ProbeSelectedItem(InventoryState());
        internal void ProbeSelectOtherCrateItem(uint skip)
        {
            if (!ScriptedInput || !crateOpen) return;
            hud.SelectColumn(inventory.Crate, inventory.Bag, false);
            if (ProbeSelectedItem == skip) hud.StepSelection(inventory.Crate, inventory.Bag, 1);
        }
        internal uint ProbeHoveredItem => hud.ProbeHoveredItem(InventoryState());
        internal void ProbeHover(bool bag, float column, float row)
        {
            if (ScriptedInput && inventoryOpen) hud.ProbeHover(InventoryState(), bag, column, row);
        }
        internal void ProbeTransferKey(bool down)
        {
            if (ScriptedInput && inventoryOpen) hud.ProbeTransferKey(InventoryState(), down);
        }
        internal bool ProbeCellInput(bool bag, int x, int y, EventType type, bool ctrl = false) =>
            ProbePointer(bag, x + 0.5f, y + 0.5f, type, ctrl);
        internal bool ProbePointer(bool bag, float column, float row, EventType type, bool ctrl = false) =>
            ScriptedInput && inventoryOpen && hud.ProbePointer(InventoryState(), bag, column, row, type, ctrl);
        internal Vector2 ProbeGhostCenter => hud.GhostCenter;
        internal bool ProbeTargetCell(bool bag, int x, int y, EventType type) =>
            ScriptedInput && inventoryOpen && hud.ProbeTargetCell(InventoryState(), bag, x, y, type);
        internal bool ProbeValidTarget(bool bag, int x, int y) =>
            ScriptedInput && inventoryOpen && hud.CanDrop(InventoryState(), bag, x, y);
        internal bool ProbeVisiblePlacement(uint id, bool bag, int x, int y, bool rotated)
        {
            if (!ScriptedInput || !inventoryOpen || (!bag && !crateOpen)) return false;
            var state = InventoryState();
            int matches = 0;
            bool atTarget = false;
            foreach (var item in state.BagItems)
                if (item.Id == id) { matches++; atTarget |= bag && item.X == x && item.Y == y && item.Rotated == rotated; }
            foreach (var item in state.CrateItems)
                if (item.Id == id) { matches++; atTarget |= !bag && item.X == x && item.Y == y && item.Rotated == rotated; }
            return matches == 1 && atTarget;
        }
        internal bool ProbePendingPlacement(uint id) => inventory.IsPending(id);
        internal bool ProbeRotateHeld() => ScriptedInput && hud.RotateHeld();
        internal void ProbeCancelDrag() { if (ScriptedInput) hud.CancelDrag(); }
#endif

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && view != null)
            {
                inventoryLooking = false;
                StopSettingsLook();
                hud?.CloseLightingControls();
                if (screen == RaidScreen.Graphics) hud?.CloseGraphicsControls();
                if (!ScriptedInput)
                {
                    hud?.CancelDrag();
                    hud?.CancelQuickTransfers();
                }
                ReleaseCursor();
            }
        }

        private static void ReleaseCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            CloseMenuViews();
            scenery?.Dispose();
            if (Manager != null)
                Manager.OnClientDisconnectCallback -= OnDisconnected;
            if (Link != null)
            {
                Link.Received -= Receive;
                Link.Dispose();
            }
        }
    }
}
