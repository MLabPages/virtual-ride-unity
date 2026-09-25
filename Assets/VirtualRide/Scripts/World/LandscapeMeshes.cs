using System.Collections.Generic;
using UnityEngine;

namespace VirtualRide.World
{
    /// <summary>Shared, deterministic meshes; no imported assets or per-tree materials.</summary>
    internal sealed class LandscapeMeshes
    {
        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Color> _colors = new List<Color>();
        private readonly List<int> _triangles = new List<int>();
        private readonly List<Vector3> _normalOverrides = new List<Vector3>();

        public Mesh Finish(string name)
        {
            var mesh = new Mesh { name = name };
            if (_vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colors);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            Vector3[] normals = mesh.normals;
            for (int i=0;i<normals.Length;i++)
                if (_normalOverrides[i].sqrMagnitude > .5f) normals[i] = _normalOverrides[i];
            mesh.normals = normals;
            mesh.RecalculateBounds();
            return mesh;
        }

        private void Vertex(Vector3 position, Color color, Vector3 normal = default)
        {
            _vertices.Add(position); _colors.Add(color); _normalOverrides.Add(normal);
        }
        private void Triangle(int a, int b, int c) { _triangles.Add(a); _triangles.Add(b); _triangles.Add(c); }

        /// <summary>
        /// Flat rectangle facing the viewer. axisX is the viewer's right, axisY the viewer's up;
        /// the front face points toward the viewer (-Cross(axisX, axisY)).
        /// </summary>
        public void Quad(Vector3 center, Vector3 axisX, Vector3 axisY, float halfX, float halfY, Color color)
        {
            Vector3 normal = -Vector3.Cross(axisX, axisY).normalized;
            int first = _vertices.Count;
            Vertex(center - axisX * halfX - axisY * halfY, color, normal);
            Vertex(center + axisX * halfX - axisY * halfY, color, normal);
            Vertex(center - axisX * halfX + axisY * halfY, color, normal);
            Vertex(center + axisX * halfX + axisY * halfY, color, normal);
            Triangle(first + 2, first + 3, first + 1);
            Triangle(first + 2, first + 1, first);
        }

        public void Branch(Vector3 from, Vector3 to, float radius, float tip, Color color)
        {
            int first = _vertices.Count;
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, (to-from).normalized);
            const int sides = 8;
            for (int ring=0; ring<2; ring++)
            for (int side=0; side<=sides; side++)
            {
                float a=side * Mathf.PI*2/sides;
                Vector3 offset=rotation * new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)) * (ring==0 ? radius : tip);
                Vertex((ring==0 ? from : to)+offset, color);
            }
            for (int side=0; side<sides; side++)
            {
                int a=first+side, b=a+sides+1;
                Triangle(a,b,a+1); Triangle(a+1,b,b+1);
            }
        }

        public void Crown(Vector3 center, Vector3 scale, int seed, Color tint)
        {
            int first=_vertices.Count;
            const int rings=9, sides=14;
            for (int ring=0; ring<=rings; ring++)
            for (int side=0; side<=sides; side++)
            {
                float a=side*Mathf.PI*2/sides, b=ring*Mathf.PI/rings;
                Vector3 n=new Vector3(Mathf.Sin(b)*Mathf.Cos(a),Mathf.Cos(b),Mathf.Sin(b)*Mathf.Sin(a));
                float roughness=.82f + Mathf.PerlinNoise(n.x*3.3f+seed, n.z*3.3f+n.y*2.7f+seed)*.36f;
                Color c=tint * Mathf.Lerp(.78f,1.16f,(n.y+1)*.5f); c.a=1;
                Vertex(center+Vector3.Scale(n,scale)*roughness,c);
            }
            for (int ring=0; ring<rings; ring++)
            for (int side=0; side<sides; side++)
            {
                int a=first+ring*(sides+1)+side, b=a+sides+1;
                Triangle(a,a+1,b); Triangle(a+1,b+1,b);
            }
        }

        public void Grass(Vector3 position, float height, Color color, float yaw)
        {
            for (int blade=0; blade<3; blade++)
            {
                float a=yaw+blade*Mathf.PI/3;
                Vector3 right=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*.07f;
                int first=_vertices.Count;
                // Opposite faces share vertices, so averaged face normals would cancel to zero.
                Vertex(position-right,color*.8f,Vector3.up); Vertex(position+right,color*.8f,Vector3.up);
                Vertex(position+new Vector3(right.z, height, -right.x),color,Vector3.up);
                Triangle(first,first+2,first+1); Triangle(first,first+1,first+2);
            }
        }

        public static float TerrainHeight(float x, float z)
        {
            float r=Mathf.Sqrt(x*x/(315f*315f)+z*z/(245f*245f));
            float rise=Mathf.SmoothStep(0,1,Mathf.InverseLerp(1.02f,2f,r));
            // PerlinNoise may return values slightly outside 0..1. A negative base in
            // Pow(...,1.8) is NaN, and 0*NaN stays NaN, which broke the whole terrain on Quest.
            float broad=Mathf.Clamp01(Mathf.PerlinNoise(x*.0034f+11.7f,z*.0034f+7.3f));
            float detail=Mathf.Clamp01(Mathf.PerlinNoise(x*.011f+23,z*.011f+41));
            return -.04f+rise*(20+Mathf.Pow(broad,1.8f)*225+detail*22);
        }

        public static Mesh Terrain()
        {
            var result=new LandscapeMeshes();
            const int columns=160, rows=144;
            for (int z=0; z<=rows; z++)
            for (int x=0; x<=columns; x++)
            {
                float px=(x-columns*.5f)*10, pz=(z-rows*.5f)*10;
                float y=TerrainHeight(px,pz);
                float distance=new Vector2(px,pz).magnitude;
                Color grass=Color.Lerp(new Color(.28f,.36f,.16f),new Color(.23f,.34f,.28f),Mathf.InverseLerp(290,520,distance));
                Color stone=Color.Lerp(grass,new Color(.40f,.47f,.47f),Mathf.InverseLerp(38,125,y));
                result.Vertex(new Vector3(px,y,pz),stone);
            }
            for (int z=0; z<rows; z++)
            for (int x=0; x<columns; x++)
            {
                int a=z*(columns+1)+x, b=a+columns+1;
                result.Triangle(a,b,a+1); result.Triangle(a+1,b,b+1);
            }
            return result.Finish("Rolling valley terrain");
        }

        public static Mesh Lake(Vector3 center, Vector3 forward, Vector3 right, float length, float width, float y)
        {
            var result=new LandscapeMeshes();
            result.Vertex(new Vector3(center.x,y,center.z),Color.white);
            const int sides=128;
            for (int i=0; i<=sides; i++)
            {
                float angle=i*Mathf.PI*2/sides;
                float variation=1+.025f*Mathf.Sin(angle*5)+.02f*Mathf.Sin(angle*7);
                Vector3 p=center+(forward*Mathf.Cos(angle)*length+right*Mathf.Sin(angle)*width)*variation;
                result.Vertex(new Vector3(p.x,y,p.z),Color.white);
                // forward cross right points up.
                if (i<sides) result.Triangle(0,i+1,i+2);
            }
            return result.Finish("Lakeshore oval");
        }
    }
}
