using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.Sample
{
    /// <summary>Bootstraps the external Emas sample without package modifications.</summary>

    public sealed class Bootstrap : MonoBehaviour
    {
        private const string AnchorId = "sample";
        private IDisposable _carSubscription;
        private IDisposable _aircraftSubscription;
        private Anchor _anchor;
        private SdkOneCarSource _firstCarSource;
        private SimulatedCockpitFeed _cockpitFeed;
        private bool _sourceReplaced;
        private float _replacementTimer;
        private Transform _cockpitScreen;
        private CockpitMarker _cockpitMarker;
        private GameObject _ground;
        private readonly List<GameObject> _runtimeObjects = new List<GameObject>();
        private readonly List<Blueprint> _runtimeBlueprints = new List<Blueprint>();

        /// <summary>Starts the sample anchor and its sources.</summary>
        private void Start()
        {
            _cockpitFeed = new SimulatedCockpitFeed();
            ConfigureCamera();
            RegisterCarBlueprint();
            RegisterAircraftBlueprint();

            _firstCarSource = new SdkOneCarSource();
            _anchor = Realm.Default.GetOrCreateAnchor(
                AnchorId,
                _firstCarSource,
                new SimulatedAircraftSource());

            _carSubscription = Realm.Default.Query()
                .OfKind(SampleKinds.Car)
                .With<I3DPosition>()
                .With<IArticulate>()
                .OnAvailable(ghost => Realm.Default.Manifest(ghost));

            _aircraftSubscription = Realm.Default.Query()
                .OfKind(SampleKinds.Aircraft)
                .With<I3DPosition>()
                .OnAvailable(ghost => Realm.Default.Manifest(ghost));

            CreateDemoEnvironment();
            CreateCockpitDemo();
        }

        /// <summary>Updates the replacement demonstration and moving cockpit marker.</summary>
        private void Update()
        {
            _replacementTimer += Time.deltaTime;
            if (!_sourceReplaced && _replacementTimer >= 4.0f)
            {
                _anchor.ReplaceSource(_firstCarSource, new SdkTwoCarSource());
                _sourceReplaced = true;
            }

            if (_cockpitScreen != null)
            {
                _cockpitScreen.localRotation = Quaternion.Euler(
                    0.0f,
                    Mathf.Sin(Time.time * 0.35f) * 7.0f,
                    Mathf.Sin(Time.time * 0.55f) * 2.0f);
            }

            if (_cockpitMarker != null && _cockpitFeed != null)
            {
                var markerData = _cockpitFeed.ReadMarker(Time.time);
                _cockpitMarker.SetData(markerData.ScreenId, markerData.NormalizedTopLeft);
                _cockpitMarker.Apply(ResolveScreen);
            }
        }

        /// <summary>Stops sample subscriptions, anchors and generated objects.</summary>
        private void OnDestroy()
        {
            if (_carSubscription != null)
            {
                _carSubscription.Dispose();
                _carSubscription = null;
            }

            if (_aircraftSubscription != null)
            {
                _aircraftSubscription.Dispose();
                _aircraftSubscription = null;
            }

            Realm.Default.RemoveAnchor(AnchorId);
            if (_cockpitScreen != null)
            {
                Destroy(_cockpitScreen.gameObject);
            }

            if (_cockpitMarker != null)
            {
                Destroy(_cockpitMarker.gameObject);
            }

            if (_ground != null)
            {
                Destroy(_ground);
            }

            for (var index = 0; index < _runtimeBlueprints.Count; index++)
            {
                if (_runtimeBlueprints[index] != null)
                {
                    Destroy(_runtimeBlueprints[index]);
                }
            }

            for (var index = 0; index < _runtimeObjects.Count; index++)
            {
                if (_runtimeObjects[index] != null)
                {
                    Destroy(_runtimeObjects[index]);
                }
            }
        }

        private void OnGUI()
        {
            var cars = Realm.Default.Query().OfKind(SampleKinds.Car).Count;
            var aircraft = Realm.Default.Query().OfKind(SampleKinds.Aircraft).Count;
            GUI.color = Color.white;
            GUI.Label(
                new Rect(16.0f, 16.0f, 900.0f, 28.0f),
                "Emas sample: moving ghosts, source replacement and optional views");
            GUI.Label(
                new Rect(16.0f, 44.0f, 900.0f, 24.0f),
                "Cars: " + cars + "    Aircraft: " + aircraft + "    Car source: " +
                (_sourceReplaced ? "SDK Two (replaced)" : "SDK One"));
            GUI.Label(
                new Rect(16.0f, 68.0f, 900.0f, 24.0f),
                "Ghost roots update even without views; cars loop around the road and aircraft fly above it.");
            GUI.Label(
                new Rect(16.0f, 92.0f, 900.0f, 24.0f),
                "The marker follows moving screen XY data and the screen transform; its view is independent of the ghosts.");
        }

        private Transform ResolveScreen(string screenId)
        {
            return string.Equals(screenId, "screen", StringComparison.Ordinal) ? _cockpitScreen : null;
        }

        private void ConfigureCamera()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            camera.transform.position = new Vector3(0.0f, 6.0f, -16.0f);
            camera.transform.LookAt(new Vector3(0.0f, 1.7f, 0.5f));
            camera.fieldOfView = 55.0f;
        }

        private void CreateDemoEnvironment()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.name = "Emas Demo Ground";
            _ground.transform.position = new Vector3(0.0f, -0.05f, 0.0f);
            _ground.transform.localScale = new Vector3(2.4f, 1.0f, 2.4f);
            SetColor(_ground, new Color(0.12f, 0.15f, 0.18f));

            CreateEnvironmentCube(
                "Vehicle Oval Road",
                new Vector3(0.0f, 0.02f, 0.0f),
                new Vector3(16.0f, 0.08f, 6.0f),
                new Color(0.25f, 0.27f, 0.30f));
            CreateEnvironmentCube(
                "Road Centre Marking",
                new Vector3(0.0f, 0.075f, 0.0f),
                new Vector3(14.0f, 0.02f, 0.08f),
                new Color(0.95f, 0.72f, 0.12f));
            CreateEnvironmentCube(
                "Aircraft Runway",
                new Vector3(0.0f, 0.025f, 5.3f),
                new Vector3(12.0f, 0.06f, 2.0f),
                new Color(0.20f, 0.22f, 0.24f));
            CreateEnvironmentCube(
                "Runway Centre Marking",
                new Vector3(0.0f, 0.08f, 5.3f),
                new Vector3(0.18f, 0.02f, 1.5f),
                Color.white);
        }

        private void CreateCockpitDemo()
        {
            var screenObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
            screenObject.name = "Cockpit Screen";
            screenObject.transform.position = new Vector3(0.0f, 2.6f, 5.0f);
            screenObject.transform.localScale = new Vector3(4.0f, 2.0f, 1.0f);
            SetColor(screenObject, new Color(0.03f, 0.13f, 0.18f));
            _cockpitScreen = screenObject.transform;

            var markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = "Cockpit Marker";
            markerObject.transform.localScale = new Vector3(0.20f, 0.20f, 0.20f);
            SetColor(markerObject, new Color(1.0f, 0.75f, 0.05f));
            _cockpitMarker = markerObject.AddComponent<CockpitMarker>();
            _cockpitMarker.SetData("screen", new Vector2(0.5f, 0.5f));
        }

        private void RegisterCarBlueprint()
        {
            var ghostTemplate = new GameObject("Sample Car Ghost Template");
            ghostTemplate.SetActive(false);
            ghostTemplate.AddComponent<CarGhost>();
            ghostTemplate.AddComponent<ApplyPosition>();
            _runtimeObjects.Add(ghostTemplate);

            var smallCar = CreateCarViewTemplate(
                "SmallCar.prefab",
                new Color(0.15f, 0.55f, 1.0f),
                new Vector3(1.25f, 0.35f, 0.70f));
            var largeCar = CreateCarViewTemplate(
                "LargeCar.prefab",
                new Color(0.20f, 0.85f, 0.35f),
                new Vector3(1.55f, 0.42f, 0.82f));
            var truck = CreateCarViewTemplate(
                "Truck.prefab",
                new Color(0.95f, 0.25f, 0.15f),
                new Vector3(1.90f, 0.55f, 0.95f));
            var unknown = CreateCarViewTemplate(
                "UnknownVehicle.prefab",
                new Color(0.85f, 0.20f, 0.85f),
                new Vector3(1.45f, 0.44f, 0.82f));

            var blueprint = ScriptableObject.CreateInstance<Blueprint>();
            blueprint.Configure(
                SampleKinds.Car,
                ghostTemplate.GetComponent<CarGhost>(),
                new[]
                {
                    new Blueprint.ViewMapping(variant: CarVariants.SmallCar, detailLevel: DetailLevel.Full, prefab: smallCar),
                    new Blueprint.ViewMapping(variant: CarVariants.LargeCar, detailLevel: DetailLevel.Full, prefab: largeCar),
                    new Blueprint.ViewMapping(variant: CarVariants.Truck, detailLevel: DetailLevel.Full, prefab: truck)
                },
                unknown);
            _runtimeBlueprints.Add(blueprint);
            Realm.Default.RegisterBlueprint(blueprint);
        }

        private void RegisterAircraftBlueprint()
        {
            var ghostTemplate = new GameObject("Sample Aircraft Ghost Template");
            ghostTemplate.SetActive(false);
            ghostTemplate.AddComponent<AircraftGhost>();
            ghostTemplate.AddComponent<ApplyPosition>();
            _runtimeObjects.Add(ghostTemplate);

            var view = CreateAircraftViewTemplate(
                "Aircraft.prefab",
                new Color(1.0f, 0.75f, 0.05f));
            var blueprint = ScriptableObject.CreateInstance<Blueprint>();
            blueprint.Configure(
                SampleKinds.Aircraft,
                ghostTemplate.GetComponent<AircraftGhost>(),
                new[]
                {
                    new Blueprint.ViewMapping(
                        variant: AircraftVariants.Trainer,
                        detailLevel: DetailLevel.Full,
                        prefab: view)
                },
                view);
            _runtimeBlueprints.Add(blueprint);
            Realm.Default.RegisterBlueprint(blueprint);
        }

        private GameObject CreateCarViewTemplate(string name, Color color, Vector3 bodyScale)
        {
            var view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            view.name = name;
            view.transform.localScale = bodyScale;
            SetColor(view, color);
            view.AddComponent<VehicleLogic>();
            view.AddComponent<ArticulationLogic>();

            var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cabin.name = name + " Cabin";
            cabin.transform.SetParent(view.transform, false);
            cabin.transform.localPosition = new Vector3(0.0f, 0.65f, -0.05f);
            cabin.transform.localScale = new Vector3(0.52f, 0.60f, 0.58f);
            SetColor(cabin, Color.Lerp(color, Color.white, 0.35f));

            CreateWheel(view, name + " Front Left Wheel", new Vector3(-0.42f, -0.48f, 0.43f));
            CreateWheel(view, name + " Front Right Wheel", new Vector3(0.42f, -0.48f, 0.43f));
            CreateWheel(view, name + " Rear Left Wheel", new Vector3(-0.42f, -0.48f, -0.43f));
            CreateWheel(view, name + " Rear Right Wheel", new Vector3(0.42f, -0.48f, -0.43f));

            view.SetActive(false);
            _runtimeObjects.Add(view);
            return view;
        }

        private GameObject CreateAircraftViewTemplate(string name, Color color)
        {
            var view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            view.name = name;
            view.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            view.transform.localScale = new Vector3(0.45f, 0.65f, 1.35f);
            SetColor(view, color);

            var wing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wing.name = name + " Wings";
            wing.transform.SetParent(view.transform, false);
            wing.transform.localPosition = new Vector3(0.0f, 0.0f, 0.0f);
            wing.transform.localScale = new Vector3(3.0f, 0.08f, 0.38f);
            SetColor(wing, Color.Lerp(color, Color.white, 0.25f));

            var tail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tail.name = name + " Tail";
            tail.transform.SetParent(view.transform, false);
            tail.transform.localPosition = new Vector3(0.0f, 0.0f, -0.80f);
            tail.transform.localScale = new Vector3(1.1f, 0.08f, 0.28f);
            SetColor(tail, Color.Lerp(color, Color.white, 0.25f));

            view.SetActive(false);
            _runtimeObjects.Add(view);
            return view;
        }

        private void CreateWheel(GameObject parent, string name, Vector3 localPosition)
        {
            var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            wheel.name = name;
            wheel.transform.SetParent(parent.transform, false);
            wheel.transform.localPosition = localPosition;
            wheel.transform.localRotation = Quaternion.Euler(90.0f, 0.0f, 0.0f);
            wheel.transform.localScale = new Vector3(0.18f, 0.08f, 0.18f);
            SetColor(wheel, new Color(0.04f, 0.04f, 0.05f));
        }

        private GameObject CreateEnvironmentCube(
            string name,
            Vector3 position,
            Vector3 scale,
            Color color)
        {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = name;
            target.transform.position = position;
            target.transform.localScale = scale;
            SetColor(target, color);
            _runtimeObjects.Add(target);
            return target;
        }

        private static void SetColor(GameObject target, Color color)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material.color = color;
            }
        }
    }
}
