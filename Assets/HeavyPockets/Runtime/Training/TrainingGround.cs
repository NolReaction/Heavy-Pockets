using UnityEngine;

namespace HeavyPockets
{
    // An isolated scene with the same metre scale, runner motor and interactables as Depot.
    internal sealed class TrainingGround : MonoBehaviour
    {
        internal const string ScenePath = "Assets/HeavyPockets/TrainingGround.unity";
        internal static bool Active { get; private set; }

        private RaidScenery scenery;
        private FreightRunnerGait gait;
        private Camera view;
        private Vector2 position = new Vector2(1f, -28f);
        private Vector2 velocity;
        private RaidLocomotion motion;
        private MoveButtons previous;
        private float yaw = -8f, pitch = 7f, boom;
        private bool nearControl;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetScene() => Active = false;

        internal static void Launch()
        {
            Active = true;
            WorldMechanisms.Reset();
            var root = new GameObject("Учебный полигон");
            PortCollisionWorld.Create(root.transform);
            root.AddComponent<TrainingGround>().Begin();
        }

        private void Begin()
        {
            scenery = new RaidScenery(transform);
            Transform runner = scenery.InstantiatePlayer(0, new Vector3(position.x, 0, position.y), yaw);
            runner.gameObject.SetActive(true);
            gait = runner.GetComponent<FreightRunnerGait>();
            view = new GameObject("Training camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            view.transform.SetParent(transform, false);
            view.tag = "MainCamera";
            view.nearClipPlane = RaidCameraBoom.NearClip;
            view.farClipPlane = RaidCameraBoom.FarClip;
            view.fieldOfView = RaidCameraBoom.VerticalFov;
            UpdateCamera(true);
            Capture(true);
        }

        private static void Capture(bool capture)
        {
            Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capture;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Capture(false);
            if (Input.GetMouseButtonDown(0) && Application.isFocused) Capture(true);
            bool control = Application.isFocused && Cursor.lockState == CursorLockMode.Locked;
            Vector2 direction = Vector2.zero;
            MoveButtons buttons = MoveButtons.None;
            if (control)
            {
                RunnerInput.Look(ref yaw, ref pitch);
                direction = RunnerInput.WorldDirection(RunnerInput.Keys(), yaw);
                buttons = RunnerInput.Buttons(Input.GetKeyDown(KeyCode.Space));
                if (Input.GetKeyDown(KeyCode.E)) WorldMechanisms.TryUseLocal(position, motion);
            }

            if (buttons != previous) motion.Press(position, velocity, yaw, previous, buttons);
            previous = buttons;
            float remaining = Mathf.Min(.15f, Time.deltaTime);
            while (remaining > 0f)
            {
                float step = Mathf.Min(remaining, 1f / 60f);
                motion.Step(ref position, ref velocity, direction, yaw, buttons, step);
                remaining -= step;
            }

            gait.transform.position = new Vector3(position.x, motion.Height, position.y);
            gait.SetLookHeading(yaw);
            gait.SetLocomotion(motion.Crouch, motion.Sprinting, motion.Airborne, motion.VerticalSpeed, motion.Sliding);
            scenery.TrainingSurfaces.BendAt(gait.transform.position);
            scenery.TickLighting(Time.deltaTime);
            nearControl = control && WorldMechanisms.Closest(position, motion) != null;
            UpdateCamera(false);
        }

        private void UpdateCamera(bool snap)
        {
            float aspect = Mathf.Clamp(view.aspect, RaidCameraBoom.MinAspect, RaidCameraBoom.MaxAspect);
            Vector3 eye = RaidCameraBoom.Position(position, yaw, pitch, RaidCameraMode.RightShoulder,
                aspect, 1f, ref boom, Time.deltaTime, snap, motion.EyeOffset);
            view.transform.SetPositionAndRotation(eye, Quaternion.Euler(pitch, yaw, 0));
        }

        private void OnGUI()
        {
            if (nearControl) GUI.Box(new Rect(Screen.width * .5f - 18, Screen.height * .58f, 36, 24), "E");
        }

        private void OnDestroy()
        {
            scenery?.Dispose();
            WorldMechanisms.Reset();
            Active = false;
            Capture(false);
        }
    }
}
