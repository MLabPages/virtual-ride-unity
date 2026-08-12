using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VirtualRide.Core;

namespace VirtualRide.World
{
    public sealed class ScenicWorldBuilder
    {
        private readonly RideRoute _route;
        private readonly Transform _root;
        private readonly Dictionary<string, Material> _materials = new Dictionary<string, Material>();
        private readonly System.Random _random = new System.Random(20260812);

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
            builder.CreateGround();
            builder.CreateRoad();
            builder.CreateLake();
            builder.CreateTrees();
            builder.CreateVillage();
            builder.CreateFields();
            builder.CreateMountains();
            builder.CreateLandmarks();
            return worldObject.transform;
        }

        private void ConfigureEnvironment()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.68f, 0.80f, 0.86f);
            RenderSettings.fogStartDistance = 105f;
            RenderSettings.fogEndDistance = 430f;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.55f, 0.69f, 0.80f);
            RenderSettings.ambientEquatorColor = new Color(0.38f, 0.48f, 0.39f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.19f, 0.13f);
            RenderSettings.ambientIntensity = 1.05f;

            Shader skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                Material sky = new Material(skyShader) { name = "Morning Sky" };
                if (sky.HasProperty("_SkyTint"))
                {
                    sky.SetColor("_SkyTint", new Color(0.35f, 0.62f, 0.88f));
                    sky.SetColor("_GroundColor", new Color(0.42f, 0.48f, 0.35f));
                    sky.SetFloat("_AtmosphereThickness", 0.85f);
                    sky.SetFloat("_Exposure", 1.18f);
                }

                RenderSettings.skybox = sky;
            }

            GameObject sunObject = new GameObject("Morning Sun");
            sunObject.transform.SetParent(_root, false);
            sunObject.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
            Light sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.91f, 0.72f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.58f;
            sun.shadowBias = 0.055f;
            sun.shadowNormalBias = 0.5f;
            RenderSettings.sun = sun;
        }

        private void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Grassland";
            ground.transform.SetParent(_root, false);
            ground.transform.position = new Vector3(0f, -0.04f, 0f);
            ground.transform.localScale = new Vector3(72f, 1f, 62f);
            ground.GetComponent<Renderer>().sharedMaterial = GetMaterial(
                "Meadow", new Color(0.25f, 0.48f, 0.22f), 0.02f, 0f);
            SetShadowMode(ground, ShadowCastingMode.Off, true);
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
            GameObject lake = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            lake.name = "Lake";
            lake.transform.SetParent(_root, false);
            lake.transform.position = new Vector3(-38f, 0.02f, -32f);
            lake.transform.localScale = new Vector3(54f, 0.035f, 34f);
            lake.GetComponent<Renderer>().sharedMaterial = GetMaterial(
                "Lake water", new Color(0.12f, 0.53f, 0.70f), 0.82f, 0.08f);
            DisableCollider(lake);
            SetShadowMode(lake, ShadowCastingMode.Off, true);

            Material reeds = GetMaterial("Reeds", new Color(0.36f, 0.48f, 0.14f), 0.05f, 0f);
            Transform reedParent = new GameObject("Lakeside reeds").transform;
            reedParent.SetParent(_root, false);
            for (int i = 0; i < 55; i++)
            {
                float angle = i / 55f * Mathf.PI * 2f + RandomRange(-0.07f, 0.07f);
                Vector3 position = new Vector3(
                    -38f + Mathf.Cos(angle) * RandomRange(52f, 57f),
                    RandomRange(0.25f, 0.5f),
                    -32f + Mathf.Sin(angle) * RandomRange(32f, 36f));
                GameObject reed = GameObject.CreatePrimitive(PrimitiveType.Cube);
                reed.name = "Reed cluster";
                reed.transform.SetParent(reedParent, false);
                reed.transform.position = position;
                reed.transform.localScale = new Vector3(0.15f, position.y * 2f, 0.15f);
                reed.GetComponent<Renderer>().sharedMaterial = reeds;
                DisableCollider(reed);
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

            for (int i = 0; i < _route.Count; i += 5)
            {
                float progress = i / (float)_route.Count;
                bool villageGap = progress > 0.43f && progress < 0.67f;
                bool lakeGap = progress > 0.24f && progress < 0.42f;
                if (villageGap || lakeGap)
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

                    float offset = RandomRange(7.5f, progress < 0.22f ? 18f : 30f);
                    Vector3 position = routePoint + right * side * offset;
                    position.y = 0f;
                    float scale = RandomRange(0.8f, 1.55f);
                    CreateTree(treeParent, position, scale, trunkMaterial, foliage[_random.Next(foliage.Length)]);
                }
            }

            // A few distant groves make open stretches feel less empty.
            for (int i = 0; i < 45; i++)
            {
                float angle = RandomRange(0f, Mathf.PI * 2f);
                float radius = RandomRange(205f, 300f);
                Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius * 0.78f);
                CreateTree(treeParent, position, RandomRange(1.1f, 2f), trunkMaterial,
                    foliage[_random.Next(foliage.Length)]);
            }
        }

        private void CreateTree(Transform parent, Vector3 position, float scale, Material trunk, Material leaves)
        {
            Transform tree = new GameObject("Tree").transform;
            tree.SetParent(parent, false);
            tree.position = position;
            tree.rotation = Quaternion.Euler(0f, RandomRange(0f, 360f), 0f);

            float trunkHeight = 2.1f * scale;
            GameObject trunkObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunkObject.name = "Trunk";
            trunkObject.transform.SetParent(tree, false);
            trunkObject.transform.localPosition = new Vector3(0f, trunkHeight * 0.5f, 0f);
            trunkObject.transform.localScale = new Vector3(0.22f * scale, trunkHeight * 0.5f, 0.22f * scale);
            trunkObject.GetComponent<Renderer>().sharedMaterial = trunk;
            DisableCollider(trunkObject);

            for (int layer = 0; layer < 3; layer++)
            {
                GameObject crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                crown.name = "Crown";
                crown.transform.SetParent(tree, false);
                crown.transform.localPosition = new Vector3(
                    layer == 1 ? 0.35f * scale : -0.12f * layer,
                    trunkHeight + (0.5f + layer * 0.52f) * scale,
                    layer == 2 ? 0.18f * scale : 0f);
                float crownScale = (1.65f - layer * 0.12f) * scale;
                crown.transform.localScale = new Vector3(crownScale, crownScale * 1.08f, crownScale);
                crown.GetComponent<Renderer>().sharedMaterial = leaves;
                DisableCollider(crown);
            }
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
                CreateHouse(village, position, Quaternion.LookRotation(-right * side, Vector3.up),
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

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject window = GameObject.CreatePrimitive(PrimitiveType.Cube);
                window.name = "Window";
                window.transform.SetParent(house, false);
                window.transform.localPosition = new Vector3(side * 1.25f * scale, 1.75f * scale, -1.87f * scale);
                window.transform.localScale = new Vector3(0.82f * scale, 0.92f * scale, 0.04f * scale);
                window.GetComponent<Renderer>().sharedMaterial = windowMaterial;
                DisableCollider(window);
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
            for (int i = 0; i < 32; i++)
            {
                float distance = _route.TotalLength * (0.70f + i * 0.0045f);
                _route.Evaluate(distance, out Vector3 position, out Vector3 forward, out Vector3 right);
                position += right * 6.2f;
                CreateFencePost(fields, position, Quaternion.LookRotation(forward, Vector3.up), fenceMaterial);
            }
        }

        private static void CreateField(Transform parent, Vector3 position, Vector3 scale, float yaw, Material material)
        {
            GameObject field = GameObject.CreatePrimitive(PrimitiveType.Cube);
            field.name = "Cultivated field";
            field.transform.SetParent(parent, false);
            field.transform.position = position;
            field.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
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

        private void CreateMountains()
        {
            Transform mountains = new GameObject("Distant mountains").transform;
            mountains.SetParent(_root, false);
            Material[] mountainMaterials =
            {
                GetMaterial("Blue mountain", new Color(0.29f, 0.40f, 0.42f), 0.03f, 0f),
                GetMaterial("Green mountain", new Color(0.24f, 0.37f, 0.29f), 0.03f, 0f),
                GetMaterial("Hazy mountain", new Color(0.40f, 0.48f, 0.47f), 0.03f, 0f)
            };

            for (int i = 0; i < 16; i++)
            {
                float angle = i / 16f * Mathf.PI * 2f + 0.08f * Mathf.Sin(i * 1.7f);
                float radius = RandomRange(275f, 360f);
                Vector3 position = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius * 0.78f);
                float height = RandomRange(38f, 90f);
                float width = RandomRange(45f, 92f);
                GameObject mountain = CreateCone("Mountain", 9, width, height);
                mountain.transform.SetParent(mountains, false);
                mountain.transform.position = position;
                mountain.transform.rotation = Quaternion.Euler(0f, RandomRange(0f, 360f), RandomRange(-4f, 4f));
                mountain.GetComponent<Renderer>().sharedMaterial = mountainMaterials[i % mountainMaterials.Length];
                SetShadowMode(mountain, ShadowCastingMode.Off, true);
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

        private static GameObject CreateCone(string objectName, int sides, float radius, float height)
        {
            Vector3[] vertices = new Vector3[sides + 2];
            vertices[0] = new Vector3(0f, height, 0f);
            vertices[vertices.Length - 1] = Vector3.zero;
            for (int i = 0; i < sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            }

            int[] triangles = new int[sides * 6];
            for (int i = 0; i < sides; i++)
            {
                int next = (i + 1) % sides;
                int triangle = i * 6;
                triangles[triangle] = 0;
                triangles[triangle + 1] = i + 1;
                triangles[triangle + 2] = next + 1;
                triangles[triangle + 3] = vertices.Length - 1;
                triangles[triangle + 4] = next + 1;
                triangles[triangle + 5] = i + 1;
            }

            Mesh mesh = new Mesh { name = objectName + " mesh", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            GameObject cone = new GameObject(objectName);
            cone.AddComponent<MeshFilter>().sharedMesh = mesh;
            cone.AddComponent<MeshRenderer>();
            return cone;
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

            Material material = new Material(shader) { name = materialName, color = color };
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
