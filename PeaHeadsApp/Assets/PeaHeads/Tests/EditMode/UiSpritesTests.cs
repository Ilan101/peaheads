using NUnit.Framework;
using UnityEngine;

namespace PeaHeads.Tests
{
    public class UiSpritesTests
    {
        // Alpha at a point given in the sprites' [-1, 1]² space (y up).
        private static float Alpha(Sprite sprite, float u, float v)
        {
            Texture2D texture = sprite.texture;
            int x = Mathf.Clamp(Mathf.FloorToInt((u + 1f) * 0.5f * texture.width), 0, texture.width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((v + 1f) * 0.5f * texture.height), 0, texture.height - 1);
            return texture.GetPixel(x, y).a;
        }

        private static void AssertFilled(Sprite sprite, float u, float v) =>
            Assert.Greater(Alpha(sprite, u, v), 0.9f, $"expected filled at ({u}, {v})");

        private static void AssertEmpty(Sprite sprite, float u, float v) =>
            Assert.Less(Alpha(sprite, u, v), 0.1f, $"expected empty at ({u}, {v})");

        [Test]
        public void Flower_HasCentreAndPetals()
        {
            Sprite flower = UiSprites.Flower();
            AssertFilled(flower, 0f, 0f);
            AssertFilled(flower, 0f, 0.55f);
            AssertEmpty(flower, -0.95f, -0.95f);
        }

        [Test]
        public void Thorn_HasCoreAndSpikes()
        {
            Sprite thorn = UiSprites.Thorn();
            AssertFilled(thorn, 0f, 0f);
            AssertFilled(thorn, 0f, 0.85f);
            AssertEmpty(thorn, -0.95f, -0.95f);
        }

        [Test]
        public void Sprout_HasStemAndLeaves()
        {
            Sprite sprout = UiSprites.Sprout();
            AssertFilled(sprout, 0f, -0.5f);
            AssertFilled(sprout, -0.35f, 0.3f);
            AssertFilled(sprout, 0.35f, 0.3f);
            AssertEmpty(sprout, 0.8f, -0.8f);
        }

        [Test]
        public void Camera_HasBodyLensHoleAndViewfinder()
        {
            Sprite camera = UiSprites.Camera();
            AssertFilled(camera, -0.7f, 0.3f);
            AssertEmpty(camera, -0.27f, 0f);
            AssertFilled(camera, 0.8f, 0f);
            AssertEmpty(camera, 0f, 0.9f);
        }

        [Test]
        public void Shield_TapersToAPoint()
        {
            Sprite shield = UiSprites.Shield();
            AssertFilled(shield, 0f, 0.5f);
            AssertFilled(shield, 0f, -0.8f);
            AssertEmpty(shield, 0.7f, -0.8f);
            AssertEmpty(shield, 0.95f, 0.95f);
        }

        [Test]
        public void Slash_IsADiagonalFromTopLeft()
        {
            Sprite slash = UiSprites.Slash();
            AssertFilled(slash, 0f, 0f);
            AssertFilled(slash, -0.6f, 0.6f);
            AssertEmpty(slash, 0.5f, 0.5f);
        }
    }
}
