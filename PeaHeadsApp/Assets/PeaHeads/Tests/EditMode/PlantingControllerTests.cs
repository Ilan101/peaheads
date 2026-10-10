using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class PlantingControllerTests
    {
        private GameObject _go;
        private PlantingController _planting;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("Plant");
            _planting = _go.AddComponent<PlantingController>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private void Plant()
        {
            _planting.Tick(0f, true);
            _planting.Tick(1.01f, false);
        }

        [Test]
        public void StartsUnplantedAndCanMove()
        {
            Assert.AreEqual(PlantState.Unplanted, _planting.State);
            Assert.IsTrue(_planting.CanMove);
            Assert.IsFalse(_planting.CanShoot);
        }

        [Test]
        public void PressStartsPlantingAndBlocksMoveAndShoot()
        {
            _planting.Tick(0.016f, true);

            Assert.AreEqual(PlantState.Planting, _planting.State);
            Assert.IsFalse(_planting.CanMove);
            Assert.IsFalse(_planting.CanShoot);
        }

        [Test]
        public void PlantingCompletesAfterOneSecond()
        {
            _planting.Tick(0f, true);
            _planting.Tick(0.99f, false);
            Assert.AreEqual(PlantState.Planting, _planting.State);

            _planting.Tick(0.02f, false);
            Assert.AreEqual(PlantState.Planted, _planting.State);
            Assert.IsTrue(_planting.CanShoot);
        }

        [Test]
        public void PressDuringTransitionIsIgnored()
        {
            _planting.Tick(0f, true);
            _planting.Tick(0.5f, true);
            Assert.AreEqual(PlantState.Planting, _planting.State);

            _planting.Tick(0.51f, false);
            Assert.AreEqual(PlantState.Planted, _planting.State);
        }

        [Test]
        public void UnplantingDisablesShootingThenReturnsToUnplanted()
        {
            Plant();

            _planting.Tick(0f, true);
            Assert.AreEqual(PlantState.Unplanting, _planting.State);
            Assert.IsFalse(_planting.CanShoot);

            _planting.Tick(1.01f, false);
            Assert.AreEqual(PlantState.Unplanted, _planting.State);
            Assert.IsTrue(_planting.CanMove);
        }

        [Test]
        public void ApplyRemoteSetsState()
        {
            _planting.ApplyRemote(PlantState.Planted);

            Assert.AreEqual(PlantState.Planted, _planting.State);
            Assert.IsTrue(_planting.CanShoot);
        }

        [Test]
        public void RepeatedRemoteValueDoesNotUndoLocalTransition()
        {
            _planting.ApplyRemote(PlantState.Planting);
            _planting.Tick(1.01f, false);
            Assert.AreEqual(PlantState.Planted, _planting.State);

            _planting.ApplyRemote(PlantState.Planting);

            Assert.AreEqual(PlantState.Planted, _planting.State);
        }

        [Test]
        public void ResetStateReturnsToStandingUnplanted()
        {
            Plant();

            _planting.ResetState();

            Assert.AreEqual(PlantState.Unplanted, _planting.State);
            Assert.AreEqual(1f, _planting.HeightScale);
        }
    }
}
