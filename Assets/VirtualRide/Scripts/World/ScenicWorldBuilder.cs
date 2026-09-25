using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualRide.Core;

namespace VirtualRide.World
{
    public sealed class ScenicWorldBuilder
    {
        public const string VisualRevision = "valley-zones-2026.09.2";
        private Mesh[] _treeTrunks;
        private Mesh[] _treeCrowns;
        private readonly RideRoute _route;
        private readonly Transform _root;
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly System.Random _random = new System.Random(20260812);
        // Footprints already taken by trees and landmarks: x, radius, z.
        private readonly List<Vector3> _occupied = new List<Vector3>();
        private Vector3 _windmillFoot;
        private Vector3 _towerFoot;
        private Vector3 _lakeCenter;
        private Vector3 _lakeForward;
        private Vector3 _lakeRight;

        private ScenicWorldBuilder(RideRoute route, Transform root)
        {
            _route = route;
            _root = root;
        }

        public static Transform Build(RideRoute route)
        {
            GameObject worldObject = new GameObject("Scenic Virtual World");
            ScenicWorldBuilder builder = new ScenicWorldBuilder(route, worldObject.transform);
            builder.ConfigureEnvironment();
            builder.ReserveLandmarkSpots();
            builder.CreateGround();
            builder.CreateRoad();
            builder.CreateLake();
            builder.CreateTrees();
            builder.CreateVillage();
            builder.CreateFields();
            builder.CreateVergeDetails();
            builder.CreateLandmarks();
            builder.CreateSpeedCues();
            builder.CreateForestConifers();
            builder.CreateLakeBoat();
            builder.CreateVillageStreet();
            builder.CreateMeadowWindmill();
            builder.CreateAutumnAvenue();
            return worldObject.transform;
        }

        private void ConfigureEnvironment()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(.78f, .86f, .87f);
            RenderSettings.fogStartDistance = 160f;
            RenderSettings.fogEndDistance = 780f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(.38f, .46f, .54f);
            RenderSettings.ambientEquatorColor = new Color(.28f, .33f, .27f);
            RenderSettings.ambientGroundColor = new Color(.14f, .17f, .11f);
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.skybox = new Material(Resources.Load<Shader>("VirtualRideSky")) { name = "Valley daylight" };
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowDistance = 85f;
            QualitySettings.shadowResolution = ShadowResolution.High;
            QualitySettings.shadowCascades = 2;
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.vSyncCount = 0;

            GameObject sunObject = new GameObject("Morning Sun");
            sunObject.transform.SetParent(_root, false);
            sunObject.transform.rotation = Quaternion.LookRotation(-new Vector3(.34f, .64f, -.42f).normalized);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, .94f, .82f);
            sun.intensity = 1.02f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = .65f;
            sun.shadowBias = .035f;
            sun.shadowNormalBias = .3f;
            RenderSettings.sun = sun;
        }

        private void CreateGround()
        {
            Material terrain = GetMaterial("Valley meadow", Color.white, .02f, 0);
            terrain.SetFloat("_VertexTint", 1);
            terrain.SetFloat("_DetailStrength", .65f);
            terrain.SetFloat("_DetailScale", .12f);
            MeshObject("Rolling valley", _root, LandscapeMeshes.Terrain(), terrain, ShadowCastingMode.Off);
        }

        private void CreateRoad()
        {
            GameObject shoulders = CreateRibbon(
                "Gravel shoulders",
                _route.RoadHalfWidth + 0.72f,
                0.012f,
                GetMaterial("Gravel", new Color(0.54f, 0.48f, 0.37f), 0.08f, 0f));
            SetShadowMode(shoulders, ShadowCastingMode.Off, true);

            GameObject road = CreateRibbon(
                "Cycling road",
                _route.RoadHalfWidth,
                0.035f,
                GetMaterial("Warm asphalt", new Color(0.155f, 0.17f, 0.17f), 0.22f, 0.04f));
            SetShadowMode(road, ShadowCastingMode.Off, true);
            road.GetComponent<Renderer>().sharedMaterial.SetFloat("_DetailStrength", .45f);
            road.GetComponent<Renderer>().sharedMaterial.SetFloat("_DetailScale", 3f);
            shoulders.GetComponent<Renderer>().sharedMaterial.SetFloat("_DetailStrength", .8f);
            shoulders.GetComponent<Renderer>().sharedMaterial.SetFloat("_DetailScale", 2f);

            CreateEdgeLine("Left edge line", -_route.RoadHalfWidth + 0.22f);
            CreateEdgeLine("Right edge line", _route.RoadHalfWidth - 0.22f);
            CreateDashedCentreLine();
        }

        private GameObject CreateRibbon(string objectName, float halfWidth, float heightOffset, Material material)
        {
            int count = _route.Count;
            Vector3[] vertices = new Vector3[(count + 1) * 2];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[count * 6];

            for (int i = 0; i <= count; i++)
            {
                Vector3 point = _route.GetPoint(i);
                Vector3 right = _route.GetRightAtIndex(i);
                point.y += heightOffset;
                vertices[i * 2] = point - right * halfWidth;
                vertices[i * 2 + 1] = point + right * halfWidth;
                float v = i / 8f;
                uv[i * 2] = new Vector2(0f, v);
                uv[i * 2 + 1] = new Vector2(1f, v);

                if (i < count)
                {
                    int triangle = i * 6;
                    int vertex = i * 2;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + 2;
                    triangles[triangle + 2] = vertex + 1;
                    triangles[triangle + 3] = vertex + 1;
                    triangles[triangle + 4] = vertex + 2;
                    triangles[triangle + 5] = vertex + 3;
                }
            }

            Mesh mesh = new Mesh { name = objectName + " mesh" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            GameObject ribbon = new GameObject(objectName);
            ribbon.transform.SetParent(_root, false);
            ribbon.AddComponent<MeshFilter>().sharedMesh = mesh;
            ribbon.AddComponent<MeshRenderer>().sharedMaterial = material;
            return ribbon;
        }

        private void CreateEdgeLine(string objectName, float lateralOffset)
        {
            int count = _route.Count;
            const float halfWidth = 0.055f;
            Vector3[] vertices = new Vector3[(count + 1) * 2];
            int[] triangles = new int[count * 6];

            for (int i = 0; i <= count; i++)
            {
                Vector3 point = _route.GetPoint(i);
                Vector3 right = _route.GetRightAtIndex(i);
                point += right * lateralOffset + Vector3.up * 0.055f;
                vertices[i * 2] = point - right * halfWidth;
                vertices[i * 2 + 1] = point + right * halfWidth;
                if (i < count)
                {
                    int triangle = i * 6;
                    int vertex = i * 2;
                    triangles[triangle] = vertex;
                    triangles[triangle + 1] = vertex + 2;
                    triangles[triangle + 2] = vertex + 1;
                    triangles[triangle + 3] = vertex + 1;
                    triangles[triangle + 4] = vertex + 2;
                    triangles[triangle + 5] = vertex + 3;
                }
            }

            Mesh mesh = new Mesh { name = objectName + " mesh", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            GameObject line = new GameObject(objectName);
            line.transform.SetParent(_root, false);
            line.AddComponent<MeshFilter>().sharedMesh = mesh;
            line.AddComponent<MeshRenderer>().sharedMaterial = GetMaterial(
                "Road edge paint", new Color(0.86f, 0.86f, 0.77f), 0.18f, 0f);
        }

        private void CreateDashedCentreLine()
        {
            Material paint = GetMaterial("Centre paint", new Color(0.92f, 0.77f, 0.26f), 0.2f, 0f);
            Transform parent = new GameObject("Dashed centre line").transform;
            parent.SetParent(_root, false);

            for (float distance = 8f; distance < _route.TotalLength; distance += 12f)
            {
                _route.Evaluate(distance, out Vector3 position, out Vector3 forward, out _);
                GameObject dash = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dash.name = "Centre dash";
                dash.transform.SetParent(parent, false);
                dash.transform.SetPositionAndRotation(
                    position + Vector3.up * 0.064f,
                    Quaternion.LookRotation(forward, Vector3.up));
                dash.transform.localScale = new Vector3(0.09f, 0.012f, 3.4f);
                dash.GetComponent<Renderer>().sharedMaterial = paint;
                DisableCollider(dash);
                SetShadowMode(dash, ShadowCastingMode.Off, false);
            }
        }

        private void CreateLake()
        {
            _route.Evaluate(_route.TotalLength*.32f, out Vector3 routePoint, out Vector3 forward, out Vector3 right);
            Vector3 center = routePoint-right*48f;
            Material shore=GetMaterial("Pale lakeshore",new Color(.63f,.61f,.43f),.03f,0);
            shore.SetFloat("_DetailStrength", .6f);
            MeshObject("Natural lakeshore",_root,LandscapeMeshes.Lake(center,forward,right,66,40,.002f),shore,ShadowCastingMode.Off);
            var water = new Material(Resources.Load<Shader>("VirtualRideWater")) { name="Rippling lake" };
            MeshObject("Lake",_root,LandscapeMeshes.Lake(center,forward,right,63,37,.009f),water,ShadowCastingMode.Off);

            Material reeds = GetMaterial("Reeds",new Color(.48f,.55f,.23f),.01f,0);
            var reedMesh = new LandscapeMeshes();
            for (int i=0; i<270; i++)
            {
                float angle=RandomRange(0,Mathf.PI*2);
                Vector3 p=center+forward*Mathf.Cos(angle)*RandomRange(63,65)+right*Mathf.Sin(angle)*RandomRange(37,39);
                p.y=.02f;
                reedMesh.Grass(p,RandomRange(.4f,1.1f),Color.white,angle);
            }
            MeshObject("Lakeside reeds",_root,reedMesh.Finish("Reeds"),reeds,ShadowCastingMode.Off);
            Material wood=GetMaterial("Jetty wood",new Color(.45f,.34f,.22f),.1f,0);
            for(int i=0;i<28;i++)
            {
                Vector3 p=center+right*(32+i*.46f); p.y=.28f;
                CreateField(_root,p,new Vector3(2.9f,.14f,.40f),Quaternion.LookRotation(right).eulerAngles.y,wood);
            }
            for(int i=0;i<4;i++)
            for(int side=-1;side<=1;side+=2)
            {
                Vector3 p=center+right*(32+i*4)+forward*side*1.25f;
                CreateFencePost(_root,p,Quaternion.identity,wood);
            }
        }

        private void CreateTrees()
        {
            Transform treeParent = new GameObject("Route trees").transform;
            treeParent.SetParent(_root, false);
            Material trunkMaterial = GetMaterial("Tree trunks", new Color(0.28f, 0.16f, 0.085f), 0.05f, 0f);
            Material[] foliage =
            {
                GetMaterial("Fresh foliage", new Color(0.20f, 0.48f, 0.18f), 0.02f, 0f),
                GetMaterial("Deep foliage", new Color(0.11f, 0.34f, 0.15f), 0.02f, 0f),
                GetMaterial("Sunlit foliage", new Color(0.36f, 0.57f, 0.19f), 0.02f, 0f)
            };

            for (int i = 0; i < _route.Count; i += 3)
            {
                float progress = i / (float)_route.Count;
                bool villageGap = progress > 0.43f && progress < 0.67f;
                bool lakeGap = progress > 0.24f && progress < 0.42f;
                bool avenueGap = progress > 0.845f;
                if (villageGap || lakeGap || avenueGap)
                {
                    continue;
                }

                Vector3 routePoint = _route.GetPoint(i);
                Vector3 right = _route.GetRightAtIndex(i);
                for (int side = -1; side <= 1; side += 2)
                {
                    if (_random.NextDouble() < 0.22)
                    {
                        continue;
                    }

                    float offset = RandomRange(7.5f, progress < 0.22f ? 20f : 35f);
                    Vector3 position = routePoint + right * side * offset;
                    position.y = 0f;
                    float scale = RandomRange(.8f, 1.35f);
                    if (!TryReserve(position, 2.7f * scale))
                    {
                        continue;
                    }

                    CreateTree(treeParent, position, scale, trunkMaterial, foliage[_random.Next(foliage.Length)]);
                }
            }

            // A few distant groves make open stretches feel less empty.
            for (int i = 0; i < 150; i++)
            {
                float angle = RandomRange(0f, Mathf.PI * 2f);
                float radius = RandomRange(205f, 300f);
                Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius * 0.78f);
                float scale = RandomRange(1.1f, 2f);
                if (DistanceToRoute(position) < 16f || InsideLake(position, 8f) || !TryReserve(position, 2.7f * scale))
                {
                    continue;
                }

                CreateTree(treeParent, position, scale, trunkMaterial, foliage[_random.Next(foliage.Length)]);
            }
        }

        /// <summary>Landmark and field footprints are reserved before any tree is placed.</summary>
        private void ReserveLandmarkSpots()
        {
            _route.Evaluate(_route.TotalLength * 0.32f, out Vector3 lakeRoute, out _lakeForward, out _lakeRight);
            _lakeCenter = lakeRoute - _lakeRight * 48f;

            _route.Evaluate(_route.TotalLength * 0.755f, out Vector3 windmillRoute, out _, out Vector3 windmillRight);
            _windmillFoot = windmillRoute - windmillRight * 36f;
            _windmillFoot.y = 0f;
            TryReserve(_windmillFoot, 6f);

            _route.Evaluate(_route.TotalLength * 0.555f, out Vector3 towerRoute, out _, out Vector3 towerRight);
            _towerFoot = towerRoute - towerRight * 27f;
            _towerFoot.y = 0f;
            TryReserve(_towerFoot, 3.5f);

            foreach (FlowerStrip strip in MeadowStrips())
            for (int part = -1; part <= 1; part++)
            {
                TryReserve(strip.Center + strip.Forward * part * 7f, 4.6f);
            }
        }

        private bool TryReserve(Vector3 position, float radius)
        {
            foreach (Vector3 taken in _occupied)
            {
                float dx = position.x - taken.x;
                float dz = position.z - taken.z;
                float minimum = (radius + taken.y) * 0.8f;
                if (dx * dx + dz * dz < minimum * minimum)
                {
                    return false;
                }
            }

            _occupied.Add(new Vector3(position.x, radius, position.z));
            return true;
        }

        private float DistanceToRoute(Vector3 position)
        {
            float best = float.MaxValue;
            for (int i = 0; i < _route.Count; i++)
            {
                Vector3 point = _route.GetPoint(i);
                float dx = position.x - point.x;
                float dz = position.z - point.z;
                best = Mathf.Min(best, dx * dx + dz * dz);
            }

            return Mathf.Sqrt(best);
        }

        private bool InsideLake(Vector3 position, float margin)
        {
            Vector3 local = position - _lakeCenter;
            float along = Vector3.Dot(local, _lakeForward) / (66f + margin);
            float across = Vector3.Dot(local, _lakeRight) / (40f + margin);
            return along * along + across * across < 1f;
        }

        private struct FlowerStrip
        {
            public Vector3 Center;
            public Vector3 Forward;
            public int Index;
        }

        private IEnumerable<FlowerStrip> MeadowStrips()
        {
            int index = 0;
            for (float distance = _route.TotalLength * 0.675f; distance < _route.TotalLength * 0.835f; distance += 26f, index++)
            {
                _route.Evaluate(distance, out Vector3 point, out Vector3 forward, out Vector3 right);
                int side = index % 2 == 0 ? 1 : -1;
                Vector3 center = point + right * side * 14.5f;
                center.y = 0.014f;
                yield return new FlowerStrip { Center = center, Forward = forward, Index = index };
            }
        }

        private void CreateTree(Transform parent, Vector3 position, float scale, Material trunk, Material leaves)
        {
            if (_treeCrowns == null) BuildTreeMeshes();
            Transform tree=new GameObject("Branching woodland tree").transform;
            tree.SetParent(parent,false);
            position.y=LandscapeMeshes.TerrainHeight(position.x,position.z);
            tree.position=position;
            tree.rotation=Quaternion.Euler(0,RandomRange(0,360),0);
            tree.localScale=Vector3.one*scale;
            int variant=_random.Next(_treeCrowns.Length);
            leaves.SetFloat("_VertexTint",1);
            MeshObject("Trunk and branches",tree,_treeTrunks[variant],trunk);
            MeshObject("Irregular canopy",tree,_treeCrowns[variant],leaves);
        }

        private void BuildTreeMeshes()
        {
            _treeTrunks=new Mesh[4]; _treeCrowns=new Mesh[4];
            for(int variant=0;variant<4;variant++)
            {
                var bark=new LandscapeMeshes(); var crown=new LandscapeMeshes();
                bark.Branch(Vector3.zero,new Vector3(.12f,5.6f,0),.23f,.08f,Color.white);
                for(int branch=0;branch<7;branch++)
                {
                    float angle=branch*2.39996f+variant;
                    Vector3 tip=new Vector3(Mathf.Cos(angle)*(1.5f+branch*.08f),4.3f+branch*.36f,Mathf.Sin(angle)*1.8f);
                    bark.Branch(new Vector3(.08f,2.8f+branch*.23f,0),tip,.1f,.02f,Color.white);
                    crown.Crown(tip+Vector3.up*.5f,new Vector3(1.7f,1.3f,1.55f),variant*13+branch,
                        Color.Lerp(new Color(.8f,.9f,.75f),Color.white,branch/6f));
                }
                crown.Crown(new Vector3(0,6.6f,0),new Vector3(1.8f,1.5f,1.7f),variant+17,Color.white);
                _treeTrunks[variant]=bark.Finish("Shared branches "+variant);
                _treeCrowns[variant]=crown.Finish("Shared canopy "+variant);
            }
        }

        private static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material,
            ShadowCastingMode shadows=ShadowCastingMode.On)
        {
            var obj=new GameObject(name);
            obj.transform.SetParent(parent,false);
            obj.AddComponent<MeshFilter>().sharedMesh=mesh;
            var renderer=obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial=material; renderer.shadowCastingMode=shadows;
            return obj;
        }

        private void CreateVillage()
        {
            Transform village = new GameObject("Small village").transform;
            village.SetParent(_root, false);
            Material[] walls =
            {
                GetMaterial("Cream walls", new Color(0.83f, 0.75f, 0.58f), 0.08f, 0f),
                GetMaterial("Terracotta walls", new Color(0.70f, 0.39f, 0.27f), 0.07f, 0f),
                GetMaterial("Pale walls", new Color(0.72f, 0.78f, 0.69f), 0.08f, 0f)
            };
            Material roof = GetMaterial("Village roofs", new Color(0.34f, 0.12f, 0.09f), 0.1f, 0f);
            Material windows = GetMaterial("Blue windows", new Color(0.18f, 0.36f, 0.43f), 0.65f, 0f);

            for (int i = 0; i < 11; i++)
            {
                float distance = _route.TotalLength * (0.46f + i * 0.016f);
                _route.Evaluate(distance, out Vector3 routePosition, out Vector3 forward, out Vector3 right);
                int side = i % 3 == 0 ? -1 : 1;
                Vector3 position = routePosition + right * side * RandomRange(11f, 20f);
                position.y = 0f;
                TryReserve(position, 3.5f);
                CreateHouse(village, position, Quaternion.LookRotation(right * side, Vector3.up),
                    RandomRange(0.85f, 1.2f), walls[i % walls.Length], roof, windows);
            }
        }

        private void CreateHouse(
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            float scale,
            Material wallMaterial,
            Material roofMaterial,
            Material windowMaterial)
        {
            Transform house = new GameObject("Village house").transform;
            house.SetParent(parent, false);
            house.SetPositionAndRotation(position, rotation);

            GameObject walls = GameObject.CreatePrimitive(PrimitiveType.Cube);
            walls.name = "Walls";
            walls.transform.SetParent(house, false);
            walls.transform.localPosition = new Vector3(0f, 1.65f * scale, 0f);
            walls.transform.localScale = new Vector3(4.6f * scale, 3.3f * scale, 3.7f * scale);
            walls.GetComponent<Renderer>().sharedMaterial = wallMaterial;
            DisableCollider(walls);

            GameObject roof = CreateRoofMesh(scale);
            roof.transform.SetParent(house, false);
            roof.transform.localPosition = new Vector3(0f, 3.3f * scale, 0f);
            roof.GetComponent<Renderer>().sharedMaterial = roofMaterial;

            Material trim=GetMaterial("Ivory trim",new Color(.9f,.86f,.72f),.1f,0);
            CreateField(house,new Vector3(0,.95f,-1.88f)*scale,new Vector3(.85f,1.9f,.10f)*scale,0,
                GetMaterial("Timber doors",new Color(.23f,.30f,.27f),.12f,0),true);
            CreateField(house,new Vector3(1.6f,4.4f,.55f)*scale,new Vector3(.58f,1.6f,.7f)*scale,0,wallMaterial,true);

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject window = GameObject.CreatePrimitive(PrimitiveType.Cube);
                window.name = "Window";
                window.transform.SetParent(house, false);
                window.transform.localPosition = new Vector3(side * 1.25f * scale, 1.75f * scale, -1.87f * scale);
                window.transform.localScale = new Vector3(0.82f * scale, 0.92f * scale, 0.04f * scale);
                window.GetComponent<Renderer>().sharedMaterial = windowMaterial;
                DisableCollider(window);
                CreateField(house,new Vector3(side*1.25f,1.75f,-1.91f)*scale,new Vector3(.055f,.96f,.06f)*scale,0,trim,true);
                CreateField(house,new Vector3(side*1.25f,1.75f,-1.92f)*scale,new Vector3(.86f,.055f,.06f)*scale,0,trim,true);
                CreateField(house,new Vector3(side*1.25f,1.24f,-1.95f)*scale,new Vector3(1f,.10f,.25f)*scale,0,trim,true);
            }
        }

        private static GameObject CreateRoofMesh(float scale)
        {
            Vector3[] vertices =
            {
                new Vector3(-2.7f, 0f, -2.25f) * scale,
                new Vector3(2.7f, 0f, -2.25f) * scale,
                new Vector3(-2.7f, 0f, 2.25f) * scale,
                new Vector3(2.7f, 0f, 2.25f) * scale,
                new Vector3(0f, 1.45f, -2.25f) * scale,
                new Vector3(0f, 1.45f, 2.25f) * scale
            };
            int[] triangles =
            {
                0, 4, 1,
                2, 3, 5,
                0, 2, 5, 0, 5, 4,
                1, 4, 5, 1, 5, 3
            };
            Mesh mesh = new Mesh { name = "Gabled roof mesh", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            GameObject roof = new GameObject("Roof");
            roof.AddComponent<MeshFilter>().sharedMesh = mesh;
            roof.AddComponent<MeshRenderer>();
            return roof;
        }

        private void CreateFields()
        {
            Transform fields = new GameObject("Colour fields").transform;
            fields.SetParent(_root, false);
            Material gold = GetMaterial("Golden field", new Color(0.73f, 0.61f, 0.18f), 0.02f, 0f);
            Material flowers = GetMaterial("Flower field", new Color(0.48f, 0.36f, 0.58f), 0.04f, 0f);
            CreateField(fields, new Vector3(55f, 0.012f, 12f), new Vector3(70f, 0.02f, 38f), 18f, gold);
            CreateField(fields, new Vector3(6f, 0.013f, 78f), new Vector3(56f, 0.02f, 32f), -12f, flowers);

            Material fenceMaterial = GetMaterial("Wood fence", new Color(0.46f, 0.29f, 0.15f), 0.08f, 0f);
            var rails=new LandscapeMeshes();
            Vector3 previous=Vector3.zero;
            for (int i = 0; i < 32; i++)
            {
                float distance = _route.TotalLength * (0.70f + i * 0.0045f);
                _route.Evaluate(distance, out Vector3 position, out Vector3 forward, out Vector3 right);
                position += right * 6.2f;
                CreateFencePost(fields, position, Quaternion.LookRotation(forward, Vector3.up), fenceMaterial);
                if(i>0)
                {
                    rails.Branch(previous+Vector3.up*.48f,position+Vector3.up*.48f,.055f,.055f,Color.white);
                    rails.Branch(previous+Vector3.up*1.05f,position+Vector3.up*1.05f,.055f,.055f,Color.white);
                }
                previous=position;
            }
            MeshObject("Meadow fence rails",fields,rails.Finish("Fence rails"),fenceMaterial);
            gold.SetFloat("_DetailStrength",.8f);
            gold.SetFloat("_DetailScale",.7f);
            flowers.SetFloat("_DetailStrength",.8f);
        }

        private static void CreateField(Transform parent, Vector3 position, Vector3 scale, float yaw, Material material, bool local=false)
        {
            GameObject field = GameObject.CreatePrimitive(PrimitiveType.Cube);
            field.name = "Cultivated field";
            field.transform.SetParent(parent, false);
            if(local)
            {
                field.transform.localPosition=position;
                field.transform.localRotation=Quaternion.Euler(0f,yaw,0f);
            }
            else
            {
                field.transform.position=position;
                field.transform.rotation=Quaternion.Euler(0f,yaw,0f);
            }
            field.transform.localScale = scale;
            field.GetComponent<Renderer>().sharedMaterial = material;
            DisableCollider(field);
            SetShadowMode(field, ShadowCastingMode.Off, true);
        }

        private static void CreateFencePost(Transform parent, Vector3 position, Quaternion rotation, Material material)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Fence post";
            post.transform.SetParent(parent, false);
            post.transform.SetPositionAndRotation(position + Vector3.up * 0.65f, rotation);
            post.transform.localScale = new Vector3(0.13f, 1.3f, 0.13f);
            post.GetComponent<Renderer>().sharedMaterial = material;
            DisableCollider(post);
        }

        private void CreateVergeDetails()
        {
            Material grass=GetMaterial("Meadow blades",Color.white,.02f,0);
            grass.SetFloat("_VertexTint",1);
            for(int chunk=0;chunk<24;chunk++)
            {
                var mesh=new LandscapeMeshes();
                for(int i=0;i<210;i++)
                {
                    float distance=_route.TotalLength*(chunk+RandomRange(0,1))/24;
                    _route.Evaluate(distance,out Vector3 p,out _,out Vector3 right);
                    int side=_random.Next(2)==0 ? -1 : 1;
                    p+=right*side*RandomRange(4.1f,7.4f);
                    p.y=.02f;
                    Color tint=Color.Lerp(new Color(.29f,.42f,.12f),new Color(.64f,.62f,.29f),RandomRange(0,1));
                    mesh.Grass(p,RandomRange(.16f,.52f),tint,RandomRange(0,6.28f));
                    if (i%7==0)
                        mesh.Crown(p+Vector3.up*.35f,Vector3.one*.065f,i,
                            chunk%3==0 ? new Color(.71f,.63f,.82f) : new Color(.96f,.89f,.55f));
                }
                MeshObject("Verge meadow "+chunk,_root,mesh.Finish("Batched meadow "+chunk),grass,ShadowCastingMode.Off);
            }
        }

        private void CreateLandmarks()
        {
            Transform landmarks = new GameObject("Route landmarks").transform;
            landmarks.SetParent(_root, false);
            Material post = GetMaterial("Sign post", new Color(0.30f, 0.20f, 0.10f), 0.12f, 0f);
            Material sign = GetMaterial("Route marker", new Color(0.08f, 0.55f, 0.59f), 0.38f, 0.05f);

            float[] progressMarkers = { 0f, 0.22f, 0.43f, 0.66f, 0.84f };
            foreach (float progress in progressMarkers)
            {
                _route.Evaluate(_route.TotalLength * progress, out Vector3 position, out Vector3 forward, out Vector3 right);
                Transform marker = new GameObject("Route marker").transform;
                marker.SetParent(landmarks, false);
                marker.SetPositionAndRotation(position + right * 5.2f, Quaternion.LookRotation(-right, Vector3.up));

                GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pole.name = "Post";
                pole.transform.SetParent(marker, false);
                pole.transform.localPosition = new Vector3(0f, 1.25f, 0f);
                pole.transform.localScale = new Vector3(0.16f, 2.5f, 0.16f);
                pole.GetComponent<Renderer>().sharedMaterial = post;
                DisableCollider(pole);

                GameObject panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
                panel.name = "Marker panel";
                panel.transform.SetParent(marker, false);
                panel.transform.localPosition = new Vector3(0f, 2.38f, 0f);
                panel.transform.localScale = new Vector3(1.35f, 0.62f, 0.10f);
                panel.GetComponent<Renderer>().sharedMaterial = sign;
                DisableCollider(panel);
            }
        }

        // Zone scenery (valley-zones-2026.09). Everything is deterministic from the fixed seed,
        // so every participant passes the same sequence. Each group is one merged,
        // vertex-coloured mesh to keep Quest draw calls low.

        /// <summary>Reflector posts close to the road give a clear sense of speed (optic flow).</summary>
        private void CreateSpeedCues()
        {
            var posts = new LandscapeMeshes();
            float offset = _route.RoadHalfWidth + 1.25f;
            float jetty = _route.TotalLength * 0.32f;
            Color white = new Color(0.92f, 0.92f, 0.88f);
            Color reflector = new Color(0.96f, 0.46f, 0.12f);
            for (float distance = 4f; distance < _route.TotalLength - 4f; distance += 16f)
            for (int side = -1; side <= 1; side += 2)
            {
                float along = distance + (side < 0 ? 0f : 8f);
                float progress = along / _route.TotalLength;
                bool village = progress > 0.44f && progress < 0.655f;
                bool nearJetty = side < 0 && Mathf.Abs(along - jetty) < 7f;
                if (village || nearJetty) continue;

                _route.Evaluate(along, out Vector3 point, out _, out Vector3 right);
                Vector3 foot = point + right * side * offset;
                foot.y = 0f;
                posts.Branch(foot, foot + Vector3.up * 0.98f, 0.055f, 0.05f, white);
                posts.Branch(foot + Vector3.up * 0.74f, foot + Vector3.up * 0.9f, 0.064f, 0.064f, reflector);
                posts.Branch(foot + Vector3.up * 0.98f, foot + Vector3.up * 1.02f, 0.05f, 0.005f, white);
            }

            MeshObject("Roadside reflector posts", _root, posts.Finish("Reflector posts"),
                VertexMaterial("Reflector posts", 0.3f), ShadowCastingMode.Off);
        }

        /// <summary>Dark conifers mixed into the first forest stretch.</summary>
        private void CreateForestConifers()
        {
            var trunks = new LandscapeMeshes();
            var needles = new LandscapeMeshes();
            float end = _route.TotalLength * 0.215f;
            for (float distance = 3f; distance < end; distance += 6.5f)
            for (int side = -1; side <= 1; side += 2)
            {
                if (_random.NextDouble() < 0.3) continue;
                _route.Evaluate(distance + RandomRange(-2f, 2f), out Vector3 point, out _, out Vector3 right);
                Vector3 foot = point + right * side * RandomRange(6.2f, 17f);
                foot.y = 0f;
                float height = RandomRange(7f, 11.5f);
                if (!TryReserve(foot, height * 0.24f))
                {
                    continue;
                }

                trunks.Branch(foot, foot + Vector3.up * height * 0.35f, 0.16f, 0.12f, new Color(0.30f, 0.19f, 0.11f));
                Color green = Color.Lerp(new Color(0.07f, 0.24f, 0.13f), new Color(0.13f, 0.33f, 0.17f), RandomRange(0f, 1f));
                for (int tier = 0; tier < 3; tier++)
                {
                    float bottom = height * (0.22f + tier * 0.22f);
                    float top = bottom + height * (0.42f - tier * 0.06f);
                    Color tint = green * (0.92f + tier * 0.07f);
                    tint.a = 1f;
                    needles.Branch(foot + Vector3.up * bottom, foot + Vector3.up * top,
                        height * (0.26f - tier * 0.06f), 0.03f, tint);
                }
            }

            MeshObject("Forest conifer trunks", _root, trunks.Finish("Conifer trunks"), VertexMaterial("Conifer bark"));
            MeshObject("Forest conifers", _root, needles.Finish("Conifer needles"), VertexMaterial("Conifer needles"));
        }

        /// <summary>A small moored sailing boat on the lake.</summary>
        private void CreateLakeBoat()
        {
            _route.Evaluate(_route.TotalLength * 0.32f, out Vector3 routePoint, out Vector3 forward, out Vector3 right);
            Vector3 center = routePoint - right * 48f;
            Vector3 boat = center - forward * 14f - right * 8f;
            float yaw = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y + 20f;
            Transform parent = new GameObject("Lake boat").transform;
            parent.SetParent(_root, false);
            CreateField(parent, boat + Vector3.up * 0.16f, new Vector3(1.5f, 0.42f, 4.2f), yaw,
                GetMaterial("Boat hull", new Color(0.62f, 0.20f, 0.14f), 0.25f, 0f));
            CreateField(parent, boat + Vector3.up * 1.9f, new Vector3(0.08f, 3.2f, 0.08f), yaw,
                GetMaterial("Jetty wood", new Color(.45f, .34f, .22f), .1f, 0));
            CreateField(parent, boat + Vector3.up * 2.0f, new Vector3(0.04f, 2.4f, 1.9f), yaw,
                GetMaterial("Sail canvas", new Color(0.95f, 0.93f, 0.86f), 0.1f, 0f));
        }

        /// <summary>Street lamps along the village road and a clock tower landmark.</summary>
        private void CreateVillageStreet()
        {
            var lamps = new LandscapeMeshes();
            float start = _route.TotalLength * 0.445f;
            float end = _route.TotalLength * 0.65f;
            int index = 0;
            for (float distance = start; distance < end; distance += 14f, index++)
            {
                int side = index % 2 == 0 ? -1 : 1;
                _route.Evaluate(distance, out Vector3 point, out _, out Vector3 right);
                Vector3 foot = point + right * side * (_route.RoadHalfWidth + 1.5f);
                foot.y = 0f;
                Vector3 top = foot + Vector3.up * 3.4f;
                lamps.Branch(foot, top, 0.07f, 0.05f, new Color(0.12f, 0.20f, 0.18f));
                lamps.Branch(top, top - right * side * 0.55f + Vector3.up * 0.12f, 0.04f, 0.04f, new Color(0.12f, 0.20f, 0.18f));
                lamps.Crown(top - right * side * 0.6f, Vector3.one * 0.2f, index, new Color(1f, 0.93f, 0.72f));
            }

            MeshObject("Village street lamps", _root, lamps.Finish("Street lamps"), VertexMaterial("Street lamps", 0.4f),
                ShadowCastingMode.Off);

            _route.Evaluate(_route.TotalLength * 0.555f, out _, out _, out Vector3 towerRight);
            Vector3 towerFoot = _towerFoot;
            float towerYaw = Quaternion.LookRotation(towerRight, Vector3.up).eulerAngles.y;
            Transform tower = new GameObject("Village clock tower").transform;
            tower.SetParent(_root, false);
            CreateField(tower, towerFoot + Vector3.up * 6.5f, new Vector3(3.6f, 13f, 3.6f), towerYaw,
                GetMaterial("Cream walls", new Color(0.83f, 0.75f, 0.58f), 0.08f, 0f));
            CreateField(tower, towerFoot + towerRight * 1.82f + Vector3.up * 10.4f, new Vector3(1.7f, 1.7f, 0.08f), towerYaw,
                GetMaterial("Clock face", new Color(0.96f, 0.95f, 0.9f), 0.3f, 0f));
            CreateField(tower, towerFoot + towerRight * 1.87f + Vector3.up * 10.6f, new Vector3(0.09f, 0.7f, 0.04f), towerYaw,
                GetMaterial("Clock hands", new Color(0.1f, 0.1f, 0.1f), 0.2f, 0f));
            var roof = new LandscapeMeshes();
            roof.Branch(towerFoot + Vector3.up * 13f, towerFoot + Vector3.up * 17.6f, 2.8f, 0.05f, new Color(0.40f, 0.14f, 0.10f));
            MeshObject("Clock tower roof", tower, roof.Finish("Tower roof"), VertexMaterial("Tower roof"));
        }

        /// <summary>A turning windmill and colourful flower strips in the meadow.</summary>
        private void CreateMeadowWindmill()
        {
            _route.Evaluate(_route.TotalLength * 0.755f, out _, out _, out Vector3 right);
            Vector3 foot = _windmillFoot;
            Vector3 toRoad = right;
            var body = new LandscapeMeshes();
            body.Branch(foot, foot + Vector3.up * 12f, 2.3f, 1.4f, new Color(0.90f, 0.86f, 0.76f));
            body.Branch(foot + Vector3.up * 12f, foot + Vector3.up * 14.4f, 1.75f, 0.05f, new Color(0.45f, 0.16f, 0.12f));
            Transform windmill = new GameObject("Meadow windmill").transform;
            windmill.SetParent(_root, false);
            MeshObject("Windmill body", windmill, body.Finish("Windmill body"), VertexMaterial("Windmill body"));

            Transform sails = new GameObject("Windmill sails").transform;
            sails.SetParent(windmill, false);
            sails.SetPositionAndRotation(foot + Vector3.up * 11.2f + toRoad * 1.9f, Quaternion.LookRotation(toRoad, Vector3.up));
            Material sailMaterial = GetMaterial("Sail canvas", new Color(0.95f, 0.93f, 0.86f), 0.1f, 0f);
            Material woodMaterial = GetMaterial("Jetty wood", new Color(.45f, .34f, .22f), .1f, 0);
            CreateField(sails, Vector3.zero, new Vector3(0.7f, 0.7f, 0.5f), 0f, woodMaterial, true);
            for (int blade = 0; blade < 4; blade++)
            {
                Transform arm = new GameObject("Sail arm").transform;
                arm.SetParent(sails, false);
                arm.localRotation = Quaternion.Euler(0f, 0f, blade * 90f);
                CreateField(arm, new Vector3(0f, 3.4f, 0.1f), new Vector3(0.14f, 6.6f, 0.1f), 0f, woodMaterial, true);
                CreateField(arm, new Vector3(0.52f, 3.9f, 0.12f), new Vector3(0.9f, 5.2f, 0.04f), 0f, sailMaterial, true);
            }

            SceneryMotion motion = sails.gameObject.AddComponent<SceneryMotion>();
            motion.LocalAxis = Vector3.forward;
            motion.DegreesPerSecond = 22f;

            Color[] palette =
            {
                new Color(0.55f, 0.42f, 0.78f),
                new Color(0.84f, 0.24f, 0.18f),
                new Color(0.95f, 0.78f, 0.18f)
            };
            Material[] fieldMaterials =
            {
                GetMaterial("Lavender strip", palette[0], 0.03f, 0f),
                GetMaterial("Poppy strip", palette[1], 0.03f, 0f),
                GetMaterial("Sunflower strip", palette[2], 0.03f, 0f)
            };
            Transform strips = new GameObject("Meadow flower strips").transform;
            strips.SetParent(_root, false);
            foreach (FlowerStrip strip in MeadowStrips())
            {
                CreateField(strips, strip.Center, new Vector3(8.5f, 0.02f, 21f),
                    Quaternion.LookRotation(strip.Forward, Vector3.up).eulerAngles.y,
                    fieldMaterials[strip.Index % fieldMaterials.Length]);
            }

            var flowers = new LandscapeMeshes();
            for (int i = 0; i < 260; i++)
            {
                float distance = _route.TotalLength * RandomRange(0.67f, 0.84f);
                _route.Evaluate(distance, out Vector3 point, out _, out Vector3 flowerRight);
                int side = _random.Next(2) == 0 ? -1 : 1;
                Vector3 p = point + flowerRight * side * RandomRange(4.3f, 9f);
                p.y = 0.3f + RandomRange(0f, 0.18f);
                flowers.Crown(p, Vector3.one * 0.085f, i, palette[_random.Next(palette.Length)]);
            }

            MeshObject("Meadow flowers", _root, flowers.Finish("Meadow flowers"), VertexMaterial("Meadow flowers"),
                ShadowCastingMode.Off);
        }

        /// <summary>A golden autumn avenue on the final hill stretch.</summary>
        private void CreateAutumnAvenue()
        {
            var trunks = new LandscapeMeshes();
            var crowns = new LandscapeMeshes();
            Color[] leaves =
            {
                new Color(0.93f, 0.68f, 0.18f),
                new Color(0.86f, 0.42f, 0.14f),
                new Color(0.95f, 0.82f, 0.36f)
            };
            int seed = 0;
            for (float distance = _route.TotalLength * 0.852f; distance < _route.TotalLength * 0.993f; distance += 9f)
            for (int side = -1; side <= 1; side += 2)
            {
                _route.Evaluate(distance, out Vector3 point, out _, out Vector3 right);
                Vector3 foot = point + right * side * 6.4f;
                foot.y = 0f;
                TryReserve(foot, 1.9f);
                float height = RandomRange(6.5f, 8.5f);
                trunks.Branch(foot, foot + Vector3.up * height * 0.62f, 0.15f, 0.09f, new Color(0.86f, 0.84f, 0.78f));
                for (int part = 0; part < 3; part++)
                {
                    Vector3 offset = new Vector3(RandomRange(-0.8f, 0.8f), height * 0.72f + part * 0.45f, RandomRange(-0.8f, 0.8f));
                    Color tint = leaves[_random.Next(leaves.Length)] * RandomRange(0.9f, 1.05f);
                    tint.a = 1f;
                    crowns.Crown(foot + offset, new Vector3(1.7f, 2.0f, 1.7f) * RandomRange(0.85f, 1.1f), seed++, tint);
                }
            }

            MeshObject("Autumn avenue trunks", _root, trunks.Finish("Avenue trunks"), VertexMaterial("Birch bark"));
            MeshObject("Autumn avenue leaves", _root, crowns.Finish("Avenue leaves"), VertexMaterial("Autumn leaves"));
        }

        private Material VertexMaterial(string materialName, float smoothness = 0.02f)
        {
            Material material = GetMaterial(materialName, Color.white, smoothness, 0f);
            material.SetFloat("_VertexTint", 1f);
            return material;
        }

        private Material GetMaterial(string materialName, Color color, float smoothness, float metallic)
        {
            if (_materials.TryGetValue(materialName, out Material existing))
            {
                return existing;
            }

            Shader shader = Resources.Load<Shader>("VirtualRideScenic");
            if (shader == null)
            {
                throw new InvalidOperationException(
                    "VirtualRideScenic shader is missing from Assets/VirtualRide/Resources.");
            }

            Material material = new Material(shader) { name = materialName, color = color, enableInstancing = true };
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", smoothness);
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", metallic);
            }

            _materials.Add(materialName, material);
            return material;
        }

        private float RandomRange(float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
        }

        private static void DisableCollider(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }

        private static void SetShadowMode(GameObject gameObject, ShadowCastingMode shadowMode, bool receiveShadows)
        {
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = shadowMode;
            renderer.receiveShadows = receiveShadows;
        }
    }
}
