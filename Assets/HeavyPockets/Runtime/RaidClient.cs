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
                    Crouch = Mathf.Lerp(from.Motion.Crouch, to.Motion.Crouch, t),
                    Sprinting = t < .5f ? from.Motion.Sprinting : to.Motion.Sprinting
                };
                if (from.Motion.Airborne || to.Motion.Airborne)
                    SampleJump(from.Motion, to.Motion, duration, t, ref motion);
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
                if (from.Airborne && to.Airborne && from.VerticalSpeed >= to.VerticalSpeed)
                {
                    // The velocity change gives the elapsed physics time independently of delivery jitter.
                    float flightTime = (from.VerticalSpeed - to.VerticalSpeed) / gravity;
                    motion.Height = Mathf.Lerp(from.Height, to.Height, t) + .5f * gravity * flightTime * flightTime * t * (1f - t);
                    motion.VerticalSpeed = Mathf.Lerp(from.VerticalSpeed, to.VerticalSpeed, t);
                    return;
                }

                // Land and take off at the floor, including when a second jump starts between packets.
                float untilLand = from.Airborne ? TimeToFloor(from.Height, from.VerticalSpeed) : 0f;
                float sinceLift = to.Airborne ? TimeToFloor(to.Height, -to.VerticalSpeed) : 0f;
                duration = Mathf.Max(duration, untilLand + sinceLift);
                bool landing = t * duration < untilLand;
                float elapsed = landing ? t * duration : (t - 1f) * duration;
                if (!landing && -elapsed >= sinceLift) return;
                var sample = landing ? from : to;
                motion.Height = Mathf.Max(0f, sample.Height + (sample.VerticalSpeed - .5f * gravity * elapsed) * elapsed);
                motion.VerticalSpeed = sample.VerticalSpeed - gravity * elapsed;
            }

            private static float TimeToFloor(float height, float velocity) =>
                (velocity + Mathf.Sqrt(velocity * velocity + 2f * RaidLocomotion.Gravity * height)) / RaidLocomotion.Gravity;
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
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            var stage = new GameObject("Freight bay");
            stage.transform.SetParent(transform, false);
            scenery = new RaidScenery(stage.transform);
            var eye = new GameObject("Runner camera");
            eye.transform.SetParent(transform, false);
            view = eye.AddComponent<Camera>();
            view.tag = "MainCamera";
            view.fieldOfView = RaidCameraBoom.VerticalFov;
            view.nearClipPlane = RaidCameraBoom.NearClip;
            view.farClipPlane = RaidCameraBoom.FarClip;
            UpdateViewport();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color32(157, 181, 178, 255);
            eye.AddComponent<AudioListener>();
            eye.transform.position = new Vector3(0, 1.58f, -1f);
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
            switch (e.Kind)
            {
                case RaidEventKind.Gear:
                    ReceiveGear(e);
                    break;
                case RaidEventKind.PouchLevel:
                    basePouchLevel = e.PouchLevel;
                    break;
                case RaidEventKind.RaidStarted:
                    StartRound(e.Round);
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
                            yaw = OwnPosition.y < 0f ? 76f : -109f;
                            pitch = OwnPosition.y < 0f ? 2f : 24f;
                            displayPosition = OwnPosition;
                            ownModel = scenery.InstantiatePlayer(e.Actor, new Vector3(e.Position.x, 0, e.Position.y), yaw);
                            ownGait = ownModel.GetComponent<FreightRunnerGait>();
                            UpdateCamera(true);
                            if (captureOnSpawn && !ScriptedInput && Application.isFocused) CaptureCursor();
                        }
                        captureOnSpawn = false;
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
                    gearMoves.Reset();
                    extracted = true;
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
            disconnected = true;
            ClearGear();
            HideInventory(false);
            ClearOtherRunners();
            ReleaseCursor();
        }

        private void StartRound(uint nextRound)
        {
            if (nextRound == round) return;
            captureOnSpawn = round != 0;
            round = nextRound;
            screen = RaidScreen.Play;
            bagFromMenu = closeRequested = queued = changingQueue = extracted = false;
            HasOwnPose = false;
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
            if (Link == null || !HasOwnPose || disconnected)
                return;
            UpdateViewport();
            shoulderPhase = RaidCameraBoom.AdvanceShoulder(shoulderPhase, CameraMode, Time.unscaledDeltaTime);
            if (!extracted && (ScriptedInput || screen == RaidScreen.Menu) &&
                (pendingProbeView || (Time.unscaledTime >= nextSend &&
                lastShoulderSample != RaidCameraBoom.SampleShoulder(shoulderPhase, CameraMode))))
            {
                pendingProbeView = false;
                bool moving = ScriptedInput && screen != RaidScreen.Menu;
                SendMovement(moving ? lastProbeDirection : Vector2.zero, moving ? probeButtons : MoveButtons.None);
            }
            if (extracted || screen == RaidScreen.Menu)
            {
                if (!ScriptedInput && Application.isFocused)
                {
                    if (Input.GetKeyDown(KeyCode.Escape)) EscapePressed();
                    else
                    {
                        int step = Input.GetKeyDown(KeyCode.DownArrow) ? 1 : Input.GetKeyDown(KeyCode.UpArrow) ? -1 : 0;
                        bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
                        if (enter || step != 0) hud.MenuKey(InventoryState(), step, enter);
                    }
                }
                if (extracted) return;
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
                    pose.Motion.Airborne, pose.Motion.VerticalSpeed);
            }
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
            displayHeight = pose.Motion.Height;
            displayCrouch = pose.Motion.Crouch;
            displayPosition = RaidMap.Move(displayPosition, expected - displayPosition);
            // After a prolonged stall recover a separated camera anchor. For ordinary
            // cornering keep sliding along the wall until the sampled route clears it.
            if ((displayPosition - expected).sqrMagnitude > 1f) displayPosition = OwnPosition;
            Quaternion look = Quaternion.Euler(pitch, yaw, 0);
            Vector3 camera = RaidCameraBoom.Position(displayPosition, yaw, pitch, CameraMode, viewAspect,
                shoulderPhase, ref boomLength, Time.unscaledDeltaTime, snap,
                displayHeight - displayCrouch * RaidLocomotion.CrouchEyeDrop);
            view.transform.SetPositionAndRotation(camera, look);
            ownModel.position = new Vector3(displayPosition.x, displayHeight, displayPosition.y);
            ownGait.SetLookHeading(ScriptedInput ? pose.Yaw : yaw, snap);
            ownGait.SetLocomotion(displayCrouch, pose.Motion.Sprinting, pose.Motion.Airborne, pose.Motion.VerticalSpeed);
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
            if (crateOpen && !closeRequested) { closeRequested = true; Link.Close(); }
            bool back = bagFromMenu;
            bagFromMenu = false;
            if (back) screen = RaidScreen.Menu;
            HideInventory(returnToGame && !back);
        }

        private void HideInventory(bool returnToGame)
        {
            crateOpen = inventoryOpen = searchComplete = inventoryLooking = false;
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
                    if (inventoryLooking) ReadMouseLook();
                    if (crateOpen)
                        hud.PhysicalTransferInput(InventoryState(), Input.GetKeyDown(KeyCode.F), Input.GetKey(KeyCode.F));
                    if (Input.GetKeyDown(KeyCode.R))
                        hud.RotateHeld();
                    else if (Input.GetKeyDown(KeyCode.E))
                    {
                        if (crateOpen) CloseInventory(true);
                        else if (!closeRequested) Link.Open();
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
                    if (Input.GetMouseButtonDown(0)) CaptureCursor();
                    if (Cursor.lockState == CursorLockMode.Locked)
                    {
                        ReadMouseLook();
                        if (Input.GetKeyDown(KeyCode.E)) Link.Open();
                    }
                }
            }
            bool control = Application.isFocused && screen == RaidScreen.Play &&
                ((Cursor.lockState == CursorLockMode.Locked) || inventoryOpen);
            if (!control) jumpPending = false;
            else if (Input.GetKeyDown(KeyCode.Space)) jumpPending = true;
            if (Time.unscaledTime >= nextSend)
            {
                var keys = new Vector2(
                    (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
                    (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));
                Vector2 direction = MovementFromKeys(keys, Application.isFocused, Cursor.lockState == CursorLockMode.Locked);
                MoveButtons buttons = MoveButtons.None;
                if (control)
                {
                    if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) buttons |= MoveButtons.Sprint;
                    if (Input.GetKey(KeyCode.C)) buttons |= MoveButtons.Crouch;
                    if (jumpPending) buttons |= MoveButtons.Jump;
                }
                SendMovement(direction, buttons);
                jumpPending = false;
            }
        }

        private void ReadMouseLook()
        {
            yaw = Mathf.Repeat(yaw + Input.GetAxisRaw("Mouse X") * 2.1f, 360f);
            pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * 2.1f, -RaidCameraBoom.MaxPitch, RaidCameraBoom.MaxPitch);
        }

        internal Vector2 MovementFromKeys(Vector2 keys, bool focused, bool cursorLocked)
        {
            if (!focused || !HasOwnPose || extracted || disconnected || screen != RaidScreen.Play ||
                (!cursorLocked && !inventoryOpen)) return Vector2.zero;
            keys = Vector2.ClampMagnitude(keys, 1f);
            var world = Quaternion.Euler(0, yaw, 0) * new Vector3(keys.x, 0, keys.y);
            return new Vector2(world.x, world.z);
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
