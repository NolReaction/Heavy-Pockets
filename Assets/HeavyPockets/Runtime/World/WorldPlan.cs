using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeavyPockets
{
    internal sealed class WorldPlan : ScriptableObject
    {
        [Serializable]
        internal struct Placement
        {
            public MapPiece Piece;
            public Vector3 Position;
            public int Turns;

            internal Quaternion Rotation => Quaternion.Euler(0, 90 * (Turns & 3), 0);
            internal Bounds Place(Bounds local)
            {
                Vector3 size = local.size;
                if ((Turns & 1) != 0) size = new Vector3(size.z, size.y, size.x);
                return new Bounds(Position + Rotation * local.center, size);
            }
        }

        public List<Placement> Objects = new List<Placement>();
        internal static string Resource(bool training) => "MapPlans/" + (training ? "Training" : "Depot");
        internal static WorldPlan Load(bool training) => Resources.Load<WorldPlan>(Resource(training));

        internal void GetGeometry(out Bounds[] solid, out Bounds[] glass)
        {
            var walls = new List<Bounds>();
            var windows = new List<Bounds>();
            foreach (var item in Objects)
            {
                if (!item.Piece) continue;
                foreach (var box in item.Piece.Solids) walls.Add(item.Place(box));
                if (item.Piece.Glazing != null)
                    foreach (var pane in item.Piece.Glazing) windows.Add(item.Place(pane));
            }
            solid = walls.ToArray();
            glass = windows.ToArray();
        }
    }
}
