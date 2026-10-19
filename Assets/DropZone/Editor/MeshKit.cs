using System.Collections.Generic;
using UnityEngine;

namespace DropZone.EditorTools
{
    /// <summary>
    /// Builds stylised meshes from simple solids (boxes, cylinders, spheres, roof prisms, tori) with
    /// one submesh per material. UVs on boxes are in world metres so textures tile consistently
    /// (a brick is the same size on every house).
    /// </summary>
    public class MeshKit
    {
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Vector3> normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<Color> colours = new List<Color>();
        readonly Dictionary<Material, List<int>> tris = new Dictionary<Material, List<int>>();
        readonly List<Material> order = new List<Material>();
        public Matrix4x4 Transform = Matrix4x4.identity;
        public Color VertexColour = Color.white;

        List<int> T(Material m)
        {
            if (!tris.TryGetValue(m, out var list)) { list = new List<int>(); tris[m] = list; order.Add(m); }
            return list;
        }

        int V(Vector3 p, Vector3 n, Vector2 uv)
        {
            verts.Add(Transform.MultiplyPoint3x4(p));
            normals.Add(Transform.MultiplyVector(n).normalized);
            uvs.Add(uv);
            colours.Add(VertexColour);
            return verts.Count - 1;
        }

        void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            var t = T(m);
            int i0 = V(a, n, ua), i1 = V(b, n, ub), i2 = V(c, n, uc), i3 = V(d, n, ud);
            t.Add(i0); t.Add(i1); t.Add(i2); t.Add(i0); t.Add(i2); t.Add(i3);
        }

        /// <summary>Axis-aligned box (in the current transform). uvScale: texture tiles per metre.</summary>
        public void Box(Material m, Vector3 centre, Vector3 size, float uvScale = 1f, bool bottom = false)
        {
            Vector3 h = size * 0.5f, c = centre;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z), p100 = c + new Vector3(h.x, -h.y, -h.z), p010 = c + new Vector3(-h.x, h.y, -h.z), p110 = c + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z), p101 = c + new Vector3(h.x, -h.y, h.z), p011 = c + new Vector3(-h.x, h.y, h.z), p111 = c + new Vector3(h.x, h.y, h.z);
            Vector2 U(float a, float b) => new Vector2(a * uvScale, b * uvScale);
            // -Z, +Z, -X, +X, +Y, (-Y)
            Quad(m, p000, p010, p110, p100, Vector3.back, U(p000.x, p000.y), U(p010.x, p010.y), U(p110.x, p110.y), U(p100.x, p100.y));
            Quad(m, p101, p111, p011, p001, Vector3.forward, U(-p101.x, p101.y), U(-p111.x, p111.y), U(-p011.x, p011.y), U(-p001.x, p001.y));
            Quad(m, p001, p011, p010, p000, Vector3.left, U(-p001.z, p001.y), U(-p011.z, p011.y), U(-p010.z, p010.y), U(-p000.z, p000.y));
            Quad(m, p100, p110, p111, p101, Vector3.right, U(p100.z, p100.y), U(p110.z, p110.y), U(p111.z, p111.y), U(p101.z, p101.y));
            Quad(m, p010, p011, p111, p110, Vector3.up, U(p010.x, p010.z), U(p011.x, p011.z), U(p111.x, p111.z), U(p110.x, p110.z));
            if (bottom) Quad(m, p000, p100, p101, p001, Vector3.down, U(p000.x, p000.z), U(p100.x, p100.z), U(p101.x, p101.z), U(p001.x, p001.z));
        }

        /// <summary>Box rotated about Y by `yaw` degrees around its centre.</summary>
        public void BoxRotated(Material m, Vector3 centre, Vector3 size, Quaternion rot, float uvScale = 1f)
        {
            var saved = Transform;
            Transform = saved * Matrix4x4.TRS(centre, rot, Vector3.one);
            Box(m, Vector3.zero, size, uvScale);
            Transform = saved;
        }

        /// <summary>A flat quad facing `normal` (for road markings, signs, glass).</summary>
        public void Plane(Material m, Vector3 centre, Vector2 size, Vector3 normal, Vector3 up, bool fitUv = true)
        {
            var right = Vector3.Cross(normal, up).normalized;
            var u = up.normalized * size.y * 0.5f;
            var r = right * size.x * 0.5f;
            if (fitUv) Quad(m, centre - r - u, centre - r + u, centre + r + u, centre + r - u, normal, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
            else Quad(m, centre - r - u, centre - r + u, centre + r + u, centre + r - u, normal, new Vector2(0, 0), new Vector2(0, size.y), new Vector2(size.x, size.y), new Vector2(size.x, 0));
        }

        /// <summary>Cylinder between two points.</summary>
        public void Cylinder(Material m, Vector3 a, Vector3 b, float radius, int seg = 12, bool caps = true, float radiusB = -1f)
        {
            if (radiusB < 0) radiusB = radius;
            var axis = (b - a).normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var side2 = Vector3.Cross(axis, side);
            var t = T(m);
            int start = verts.Count;
            for (int i = 0; i <= seg; i++)
            {
                float ang = i * Mathf.PI * 2f / seg;
                var dir = side * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
                V(a + dir * radius, dir, new Vector2((float)i / seg, 0));
                V(b + dir * radiusB, dir, new Vector2((float)i / seg, 1));
            }
            for (int i = 0; i < seg; i++)
            {
                int k = start + i * 2;
                t.Add(k); t.Add(k + 3); t.Add(k + 1); t.Add(k); t.Add(k + 2); t.Add(k + 3);
            }
            if (!caps) return;
            foreach (var (c, n, r) in new[] { (a, -axis, radius), (b, axis, radiusB) })
            {
                int centre = V(c, n, new Vector2(0.5f, 0.5f));
                int first = verts.Count;
                for (int i = 0; i <= seg; i++)
                {
                    float ang = i * Mathf.PI * 2f / seg;
                    var dir = side * Mathf.Cos(ang) + side2 * Mathf.Sin(ang);
                    V(c + dir * r, n, new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
                }
                for (int i = 0; i < seg; i++)
                {
                    if (n == axis) { t.Add(centre); t.Add(first + i); t.Add(first + i + 1); }
                    else { t.Add(centre); t.Add(first + i + 1); t.Add(first + i); }
                }
            }
        }

        /// <summary>Ellipsoid.</summary>
        public void Sphere(Material m, Vector3 centre, Vector3 radii, int seg = 12, int rings = 8)
        {
            var t = T(m);
            int start = verts.Count;
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                for (int s = 0; s <= seg; s++)
                {
                    float th = Mathf.PI * 2 * s / seg;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th));
                    var p = Vector3.Scale(n, radii);
                    V(centre + p, new Vector3(n.x / radii.x, n.y / radii.y, n.z / radii.z).normalized, new Vector2((float)s / seg, (float)r / rings));
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < seg; s++)
                {
                    int a = start + r * (seg + 1) + s, b = a + seg + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
        }

        /// <summary>A torus around `axis` (wheels, tyres).</summary>
        public void Torus(Material m, Vector3 centre, Vector3 axis, float major, float minor, int seg = 20, int tube = 8)
        {
            axis = axis.normalized;
            var side = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
            var side2 = Vector3.Cross(axis, side);
            var t = T(m);
            int start = verts.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                var dir = side * Mathf.Cos(a) + side2 * Mathf.Sin(a);
                for (int j = 0; j <= tube; j++)
                {
                    float b = j * Mathf.PI * 2 / tube;
                    var n = dir * Mathf.Cos(b) + axis * Mathf.Sin(b);
                    V(centre + dir * major + n * minor, n, new Vector2((float)i / seg, (float)j / tube));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < tube; j++)
                {
                    int a = start + i * (tube + 1) + j, b = a + tube + 1;
                    t.Add(a); t.Add(b); t.Add(a + 1); t.Add(a + 1); t.Add(b); t.Add(b + 1);
                }
        }

        /// <summary>
        /// A pitched roof running along Z: from x0 to x1 at eaves height y0, ridge at (x0+x1)/2, height h.
        /// Includes the two gable triangles. Overhang extends the slopes past the walls.
        /// </summary>
        public void GableRoof(Material roof, Material gable, float x0, float x1, float z0, float z1, float y0, float h, float overhang = 0.25f)
        {
            float xm = (x0 + x1) * 0.5f;
            float run = xm - x0;
            float ox = overhang * run / Mathf.Sqrt(run * run + h * h);
            float oy = overhang * h / Mathf.Sqrt(run * run + h * h);
            var e0 = new Vector3(x0 - ox, y0 - oy, 0); var e1 = new Vector3(x1 + ox, y0 - oy, 0); var r = new Vector3(xm, y0 + h, 0);
            float len = Mathf.Sqrt(run * run + h * h) + overhang;
            var nl = new Vector3(-h, run, 0).normalized; var nr = new Vector3(h, run, 0).normalized;
            float zA = z0 - overhang * 0.4f, zB = z1 + overhang * 0.4f;
            Quad(roof, e0 + Vector3.forward * zB, r + Vector3.forward * zB, r + Vector3.forward * zA, e0 + Vector3.forward * zA, nl,
                new Vector2(zB, 0), new Vector2(zB, len), new Vector2(zA, len), new Vector2(zA, 0));
            Quad(roof, e1 + Vector3.forward * zA, r + Vector3.forward * zA, r + Vector3.forward * zB, e1 + Vector3.forward * zB, nr,
                new Vector2(zA, 0), new Vector2(zA, len), new Vector2(zB, len), new Vector2(zB, 0));
            if (gable == null) return;
            // gable ends (walls), flush with the house
            var ta = T(gable);
            foreach (var (z, n) in new[] { (z0, Vector3.back), (z1, Vector3.forward) })
            {
                int a = V(new Vector3(x0, y0, z), n, new Vector2(x0, y0)), b = V(new Vector3(x1, y0, z), n, new Vector2(x1, y0)), c = V(new Vector3(xm, y0 + h, z), n, new Vector2(xm, y0 + h));
                if (n == Vector3.back) { ta.Add(a); ta.Add(c); ta.Add(b); } else { ta.Add(a); ta.Add(b); ta.Add(c); }
            }
        }

        // ------------------------------------------------------------------ DropZone pieces
        // Winding rule used throughout: a triangle (a, b, c) faces along Cross(b - a, c - a).

        void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc)
        {
            var t = T(m);
            t.Add(V(a, n, ua)); t.Add(V(b, n, ub)); t.Add(V(c, n, uc));
        }

        /// <summary>
        /// Extrude a star-shaped outline (counter-clockwise in XY, around the origin) between z0 (front,
        /// facing -Z) and z1 (back). Used for embossed symbols.
        /// </summary>
        public void Prism(Material m, Vector2[] outline, float z0, float z1, bool front = true, bool back = true)
        {
            int n = outline.Length;
            for (int i = 0; i < n; i++)
            {
                var a = outline[i]; var b = outline[(i + 1) % n];
                Vector3 a0 = new Vector3(a.x, a.y, z0), b0 = new Vector3(b.x, b.y, z0), a1 = new Vector3(a.x, a.y, z1), b1 = new Vector3(b.x, b.y, z1);
                // front cap (fan from the centre); clockwise as seen from -Z
                if (front) Tri(m, new Vector3(0, 0, z0), b0, a0, Vector3.back, new Vector2(0.5f, 0.5f), b * 0.5f + Vector2.one * 0.5f, a * 0.5f + Vector2.one * 0.5f);
                if (back) Tri(m, new Vector3(0, 0, z1), a1, b1, Vector3.forward, new Vector2(0.5f, 0.5f), a * 0.5f + Vector2.one * 0.5f, b * 0.5f + Vector2.one * 0.5f);
                // side wall, facing outwards
                var edge = b - a;
                var nrm = new Vector3(edge.y, -edge.x, 0).normalized;
                Quad(m, a0, b0, b1, a1, nrm, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1));
            }
        }

        /// <summary>A flat ring (washer) between radii r0 and r1, extruded from z0 to z1, axis along Z.</summary>
        public void Annulus(Material m, Vector3 centre, float r0, float r1, float z0, float z1, int seg = 32)
        {
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg, b = (i + 1) * Mathf.PI * 2f / seg;
                var da = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0); var db = new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0);
                Vector3 P(Vector3 d, float r, float z) => centre + d * r + new Vector3(0, 0, z);
                Quad(m, P(da, r0, z0), P(db, r0, z0), P(db, r1, z0), P(da, r1, z0), Vector3.back, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                Quad(m, P(da, r0, z1), P(da, r1, z1), P(db, r1, z1), P(db, r0, z1), Vector3.forward, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                // outer wall faces outwards, inner wall faces the centre
                Quad(m, P(da, r1, z0), P(db, r1, z0), P(db, r1, z1), P(da, r1, z1), (da + db).normalized, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
                Quad(m, P(da, r0, z0), P(da, r0, z1), P(db, r0, z1), P(db, r0, z0), -(da + db).normalized, Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
            }
        }

        /// <summary>
        /// A plate pierced by a grid of round holes (the board face). `firstCell` is the centre of the
        /// bottom-left hole. Front face at z0 faces -Z, back face at z1 faces +Z; hole walls face inwards.
        /// seg must be a multiple of 8 so the square cell corners are hit exactly.
        /// </summary>
        public void HolePlate(Material m, int cols, int rows, float cell, float holeR, Vector2 firstCell, float z0, float z1, int seg = 32)
        {
            float h = cell * 0.5f;
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows; r++)
                {
                    var ctr = new Vector3(firstCell.x + c * cell, firstCell.y + r * cell, 0);
                    for (int i = 0; i < seg; i++)
                    {
                        float a = i * Mathf.PI * 2f / seg, b = (i + 1) * Mathf.PI * 2f / seg;
                        var da = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0); var db = new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0);
                        // matching points on the square cell edge
                        Vector3 Sq(Vector3 d) { float k = h / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y)); return d * k; }
                        Vector3 ca = ctr + da * holeR, cb = ctr + db * holeR, sa = ctr + Sq(da), sb = ctr + Sq(db);
                        Vector3 Z(Vector3 p, float z) => new Vector3(p.x, p.y, z);
                        Vector2 Uv(Vector3 p) => new Vector2(p.x, p.y);
                        Quad(m, Z(ca, z0), Z(cb, z0), Z(sb, z0), Z(sa, z0), Vector3.back, Uv(ca), Uv(cb), Uv(sb), Uv(sa));
                        Quad(m, Z(ca, z1), Z(sa, z1), Z(sb, z1), Z(cb, z1), Vector3.forward, Uv(ca), Uv(sa), Uv(sb), Uv(cb));
                        // hole wall, facing the hole's centre
                        var nIn = -(da + db).normalized;
                        Quad(m, Z(ca, z0), Z(ca, z1), Z(cb, z1), Z(cb, z0), nIn, new Vector2(a, 0), new Vector2(a, 1), new Vector2(b, 1), new Vector2(b, 0));
                    }
                }
        }

        /// <summary>A flat quad facing -Z (for rings, beams and decals), centred on `centre`.</summary>
        public void FrontQuad(Material m, Vector3 centre, Vector2 size)
        {
            var hx = size.x * 0.5f; var hy = size.y * 0.5f;
            Quad(m, centre + new Vector3(-hx, -hy, 0), centre + new Vector3(-hx, hy, 0), centre + new Vector3(hx, hy, 0), centre + new Vector3(hx, -hy, 0), Vector3.back,
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }

        public bool Empty => verts.Count == 0;

        public (Mesh mesh, Material[] materials) Build(string name)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colours);
            mesh.subMeshCount = order.Count;
            for (int i = 0; i < order.Count; i++) mesh.SetTriangles(tris[order[i]], i);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return (mesh, order.ToArray());
        }
    }
}
