using System;
using System.Collections;
using Emas.AircraftFormation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Emas.Tests.Samples
{
    /// <summary>Exercises the cockpit sample through authored assets and public observation and presentation APIs.</summary>
    public sealed class AircraftFormationTests
    {
        private Scene _scene;

        /// <summary>Unloads the authored scene and its tracked aircraft after each consumer scenario.</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(_scene);
            }
        }

        /// <summary>All three aircraft retain equal Mach 1 velocity and forward separation through combined turns, pitch and bank with irregular manual steps.</summary>
        [UnityTest]
        public IEnumerator CleanFlight_EqualSpeedAndCameraRelativeProjectionThroughManeuvers()
        {
            yield return Load(false);
            RealmSetup setup = Find<RealmSetup>();
            Realm realm = setup.Realm;
            FlightSource source = Find<FlightSource>();
            Camera camera = Find<Camera>();
            realm.InterpolationDelay = 0;
            foreach (IGhost entity in realm.Ghosts)
            {
                Ghost ghost = (Ghost)entity;
                ghost.GetRequired<Extrapolation>().enabled = false;
                ghost.GetRequired<Smoothing>().enabled = false;
                ghost.GetRequired<Spatial>().ResetPresentation();
            }
            realm.Update();
            double[] steps = { 0.011, 0.037, 0.016, 0.025 };
            Spatial reference = Aircraft(realm, "reference").GetRequired<Spatial>();
            Double3 previousPosition = reference.Position;
            Quaternion firstRotation = reference.Rotation;
            float maximumTurn = 0;
            for (int index = 0; index < 540; index++)
            {
                double step = steps[index % steps.Length];
                source.Advance(step);
                realm.Update();
                double measuredSpeed = Double3.Distance(reference.Position, previousPosition) / step;
                Assert.That(measuredSpeed, Is.EqualTo(343).Within(0.02));
                previousPosition = reference.Position;
                maximumTurn = Mathf.Max(maximumTurn, Quaternion.Angle(firstRotation, reference.Rotation));
                Ghost referenceGhost = Aircraft(realm, "reference");
                Assert.That(referenceGhost.transform.position.magnitude, Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(referenceGhost.transform.rotation, Quaternion.identity), Is.LessThan(0.02f));
                Assert.That(camera.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(camera.transform.rotation, Is.EqualTo(Quaternion.identity));
                foreach (string id in new[] { "left", "right" })
                {
                    Ghost wingman = Aircraft(realm, id);
                    Spatial spatial = wingman.GetRequired<Spatial>();
                    Vector3 offset = new Vector3(id == "left" ? -70 : 70, id == "left" ? 20 : -20, 350);
                    Double3 separation = spatial.Position - reference.Position;
                    Assert.That(Double3.Distance(separation, new Double3(offset.x, offset.y, offset.z)), Is.LessThan(0.00001));
                    Vector3 expected = Quaternion.Inverse(reference.Rotation) * offset;
                    Assert.That(Vector3.Distance(wingman.transform.position, expected), Is.LessThan(0.002f));
                    Assert.That(wingman.transform.position.z, Is.GreaterThan(200));
                    Assert.That(spatial.PositionTime, Is.EqualTo(reference.PositionTime));
                    Extrapolation extrapolation = wingman.GetRequired<Extrapolation>();
                    Assert.That(Double3.Distance(default(Double3), extrapolation.Velocity), Is.EqualTo(343).Within(0.00001));
                    Assert.That(extrapolation.HasAngularVelocity, Is.True);
                }
            }
            Assert.That(maximumTurn, Is.GreaterThan(35), "The scenario must actually exercise combined angular motion.");
        }

        /// <summary>Independent noisy, delayed capture streams preserve forward wingmen, monotonic timestamps and bounded bearing error through irregular presentation frames.</summary>
        [UnityTest]
        public IEnumerator DelayedFlight_IndependentTimestampsAndBoundedAngularPresentation()
        {
            yield return Load(true);
            Realm realm = Find<RealmSetup>().Realm;
            FlightSource source = Find<FlightSource>();
            source.AutomaticAdvance = true;
            double warmup = Time.realtimeSinceStartupAsDouble + 1;
            while (Time.realtimeSinceStartupAsDouble < warmup)
            {
                yield return null;
            }
            string[] ids = { "reference", "left", "right" };
            Timestamp[] previous = new Timestamp[3];
            int[] fresh = new int[3];
            bool independentCaptures = false;
            float maximumBearingError = 0;
            double minimumAge = double.MaxValue;
            double maximumAge = 0;
            Quaternion initial = realm.ReferenceFrame.Rotation;
            float maximumTurn = 0;
            double deadline = Time.realtimeSinceStartupAsDouble + 7;
            int frames = 0;
            do
            {
                // Deliberately vary update opportunities while both SDK and Realm retain their real-time clocks.
                yield return new WaitForSecondsRealtime(frames % 3 == 0 ? 0.031f : frames % 3 == 1 ? 0.005f : 0.017f);
                frames++;
                maximumTurn = Mathf.Max(maximumTurn, Quaternion.Angle(initial, realm.ReferenceFrame.Rotation));
                for (int index = 0; index < ids.Length; index++)
                {
                    Ghost ghost = Aircraft(realm, ids[index]);
                    Spatial spatial = ghost.GetRequired<Spatial>();
                    Assert.That(spatial.IsInRange, Is.True);
                    Assert.That(ghost.GetComponentInChildren<View>(), Is.Not.Null);
                    Timestamp sample = spatial.PositionTime.Value;
                    Assert.That(sample.CompareTo(previous[index]), Is.GreaterThanOrEqualTo(0));
                    if (sample.CompareTo(previous[index]) > 0)
                    {
                        fresh[index]++;
                    }
                    previous[index] = sample;
                    double age = source.ElapsedSeconds - sample.ElapsedSince(new Timestamp(0, 0));
                    minimumAge = Math.Min(minimumAge, age);
                    maximumAge = Math.Max(maximumAge, age);
                    if (index == 0)
                    {
                        Assert.That(ghost.transform.position.magnitude, Is.LessThan(0.001f));
                        continue;
                    }
                    independentCaptures |= sample.CompareTo(previous[0]) != 0;
                    Vector3 offset = new Vector3(index == 1 ? -70 : 70, index == 1 ? 20 : -20, 350);
                    Vector3 expected = Quaternion.Inverse(realm.ReferenceFrame.Rotation) * offset;
                    maximumBearingError = Mathf.Max(maximumBearingError, Vector3.Angle(expected, ghost.transform.position));
                    Assert.That(ghost.transform.position.z, Is.GreaterThan(200));
                    Assert.That(ghost.transform.position.magnitude, Is.InRange(290, 430));
                }
            }
            while (Time.realtimeSinceStartupAsDouble < deadline);
            TestContext.WriteLine("Noisy Mach 1: frames=" + frames + "; max bearing error=" + maximumBearingError
                + " deg; capture ages=" + minimumAge + ".." + maximumAge + " s; max turn=" + maximumTurn + " deg");
            Assert.That(fresh, Has.All.GreaterThan(25));
            Assert.That(independentCaptures, Is.True);
            Assert.That(maximumAge - minimumAge, Is.GreaterThan(0.02), "Transport age must vary across streams and frames.");
            Assert.That(maximumBearingError, Is.LessThan(10), "Wingmen must remain useful angular references under default impairments.");
            Assert.That(maximumTurn, Is.GreaterThan(20));
        }

        private IEnumerator Load(bool impairments)
        {
            const string path = "Assets/Samples/AircraftFormation/AircraftFormation.unity";
            yield return SceneManager.LoadSceneAsync(path, LoadSceneMode.Additive);
            _scene = SceneManager.GetSceneByPath(path);
            RealmSetup setup = Find<RealmSetup>();
            setup.StopRealm();
            FlightSource source = Find<FlightSource>();
            source.AutomaticAdvance = false;
            source.SimulateJitterAndDelay = impairments;
            setup.StartRealm();
            if (impairments)
            {
                source.Advance(0.25);
            }
            setup.Realm.Update();
        }

        private T Find<T>() where T : Component
        {
            foreach (GameObject root in _scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>();
                if (component != null)
                {
                    return component;
                }
            }
            Assert.Fail("Missing authored sample component: " + typeof(T).Name);
            return null;
        }

        private static Ghost Aircraft(Realm realm, string id)
        {
            Assert.That(realm.TryGetGhost(new Key("aircraft-formation", FlightSource.Kind, id), out IGhost ghost), Is.True, id);
            return (Ghost)ghost;
        }
    }
}
