using System;
using System.Collections.Generic;
using UnityEngine;

namespace Emas.RelativeWorld
{
    /// <summary>
    /// Keeps the ego car fixed in Unity while traffic is projected relative to its large network coordinates.
    /// Add this component to an empty scene object and enter Play Mode.
    /// </summary>
    [AddComponentMenu("Emas/Examples/Relative World")]
    public sealed class RelativeWorld : MonoBehaviour
    {
        private Realm _realm;
        private RelativeCarSource _source;
        private IDisposable _views;
        private Blueprint _blueprint;
        private GameObject _environment;
        private readonly List<Material> _materials = new List<Material>();
        private double _elapsed;

        /// <summary>
        /// Gets the isolated realm owned by this demonstration while it is enabled.
        /// </summary>
        public Realm Realm
        {
            get
            {
                return _realm;
            }
        }

        private void OnEnable()
        {
            _elapsed = 0.0;
            _realm = new Realm();
            _realm.ReferenceFrame = new ReferenceFrame
            {
                FollowedGhost = new Key("relative-world", RelativeCar.Kind, "ego"),
                UnityPosition = Vector3.zero,
                UnityRotation = Quaternion.identity,
                FollowRotation = true,
                MaxDistance = 45.0
            };

            _environment = new GameObject("Relative World Environment");
            _environment.transform.SetParent(transform, false);
            GameObject egoView = CreateCar("Ego Car Template", new Color(0.2f, 0.85f, 0.45f));
            GameObject trafficView = CreateCar("Traffic Template", new Color(1f, 0.55f, 0.15f));
            _blueprint = ScriptableObject.CreateInstance<Blueprint>();
            _blueprint.Configure(RelativeCar.Kind, null, new[]
            {
                new Blueprint.ViewMapping(RelativeCar.Ego, DetailLevel.Full, egoView),
                new Blueprint.ViewMapping(RelativeCar.Traffic, DetailLevel.Full, trafficView)
            }, null);
            _realm.RegisterBlueprint(_blueprint);
            _source = new RelativeCarSource { Name = "Large-coordinate cars" };
            _realm.GetOrCreateAnchor("relative-world", _source);
            _views = _realm.Query().OfKind(RelativeCar.Kind).OnAvailable(ghost => _realm.Manifest(ghost));
            CreateEnvironment();
            _realm.Update();
        }

        private void Update()
        {
            Advance(Time.deltaTime);
        }

        /// <summary>
        /// Advances simulated driving and projects the latest independent publications into Unity.
        /// </summary>
        /// <param name="seconds">Elapsed simulation time, in seconds.</param>
        public void Advance(double seconds)
        {
            if (_realm == null)
            {
                return;
            }

            _elapsed += seconds;
            double travel = Math.Sin(_elapsed * 0.15) * 30.0;
            _source.PublishEgoPosition(travel);
            // Orientation can arrive independently from position, as it does in many real SDKs.
            _source.PublishEgoRotation(Quaternion.Euler(0f, (float)Math.Sin(_elapsed * 0.1) * 12f, 0f));
            _realm.Update();
        }

        private void OnDisable()
        {
            _views?.Dispose();
            _views = null;
            _realm?.Dispose();
            _realm = null;
            _source = null;
            Destroy(_environment);
            Destroy(_blueprint);
            foreach (Material material in _materials)
            {
                Destroy(material);
            }

            _materials.Clear();
        }

        private GameObject CreateCar(string name, Color color)
        {
            GameObject template = new GameObject(name);
            template.transform.SetParent(_environment.transform, false);
            template.SetActive(false);
            CreateCube(template.transform, "Body", new Vector3(0f, 0.45f, 0f), new Vector3(1.8f, 0.6f, 3.8f), color);
            CreateCube(template.transform, "Cabin", new Vector3(0f, 1f, -0.2f), new Vector3(1.4f, 0.6f, 1.8f), color * 0.65f);
            return template;
        }

        private void CreateEnvironment()
        {
            CreateCube(_environment.transform, "Unity Ground", new Vector3(0f, -0.1f, 0f),
                new Vector3(30f, 0.1f, 110f), new Color(0.12f, 0.15f, 0.18f));
            GameObject cameraObject = new GameObject("Relative World Camera");
            cameraObject.transform.SetParent(_environment.transform, false);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(15f, 22f, -28f);
            camera.transform.LookAt(new Vector3(0f, 0f, 10f));
            camera.farClipPlane = 180f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.04f, 0.06f, 0.09f);
            GameObject lightObject = new GameObject("Relative World Light");
            lightObject.transform.SetParent(_environment.transform, false);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        }

        private void CreateCube(Transform parent, string name, Vector3 position, Vector3 scale, Color color)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            Material material = cube.GetComponent<Renderer>().material;
            material.color = color;
            _materials.Add(material);
        }

        private void OnGUI()
        {
            GUI.Label(new Rect(16f, 16f, 1000f, 26f), "Relative world: green ego stays fixed; orange traffic moves inversely.");
            GUI.Label(new Rect(16f, 42f, 1000f, 26f), "Network coordinates are around 1,000,000,000 m. Views return within 45 m.");
        }
    }
}
