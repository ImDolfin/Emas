using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Emas.Tests
{
    /// <summary>Exercises the public observation-filter extension and its prediction boundary.</summary>
    public sealed class FilterTests
    {
        private sealed class Detector : PresenceDetector
        {
            internal Ghost Publish(string id)
            {
                return GetOrCreate<Ghost>(id, new Kind("filtered"));
            }
        }

        private sealed class Offset : Filter
        {
            internal int Calls;
            internal Timestamp? BodySample;
            internal bool SawRelabeledBodyRate;

            /// <summary>Demonstrates a source-coordinate observation correction.</summary>
            /// <param name="input">The preceding observation estimate.</param>
            /// <param name="context">The filter's clock and axes.</param>
            /// <returns>A translated estimate without changing the raw Spatial channel.</returns>
            protected override FilterState Process(FilterState input, FilterContext context)
            {
                Calls++;
                if (input.AngularVelocityIsWorld && input.AngularVelocity.HasValue && BodySample.HasValue
                    && Nullable.Equals(input.AngularVelocityTime, BodySample))
                {
                    SawRelabeledBodyRate = true;
                }

                if (input.Position.HasValue)
                {
                    input.Position += new Double3(100, 0, 0);
                }

                return input;
            }
        }

        private sealed class Scale : Filter
        {
            /// <summary>Demonstrates a second estimator operating on the first estimator's result.</summary>
            /// <param name="input">The preceding observation estimate.</param>
            /// <param name="context">The filter's clock and axes.</param>
            /// <returns>A scaled pose and any supplied linear motion.</returns>
            protected override FilterState Process(FilterState input, FilterContext context)
            {
                input.Position *= 2d;
                input.Velocity *= 2d;
                input.Acceleration *= 2d;
                return input;
            }
        }

        private sealed class AngularProbe : Filter
        {
            internal Double3 Rate;

            /// <summary>Reads the public quaternion-world angular conversion used by custom estimators.</summary>
            /// <param name="input">The preceding filtered pose and SDK angular motion.</param>
            /// <param name="context">Source coordinate and orientation conventions.</param>
            /// <returns>The unchanged estimate.</returns>
            protected override FilterState Process(FilterState input, FilterContext context)
            {
                Rate = context.WorldAngularVelocity(input);
                return input;
            }
        }

        private sealed class RecoveringFilter : Filter
        {
            internal bool Invalid = true;
            internal int Resets;

            /// <summary>Demonstrates an estimator that first produces an invalid attitude and later recovers.</summary>
            /// <param name="input">Accepted observations.</param>
            /// <param name="context">Filter timing and coordinates.</param>
            /// <returns>An invalid first output or a finite translated estimate after recovery.</returns>
            protected override FilterState Process(FilterState input, FilterContext context)
            {
                if (Invalid)
                {
                    input.Rotation = new Quaternion(0, 0, 0, 0);
                }
                else if (input.Position.HasValue)
                {
                    input.Position += new Double3(10, 0, 0);
                }

                return input;
            }

            /// <summary>Records the framework discarding invalid estimator state.</summary>
            protected override void OnReset()
            {
                Resets++;
            }
        }

        private sealed class Probe : IExtrapolationStrategy
        {
            internal IExtrapolationStrategy Model = new ConstantAccelerationExtrapolation();
            internal int Calls;
            internal PredictionRequest Request;
            internal Prediction Result;
            internal Double3 HistoryPosition;

            /// <summary>Captures the exact state/history visible to an application predictor.</summary>
            /// <param name="request">Filtered observations and independent bounded horizons.</param>
            /// <returns>The delegated full prediction.</returns>
            public Prediction Predict(PredictionRequest request)
            {
                Calls++;
                Request = request;
                request.PositionContext.TryGetPositionSample(0, out _, out HistoryPosition);
                Result = Model.Predict(request);
                return Result;
            }
        }

        private Realm _realm;
        private Detector _source;

        /// <summary>Creates an isolated Cartesian consumer with explicit source timestamps.</summary>
        [SetUp]
        public void SetUp()
        {
            _realm = new Realm { ReferenceFrame = new ReferenceFrame { Position = default(Double3) } };
            _source = new Detector();
            _realm.GetOrCreateAnchor("filters", _source);
        }

        /// <summary>Releases all filter instances and their Ghosts.</summary>
        [TearDown]
        public void TearDown()
        {
            _realm.Dispose();
        }

        /// <summary>Ordered custom filters feed pose and fitting history to one prediction without changing SDK values; attachments and distance bypass them.</summary>
        [Test]
        public void Filters_ChainBeforePredictionWithSeparateHistoryAndLifecycle()
        {
            Spatial first = Create("offset then scale", out Probe firstModel);
            Offset firstOffset = first.gameObject.AddComponent<Offset>();
            first.gameObject.AddComponent<Scale>();
            Spatial second = Create("scale then offset", out Probe secondModel);
            second.gameObject.AddComponent<Scale>();
            second.gameObject.AddComponent<Offset>();
            for (int index = 0; index < 2; index++)
            {
                Timestamp time = new Timestamp(1000 + index, 0);
                first.SetCartesianPosition(new Double3(10 + index, 0, 0), time);
                second.SetCartesianPosition(new Double3(10 + index, 0, 0), time);
                firstModel.Calls = secondModel.Calls = 0;
                _realm.Update();
                Assert.That(firstModel.Calls, Is.EqualTo(1));
                Assert.That(secondModel.Calls, Is.EqualTo(1));
            }

            Assert.That(firstModel.HistoryPosition.X, Is.EqualTo(222));
            Assert.That(secondModel.HistoryPosition.X, Is.EqualTo(122));
            Assert.That(firstModel.Result.Velocity.X, Is.EqualTo(2).Within(0.0001d));
            Assert.That(firstModel.Result.Position.X, Is.EqualTo(222 + 2 * firstModel.Request.PositionSeconds).Within(0.0001d));
            Assert.That(first.Position.X, Is.EqualTo(11));
            Assert.That(first.PositionTime, Is.EqualTo(new Timestamp(1001, 0)));

            first.Attach(second.GetComponent<Ghost>().Key, Vector3.zero);
            firstOffset.Calls = firstModel.Calls = 0;
            _realm.Update();
            Assert.That(firstOffset.Calls, Is.Zero);
            Assert.That(firstModel.Calls, Is.Zero);
            first.Detach();
            _realm.Update();
            Assert.That(firstOffset.Calls, Is.EqualTo(1));
            _realm.PoseProcessingDistance = 1;
            firstOffset.Calls = firstModel.Calls = 0;
            _realm.Update();
            Assert.That(firstOffset.Calls, Is.Zero);
            Assert.That(firstModel.Calls, Is.Zero);
            Assert.That(first.transform.position.x, Is.EqualTo(11).Within(0.0001f));
        }

        /// <summary>Live processing observes filters and prediction components added, disabled or replaced after earlier projections.</summary>
        [Test]
        public void ProcessingComponents_ObserveLateAdditionDisablingAndReplacement()
        {
            Spatial spatial = _source.Publish("live processing").gameObject.AddComponent<Spatial>();
            spatial.SetCartesianPosition(new Double3(1, 0, 0));
            _realm.Update();

            // Filters must also work when prediction is absent, including after a
            // missing lookup or replacement of the component used to check presence.
            Offset filter = spatial.gameObject.AddComponent<Offset>();
            _realm.Update();
            Assert.That(filter.Calls, Is.EqualTo(1));
            filter.enabled = false;
            _realm.Update();
            Assert.That(filter.Calls, Is.EqualTo(1));
            UnityEngine.Object.DestroyImmediate(filter);
            filter = spatial.gameObject.AddComponent<Offset>();
            _realm.Update();
            Assert.That(filter.Calls, Is.EqualTo(1));

            // A cached prediction component must retain live enabled state and
            // release its place to a replacement without discarding the filter.
            Extrapolation extrapolation = spatial.gameObject.AddComponent<Extrapolation>();
            Probe first = new Probe();
            extrapolation.Strategy = first;
            _realm.Update();
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(first.Request.PositionContext.Position.Value.X, Is.EqualTo(101));
            extrapolation.enabled = false;
            _realm.Update();
            Assert.That(first.Calls, Is.EqualTo(1));
            extrapolation.enabled = true;
            _realm.Update();
            Assert.That(first.Calls, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(extrapolation);
            _realm.Update();
            Assert.That(first.Calls, Is.EqualTo(2));

            Probe replacement = new Probe();
            spatial.gameObject.AddComponent<Extrapolation>().Strategy = replacement;
            _realm.Update();
            Assert.That(replacement.Calls, Is.EqualTo(1));
            Assert.That(replacement.Request.PositionContext.Position.Value.X, Is.EqualTo(101));
            Assert.That(spatial.Position.X, Is.EqualTo(1));
        }

        /// <summary>Pose-only smoothing declares its effective age, so filtered-history prediction recovers sustained translation and bank without manual extra lead.</summary>
        /// <returns>An iterator that supplies observations across real filter updates.</returns>
        [UnityTest]
        public IEnumerator Smoothing_PredictsFromTheFilteredStateTime()
        {
            Spatial spatial = Create("smooth before predict", out Probe model);
            model.Model = new ConstantVelocityExtrapolation();
            Smoothing smoothing = spatial.gameObject.AddComponent<Smoothing>();
            smoothing.PositionHalfLife = 0.2f;
            smoothing.RotationHalfLife = 0.15f;
            spatial.SetCartesianPosition(default(Double3), new Timestamp(1000, 0));
            spatial.SetSourceRotation(Quaternion.identity, new Timestamp(1000, 0));
            _realm.Update();
            yield return new WaitForSecondsRealtime(0.03f);
            Timestamp creation = new Timestamp(1000, 100000000);
            spatial.SetCartesianPosition(new Double3(1, 0, 0), creation);
            spatial.SetSourceRotation(Quaternion.AngleAxis(9, Vector3.forward), creation);
            for (int frame = 0; frame < 4; frame++)
            {
                model.Calls = 0;
                _realm.Update();
                Assert.That(model.Calls, Is.EqualTo(1));
                double positionDelay = creation.ElapsedSince(model.Request.PositionContext.PositionTime.Value);
                double rotationDelay = creation.ElapsedSince(model.Request.RotationContext.RotationTime.Value);
                Assert.That(positionDelay, Is.GreaterThan(0));
                Assert.That(rotationDelay, Is.EqualTo(0));
                Assert.That(spatial.HasAngularVelocity, Is.False, "Filter-derived motion must remain separate from SDK data.");
                Assert.That(model.Request.Position.X, Is.InRange(0d, 1d));
                Assert.That(model.Result.Position.X,
                    Is.EqualTo(1 + 10 * (model.Request.PositionSeconds - positionDelay)).Within(0.001d));
                Quaternion expected = Quaternion.AngleAxis((float)(9 + 90 * (model.Request.RotationSeconds - rotationDelay)), Vector3.forward);
                Assert.That(Quaternion.Angle(model.Result.Rotation, expected), Is.LessThan(0.05f));
                Assert.That(spatial.Position.X, Is.EqualTo(1));
                Assert.That(spatial.PositionTime, Is.EqualTo(creation));
                yield return null;
            }
        }

        /// <summary>Changing the supplied angular frame preserves independent creation times and never labels an older body rate as ECEF world motion.</summary>
        /// <param name="sourceRotation">Whether to use source quaternions with NED axes instead of geographic attitudes.</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Filters_AngularFrameChangesExcludeFutureRates(bool sourceRotation)
        {
            GeoPosition origin = new GeoPosition(52, 13, 1000);
            _realm.ReferenceFrame = new ReferenceFrame
            {
                Space = ReferenceSpace.Geographic,
                GeographicPosition = origin,
                Coordinates = sourceRotation ? CoordinateSystem.NorthEastDown : CoordinateSystem.Unity
            };
            Spatial spatial = Create("angular frame", out Probe model);
            model.Model = new ConstantVelocityExtrapolation();
            Offset filter = spatial.gameObject.AddComponent<Offset>();
            AngularProbe angular = spatial.gameObject.AddComponent<AngularProbe>();
            Timestamp bodyTime = new Timestamp(1000, 0);
            spatial.SetGeographicPosition(origin, bodyTime);
            if (sourceRotation)
            {
                spatial.SetSourceRotation(Quaternion.identity, bodyTime);
            }
            else
            {
                spatial.SetGeographicRotation(0, 0, 0, bodyTime);
            }

            spatial.SetBodyAngularVelocity(new Double3(0, 1, 0), CoordinateSystem.Unity, bodyTime);
            _realm.Update();
            filter.BodySample = bodyTime;
            Timestamp attitudeTime = new Timestamp(1000, 100000000);
            Timestamp worldTime = new Timestamp(1000, 200000000);
            if (sourceRotation)
            {
                spatial.SetSourceRotation(Quaternion.AngleAxis(5, Vector3.up), attitudeTime);
            }
            else
            {
                spatial.SetGeographicRotation(0.1d * 180d / Math.PI, 0, 0, attitudeTime);
            }

            spatial.SetEarthCenteredAngularVelocity(default(Double3), worldTime);
            _realm.Update();
            Assert.That(filter.SawRelabeledBodyRate, Is.False);
            Assert.That(model.Request.RotationContext.ObservationTime, Is.EqualTo(attitudeTime));
            Assert.That(model.Request.RotationContext.LatestAngularVelocityTime, Is.EqualTo(worldTime));
            Assert.That(model.Request.RotationContext.AngularVelocityIsEstimated, Is.True,
                "The future-only zero world rate must not override motion at the older attitude.");
            Assert.That(spatial.AngularVelocity, Is.EqualTo(default(Double3)));
            Assert.That(spatial.RotationTime, Is.EqualTo(attitudeTime));
            spatial.SetEarthCenteredAngularVelocity(new Double3(0.3, -0.2, 0.1), new Timestamp(1000, 250000000));
            Timestamp nextAttitude = new Timestamp(1000, 300000000);
            if (sourceRotation)
            {
                spatial.SetSourceRotation(Quaternion.AngleAxis(15, Vector3.up), nextAttitude);
            }
            else
            {
                spatial.SetGeographicRotation(20, 0, 0, nextAttitude);
            }

            _realm.Update();
            Assert.That(model.Request.RotationContext.AngularVelocityIsWorld, Is.True);
            Assert.That(Double3.Distance(model.Result.AngularVelocity, angular.Rate), Is.LessThan(0.00001d),
                "Filter motion and prediction must use the same quaternion world axes, including mirrored source axes.");
        }

        /// <summary>An invalid custom estimate falls back to valid observations and can recover without corrupting SDK input or skipping prediction.</summary>
        [Test]
        public void Filters_InvalidOutputRetainsValidStateAndCanRecover()
        {
            Spatial spatial = Create("recovering filter", out Probe model);
            spatial.SetCartesianPosition(default(Double3), new Timestamp(1000, 0));
            RecoveringFilter filter = spatial.gameObject.AddComponent<RecoveringFilter>();
            _realm.Update();
            Assert.That(filter.Resets, Is.GreaterThanOrEqualTo(1));
            Assert.That(model.Calls, Is.EqualTo(1));
            Assert.That(spatial.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(spatial.HasRotation, Is.False);
            filter.Invalid = false;
            _realm.Update();
            Assert.That(model.Calls, Is.EqualTo(2));
            Assert.That(spatial.transform.position.x, Is.EqualTo(10).Within(0.0001f));
            Assert.That(spatial.Position, Is.EqualTo(default(Double3)));
            Assert.That(spatial.HasRotation, Is.False);
        }

        private Spatial Create(string id, out Probe model)
        {
            Spatial spatial = _source.Publish(id).gameObject.AddComponent<Spatial>();
            Extrapolation extrapolation = spatial.gameObject.AddComponent<Extrapolation>();
            model = new Probe();
            extrapolation.Strategy = model;
            extrapolation.PredictionTime = 0.2f;
            extrapolation.MaximumExtrapolation = 1;
            return spatial;
        }
    }
}
