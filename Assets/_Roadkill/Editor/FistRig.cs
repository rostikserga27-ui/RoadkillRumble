using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Roadkill.EditorTools
{
    /// <summary>
    /// Fists for the fisherman, whose model has no finger bones. The recipe (ArtSource/Fisherman) builds each
    /// hand from a palm, four fingers and a thumb as separate pieces, all skinned to the hand bone; this finds
    /// those pieces by connectivity and position along the hand (in the recipe's hand units: knuckles at
    /// 0.135, middle joints at ~0.175, fingertips at ~0.215, thumb base at 0.05) and
    ///   - on the body: adds bones for the fingers, their tips and the thumb under each hand bone, rebinds
    ///     those vertices to them and adds FistCurl to bend them (saved as a new mesh asset);
    ///   - on the first-person arm (a plain mesh): bends the fingers into a fist in the mesh itself.
    /// </summary>
    public static class FistRig
    {
        const float Knuckle = 0.135f, Middle = 0.175f, TipSplit = 0.188f, Tip = 0.214f, ThumbBase = 0.05f, ThumbSide = 0.03f;
        const string BodyMeshPath = "Assets/_Roadkill/Generated/SK_Fisherman_Body_Fists.asset";
        const string ArmMeshPath = "Assets/_Roadkill/Generated/FisherFPArm_Fist.asset";
        const string ArmPrefabPath = "Assets/_Roadkill/Generated/FisherFPArm_Fist.prefab";

        enum Piece { Palm, Finger, FingerTip, Thumb }

        /// <summary>One hand, worked out from its vertices: which piece each belongs to and where the joints are.</summary>
        class HandShape
        {
            public readonly Dictionary<int, Piece> Pieces = new Dictionary<int, Piece>();
            public Vector3 Knuckles, MiddleJoints, ThumbRoot;
            public Vector3 Bend;       // finger bend axis: positive rotation closes the hand
            public Vector3 ThumbTuck;  // thumb axis: positive rotation folds it across the fingers
        }

        /// <summary>
        /// Classify the vertices of one hand. `positions` are in any common frame; `wrist` is the hand's start
        /// and `along` runs toward the fingertips.
        /// </summary>
        static HandShape Shape(Vector3[] positions, int[] triangles, IEnumerable<int> handVertices, Vector3 wrist, Vector3 along)
        {
            var shape = new HandShape();
            var verts = handVertices.ToList();
            float T(int i) => Vector3.Dot(positions[i] - wrist, along);
            float scale = verts.Max(T) / Tip;   // the recipe's hand units to this mesh's metres

            // Pieces: vertices joined by triangles (flat shading splits vertices, so weld by position first).
            var island = Islands(positions, triangles);
            var groups = verts.GroupBy(i => island[i]).ToList();

            // Palm direction: fingertips curl a little toward the palm in the model.
            var tips = verts.Where(i => T(i) > TipSplit * scale).ToList();
            Vector3 tipCentre = tips.Aggregate(Vector3.zero, (s, i) => s + positions[i]) / Mathf.Max(1, tips.Count);
            Vector3 palm = Vector3.ProjectOnPlane(tipCentre - wrist, along).normalized;
            Vector3 side = Vector3.Cross(along, palm).normalized;

            // The thumb is the piece reaching furthest out to one side behind the knuckles.
            float SideOf(IEnumerable<int> g) => g.Average(i => Vector3.Dot(positions[i] - wrist, side));
            var behind = groups.Where(g => g.Max(T) < Knuckle * scale).ToList();
            var thumb = behind.OrderByDescending(g => Mathf.Abs(SideOf(g))).FirstOrDefault();
            float thumbSign = thumb != null && SideOf(thumb) < 0f ? -1f : 1f;

            foreach (var g in groups)
            {
                if (g == thumb)
                {
                    foreach (int i in g) shape.Pieces[i] = Piece.Thumb;
                    continue;
                }
                bool finger = g.Max(T) > TipSplit * scale && g.Min(T) > (Knuckle - 0.03f) * scale;
                foreach (int i in g)
                    shape.Pieces[i] = !finger || T(i) <= (Knuckle + 0.004f) * scale ? Piece.Palm
                        : T(i) > TipSplit * scale ? Piece.FingerTip : Piece.Finger;
            }

            shape.Knuckles = wrist + along * (Knuckle * scale);
            shape.MiddleJoints = wrist + along * (Middle * scale) + palm * (0.010f * scale);
            shape.ThumbRoot = wrist + along * (ThumbBase * scale) + side * (thumbSign * ThumbSide * scale);
            // Rotating `along` about cross(along, palm) by a positive angle turns it toward the palm.
            shape.Bend = Vector3.Cross(along, palm).normalized;
            // The thumb sticks out sideways; turning about the hand's axis folds it over the palm side.
            Vector3 thumbOut = side * thumbSign;
            shape.ThumbTuck = Vector3.Dot(Vector3.Cross(along, thumbOut), palm) > 0f ? along : -along;
            return shape;
        }

        static int[] Islands(Vector3[] positions, int[] triangles)
        {
            var parent = Enumerable.Range(0, positions.Length).ToArray();
            int Find(int x) { while (parent[x] != x) x = parent[x] = parent[parent[x]]; return x; }
            void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[a] = b; }
            var byPosition = new Dictionary<Vector3Int, int>();
            for (int i = 0; i < positions.Length; i++)
            {
                var key = Vector3Int.RoundToInt(positions[i] * 100000f);
                if (byPosition.TryGetValue(key, out int other)) Union(i, other); else byPosition[key] = i;
            }
            for (int t = 0; t < triangles.Length; t += 3)
            {
                Union(triangles[t], triangles[t + 1]);
                Union(triangles[t], triangles[t + 2]);
            }
            return Enumerable.Range(0, positions.Length).Select(Find).ToArray();
        }

        // ---- the body (third person): finger bones + FistCurl ----------------------------------------

        public static void AddToBody(GameObject model)
        {
            var smr = model.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(r => r.name.EndsWith("Body"));
            if (smr == null) return;
            var mesh = Object.Instantiate(smr.sharedMesh);
            mesh.name = "SK_Fisherman_Body_Fists";
            var bones = smr.bones.ToList();
            var bindposes = mesh.bindposes.ToList();
            var weights = mesh.boneWeights;
            var world = mesh.vertices.Select(v => smr.transform.TransformPoint(v)).ToArray();   // the model is at its bind pose
            var triangles = mesh.triangles;
            var curl = model.AddComponent<FistCurl>();

            for (int h = 0; h < 2; h++)
            {
                string side = h == 0 ? "Left" : "Right";
                var hand = Find(model.transform, side + "Hand");
                var lower = Find(model.transform, side + "LowerArm");
                int handIndex = bones.IndexOf(hand);
                if (hand == null || lower == null || handIndex < 0) continue;
                Vector3 along = (hand.position - lower.position).normalized;
                var mine = Enumerable.Range(0, world.Length).Where(i => weights[i].boneIndex0 == handIndex && weights[i].weight0 > 0.5f);
                var shape = Shape(world, triangles, mine, hand.position, along);

                Transform Bone(string name, Transform parent, Vector3 at)
                {
                    var bone = new GameObject(name).transform;
                    bone.SetParent(parent, false);
                    bone.position = at;
                    bones.Add(bone);
                    bindposes.Add(bone.worldToLocalMatrix * smr.transform.localToWorldMatrix);
                    return bone;
                }
                var fingers = Bone(side + "Fingers", hand, shape.Knuckles);
                var tips = Bone(side + "FingerTips", fingers, shape.MiddleJoints);
                var thumb = Bone(side + "Thumb", hand, shape.ThumbRoot);
                int fi = bones.IndexOf(fingers), ti = bones.IndexOf(tips), th = bones.IndexOf(thumb);
                foreach (var pair in shape.Pieces)
                {
                    int bone = pair.Value == Piece.Finger ? fi : pair.Value == Piece.FingerTip ? ti : pair.Value == Piece.Thumb ? th : -1;
                    if (bone >= 0) weights[pair.Key] = new BoneWeight { boneIndex0 = bone, weight0 = 1f };
                }
                curl.hands[h] = new FistCurl.Hand
                {
                    fingers = fingers, tips = tips, thumb = thumb,
                    fingersAxis = fingers.InverseTransformDirection(shape.Bend),
                    tipsAxis = tips.InverseTransformDirection(shape.Bend),
                    thumbAxis = thumb.InverseTransformDirection(shape.ThumbTuck)
                };
            }

            mesh.bindposes = bindposes.ToArray();
            mesh.boneWeights = weights;
            AssetDatabase.DeleteAsset(BodyMeshPath);
            AssetDatabase.CreateAsset(mesh, BodyMeshPath);
            smr.bones = bones.ToArray();
            smr.sharedMesh = mesh;
        }

        // ---- the first-person arm: a fist baked into the mesh ---------------------------------------

        /// <summary>The first-person arm with its hand closed into a fist, saved as a prefab next to the generated assets.</summary>
        public static GameObject FirstPersonFist(GameObject armModel, float knuckles = 85f, float middles = 90f, float thumbTuck = 50f)
        {
            if (armModel == null) return null;
            var copy = Object.Instantiate(armModel);
            var filter = copy.GetComponentInChildren<MeshFilter>();
            if (filter == null) { Object.DestroyImmediate(copy); return armModel; }
            var mesh = Object.Instantiate(filter.sharedMesh);
            mesh.name = "FisherFPArm_Fist";
            var positions = mesh.vertices;
            var normals = mesh.normals;
            // The recipe's first-person hand: centred on the origin, fingers toward -Y, forearm toward +Y.
            Vector3 along = Vector3.down;
            float reach = positions.Max(p => Vector3.Dot(p, along));               // fingertips
            float scale = reach / (Tip - 0.07f);
            Vector3 wrist = -along * (0.07f * scale);
            var hand = Enumerable.Range(0, positions.Length).Where(i => Vector3.Dot(positions[i] - wrist, along) > -0.01f);
            var shape = Shape(positions, mesh.triangles, hand, wrist, along);

            Quaternion knuckleTurn = Quaternion.AngleAxis(knuckles, shape.Bend);
            Quaternion middleTurn = Quaternion.AngleAxis(middles, shape.Bend);
            Quaternion thumbTurn = Quaternion.AngleAxis(thumbTuck, shape.ThumbTuck);
            Vector3 middleAfter = shape.Knuckles + knuckleTurn * (shape.MiddleJoints - shape.Knuckles);
            foreach (var pair in shape.Pieces)
            {
                int i = pair.Key;
                switch (pair.Value)
                {
                    case Piece.Finger:
                        positions[i] = shape.Knuckles + knuckleTurn * (positions[i] - shape.Knuckles);
                        normals[i] = knuckleTurn * normals[i];
                        break;
                    case Piece.FingerTip:
                        Vector3 p = shape.Knuckles + knuckleTurn * (positions[i] - shape.Knuckles);
                        positions[i] = middleAfter + knuckleTurn * middleTurn * Quaternion.Inverse(knuckleTurn) * (p - middleAfter);
                        normals[i] = knuckleTurn * middleTurn * normals[i];
                        break;
                    case Piece.Thumb:
                        positions[i] = shape.ThumbRoot + thumbTurn * (positions[i] - shape.ThumbRoot);
                        normals[i] = thumbTurn * normals[i];
                        break;
                }
            }
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            AssetDatabase.DeleteAsset(ArmMeshPath);
            AssetDatabase.CreateAsset(mesh, ArmMeshPath);
            filter.sharedMesh = mesh;
            copy.name = "FisherFPArm_Fist";
            var prefab = PrefabUtility.SaveAsPrefabAsset(copy, ArmPrefabPath);
            Object.DestroyImmediate(copy);
            return prefab;
        }

        static Transform Find(Transform parent, string name)
        {
            if (parent.name == name) return parent;
            foreach (Transform child in parent)
            {
                var found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
