using UnityEngine;

namespace HeavyPockets
{
    internal static class YardPlanting
    {
        // Surveyed trunk footprints on the flat, walkable quay. The canopy is wider and permeable.
        internal static readonly Bounds[] Trunks = {
            Trunk(-29.5f, 9.8f, .29f, 4.65f), Trunk(-27.3f, 25.8f, .23f, 3.9f),
            Trunk(-44.1f, 14.8f, .32f, 5.2f), Trunk(-62.0f, 22.2f, .27f, 4.35f),
            Trunk(-58.8f, 24.9f, .19f, 3.45f), Trunk(-33.1f, -32.5f, .28f, 4.55f),
            Trunk(-1.7f, -26.8f, .23f, 3.8f), Trunk(22.8f, 15.1f, .31f, 5.05f),
            Trunk(25.3f, 11.6f, .21f, 3.6f), Trunk(55.8f, -13.1f, .26f, 4.2f),
            Trunk(60.2f, -16.3f, .19f, 3.3f), Trunk(33.9f, -32.8f, .25f, 4.15f)
        };

        private static Bounds Trunk(float x, float z, float diameter, float height) =>
            new Bounds(new Vector3(x, height * .5f, z), new Vector3(diameter, height, diameter));
    }
}
